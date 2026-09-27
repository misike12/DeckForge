using System.Text.Json;
using DeckForge.Core.Plugins;
using DeckForge.CodeGen.Generation;
using DeckForge.Validators;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DeckForge.Tests;

[TestFixture]
public class PluginProjectGeneratorTests
{
    private static NewProjectOptions Options(string? repo = null) => new()
    {
        PluginName = "Acme Light Control",
        PluginId = "com.acme.light-control",
        Publisher = "Acme",
        Description = "Controls lights.",
        ParentDirectory = Path.GetTempPath(),
        Repository = repo,
        Platforms = ["win-x64", "osx-arm64", "linux-x64"],
        Languages = ["de"],
    };

    private static ProjectContentBuilder Build(NewProjectOptions options)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .BuildServiceProvider();
        var generator = new PluginProjectGenerator(
            services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginProjectGenerator>.Instance);
        return generator.BuildContent(options);
    }

    [Test]
    public void Generates_all_stock_files()
    {
        var b = Build(Options());
        var p = b.ProjectName;

        Assert.Multiple(() =>
        {
            Assert.That(b.HasFile($"{p}.slnx"), Is.True);
            Assert.That(b.HasFile("Directory.Build.props"), Is.True);
            Assert.That(b.HasFile("Directory.Packages.props"), Is.True);
            Assert.That(b.HasFile("NuGet.config"), Is.True);
            Assert.That(b.HasFile(".gitignore"), Is.True);
            Assert.That(b.HasFile($"src/{p}/manifest.json"), Is.True);
            Assert.That(b.HasFile($"src/{p}/macrodeck-build.json"), Is.True);
            Assert.That(b.HasFile($"src/{p}/Program.cs"), Is.True);
            Assert.That(b.HasFile($"src/{p}/PluginIntegration.cs"), Is.True);
            Assert.That(b.HasFile($"src/{p}/LogMessageAction.cs"), Is.True);
            Assert.That(b.HasFile($"src/{p}/Localization/Strings.resx"), Is.True);
            Assert.That(b.HasFile($"src/{p}/Localization/Strings.de.resx"), Is.True);
            Assert.That(b.HasFile($"src/{p}/Assets/icon.svg"), Is.True);
            Assert.That(b.HasFile($"src/{p}/Properties/launchSettings.json"), Is.True);
            Assert.That(b.HasFile($"tests/{p}.Tests/{p}.Tests.csproj"), Is.True);
            Assert.That(b.HasFile($"tests/{p}.Tests/PluginIntegrationTests.cs"), Is.True);
        });
    }

    [Test]
    public void Manifest_is_valid_json_and_passes_native_validator()
    {
        var b = Build(Options());
        var manifest = b.Files[$"src/{b.ProjectName}/manifest.json"];

        var result = ManifestValidator.Validate(manifest);
        var errors = result.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();

        Assert.That(errors, Is.Empty, string.Join("\n", errors.Select(e => e.Message)));
    }

    [Test]
    public void Manifest_pins_confirmed_sdk_version()
    {
        var b = Build(Options());
        Assert.That(b.Files["Directory.Packages.props"], Does.Contain(MacroDeckSdkInfo.DefaultVersion));
        Assert.That(MacroDeckSdkInfo.DefaultVersion, Is.EqualTo("3.0.0-beta.14"));
    }

    [Test]
    public void manifest_json_parses_with_expected_fields()
    {
        var b = Build(Options());
        var doc = JsonDocument.Parse(b.Files[$"src/{b.ProjectName}/manifest.json"]);
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("com.acme.light-control"));
            Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Acme Light Control"));
            Assert.That(root.GetProperty("manifestVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("entrypoints").EnumerateObject().Count(), Is.EqualTo(3));
            Assert.That(root.GetProperty("publisher").GetProperty("name").GetString(), Is.EqualTo("Acme"));
            Assert.That(root.GetProperty("compatibility").GetProperty("macroDeck").GetString(), Is.EqualTo(">=3.0.0-0"));
        });
    }

    [Test]
    public void launchsettings_and_buildconfig_are_valid_json()
    {
        var b = Build(Options());
        Assert.DoesNotThrow(() => JsonDocument.Parse(b.Files[$"src/{b.ProjectName}/macrodeck-build.json"]));
        Assert.DoesNotThrow(() => JsonDocument.Parse(b.Files[$"src/{b.ProjectName}/Properties/launchSettings.json"]));
    }

    [Test]
    public void resx_files_are_valid_xml()
    {
        var b = Build(Options());
        var p = b.ProjectName;
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(b.Files[$"src/{p}/Localization/Strings.resx"]));
        Assert.DoesNotThrow(() => System.Xml.Linq.XDocument.Parse(b.Files[$"src/{p}/Localization/Strings.de.resx"]));
    }

    [Test]
    public void generated_cs_has_no_unreplaced_tokens()
    {
        var b = Build(Options());
        foreach (var (path, content) in b.Files)
        {
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                Assert.That(content, Does.Not.Contain("$RootNamespace"), path);
                Assert.That(content, Does.Not.Contain("$ProjectName"), path);
            }
        }
    }

    [Test]
    public void self_contained_manifest_uses_exe_on_windows()
    {
        var options = Options() with { SelfContained = true, Platforms = ["win-x64"] };
        var b = Build(options);
        var manifest = b.Files[$"src/{b.ProjectName}/manifest.json"];
        Assert.That(manifest, Does.Contain($"runtimes/win-x64/{b.ProjectName}.exe"));
        Assert.That(manifest, Does.Not.Contain("FrameworkDependent"));
    }

    [Test]
    public void invalid_options_fail_validation()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        var generator = new PluginProjectGenerator(
            services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginProjectGenerator>.Instance);

        Assert.Throws<GenerationException>(() => generator.BuildContent(Options() with { PluginId = "NotValid" }));
        Assert.Throws<GenerationException>(() => generator.BuildContent(Options() with { Publisher = "" }));
    }
}
