using DeckForge.Core.Utils;
using DeckForge.Validators;
using NUnit.Framework;

namespace DeckForge.Tests;

[TestFixture]
public class MacroDeckRulesTests
{
    [TestCase("com.acme.light-control", true)]
    [TestCase("app.macro-deck.template", true)]
    [TestCase("com.example.x2", true)]
    [TestCase("myplugin", false)]
    [TestCase("com.example", true)]
    [TestCase("com.Example.thing", false)]
    [TestCase("com.example.my_plugin", false)]
    [TestCase("-com.example.thing", false)]
    public void Plugin_id_rules(string id, bool valid) =>
        Assert.That(MacroDeckRules.IsValidPluginId(id), Is.EqualTo(valid), id);

    [TestCase("log-message", true)]
    [TestCase("set-brightness", true)]
    [TestCase("Log-Message", false)]
    [TestCase("owner::local", false)]
    [TestCase("", false)]
    public void Local_id_rules(string id, bool valid) =>
        Assert.That(MacroDeckRules.IsValidLocalId(id), Is.EqualTo(valid), id);

    [TestCase("1.0.0", true)]
    [TestCase("1.0.0-beta.1", true)]
    [TestCase("1.0", false)]
    [TestCase("v1.0.0", false)]
    public void Semver_rules(string v, bool valid) =>
        Assert.That(MacroDeckRules.IsValidSemVer(v), Is.EqualTo(valid), v);

    [Test]
    public void Suggests_plugin_id_from_name_and_publisher()
    {
        Assert.That(MacroDeckRules.SuggestPluginId("Acme Light Control", "Acme"),
            Is.EqualTo("com.acme.light-control"));
    }

    [Test]
    public void Suggests_project_name()
    {
        Assert.That(MacroDeckRules.SuggestProjectName("Acme Light Control"), Is.EqualTo("AcmeLightControl"));
    }
}

[TestFixture]
public class ManifestValidatorTests
{
    private const string ValidManifest = """
        {
          "manifestVersion": 1,
          "id": "com.example.hue-lights",
          "name": "Hue Lights",
          "version": "1.0.0",
          "description": "Control Hue lights from Macro Deck.",
          "icon": "Assets/icon.svg",
          "entrypoints": {
            "win-x64": { "executable": "runtimes/win-x64/HueLights.dll", "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" } }
          },
          "publisher": { "name": "Example Publisher" },
          "license": "MIT",
          "repository": "https://github.com/example/hue-lights",
          "compatibility": { "macroDeck": ">=3.0.0-0" }
        }
        """;

    [Test]
    public void Accepts_the_documented_example_manifest()
    {
        var result = ManifestValidator.Validate(ValidManifest);
        var errors = result.Issues.Where(i => i.Severity == ValidationSeverity.Error);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Rejects_bad_manifest_version()
    {
        var result = ManifestValidator.Validate(ValidManifest.Replace("\"manifestVersion\": 1", "\"manifestVersion\": 2"));
        Assert.That(result.Issues.Any(i => i.Code == "unsupported-manifest-version"), Is.True);
    }

    [Test]
    public void Rejects_underscored_id()
    {
        var result = ManifestValidator.Validate(ValidManifest.Replace("com.example.hue-lights", "com.example.hue_lights"));
        Assert.That(result.Issues.Any(i => i.Code == "invalid-plugin-id"), Is.True);
    }

    [Test]
    public void Rejects_script_entrypoint()
    {
        var result = ManifestValidator.Validate(ValidManifest.Replace("runtimes/win-x64/HueLights.dll", "run.sh"));
        Assert.That(result.Issues.Any(i => i.Code == "schema:not"), Is.True);
    }

    [Test]
    public void Warns_on_unknown_permission_and_errors_on_duplicate()
    {
        var withPerms = ValidManifest.Replace(
            "\"compatibility\"",
            "\"permissions\": [\"host:variables\", \"host:variables\", \"not:a:perm\"], \"compatibility\"");
        var result = ManifestValidator.Validate(withPerms);
        Assert.That(result.Issues.Any(i => i.Code == "schema:uniqueItems" && i.Severity == ValidationSeverity.Error), Is.True);
        Assert.That(result.Issues.Any(i => i.Code == "unknown-permission" && i.Severity == ValidationSeverity.Warning), Is.True);
    }

    [Test]
    public void Accepts_the_template_repository_placeholder()
    {
        // An earlier version invented a rule rejecting this URL. Verified against the real tool:
        // it checks the shape, ^https?://, and accepts the placeholder. The Store refuses a
        // placeholder at upload, which is a later gate with different information.
        var result = ManifestValidator.Validate(ValidManifest.Replace(
            "https://github.com/example/hue-lights",
            "https://github.com/example/my-plugin"),
            ManifestValidationLevel.Publication);

        Assert.That(
            result.Issues.Any(i => i.Message.Contains("placeholder", StringComparison.OrdinalIgnoreCase)),
            Is.False,
            string.Join("\n", result.Issues.Select(i => i.Message)));
    }

    [Test]
    public void Flags_framework_dependent_dll_rule()
    {
        var result = ManifestValidator.Validate(ValidManifest.Replace("FrameworkDependent", "SelfContained"));
        Assert.That(result.Issues.Any(i => i.Code == "invalid-entrypoint-runtime"), Is.True);
    }
}

[TestFixture]
public class LocalizationValidatorTests
{
    [Test]
    public void Placeholder_mismatch_is_detected()
    {
        var placeholders = LocalizationValidator.Placeholders("Hello {name}, you have {count} items.");
        Assert.That(placeholders, Is.EqualTo(new[] { "name", "count" }));
        Assert.That(LocalizationValidator.Placeholders("No placeholders here"), Is.Empty);
    }
}
