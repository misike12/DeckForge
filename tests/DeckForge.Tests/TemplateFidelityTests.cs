using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the files a generated plugin is expected to contain.
/// </summary>
/// <remarks>
/// These are the gaps between DeckForge's output and the official Macro Deck template. Each one is
/// a file or a line the host or the SDK expects and DeckForge did not emit, and each was found by
/// comparing the two rather than by running anything.
/// </remarks>
[TestFixture]
public sealed class TemplateFidelityTests
{
    private TempDirectory _temp = null!;

    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-fidelity");
        _root = _temp.Root;
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private NewProjectOptions Options() => new()
    {
        PluginName = "Fidelity",
        PluginId = "com.example.fidelity",
        Publisher = "Example",
        ParentDirectory = _root,
        ProjectName = "Fidelity",
        InitGit = false,
    };

    private ProjectContentBuilder Build(NewProjectOptions? options = null)
    {
        var services = new ServiceCollection();
        return new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(options ?? Options());
    }

    [Test]
    public void A_license_is_generated_because_the_manifest_declares_one()
    {
        var builder = Build();
        var manifest = builder.GetFile("src/Fidelity/manifest.json")!;

        Assume.That(manifest, Does.Contain("\"license\""));

        var license = builder.GetFile("LICENSE");
        Assert.That(license, Is.Not.Null, "The manifest declares a licence and no LICENSE file is generated.");
        Assert.That(license, Is.Not.Empty);
    }

    [Test]
    public void The_mit_license_names_the_publisher_and_the_current_year()
    {
        var license = Build(Options() with { License = "MIT", Publisher = "Acme" }).GetFile("LICENSE")!;

        Assert.Multiple(() =>
        {
            Assert.That(license, Does.Contain("MIT License"));
            Assert.That(license, Does.Contain("Acme"));
            Assert.That(license, Does.Contain(DateTime.UtcNow.Year.ToString()));
        });
    }

    [Test]
    public void An_unrecognised_licence_still_produces_a_file_and_says_what_is_missing()
    {
        var license = Build(Options() with { License = "MPL-2.0" }).GetFile("LICENSE")!;

        Assert.Multiple(() =>
        {
            Assert.That(license, Does.Contain("MPL-2.0"));
            Assert.That(license, Does.Contain("Replace this file"),
                "An unrecognised licence must say it is a placeholder, not pass as the real text.");
        });
    }

    [Test]
    public void The_local_feed_directory_nuget_config_declares_actually_exists()
    {
        var builder = Build();

        Assert.Multiple(() =>
        {
            Assert.That(builder.GetFile("NuGet.config"), Does.Contain("local-feed"));
            Assert.That(
                builder.HasFile("local-feed/.gitkeep"),
                "NuGet.config names local-feed as a source, so every restore warns about it.");
        });
    }

    [Test]
    public void The_generated_gitignore_keeps_the_feed_placeholder_but_not_its_contents()
    {
        var gitignore = Build().GetFile(".gitignore")!;

        Assert.Multiple(() =>
        {
            Assert.That(gitignore, Does.Contain("local-feed/*"));
            Assert.That(gitignore, Does.Contain("!local-feed/.gitkeep"));
        });
    }

    [Test]
    public void Every_file_the_template_promises_is_actually_emitted()
    {
        var builder = Build();
        var expected = new[]
        {
            "Fidelity.slnx",
            "Directory.Build.props",
            "Directory.Packages.props",
            "NuGet.config",
            ".gitignore",
            "README.md",
            "LICENSE",
            "local-feed/.gitkeep",
            "src/Fidelity/Fidelity.csproj",
            "src/Fidelity/manifest.json",
            "src/Fidelity/macrodeck-build.json",
            "src/Fidelity/Program.cs",
            "src/Fidelity/PluginIntegration.cs",
            "src/Fidelity/LogMessageAction.cs",
            "src/Fidelity/Localization/Strings.resx",
            "src/Fidelity/Assets/icon.svg",
            "src/Fidelity/Properties/launchSettings.json",
            "tests/Fidelity.Tests/Fidelity.Tests.csproj",
            "tests/Fidelity.Tests/PluginIntegrationTests.cs",
        };

        var missing = expected.Where(e => !builder.HasFile(e)).ToList();

        Assert.That(missing, Is.Empty, "The template did not emit: " + string.Join(", ", missing));
    }

    [Test]
    public void A_generated_language_gets_its_own_resx()
    {
        var builder = Build(Options() with { Languages = ["de-DE", "fr-FR"] });

        Assert.Multiple(() =>
        {
            Assert.That(builder.HasFile("src/Fidelity/Localization/Strings.de-DE.resx"), Is.True);
            Assert.That(builder.HasFile("src/Fidelity/Localization/Strings.fr-FR.resx"), Is.True);
        });
    }

    [Test]
    public void The_generated_readme_does_not_invent_a_build_option()
    {
        // The README documented `macrodeck-plugin build --project .`, and there is no --project
        // on the build command. It is signed and packed, and validation is a different verb.
        var readme = Build().GetFile("README.md")!;

        Assert.That(readme, Does.Not.Contain("macrodeck-plugin build --project"));
    }
}
