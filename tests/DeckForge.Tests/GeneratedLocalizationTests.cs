using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using DeckForge.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the resx keys and C# that the editors generate for a plugin.
/// </summary>
/// <remarks>
/// The Events editor wrote its own strings to <c>Events.&lt;Pascal&gt;</c> and its parameters to
/// <c>Events.&lt;Pascal&gt;.&lt;Param&gt;.Label</c>. The first is a key and the second makes it a
/// group, which is the collision the project's own MDLOC008 rule reports - so the editor produced
/// output its own validator rejected. These tests check the generated resx against that validator
/// rather than against a hand-written list of expectations.
/// </remarks>
[TestFixture]
public sealed class GeneratedLocalizationTests
{
    private TempDirectory _temp = null!;

    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-loc");
        _root = _temp.Root;
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    /// <summary>
    /// A project directory holding the template's real resx, so the tests check what the
    /// generator emits rather than a hand-written approximation of it.
    /// </summary>
    private string PluginProject(ProjectContentBuilder builder)
    {
        var project = Path.Combine(_root, "src", "Loc");
        var localization = Path.Combine(project, "Localization");
        Directory.CreateDirectory(localization);

        File.WriteAllText(Path.Combine(localization, "Strings.resx"), builder.GetFile("src/Loc/Localization/Strings.resx")!);

        foreach (var language in builder.Files.Keys.Where(k => k.Contains("Strings.", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(language);
            File.WriteAllText(Path.Combine(localization, name), builder.GetFile(language)!);
        }

        return project;
    }

    private NewProjectOptions Options(string? parent = null, params string[] presets) => new()
    {
        PluginName = "Loc",
        PluginId = "com.example.loc",
        Publisher = "Example",
        ParentDirectory = parent ?? _root,
        ProjectName = "Loc",
        InitGit = false,
        CapabilityPresets = presets,
    };

    private ProjectContentBuilder Build(NewProjectOptions? options = null)
    {
        var services = new ServiceCollection();

        // The events capability is the one that reads Strings.*() from generated code, and the
        // keys it needs are the subject of these tests.
        services.AddSingleton<CodeGen.Capabilities.CapabilityPresetContributor>();
        services.AddSingleton<IProjectContentContributor>(
            sp => sp.GetRequiredService<CodeGen.Capabilities.CapabilityPresetContributor>());

        return new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).BuildContent(options ?? Options());
    }

    [Test]
    public void A_capability_that_adds_resx_keys_does_not_create_a_key_and_a_group()
    {
        var builder = Build(Options(presets: "events"));
        var project = PluginProject(builder);
        var resx = Path.Combine(project, "Localization", "Strings.resx");

        // Exactly what the events capability contributes.
        ResxMerger.AddKeys(resx, new Dictionary<string, string>
        {
            ["Events.SomethingHappened.Name"] = "Something happened",
            ["Events.SomethingHappened.Description"] = "Raised when something happens.",
            ["Events.SomethingHappened.Trigger.Label"] = "Trigger",
        });

        var result = LocalizationValidator.ValidateProject(new LocalizationDirectory(project));

        Assert.That(
            result.Issues.Where(i => i.Code == "MDLOC008"),
            Is.Empty,
            "A key that is also a prefix of others is a collision: "
            + string.Join("; ", result.Issues.Where(i => i.Code == "MDLOC008").Select(i => i.Message)));
    }

    [Test]
    public void The_stock_seed_keys_pass_the_projects_own_validator()
    {
        var builder = Build(Options(presets: "events"));
        var project = PluginProject(builder);

        var result = LocalizationValidator.ValidateProject(new LocalizationDirectory(project));

        Assert.That(
            result.Issues,
            Is.Empty,
            "The template's own strings were rejected: "
            + string.Join("; ", result.Issues.Select(i => $"{i.Code} {i.Message}")));
    }

    [Test]
    public void Every_resx_key_the_template_ships_has_a_readable_placeholder()
    {
        // A key the generated code references but the resx does not define compiles fine and then
        // throws at run time, which is how the events capability's missing key was invisible until
        // a plugin was launched.
        var builder = Build(Options(presets: "events"));
        var project = PluginProject(builder);
        var resx = Path.Combine(project, "Localization", "Strings.resx");

        var integration = builder.GetFile("src/Loc/PluginIntegration.cs")!;
        var referenced = System.Text.RegularExpressions.Regex
            .Matches(integration, @"Strings\.([A-Za-z0-9_.]+)\(\)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var defined = ResxMerger.ReadKeys(resx).Keys.ToHashSet(StringComparer.Ordinal);
        var missing = referenced.Where(k => !defined.Contains(k)).ToList();

        Assert.That(referenced, Is.Not.Empty, "The template references no strings, so this proves nothing.");
        Assert.That(missing, Is.Empty, "The generated code reads keys the resx does not define: "
            + string.Join(", ", missing));
    }

    [Test]
    public void A_shipped_language_gets_the_same_keys_as_the_default_one()
    {
        // An empty translation file falls back to the default for every key, so it is harmless - but
        // a file with a subset is a partial translation the user cannot see is incomplete. The
        // template scaffolds a translation as a copy of the default, so the pair is complete.
        var builder = Build(Options() with { Languages = ["de-DE"] });
        var project = PluginProject(builder);

        var result = LocalizationValidator.ValidateProject(new LocalizationDirectory(project));

        Assert.That(
            result.Issues,
            Is.Empty,
            "A complete translation was reported as incomplete: "
            + string.Join("; ", result.Issues.Select(i => $"{i.Code} {i.Message}")));
    }
}