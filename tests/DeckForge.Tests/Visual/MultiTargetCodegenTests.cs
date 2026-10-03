using System.Diagnostics;
using System.Text;
using DeckForge.CodeGen.Capabilities;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using DeckForge.Core.Visual;
using DeckForge.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Phase 9's exit criterion, built rather than asserted: a widget node's handler and a config workflow
/// with canvas blocks in them, compiled inside a scaffolded plugin.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in Phase 9 is string comparison, and string comparison is what let the stock widget
/// handler ship a comment that swallowed its own closing brace: the generated file was plausible, the
/// tests were green, and the first plugin to use an untouched event would not compile. So this fixture
/// runs <c>dotnet build</c> against the real SDK.
/// </para>
/// <para>
/// The blocks used are the ones that pull in the generated runtime (<c>VisualRuntime</c>), because a
/// region that references it has to be inside a file that has the runtime file next to it - which is
/// exactly the coupling a multi-target writer can get wrong.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class MultiTargetCodegenTests
{
    private static bool Skip => Environment.GetEnvironmentVariable("DECKFORGE_SKIP_SLOW_TESTS") == "1";

    [Test]
    public void A_widget_nodes_handler_compiles_with_canvas_blocks_in_it()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var options = Options();
        var design = new WidgetDesign
        {
            WidgetTypeId = "gauge",
            WidgetName = "Gauge",
            HasConfiguration = false,
        };

        var node = new DesignedNode
        {
            Key = "readout",
            NodeType = "ui.text",
            Text = "0",
            Events = [new DesignedEvent { Name = "press" }],
        };

        design.Nodes.Add(node);

        var provider = WidgetGenerator.Render(design, options.ProjectName!);
        var project = Project(TargetKind.WidgetHandler, "press");
        var result = VisualTargetWriter.Write(provider, project, project.Targets[0]);

        Assert.That(result.Success, Is.True, result.Message);

        var (_, output) = WriteAndBuild(
            options,
            [
                ($"src/{options.ProjectName}/{design.ClassName}.cs", result.Content),
                Runtime(options.ProjectName!),
            ],
            WidgetGenerator.BuildStrings(design));

        AssertNoCompilerErrors(output);
    }

    [Test]
    public void A_config_workflow_compiles_with_canvas_blocks_in_its_start_hook()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var workspace = Generate(out var root, out var projectName);
        var scaffolded = CapabilityScaffolder.Scaffold(workspace, "config-flows");
        _ = workspace;
        Assert.That(scaffolded.Failed, Is.False, scaffolded.Message);

        var integration = File.ReadAllText(Path.Combine(root, "src", projectName, "PluginIntegration.cs"));
        var project = Project(TargetKind.ConfigFlowHook, "StartAsync");
        var result = VisualTargetWriter.Write(integration, project, project.Targets[0]);

        Assert.That(result.Success, Is.True, result.Message);

        var (_, output) = WriteAndBuild(
            Options(),
            [
                ($"src/{Path.GetFileName(root)}/PluginIntegration.cs", result.Content),
                Runtime(Path.GetFileName(root)),
            ]);

        AssertNoCompilerErrors(output);
    }

    [Test]
    public void A_lifecycle_hook_compiles_with_canvas_blocks_in_initialise()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var workspace = Generate(out var root, out var projectName);
        var integration = File.ReadAllText(Path.Combine(root, "src", projectName, "PluginIntegration.cs"));
        var project = Project(TargetKind.Lifecycle, "InitializeAsync");
        var result = VisualTargetWriter.Write(integration, project, project.Targets[0]);

        Assert.That(result.Success, Is.True, result.Message);

        var (_, output) = WriteAndBuild(
            Options(),
            [
                ($"src/{Path.GetFileName(root)}/PluginIntegration.cs", result.Content),
                Runtime(Path.GetFileName(root)),
            ]);

        AssertNoCompilerErrors(output);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>A project whose single target logs a line, plus a variable the region reads.</summary>
    private static VisualProject Project(TargetKind kind, string anchorId)
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget
        {
            Id = $"{kind}:{anchorId}",
            Kind = kind,
            AnchorId = anchorId,
            Name = anchorId,
        };
        project.Targets.Add(target);

        target.Scripts.Add(new VisualScript
        {
            Id = "script-1",
            Name = anchorId,
            Hat = new Block { Kind = "hat.widget-event", Id = "hat-1" },
            Body =
            [
                new Block
                {
                    Kind = "var.set",
                    Id = "b-1",
                    Inputs = new Dictionary<string, BlockInput>(StringComparer.Ordinal)
                    {
                        ["var"] = new() { Variable = "pressed" },
                        ["value"] = new() { Text = "1" },
                    },
                },
                new Block
                {
                    Kind = "var.change",
                    Id = "b-2",
                    Inputs = new Dictionary<string, BlockInput>(StringComparer.Ordinal)
                    {
                        ["var"] = new() { Variable = "pressed" },
                        ["amount"] = new() { Number = 2 },
                    },
                },
            ],
        });

        return project;
    }

    /// <summary>The runtime support file, named the way the writer names it.</summary>
    private static (string RelativePath, string Content) Runtime(string projectName)
    {
        var (fileName, content) = VisualTargetWriter.RuntimeFile(projectName);

        return ($"src/{projectName}/{fileName}", content);
    }

    private static NewProjectOptions Options() => new()
    {
        PluginName = "Multi Target Probe",
        PluginId = "com.example.multi-target-" + Guid.NewGuid().ToString("N")[..8],
        Publisher = "Example",
        Description = "Phase 9's targets, with canvas blocks in them.",
        ParentDirectory = Path.GetTempPath(),
        ProjectName = "MultiTargetProbe" + Guid.NewGuid().ToString("N")[..8],
        Platforms = ["win-x64"],
    };

    private static WorkspaceContext Generate(out string root, out string projectName)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var generator = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance);
        var options = Options();
        root = generator.Generate(options);
        projectName = options.ProjectName!;

        var manifest = ManifestDocument.Load(Path.Combine(root, "src", projectName, "manifest.json"));
        return new WorkspaceContext(
            Path.Combine(root, projectName + ".slnx"),
            new NewProjectOptions
            {
                PluginName = manifest.Name,
                PluginId = manifest.Id,
                Publisher = manifest.PublisherName,
                ParentDirectory = root,
                ProjectName = projectName,
            });
    }

    private static (string Root, string Output) WriteAndBuild(
        NewProjectOptions options,
        IReadOnlyList<(string RelativePath, string Content)> extras,
        IReadOnlyDictionary<string, string>? resxEntries = null)
    {
        var scoped = options with { ParentDirectory = Path.GetTempPath() };
        var sandbox = Path.Combine(Path.GetTempPath(), "deckforge-p9-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(sandbox);
        var scopedSandbox = scoped with { ParentDirectory = sandbox };

        var services = new ServiceCollection().BuildServiceProvider();
        var generator = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance);
        var root = generator.Generate(scopedSandbox);

        foreach (var (relative, content) in extras)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        if (resxEntries is { Count: > 0 })
        {
            ResxMerger.AddKeys(
                Path.Combine(root, "src", scopedSandbox.ProjectName!, "Localization", "Strings.resx"),
                resxEntries);
        }

        var slnx = Directory.GetFiles(root, "*.slnx").First();
        return (root, RunBuild(Path.GetDirectoryName(slnx)!));
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

        foreach (var arg in new[] { "build", "--nologo", "-v", "q", "-clp:NoSummary" })
        {
            psi.ArgumentList.Add(arg);
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

    private static void AssertNoCompilerErrors(string output)
    {
        var errors = output
            .Split('\n')
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                           || line.Contains(": warning MDP", StringComparison.OrdinalIgnoreCase)
                           || line.Contains(": warning MDLOC", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .ToList();

        Assert.That(
            errors,
            Is.Empty,
            "Generated code did not compile cleanly:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}