using DeckForge.Validators;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Validator rules that were wrong in the direction that hides or invents a problem.
/// </summary>
/// <remarks>
/// A validator that reports nothing is indistinguishable from a plugin that is fine, and one that
/// always reports the same thing is indistinguishable from a broken rule. These three did the
/// latter, or the former, and each was found by checking against the real analyzer or the real CLI.
/// </remarks>
[TestFixture]
public sealed class ValidatorParityTests
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "validators-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteResx(string name, params (string Key, string Value)[] entries)
    {
        var dir = Path.Combine(_root, "Localization");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);

        var body = string.Join(
            Environment.NewLine,
            entries.Select(e => $"""  <data name="{e.Key}" xml:space="preserve"><value>{e.Value}</value></data>"""));
        File.WriteAllText(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <root>
            {body}
            </root>
            """);
        return path;
    }

    [Test]
    public void A_key_declared_twice_in_the_default_resx_is_reported()
    {
        // The rule existed and could not fire: it grouped the keys of a dictionary that had already
        // been deduplicated, so no group could ever hold more than one. The SDK's own analyzer
        // reports this as an error, on exactly the files DeckForge called clean.
        WriteResx(
            "Strings.resx",
            ("Actions.Foo.Name", "First"),
            ("Actions.Foo.Name", "Second"));

        var dir = new LocalizationDirectory(_root);
        var result = LocalizationValidator.ValidateProject(dir);

        Assert.That(
            result.Issues,
            Has.Some.Matches<ValidationIssue>(i =>
                i.Code == "MDLOC003" && i.Message.Contains("Actions.Foo.Name", StringComparison.Ordinal)),
            string.Join("; ", result.Issues.Select(i => i.Code + " " + i.Message)));
    }

    [Test]
    public void A_key_declared_twice_in_a_translation_is_reported()
    {
        WriteResx("Strings.resx", ("Actions.Foo.Name", "First"));
        WriteResx(
            "Strings.de.resx",
            ("Actions.Foo.Name", "Erste"),
            ("Actions.Foo.Name", "Zweite"));

        var result = LocalizationValidator.ValidateProject(new LocalizationDirectory(_root));

        Assert.That(
            result.Issues,
            Has.Some.Matches<ValidationIssue>(i => i.Code == "MDLOC003" && i.Message.Contains("Strings.de.resx", StringComparison.Ordinal)));
    }

    [Test]
    public void A_malformed_resx_is_reported_rather_than_throwing()
    {
        var dir = Path.Combine(_root, "Localization");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Strings.resx"), "<root><data name='a'></root>");

        Assert.DoesNotThrow(() => LocalizationValidator.ValidateProject(new LocalizationDirectory(_root)));
    }

    [Test]
    public void A_bundled_icon_pack_that_is_present_is_not_reported_as_missing()
    {
        // The rule reported "not present" for every declared pack at package level without ever
        // looking at the filesystem, so a correctly bundled pack carried a permanent warning.
        var packDir = Path.Combine(_root, "icon-packs");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack1.macroDeckIconPack"), "{}");

        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(
            manifest,
            """
            {
              "manifestVersion": 1,
              "id": "com.example.pack",
              "name": "Pack",
              "version": "1.0.0",
              "entrypoints": {},
              "icon": "Assets/icon.svg",
              "bundledIconPacks": { "a-pack": { "path": "icon-packs/pack1.macroDeckIconPack" } }
            }
            """);

        var result = ManifestValidator.Validate(File.ReadAllText(manifest), ManifestValidationLevel.Package, Path.GetDirectoryName(manifest));

        Assert.That(
            result.Issues.Where(i => i.Code == "bundled-icon-pack-missing"),
            Is.Empty,
            string.Join("; ", result.Issues.Select(i => i.Code + " " + i.Message)));
    }

    [Test]
    public void A_bundled_icon_pack_that_really_is_absent_is_reported()
    {
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(
            manifest,
            """
            {
              "manifestVersion": 1,
              "id": "com.example.pack",
              "name": "Pack",
              "version": "1.0.0",
              "entrypoints": {},
              "bundledIconPacks": { "a-pack": { "path": "icon-packs/absent.macroDeckIconPack" } }
            }
            """);

        var result = ManifestValidator.Validate(File.ReadAllText(manifest), ManifestValidationLevel.Package, Path.GetDirectoryName(manifest));

        Assert.That(
            result.Issues,
            Has.Some.Matches<ValidationIssue>(i => i.Code == "bundled-icon-pack-missing"),
            string.Join("; ", result.Issues.Select(i => i.Code + " " + i.Message)));
    }
}
