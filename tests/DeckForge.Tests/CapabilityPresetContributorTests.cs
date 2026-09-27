using System.Text.Json.Nodes;
using DeckForge.CodeGen.Capabilities;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the generation extension point.
/// </summary>
/// <remarks>
/// The contributor pipeline was documented, resolved from DI and run in order - and had zero
/// implementations, so it was a no-op. Separately, the stock files were written before contributors
/// ran, so every list a contributor fills had already been consumed or was never read. Both mean a
/// capability selected in the new-project wizard produced exactly the same project as one where
/// nothing was selected. These tests are what make that impossible to reintroduce.
/// </remarks>
[TestFixture]
public sealed class CapabilityPresetContributorTests
{
    private static NewProjectOptions Options(params string[] presets) => new()
    {
        PluginName = "Preset probe",
        PluginId = "com.example.presetprobe",
        Publisher = "Example",
        ParentDirectory = Sandbox(),
        ProjectName = "PresetProbe",
        InitGit = false,
        CapabilityPresets = presets,
    };

    private static string Sandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "deckforge-preset-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProjectContentBuilder Build(params string[] presets)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityPresetContributor>();
        services.AddSingleton<IProjectContentContributor>(
            sp => sp.GetRequiredService<CapabilityPresetContributor>());

        var generator = new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance);

        return generator.BuildContent(Options(presets));
    }

    [Test]
    public void The_contributor_is_registered_and_runs()
    {
        var builder = Build("variables");

        Assert.That(
            builder.ExtraIntegrationInterfaces,
            Does.Contain("IVariableProvider"),
            "The capability preset contributed no interface, so selecting it changed nothing.");
    }

    [Test]
    public void The_stock_template_is_written_before_contributors_so_they_can_patch_it()
    {
        var builder = Build("variables");
        var source = builder.GetFile("src/PresetProbe/PluginIntegration.cs");

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("IVariableProvider"));
    }

    [Test]
    public void Usings_interfaces_and_members_all_reach_the_generated_file()
    {
        var source = Build("variables", "events").GetFile("src/PresetProbe/PluginIntegration.cs")!;

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("using MacroDeck.Sdk.Variables;"));
            Assert.That(source, Does.Contain("using MacroDeck.Sdk.Events;"));
            Assert.That(source, Does.Contain("IVariableProvider"));
            Assert.That(source, Does.Contain("IEventProvider"));
            Assert.That(source, Does.Contain("VariableDefinition"));
            Assert.That(source, Does.Contain("EventDefinition"));
        });
    }

    [Test]
    public void A_capabilitys_permissions_reach_the_manifest()
    {
        var manifest = JsonNode.Parse(Build("config-flows").GetFile("src/PresetProbe/manifest.json")!)!;
        var permissions = manifest["permissions"]?.AsArray();

        Assert.That(permissions, Is.Not.Null, "The manifest declared no permissions at all.");
        Assert.That(
            permissions!.Select(p => p!.GetValue<string>()),
            Does.Contain("host:config"));
    }

    [Test]
    public void Permissions_a_user_asked_for_are_merged_with_the_capability_ones()
    {
        var options = Options("config-flows") with { Permissions = ["host:deck", "host:config"] };
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityPresetContributor>();
        services.AddSingleton<IProjectContentContributor>(
            sp => sp.GetRequiredService<CapabilityPresetContributor>());

        var builder = new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(options);

        var manifest = JsonNode.Parse(builder.GetFile("src/PresetProbe/manifest.json")!)!;
        var permissions = manifest["permissions"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(permissions, Does.Contain("host:config"));
            Assert.That(permissions, Does.Contain("host:deck"));
            Assert.That(permissions, Is.Unique, "A permission was declared twice.");
        });
    }

    [Test]
    public void Resx_keys_a_capability_needs_are_present()
    {
        var resx = Build("events").GetFile("src/PresetProbe/Localization/Strings.resx")!;

        Assert.Multiple(() =>
        {
            Assert.That(resx, Does.Contain("Events.SomethingHappened.Name"));
            Assert.That(resx, Does.Contain("Actions.LogMessage.Name"), "The stock keys were lost.");
        });
    }

    [Test]
    public void No_presets_means_no_extra_code()
    {
        var builder = Build();

        Assert.Multiple(() =>
        {
            Assert.That(builder.ExtraIntegrationInterfaces, Is.Empty);
            Assert.That(builder.ExtraIntegrationMembers, Is.Empty);
            Assert.That(builder.GetFile("src/PresetProbe/manifest.json"), Does.Not.Contain("permissions"));
        });
    }

    [Test]
    public void Every_capability_the_gallery_offers_is_selectable_at_creation()
    {
        // The gallery and the wizard read different lists. If they drift, a capability is offered
        // in one place and cannot be chosen in the other.
        Assert.That(
            CapabilityPresetContributor.PresetIds,
            Is.EqualTo(CapabilityDefinitions.Ids));
    }

    [Test]
    public void An_unknown_preset_is_ignored_rather_than_throwing()
    {
        var builder = Build("not-a-capability");

        Assert.That(builder.ExtraIntegrationInterfaces, Is.Empty);
    }

    [Test]
    public void Build_json_honours_the_self_contained_choice()
    {
        // It was hardcoded to "false" while the manifest entrypoints said true, so a
        // self-contained request shipped a framework-dependent app.
        var frameworkDependent = Build();
        var selfContained = BuildWith(options => options with { SelfContained = true });

        Assert.Multiple(() =>
        {
            Assert.That(
                frameworkDependent.GetFile("src/PresetProbe/macrodeck-build.json"),
                Does.Contain("\"--self-contained\", \"false\""));
            Assert.That(
                selfContained.GetFile("src/PresetProbe/macrodeck-build.json"),
                Does.Contain("\"--self-contained\", \"true\""));
        });
    }

    [Test]
    public void A_self_contained_build_does_not_ask_for_no_aphost()
    {
        // UseAppHost=false is right for a framework-dependent build and wrong for a self-contained
        // one, where the .exe is the entrypoint the manifest names.
        var json = BuildWith(options => options with { SelfContained = true })
            .GetFile("src/PresetProbe/macrodeck-build.json")!;

        Assert.That(json, Does.Not.Contain("UseAppHost"));
    }

    [Test]
    public void Manifest_strings_are_json_escaped()
    {
        // license and homepage went into the manifest unescaped, so a license containing a quote
        // produced a manifest.json that would not parse - and the failure surfaced at pack time,
        // long after the wizard accepted the value. The plugin id is validated separately and
        // cannot contain one.
        var options = Options() with
        {
            License = "MIT\" OR \"Apache-2.0\"",
            Homepage = "https://example.com/a\"b",
            Description = "Line one\nLine \"two\"",
        };

        var services = new ServiceCollection();
        var builder = new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(options);

        var text = builder.GetFile("src/PresetProbe/manifest.json")!;

        Assert.That(
            () => JsonNode.Parse(text),
            Throws.Nothing,
            "The generated manifest is not valid JSON.");
    }

    private static ProjectContentBuilder BuildWith(Func<NewProjectOptions, NewProjectOptions> adjust)
    {
        var services = new ServiceCollection();
        return new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(adjust(Options()));
    }
}
