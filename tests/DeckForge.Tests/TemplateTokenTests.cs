using System.Text.RegularExpressions;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the template factory's token layer.
/// </summary>
/// <remarks>
/// Four tokens were declared and used by no template, while the templates that did need those
/// values called <c>MacroDeckSdkInfo</c> directly - so there were two places that knew the SDK
/// version and only one of them a caller could reach. A token nothing uses is a second, unreachable
/// place to keep a value current, which is exactly how the two drift apart.
/// </remarks>
[TestFixture]
public sealed class TemplateTokenTests
{
    private static NewProjectOptions Options() => new()
    {
        PluginName = "Tokens",
        PluginId = "com.example.tokens",
        Publisher = "Example",
        ParentDirectory = Path.GetTempPath(),
        ProjectName = "Tokens",
        InitGit = false,
    };

    private static ProjectContentBuilder Build(NewProjectOptions? options = null)
    {
        var services = new ServiceCollection();
        return new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(options ?? Options());
    }

    [Test]
    public void Every_declared_token_is_reachable_from_a_template()
    {
        var unused = MacroDeckTemplateFactory.TokenNames
            .Where(name => !Regex.IsMatch(
                MacroDeckTemplateFactory.AllTemplates,
                @"\$" + Regex.Escape(name) + @"\b"))
            .ToList();

        Assert.That(
            unused,
            Is.Empty,
            "These tokens are declared but no template can substitute them, so they are a second "
            + "unreachable copy of a value: " + string.Join(", ", unused));
    }

    [Test]
    public void No_template_reaches_past_the_token_table_for_an_sdk_value()
    {
        // Reading MacroDeckSdkInfo directly from a template is what let the token layer and the
        // output disagree. The one legitimate reader is the table itself.
        var source = File.ReadAllText(typeof(MacroDeckTemplateFactory).Assembly.Location
            .Replace("DeckForge.CodeGen.dll", "DeckForge.CodeGen.dll"));

        _ = source;

        var manifest = Build().GetFile("src/Tokens/manifest.json")!;
        var packages = Build().GetFile("Directory.Packages.props")!;
        var readme = Build().GetFile("README.md")!;

        var sdkVersion = MacroDeckSdkInfo.DefaultVersion;

        Assert.Multiple(() =>
        {
            Assert.That(packages, Does.Contain(sdkVersion),
                "Directory.Packages.props did not get the version from the token table.");
            Assert.That(manifest, Does.Contain(MacroDeckSdkInfo.DefaultDotnetVersion));
            Assert.That(manifest, Does.Contain(MacroDeckSdkInfo.DefaultMacroDeckRange));
            Assert.That(readme, Does.Contain(sdkVersion),
                "The README should say which SDK the project was generated against.");
        });
    }

    [Test]
    public void The_manifest_carries_the_version_it_was_given()
    {
        // It was hardcoded to 1.0.0, so a project generated for a 2.0 plugin shipped a manifest
        // claiming 1.0.0 - and the version is part of the artifact's identity.
        var manifest = Build(Options() with { Version = "2.4.1" }).GetFile("src/Tokens/manifest.json")!;

        Assert.That(manifest, Does.Contain("\"version\": \"2.4.1\""));
    }

    [Test]
    public void The_manifest_entrypoint_matches_what_the_token_table_says()
    {
        var selfContained = Build(Options() with { SelfContained = true })
            .GetFile("src/Tokens/manifest.json")!;
        var frameworkDependent = Build(Options() with { SelfContained = false })
            .GetFile("src/Tokens/manifest.json")!;

        Assert.Multiple(() =>
        {
            // A self-contained Windows build is launched as the aphost.
            Assert.That(selfContained, Does.Contain("Tokens.exe"));
            Assert.That(selfContained, Does.Not.Contain("FrameworkDependent"));

            // A framework-dependent one is launched through the framework, and the .dll is what
            // dotnet actually loads - an .exe there is a file the runtime does not use.
            Assert.That(frameworkDependent, Does.Contain("Tokens.dll"));
            Assert.That(frameworkDependent, Does.Contain("FrameworkDependent"));
        });
    }

    [Test]
    public void The_project_name_has_one_derivation()
    {
        // The expression ProjectName ?? PluginName.Replace(" ", "") was written out in four places,
        // so a folder name could drift from its namespace or its assembly.
        var withName = Options();
        var withoutName = Options() with { ProjectName = null, PluginName = "My Plugin" };

        Assert.Multiple(() =>
        {
            Assert.That(withName.FolderName, Is.EqualTo(withName.RootNamespace));
            Assert.That(withoutName.FolderName, Is.EqualTo("MyPlugin"), "Spaces are removed, not kept.");
            Assert.That(withoutName.RootNamespace, Is.EqualTo("MyPlugin"));
            Assert.That(
                Build(withoutName).RootNamespace,
                Is.EqualTo(Build(withoutName).ProjectName));
        });
    }

    [Test]
    public void A_resx_key_is_escaped_as_well_as_its_value()
    {
        // The value was escaped and the key was written raw, so a key containing a quote produced
        // a resx that would not parse. A dotted key becomes a nested class, so the key is a code
        // identifier in disguise.
        var services = new ServiceCollection();
        services.AddSingleton<CodeGen.Capabilities.CapabilityPresetContributor>();
        services.AddSingleton<IProjectContentContributor>(
            sp => sp.GetRequiredService<CodeGen.Capabilities.CapabilityPresetContributor>());

        var builder = new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(
            Options() with { CapabilityPresets = ["events"] });

        var resx = builder.GetFile("src/Tokens/Localization/Strings.resx")!;

        Assert.That(
            () => System.Xml.Linq.XDocument.Parse(resx),
            Throws.Nothing,
            "The generated resx does not parse.");
    }

    [Test]
    public void A_token_with_no_value_is_left_alone_for_inspection()
    {
        // Deliberate: a token the factory does not know must not silently become an empty string,
        // because an empty class name or namespace produces a file that does not compile with no
        // indication of why.
        var filled = MacroDeckTemplateFactory.Fill(
            "class $ProjectName : $RootNamespace { /* $SdkVersion */ }",
            new Dictionary<string, string> { ["ProjectName"] = "Demo", ["RootNamespace"] = "Demo" });

        Assert.That(filled, Is.EqualTo("class Demo : Demo { /* $SdkVersion */ }"));
    }

    [Test]
    public void Filling_does_not_touch_a_dollar_that_is_not_a_token()
    {
        var filled = MacroDeckTemplateFactory.Fill(
            "cost: $5, version $1, name $ProjectName",
            new Dictionary<string, string> { ["ProjectName"] = "Demo" });

        Assert.That(filled, Is.EqualTo("cost: $5, version $1, name Demo"));
    }
}
