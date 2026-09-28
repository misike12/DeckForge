using DeckForge.CodeGen.Generation;
using DeckForge.Core.Code;
using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// The host hand-off, end to end.
/// </summary>
/// <remarks>
/// <para>
/// The SDK hands <c>IIntegrationContext</c> to exactly one place, <c>InitializeAsync</c>, and
/// <c>IActionDefinition.CreateExecutor()</c> takes no parameters. So an action that wants to change
/// a folder, notify, or read a variable can never be given the host by the SDK, and the plugin author
/// has to carry it by hand.
/// </para>
/// <para>
/// DeckForge carries it, and these tests exist because the first version of that carry was broken in
/// a way that compiled cleanly: <c>ActionGenerator</c> emitted a <c>_integration</c> field and an
/// executor that accepted it, but <c>CreateExecutor()</c> called <c>new Executor(_logger)</c> and
/// nothing ever supplied the context - so the field was permanently null and every host-calling
/// block threw <c>NullReferenceException</c> the first time a user ran it.
/// </para>
/// </remarks>
[TestFixture]
public class IntegrationContextWiringTests
{
    private static NewProjectOptions Options() => new()
    {
        PluginName = "Wiring",
        PluginId = "com.example.wiring",
        Publisher = "Example",
        ParentDirectory = Path.GetTempPath(),
        ProjectName = "Wiring",
        InitGit = false,
    };

    private static ProjectContentBuilder Build() =>
        new PluginProjectGenerator(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(Options());

    [Test]
    public void The_integration_hands_the_context_to_every_action_that_asked_for_one()
    {
        var integration = Build().GetFile($"src/Wiring/PluginIntegration.cs")!;

        Assert.Multiple(() =>
        {
            Assert.That(integration, Does.Contain("IIntegrationContextAware"),
                "Nothing in the integration hands the context over, so no action can ever have it.");
            Assert.That(integration, Does.Contain("SetIntegrationContext(context)"),
                "The hand-off must supply the session's context, not just cast.");
        });
    }

    [Test]
    public void The_hand_off_runs_before_the_integrations_own_work()
    {
        // After InitializeAsync, actions may be executed. A hand-off injected after the
        // integration's own body would leave a window where an action runs without a host.
        var integration = Build().GetFile($"src/Wiring/PluginIntegration.cs")!;
        var start = integration.IndexOf("InitializeAsync(IIntegrationContext", StringComparison.Ordinal);
        var handoff = integration.IndexOf("SetIntegrationContext(context)", start, StringComparison.Ordinal);
        var own = integration.IndexOf("_logger.Information", start, StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0), integration);
        Assert.Multiple(() =>
        {
            Assert.That(handoff, Is.GreaterThan(start), integration);
            Assert.That(own, Is.GreaterThan(handoff),
                "The hand-off ran after the integration's own work, so the first execution could see a null context.");
        });
    }

    [Test]
    public void The_example_action_reaches_its_executor_with_the_context_it_was_given()
    {
        var action = Build().GetFile($"src/Wiring/LogMessageAction.cs")!;

        Assert.Multiple(() =>
        {
            Assert.That(action, Does.Contain("IIntegrationContextAware"), "The action does not opt in.");
            Assert.That(action, Does.Contain("public void SetIntegrationContext(IIntegrationContext context)"), action);
            Assert.That(action, Does.Contain("new Executor(_logger, _integration)"),
                "CreateExecutor() does not forward the context, so the executor's field is always null.");
        });
    }

    [Test]
    public void Patching_an_action_twice_changes_nothing_the_second_time()
    {
        // Regeneration re-applies every patch, so a non-idempotent one duplicates the field and the
        // setter on each save - which is CS0102 the first time a user edits a block twice.
        var action = Build().GetFile($"src/Wiring/LogMessageAction.cs")!;

        var first = ActionContextPatcher.PatchAction(action);
        Assert.That(first.Outcome, Is.EqualTo(PatchOutcome.AlreadyPresent), first.Message);

        var again = ActionContextPatcher.PatchAction(first.Content);
        Assert.That(again.Content, Is.EqualTo(first.Content));
    }

    [Test]
    public void An_unrecognised_action_reports_why_instead_of_half_rewriting_it()
    {
        // A silent partial patch is worse than no patch: it produces code that compiles and then
        // throws at run time, which is much harder to diagnose than a refused save.
        const string Source = "namespace Wiring;\npublic sealed class Odd\n{\n}\n";

        var patch = ActionContextPatcher.PatchAction(Source);

        Assert.Multiple(() =>
        {
            Assert.That(patch.Outcome, Is.EqualTo(PatchOutcome.AnchorMissing));
            Assert.That(patch.Content, Is.EqualTo(Source), "A failed patch must leave the source untouched.");
            Assert.That(patch.Message, Is.Not.Empty, "A failure with no reason cannot be acted on.");
        });
    }

    [Test]
    public void The_patch_uses_the_indentation_the_file_already_uses()
    {
        // The official template is tab-indented and the generators are space-indented. Hardcoding
        // four spaces is what made the first attempt land its members at column zero.
        var tabbed = string.Join(
            "\n",
            "namespace Wiring;",
            "",
            "public sealed class Thing : IActionDefinition",
            "{",
            "\tprivate readonly ILogger _logger;",
            "",
            "\tpublic Thing(ILogger logger) => _logger = logger;",
            "",
            "\tpublic IActionExecutor CreateExecutor() => new Executor(_logger);",
            "",
            "\tprivate sealed class Executor : IActionExecutor",
            "\t{",
            "\t\tprivate readonly ILogger _logger;",
            "",
            "\t\tpublic Executor(ILogger logger) => _logger = logger;",
            "\t}",
            "}",
            "");

        var patch = ActionContextPatcher.PatchAction(tabbed);
        Assert.That(patch.Outcome, Is.EqualTo(PatchOutcome.Patched), patch.Message);

        var lines = patch.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var misindented = lines
            .Where(l => l.TrimStart().StartsWith("private IIntegrationContext?", StringComparison.Ordinal)
                     || l.TrimStart().StartsWith("public void SetIntegrationContext", StringComparison.Ordinal)
                     || l.TrimStart().StartsWith("public Thing(", StringComparison.Ordinal))
            .Where(l => l.Length > 0 && l[0] != '\t')
            .ToList();

        Assert.That(misindented, Is.Empty, string.Join("\n", misindented));
    }

    [Test]
    public void An_executor_constructor_whose_expression_contains_a_semicolon_is_not_cut_in_half()
    {
        // The member scan used to stop at the first `;` after the constructor, so a lambda with a
        // block in the expression body truncated the member - and the patch reported success while
        // writing a file that does not parse.
        var source = string.Join(
            "\n",
            "namespace Wiring;",
            "",
            "using MacroDeck.Sdk;",
            "using Serilog;",
            "",
            "public sealed class Thing : IActionDefinition",
            "{",
            "    private readonly ILogger _logger;",
            "",
            "    public Thing(ILogger logger) => _logger = logger;",
            "",
            "    public IActionExecutor CreateExecutor() => new Executor(_logger);",
            "",
            "    private sealed class Executor : IActionExecutor",
            "    {",
            "        private readonly ILogger _logger;",
            "",
            "        public Executor(ILogger logger) => Register(() => { _logger = logger; });",
            "    }",
            "}",
            "");

        var patch = ActionContextPatcher.PatchAction(source);
        Assert.That(patch.Outcome, Is.EqualTo(PatchOutcome.Patched), patch.Message);

        // Balanced braces and no orphaned statement: the file has to be parseable C#.
        var open = patch.Content.Count(c => c == '{');
        var close = patch.Content.Count(c => c == '}');
        Assert.Multiple(() =>
        {
            Assert.That(open, Is.EqualTo(close), "The rewritten file has unbalanced braces.");
            Assert.That(patch.Content, Does.Contain("Register(() => { _logger = logger; });"),
                "The original expression body was truncated.");
            Assert.That(patch.Content, Does.Contain("_integration = integration;"));
        });
    }

    [Test]
    public void A_brace_or_semicolon_inside_a_string_does_not_end_the_member_scan()
    {
        // A semicolon or brace in a string literal is not code, and treating it as such truncated
        // the member - or ended the scan early and dropped the rest of the constructor.
        var source = string.Join(
            "\n",
            "namespace Wiring;",
            "",
            "using MacroDeck.Sdk;",
            "using Serilog;",
            "",
            "public sealed class Thing : IActionDefinition",
            "{",
            "    private readonly ILogger _logger;",
            "",
            "    public Thing(ILogger logger) => _logger = logger;",
            "",
            "    public IActionExecutor CreateExecutor() => new Executor(_logger);",
            "",
            "    private sealed class Executor : IActionExecutor",
            "    {",
            "        private readonly ILogger _logger;",
            "",
            "        public Executor(ILogger logger) => Register(\"a; b {{ c\");",
            "    }",
            "}",
            "");

        var patch = ActionContextPatcher.PatchAction(source);

        Assert.That(patch.Outcome, Is.EqualTo(PatchOutcome.Patched), patch.Message);
        Assert.That(patch.Content, Does.Contain("Register(\"a; b {{ c\")"),
            "The string literal was cut at its semicolon.");
    }

    [Test]
    public void A_generated_action_forwards_the_context_into_its_executor()
    {
        // The same defect existed in the action generator, which is what a user's own actions are
        // written from, so the regression has to be covered on both templates.
        var source = ActionGenerator.Generate(
            new ActionDesign
            {
                ActionId = "do-thing",
                ActionName = "Do Thing",
                ActionDescription = "does the thing",
                Parameters = [new ActionParameterSpec { Name = "target", Label = "Target" }],
            },
            "Wiring").Content;

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IIntegrationContextAware"), source);
            Assert.That(source, Does.Contain("new Executor(_logger, _integration)"),
                "A generated action's CreateExecutor() drops the context on the floor.");
            Assert.That(source, Does.Contain("SetIntegrationContext(IIntegrationContext context)"), source);
        });
    }
}
