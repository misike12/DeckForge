using DeckForge.CodeGen.Generation;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the source-patching the editors perform on a generated plugin.
/// </summary>
/// <remarks>
/// Four copies of this logic existed. One anchored on <c>IndexOf("using ")</c>, which matches
/// inside the stock file's XML doc comment, so an import could land inside a comment. Two demanded
/// the exact stock class declaration, so a plugin that had already opted into another capability
/// could not have an event or a config flow added at all. This is the one implementation, and these
/// tests pin the behaviour the copies disagreed about.
/// </remarks>
[TestFixture]
public sealed class IntegrationPatcherTests
{
    private const string Stock = """
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.Actions;
        using Serilog;

        namespace Sample;

        /// <summary>
        /// The plugin's one integration. Opt in by implementing an interface here
        /// (<c>IVariableProvider</c> and so on).
        /// </summary>
        public sealed class PluginIntegration : IPluginIntegration
        {
            private readonly ILogger _logger;

            public PluginIntegration(ILogger logger)
            {
                _logger = logger.ForContext<PluginIntegration>();
            }

            public IReadOnlyList<IActionDefinition> Actions { get; }
        }
        """;

    [Test]
    public void A_using_directive_does_not_land_inside_a_comment()
    {
        // The old copies used IndexOf("using "). The doc comment here mentions the interfaces, and
        // in a real generated file it also mentions namespaces - a comment match is a using
        // directive inside a // block, which does not compile.
        var patched = IntegrationPatcher.AddUsing(Stock, "MacroDeck.Sdk.Events").Content;

        Assert.Multiple(() =>
        {
            Assert.That(patched, Does.Contain("using MacroDeck.Sdk.Events;"));
            Assert.That(
                patched,
                Does.Not.Contain("(<c>I</c>using"),
                "The directive was inserted inside the doc comment.");
            Assert.That(
                patched.IndexOf("using MacroDeck.Sdk.Events;", StringComparison.Ordinal),
                Is.LessThan(patched.IndexOf("namespace ", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void A_using_directive_lands_on_its_own_line()
    {
        // The CRLF bug: EndsWith(";") against an untrimmed line fails on a line ending in \r, so
        // every directive in a CRLF file looked like no directive at all.
        var crlf = Stock.Replace("\n", "\r\n", StringComparison.Ordinal);
        var patch = IntegrationPatcher.AddUsing(crlf, "MacroDeck.Sdk.Events");

        Assert.Multiple(() =>
        {
            Assert.That(patch.Outcome, Is.EqualTo(PatchOutcome.Patched));
            Assert.That(patch.Content, Does.Contain("using MacroDeck.Sdk.Events;"));
        });
    }

    [Test]
    public void An_interface_is_added_when_the_base_list_has_already_grown()
    {
        // The old copies matched the literal stock declaration, so once any other capability had
        // been added the declaration no longer matched and the editor refused outright.
        var grown = IntegrationPatcher.AddInterface(Stock, "IVariableProvider").Content;
        var second = IntegrationPatcher.AddInterface(grown, "IConfigFlowProvider");

        Assert.Multiple(() =>
        {
            Assert.That(second.Outcome, Is.EqualTo(PatchOutcome.Patched));
            Assert.That(second.Content, Does.Contain("IVariableProvider"));
            Assert.That(second.Content, Does.Contain("IConfigFlowProvider"));
        });
    }

    [Test]
    public void An_interface_is_added_before_the_brace_not_after_the_newline()
    {
        // The stock template puts the opening brace on the next line, so appending at the brace put
        // the interface after the newline - valid C#, but it reads as mangled output.
        var patched = IntegrationPatcher.AddInterface(Stock, "IEventProvider").Content;

        Assert.That(
            patched,
            Does.Contain("public sealed class PluginIntegration : IPluginIntegration , IEventProvider\n{"),
            "The interface was appended after the newline rather than at the end of the base list.");
    }

    [Test]
    public void Adding_the_same_interface_twice_changes_nothing()
    {
        var once = IntegrationPatcher.AddInterface(Stock, "IEventProvider");
        var twice = IntegrationPatcher.AddInterface(once.Content, "IEventProvider");

        Assert.Multiple(() =>
        {
            Assert.That(twice.Outcome, Is.EqualTo(PatchOutcome.AlreadyPresent));
            Assert.That(twice.Content, Is.EqualTo(once.Content));
        });
    }

    [Test]
    public void Adding_the_same_using_twice_changes_nothing()
    {
        var once = IntegrationPatcher.AddUsing(Stock, "MacroDeck.Sdk.Events");
        var twice = IntegrationPatcher.AddUsing(once.Content, "MacroDeck.Sdk.Events");

        Assert.Multiple(() =>
        {
            Assert.That(twice.Outcome, Is.EqualTo(PatchOutcome.AlreadyPresent));
            Assert.That(twice.Content, Is.EqualTo(once.Content));
        });
    }

    [Test]
    public void A_member_goes_inside_the_class_body()
    {
        var patch = IntegrationPatcher.AddMember(
            Stock,
            "public IConfigFlow CreateConfigFlow() => new SetupFlow();");

        Assert.Multiple(() =>
        {
            Assert.That(patch.Outcome, Is.EqualTo(PatchOutcome.Patched));
            Assert.That(
                patch.Content,
                Does.Contain("public IConfigFlow CreateConfigFlow() => new SetupFlow();"));
            Assert.That(
                patch.Content.IndexOf("CreateConfigFlow", StringComparison.Ordinal),
                Is.LessThan(patch.Content.LastIndexOf('}')), "The member went after the class closed.");
        });
    }

    [Test]
    public void A_member_is_added_once()
    {
        var once = IntegrationPatcher.AddMember(Stock, "public int Answer => 42;");
        var twice = IntegrationPatcher.AddMember(once.Content, "public int Answer => 42;");

        Assert.Multiple(() =>
        {
            Assert.That(twice.Outcome, Is.EqualTo(PatchOutcome.AlreadyPresent));
            Assert.That(twice.Content, Is.EqualTo(once.Content));
            Assert.That(
                System.Text.RegularExpressions.Regex.Matches(twice.Content, "=> 42").Count,
                Is.EqualTo(1),
                "The member was added twice.");
        });
    }

    [Test]
    public void A_source_without_the_class_is_reported_rather_than_mangled()
    {
        const string notAPlugin = "using System;\n\nnamespace Other;\n\npublic sealed class Something\n{\n}\n";

        Assert.Multiple(() =>
        {
            Assert.That(
                IntegrationPatcher.AddInterface(notAPlugin, "IEventProvider").Outcome,
                Is.EqualTo(PatchOutcome.AnchorMissing));
            Assert.That(
                IntegrationPatcher.AddMember(notAPlugin, "public int X;").Outcome,
                Is.EqualTo(PatchOutcome.AnchorMissing));
        });
    }

    [Test]
    public void A_source_with_no_using_directive_is_reported_rather_than_mangled()
    {
        const string noUsings = "namespace Other;\n\npublic sealed class PluginIntegration : IPluginIntegration\n{\n}\n";

        Assert.That(
            IntegrationPatcher.AddUsing(noUsings, "MacroDeck.Sdk.Events").Outcome,
            Is.EqualTo(PatchOutcome.AnchorMissing));
    }

    [Test]
    public void Patching_never_removes_a_previous_directive()
    {
        // The bug this replaced overwrote the first directive on every call, so after sixteen
        // capabilities `using MacroDeck.Sdk;` was gone and the class could not see
        // IPluginIntegration at all.
        var source = Stock;
        foreach (var ns in new[]
                 {
                     "MacroDeck.Sdk.Variables", "MacroDeck.Sdk.Events", "MacroDeck.Sdk.Messaging",
                     "MacroDeck.Sdk.Devices", "MacroDeck.Sdk.Layouts", "MacroDeck.Sdk.Weather",
                 })
        {
            source = IntegrationPatcher.AddUsing(source, ns).Content;
        }

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("using MacroDeck.Sdk;"), "The stock using was deleted.");
            Assert.That(source, Does.Contain("using MacroDeck.Sdk.Actions;"));
            Assert.That(source, Does.Contain("using Serilog;"));
            Assert.That(
                source.Split('\n').Count(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal)),
                Is.EqualTo(9), "A blank line appeared per insertion, or a directive was lost.");
        });
    }
}
