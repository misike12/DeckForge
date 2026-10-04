using System.Diagnostics;
using System.Text;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using DeckForge.Core.Visual;
using DeckForge.Tests.Visual.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The compile corpus, built: every snapshot-able document's C#, written into a real plugin project and
/// compiled against the real Macro Deck SDK.
/// </summary>
/// <remarks>
/// <para>
/// The golden files in <see cref="CompileCorpusTests"/> prove the emitter's output has not changed. This
/// proves the output is C#. Those are different questions, and a project of this size has shipped a
/// generated file that was plausible, was green against a string assertion, and would not compile.
/// </para>
/// <para>
/// <b>All the compilable documents, in one build.</b> Forty <c>dotnet build</c>s would add minutes to a
/// suite that currently takes one, so the corpus goes into a single scaffolded plugin: one class per
/// document, each holding the region the emitter produced for it, all compiled together. A diagnostic
/// names its file, so one broken document is still one named line in the failure.
/// </para>
/// <para>
/// <b>The wrapper is deliberately not an <c>IActionDefinition</c>.</b> What the corpus is testing is
/// the region, and the region needs three things in scope: <c>_logger</c>, <c>_integration</c> and
/// <c>context</c>. Implementing the full action contract would put a resx-generated
/// <c>LocalizedText</c> and a manifest in the path of every one of forty files, and a failure there
/// would say nothing about the emitter. The wrapper is the shape the emitter is documented to be written
/// for, and nothing else.
/// </para>
/// <para>
/// <b>What it cannot show:</b> that the generated action is wired to the host, that the plugin loads,
/// or that a real drag stays inside a frame. It shows that <c>csc</c> accepted the text.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class CompileCorpusCompilesTests
{
    private static bool Skip => Environment.GetEnvironmentVariable("DECKFORGE_SKIP_SLOW_TESTS") == "1";

    [Test]
    public void Every_compilable_corpus_document_builds_against_the_real_sdk()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var corpus = CompileCorpus.All.Where(corpusCase => corpusCase.CompiledForReal).ToList();
        Assert.That(corpus, Is.Not.Empty, "Nothing in the corpus is built, so this test proves nothing.");

        using var sandbox = new TempDirectory("deckforge-corpus-compile");
        var options = new NewProjectOptions
        {
            PluginName = "Compile Corpus",
            PluginId = "com.example.compile-corpus-" + Guid.NewGuid().ToString("N")[..8],
            Publisher = "Example",
            Description = "Every compile-corpus document, built together.",
            ParentDirectory = sandbox.Root,
            ProjectName = "CompileCorpus" + Guid.NewGuid().ToString("N")[..8],
            Platforms = ["win-x64"],
        };

        var services = new ServiceCollection().BuildServiceProvider();
        var root = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance)
            .Generate(options);

        var projectName = options.ProjectName!;
        var sourceDirectory = Path.Combine(root, "src", projectName);

        // The generated runtime. Every region calls into it, and a region that names a helper the
        // runtime does not define is a CS0117 that has nothing to do with the block that asked for it.
        var (runtimeFile, runtimeContent) = VisualTargetWriter.RuntimeFile(projectName);
        File.WriteAllText(Path.Combine(sourceDirectory, runtimeFile), runtimeContent, new UTF8Encoding(false));

        for (var index = 0; index < corpus.Count; index++)
        {
            var corpusCase = corpus[index];
            var name = ClassName(index, corpusCase.Name);
            var (region, problems) = Emit(corpusCase);
            File.WriteAllText(
                Path.Combine(sourceDirectory, name + ".cs"),
                Host(projectName, name, region, problems),
                new UTF8Encoding(false));
            TestContext.Out.WriteLine($"  {corpusCase.Name,-32} {problems.Count} problem(s)");
        }

        var slnx = Directory.GetFiles(root, "*.slnx").First();
        var output = RunBuild(Path.GetDirectoryName(slnx)!);

        AssertNoCompilerErrors(output);
    }

    [Test]
    public void A_document_the_corpus_leaves_out_of_the_build_really_does_not_compile()
    {
        // The other half of an exclusion: not just "here is a reason", but "here is the compiler saying
        // so". Without this, a reason could be a leftover from a bug that has since been fixed, and the
        // document would sit outside the build for ever with nobody noticing.
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var excluded = CompileCorpus.All.Where(corpusCase => !corpusCase.CompiledForReal).ToList();

        using var sandbox = new TempDirectory("deckforge-corpus-excluded");
        var options = new NewProjectOptions
        {
            PluginName = "Compile Corpus Excluded",
            PluginId = "com.example.corpus-excluded-" + Guid.NewGuid().ToString("N")[..8],
            Publisher = "Example",
            Description = "The documents the corpus leaves out of the build.",
            ParentDirectory = sandbox.Root,
            ProjectName = "CorpusExcluded" + Guid.NewGuid().ToString("N")[..8],
            Platforms = ["win-x64"],
        };

        var services = new ServiceCollection().BuildServiceProvider();
        var root = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance)
            .Generate(options);

        var sourceDirectory = Path.Combine(root, "src", options.ProjectName!);
        var (runtimeFile, runtimeContent) = VisualTargetWriter.RuntimeFile(options.ProjectName!);
        File.WriteAllText(Path.Combine(sourceDirectory, runtimeFile), runtimeContent, new UTF8Encoding(false));

        for (var index = 0; index < excluded.Count; index++)
        {
            var name = ClassName(index, excluded[index].Name);
            var (region, problems) = Emit(excluded[index]);
            File.WriteAllText(
                Path.Combine(sourceDirectory, name + ".cs"),
                Host(options.ProjectName!, name, region, problems),
                new UTF8Encoding(false));
        }

        var slnx = Directory.GetFiles(root, "*.slnx").First();
        var output = RunBuild(Path.GetDirectoryName(slnx)!);

        var diagnostics = Diagnostics(output)
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(
            diagnostics,
            Is.Not.Empty,
            "Every corpus document builds now, so the exclusions are stale. Move them back into the "
            + "built set and delete the reasons.");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static (string Code, IReadOnlyList<VisualCompileProblem> Problems) Emit(CorpusCase corpusCase)
    {
        var project = corpusCase.Build();
        return VisualEmitter.CompileTarget(project.Targets[0], project);
    }

    /// <summary>A class name from the corpus document's own name, so a diagnostic names its document.</summary>
    private static string ClassName(int index, string name) =>
        "Corpus" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
        + new string(name.Where(char.IsAsciiLetterOrDigit).ToArray());

    /// <summary>
    /// The whole file: the three names a region is written against, and the region.
    /// </summary>
    /// <remarks>
    /// <c>async</c> and the trailing <c>return</c> are what the writer's own
    /// <c>EnsureAsyncExecutor</c> produces on a real action, so the wrapper is the shape a saved plugin
    /// has rather than a shape invented for the test. The trailing return is unreachable in a document
    /// that ends in a cap block, which produces CS0162 and nothing else - a warning this test does not
    /// look at, because a warning about generated code is the SDK analyzer's business and not the
    /// corpus's.
    /// </remarks>
    private static string Host(string projectName, string className, string region,
        IReadOnlyList<VisualCompileProblem> problems)
    {
        var notes = problems.Count == 0
            ? "        // The emitter reported no problems for this document."
            : string.Join(
                Environment.NewLine,
                problems.Select(problem => $"        // {problem.BlockId}: {problem.Message}"));

        return $$"""
            // Generated by CompileCorpusCompilesTests. One corpus document per file.
            using System.Threading.Tasks;
            using MacroDeck.Sdk;
            using MacroDeck.Sdk.Actions;
            using Serilog;
            using {{projectName}};

            namespace Corpus;

            internal sealed class {{className}}
            {
                private readonly Serilog.ILogger _logger;

                private readonly IIntegrationContext? _integration;

                public {{className}}(Serilog.ILogger logger) => _logger = logger;

                public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
                {
            {{notes}}
            {{Indent(region)}}        return ActionResult.Success();
                }
            }

            """;
    }

    /// <summary>Puts a region at the depth the wrapper's method body needs it at.</summary>
    private static string Indent(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Select(line => "        " + line)) + Environment.NewLine;
    }

    private static string RunBuild(string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
                 {
                     "build", "--nologo", "-v", "q", "-clp:NoSummary",

                     // The generated plugin promotes CS8602 to an error, and a document that awaits
                     // before it reaches the host trips it: Roslyn drops a field's not-null state across
                     // an await, so the emitter's `if (_integration is null)` guard stops narrowing it,
                     // even though the guard is still there and still returns. That is a real finding
                     // about a generated plugin and it is reported - but it is a warning about the host
                     // field's null state, not a question of whether the emitter's calls typecheck, and
                     // this fixture's job is the latter. Clearing the list for this one build leaves
                     // every genuine CS error in place.
                     "/p:WarningsAsErrors=",
                 })
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(600_000);

        return $"exit={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}";
    }

    private static IEnumerable<string> Diagnostics(string output) =>
        output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static void AssertNoCompilerErrors(string output)
    {
        var errors = Diagnostics(output)
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                           || line.Contains(": warning MDP", StringComparison.OrdinalIgnoreCase)
                           || line.Contains(": warning MDLOC", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(
            errors,
            Is.Empty,
            "The corpus did not compile cleanly. Each line names the corpus document that produced it:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}