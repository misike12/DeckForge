using System.Text;
using DeckForge.CodeGen.Capabilities;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using DeckForge.Core.Workspace;
using DeckForge.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Scaffolds every capability into a real plugin project and compiles the result.
/// </summary>
/// <remarks>
/// <para>
/// The Capability Gallery offered twenty-two capabilities and implemented two. The other twenty
/// returned "not implemented yet" behind a button that was enabled anyway. The two that were
/// implemented had their own defect: the events arm emitted
/// <c>Strings.Events.SomethingHappened.Name()</c> without creating that resx key, so the project it
/// produced could not compile - and the status line reported success regardless.
/// </para>
/// <para>
/// So this is one test that scaffolds all twenty-two into a single integration, then builds it.
/// A capability whose interface name, namespace or member shape is wrong fails here rather than in
/// a user's plugin.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class CapabilityScaffoldingTests
{
    private static bool Skip => Environment.GetEnvironmentVariable("DECKFORGE_SKIP_SLOW_TESTS") == "1";

    private static NewProjectOptions Options() => new()
    {
        PluginName = "Capability Probe",
        PluginId = "com.example.capability-probe",
        Publisher = "Example",
        Description = "Exercises every capability.",
        ParentDirectory = Path.GetTempPath(),
        ProjectName = "CapabilityProbe" + Guid.NewGuid().ToString("N")[..8],
        Platforms = ["win-x64"],
    };

    private static WorkspaceContext Generate(out string root)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var generator = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance);
        root = generator.Generate(Options());

        var projectName = Path.GetFileName(root);
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

    [Test]
    public void Every_capability_is_defined()
    {
        Assert.That(CapabilityDefinitions.All, Has.Count.EqualTo(23));
        Assert.That(CapabilityDefinitions.Ids.Distinct().Count(), Is.EqualTo(23));
        Assert.That(Core.Capabilities.CapabilityCatalog.All, Has.Count.EqualTo(23),
            "The docs list twenty-three features; the catalog shipped twenty-two.");
    }

    [Test]
    public void The_catalog_and_the_definitions_agree_on_the_capability_set()
    {
        // The gallery shows Core's catalog; the scaffolder works from this table. If they drift,
        // a card would advertise something the scaffolder cannot do.
        var catalog = Core.Capabilities.CapabilityCatalog.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var defined = CapabilityDefinitions.Ids.ToHashSet(StringComparer.Ordinal);

        Assert.That(defined.Except(catalog), Is.Empty, "Defined but not in the catalog: " + string.Join(", ", defined.Except(catalog)));
        Assert.That(catalog.Except(defined), Is.Empty, "In the catalog but not defined: " + string.Join(", ", catalog.Except(defined)));
    }

    [Test]
    public void Every_capability_declares_permissions_that_exist()
    {
        foreach (var definition in CapabilityDefinitions.All)
        {
            foreach (var permission in definition.Permissions)
            {
                Assert.That(
                    Core.Plugins.PermissionCatalog.All.Any(p => p.Name == permission),
                    Is.True,
                    $"{definition.Id} asks for '{permission}', which is not in the permission catalog.");
            }
        }
    }

    [Test]
    public void Scaffolding_every_capability_produces_a_plugin_that_compiles()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var workspace = Generate(out var root);
        var notes = new List<string>();

        foreach (var id in CapabilityDefinitions.Ids)
        {
            var result = CapabilityScaffolder.Scaffold(workspace, id);
            Assert.That(result.Failed, Is.False, $"{id}: {result.Message}");
            notes.Add($"{id}: {result.Message}");
        }

        // The manifest must still validate after every permission was added.
        var manifestResult = ManifestValidator.Validate(
            File.ReadAllText(workspace.ManifestPath), ManifestValidationLevel.Publication);
        Assert.That(
            manifestResult.Issues.Where(i => i.Severity == ValidationSeverity.Error),
            Is.Empty,
            string.Join("\n", manifestResult.Issues.Select(i => i.Message)));

        var output = RunBuild(root);
        var errors = output
            .Split('\n')
            .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                        || l.Contains(": warning MDP", StringComparison.OrdinalIgnoreCase)
                        || l.Contains(": warning MDLOC", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .Distinct()
            .ToList();

        Assert.That(errors, Is.Empty,
            "Scaffolding every capability produced code that does not compile:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors)
            + Environment.NewLine + Environment.NewLine
            + "Notes:" + Environment.NewLine + string.Join(Environment.NewLine, notes));
    }

    [Test]
    public void Scaffolding_is_idempotent()
    {
        var workspace = Generate(out _);
        var id = "events";

        Assert.That(CapabilityScaffolder.Scaffold(workspace, id).Changed, Is.True);
        var first = File.ReadAllText(Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs"));

        var second = CapabilityScaffolder.Scaffold(workspace, id);
        var after = File.ReadAllText(Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(second.Changed, Is.False, "A second run reported a change.");
            Assert.That(after, Is.EqualTo(first), "A second run rewrote the integration.");
            Assert.That(CapabilityScaffolder.ScaffoldsPresent(workspace), Does.Contain(id));
        });
    }

    [Test]
    public void A_capability_that_adds_strings_really_adds_them()
    {
        // The events arm used to reference a resx key it never created, so the generated project
        // could not compile while the status line claimed success.
        var workspace = Generate(out _);
        CapabilityScaffolder.Scaffold(workspace, "events");

        var resx = File.ReadAllText(Path.Combine(workspace.LocalizationDirectory, "Strings.resx"));
        var integration = File.ReadAllText(Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs"));

        foreach (System.Text.RegularExpressions.Match reference in
                 System.Text.RegularExpressions.Regex.Matches(integration, @"Strings\.([A-Za-z0-9_.]+)\(\)"))
        {
            var key = reference.Groups[1].Value;
            Assert.That(resx, Does.Contain($"name=\"{key}\""),
                $"The integration reads Strings.{key}() but no resx entry produces it.");
        }
    }

    [Test]
    public void An_unknown_capability_is_refused_rather_than_pretending()
    {
        var workspace = Generate(out _);
        var result = CapabilityScaffolder.Scaffold(workspace, "not-a-capability");

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Failed, Is.True);
        });
    }

    [Test]
    public void Permissions_reach_the_manifest()
    {
        var workspace = Generate(out _);
        CapabilityScaffolder.Scaffold(workspace, "config-flows");

        var document = ManifestDocument.Load(workspace.ManifestPath);
        Assert.That(document.Permissions, Does.Contain("host:config"));
    }

    [Test]
    public void A_missing_integration_is_reported_rather_than_silently_succeeding()
    {
        var workspace = Generate(out var root);
        File.Delete(Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs"));

        var result = CapabilityScaffolder.Scaffold(workspace, "events");

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Message, Does.Contain("PluginIntegration.cs"));
        });

        _ = root;
    }

    [Test]
    public void The_interface_landed_on_the_integration_class()
    {
        var workspace = Generate(out _);
        CapabilityScaffolder.Scaffold(workspace, "variables");

        var source = File.ReadAllText(Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IPluginIntegration , IVariableProvider"));
            Assert.That(source, Does.Contain("using MacroDeck.Sdk.Variables;"));
            Assert.That(source, Does.Contain("VariableDefinition> Variables"));
            Assert.That(source, Does.Contain("ReadAsync"));
        });
    }

    private static string RunBuild(string workingDirectory)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
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

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(600_000);
        return $"exit={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}";
    }
}
