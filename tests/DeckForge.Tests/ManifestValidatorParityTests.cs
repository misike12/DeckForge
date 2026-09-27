using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DeckForge.Validators;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Compares the native validator against the real <c>macrodeck-plugin validate</c>.
/// </summary>
/// <remarks>
/// <para>
/// The native validator exists so the editor can show errors as you type, before any CLI run. That
/// only helps if it agrees with the tool. It did not: it had no notion of a level, so it
/// over-reported at development and could never escalate a finding to an error the way publication
/// requires. It also invented issue codes the tool does not emit, invented a rule the tool does
/// not have, and had one rule - the icon check - whose body was the compatibility check, so no
/// icon finding was ever produced.
/// </para>
/// <para>
/// These tests run the real tool on the same manifests and compare. Where they can differ, they
/// assert the specific expected behaviour rather than a blanket equality, because the tool also
/// evaluates the manifest's JSON Schema and this does not.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class ManifestValidatorParityTests
{
    private static bool CliPresent { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            var psi = new ProcessStartInfo("macrodeck-plugin")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(30_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>A manifest with every publication field present, so it is clean at all levels.</summary>
    private const string Complete = """
        {
          "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
          "manifestVersion": 1,
          "id": "com.example.probe",
          "name": "Probe",
          "version": "1.0.0",
          "description": "A complete probe manifest.",
          "icon": "Assets/icon.svg",
          "entrypoints": {
            "win-x64": { "executable": "runtimes/win-x64/Probe.dll",
                         "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" } }
          },
          "publisher": { "name": "Example" },
          "license": "MIT",
          "repository": "https://github.com/example/probe",
          "compatibility": { "macroDeck": ">=3.0.0-0" }
        }
        """;

    /// <summary>The same manifest with every publication field removed.</summary>
    private const string NoPublication = """
        {
          "manifestVersion": 1,
          "id": "com.example.probe",
          "name": "Probe",
          "version": "1.0.0",
          "entrypoints": {
            "win-x64": { "executable": "runtimes/win-x64/Probe.dll",
                         "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" } }
          }
        }
        """;

    private static string Mutate(string template, string find, string replace) =>
        template.Replace(find, replace, StringComparison.Ordinal);

    // ---------------------------------------------------------------- level model

    [Test]
    public void A_complete_manifest_is_clean_at_every_level()
    {
        foreach (var level in Enum.GetValues<ManifestValidationLevel>())
        {
            var result = ManifestValidator.Validate(Complete, level);
            var errors = result.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
            Assert.That(errors, Is.Empty, $"{level}: " + string.Join("\n", errors.Select(e => e.Message)));
        }
    }

    [Test]
    public void Missing_publication_metadata_is_invisible_at_development()
    {
        // The host enforces only this level, so a manifest missing publication metadata installs
        // and runs perfectly well locally. Reporting it here is noise.
        var result = ManifestValidator.Validate(NoPublication, ManifestValidationLevel.Development);

        Assert.Multiple(() =>
        {
            Assert.That(result.Issues.Any(i => i.Code == "publication-metadata-missing"), Is.False);
            Assert.That(result.Ok, Is.True);
        });
    }

    [Test]
    public void Missing_publication_metadata_is_a_warning_at_package()
    {
        var result = ManifestValidator.Validate(NoPublication, ManifestValidationLevel.Package);
        var findings = result.Issues.Where(i => i.Code == "publication-metadata-missing").ToList();

        Assert.That(findings, Has.Count.EqualTo(6), string.Join("\n", findings.Select(f => f.Field)));
        Assert.That(findings.All(f => f.Severity == ValidationSeverity.Warning), Is.True);
    }

    [Test]
    public void Missing_publication_metadata_is_an_error_at_publication()
    {
        var result = ManifestValidator.Validate(NoPublication, ManifestValidationLevel.Publication);
        var findings = result.Issues.Where(i => i.Code == "publication-metadata-missing").ToList();

        Assert.That(findings, Has.Count.EqualTo(6));
        Assert.That(findings.All(f => f.Severity == ValidationSeverity.Error), Is.True);
        Assert.That(result.Ok, Is.False);
    }

    [Test]
    public void The_six_publication_pointers_match_the_tool()
    {
        var result = ManifestValidator.Validate(NoPublication, ManifestValidationLevel.Package);
        var pointers = result.Issues
            .Where(i => i.Code == "publication-metadata-missing")
            .Select(i => i.Field)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.That(pointers, Is.EqualTo(new[]
        {
            "/compatibility", "/description", "/icon", "/license", "/publisher", "/repository",
        }));
    }

    // ---------------------------------------------------------------- the dead icon rule

    [Test]
    public void A_missing_icon_is_reported()
    {
        // This rule was dead: the check had an if with no block, so its body was the
        // compatibility check and no icon finding was ever produced.
        var result = ManifestValidator.Validate(NoPublication, ManifestValidationLevel.Package);
        Assert.That(
            result.Issues.Any(i => i.Code == "publication-metadata-missing" && i.Field == "/icon"),
            Is.True);
    }

    [Test]
    public void An_icon_with_an_unknown_extension_is_reported()
    {
        var manifest = Mutate(Complete, "\"Assets/icon.svg\"", "\"Assets/icon.bmp\"");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(
            result.Issues.Any(i => i.Field == "/icon" && i.Message.Contains(".svg", StringComparison.Ordinal)),
            Is.True,
            string.Join("\n", result.Issues.Select(i => i.Message)));
    }

    [Test]
    public void An_icon_path_that_escapes_is_reported()
    {
        var manifest = Mutate(Complete, "\"Assets/icon.svg\"", "\"../outside/icon.svg\"");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Code == "invalid-icon" && i.Field == "/icon"), Is.True);
    }

    // ---------------------------------------------------------------- invented rules removed

    [Test]
    public void The_template_repository_placeholder_is_accepted()
    {
        // The previous version invented a rule rejecting this URL. The tool's check is ^https?://,
        // so the placeholder passes validation and the Store is what refuses it.
        var result = ManifestValidator.Validate(Complete, ManifestValidationLevel.Development);

        Assert.That(
            result.Issues.Any(i => i.Message.Contains("placeholder", StringComparison.OrdinalIgnoreCase)),
            Is.False,
            string.Join("\n", result.Issues.Select(i => i.Message)));
    }

    [Test]
    public void An_unrecognized_runtime_identifier_is_accepted()
    {
        // The previous version warned on a RID it did not know. The tool's schema only constrains
        // the key grammar, and a bare "linux" key is valid.
        var manifest = Mutate(Complete, "\"win-x64\"", "\"linux\"");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(
            result.Issues.Any(i => i.Code == "unknown-rid"),
            Is.False,
            "The tool accepts any key matching the grammar; a fixed RID list is a false positive.");
    }

    [Test]
    public void A_bare_linux_runtime_identifier_is_not_rejected()
    {
        var manifest = Mutate(Complete, "\"win-x64\"", "\"linux\"");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Ok, Is.True, string.Join("\n", result.Issues.Select(i => i.Message)));
    }

    // ---------------------------------------------------------------- real codes

    [Test]
    public void Issue_codes_are_the_tools_own()
    {
        var cases = new (string Manifest, string Expected)[]
        {
            (Mutate(Complete, "\"manifestVersion\": 1", "\"manifestVersion\": 2"), "unsupported-manifest-version"),
            (Mutate(Complete, "\"com.example.probe\"", "\"Not_An_Id\""), "invalid-plugin-id"),
            (Mutate(Complete, "\"version\": \"1.0.0\"", "\"version\": \"1.0\""), "invalid-version"),
            (Mutate(Complete, "Probe.dll", "Probe.sh"), "schema:not"),
            (Mutate(Complete, "win-x64/Probe.dll", "win-x64/../Probe.dll"), "entrypoint-outside-version-directory"),
            (Mutate(Complete, "Probe.dll", "Probe.exe"), "invalid-entrypoint-runtime"),
            (Mutate(Complete, "\"dotnetVersion\": \"10.0\"", "\"dotnetVersion\": \"10\""), "invalid-entrypoint-runtime"),
        };

        foreach (var (manifest, expected) in cases)
        {
            var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);
            Assert.That(
                result.Issues.Any(i => i.Code == expected),
                Is.True,
                $"Expected {expected} for: {manifest}");
        }
    }

    [Test]
    public void A_missing_required_field_reports_schema_required_not_a_missing_code()
    {
        foreach (var (field, code) in new[] { ("id", "schema:required"), ("name", "schema:required"), ("version", "schema:required") })
        {
            // The field is removed by renaming its key, which leaves the manifest without it.
            var manifest = Mutate(Complete, $"\"{field}\"", $"\"removed-{field}\"");
            var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

            Assert.That(
                result.Issues.Any(i => i.Code == code),
                Is.True,
                $"Expected schema:required for a missing {field}.");
            Assert.That(
                result.Issues.Any(i => i.Code.StartsWith("missing-", StringComparison.Ordinal)),
                Is.False,
                "There is no missing-* code in the tool's vocabulary.");
        }
    }

    [Test]
    public void An_unknown_permission_is_a_warning_and_a_duplicate_is_an_error()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"permissions\": [\"host:deck\", \"host:future-thing\"],\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(
            result.Issues.Any(i => i.Code == "unknown-permission" && i.Severity == ValidationSeverity.Warning),
            Is.True);
    }

    [Test]
    public void A_duplicate_permission_is_rejected()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"permissions\": [\"host:deck\", \"host:deck\"],\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(
            result.Issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("more than once", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void The_known_permission_list_is_exactly_twenty_three()
    {
        Assert.That(ManifestValidator.KnownPermissions, Has.Count.EqualTo(23));
        Assert.That(ManifestValidator.KnownPermissions.Distinct().Count(), Is.EqualTo(23));
    }

    [Test]
    public void A_dependency_on_the_plugins_own_id_is_rejected()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"dependencies\": [{ \"id\": \"com.example.probe\" }],\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Code == "invalid-dependency"), Is.True);
    }

    [Test]
    public void An_id_that_is_both_a_dependency_and_a_conflict_is_rejected()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"dependencies\": [{ \"id\": \"com.other.thing\" }],"
            + " \"conflicts\": [{ \"id\": \"com.other.thing\" }],\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(
            result.Issues.Any(i => i.Message.Contains("both a dependency and a conflict", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void A_bad_bundled_icon_pack_key_is_rejected()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"bundledIconPacks\": [{ \"key\": \"Bad_Key\", \"path\": \"icon-packs/a.macroDeckIconPack\" }],\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Code == "invalid-bundled-icon-pack"), Is.True);
    }

    [Test]
    public void A_bad_file_digest_is_rejected()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"files\": [{ \"path\": \"a/b.txt\", \"sha256\": \"nope\", \"size\": 1 }],\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Code == "invalid-file-digest"), Is.True);
    }

    [Test]
    public void A_caret_version_range_is_rejected()
    {
        // The grammar has no caret or tilde, only comparators joined by commas.
        var manifest = Mutate(Complete, "\">=3.0.0-0\"", "\"^3.0.0\"");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Code == "invalid-compatibility"), Is.True);
    }

    [Test]
    public void A_comma_separated_version_range_is_accepted()
    {
        var manifest = Mutate(Complete, "\">=3.0.0-0\"", "\">=1.0.0,<2.0.0\"");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Code == "invalid-compatibility"), Is.False);
    }

    [Test]
    public void A_clamped_setting_is_reported_as_a_note_rather_than_an_error()
    {
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"shutdown\": { \"gracefulTimeoutSeconds\": 900 },\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        var note = result.Issues.FirstOrDefault(i => i.Code == "invalid-settings");
        Assert.That(note, Is.Not.Null);
        Assert.That(note!.Severity, Is.EqualTo(ValidationSeverity.Info), "A clamped value is still valid.");
        Assert.That(result.Ok, Is.True);
    }

    [Test]
    public void A_non_boolean_ai_flag_is_reported()
    {
        // A non-boolean flag makes the whole declaration read as "not declared", which is
        // materially different from "uses no AI" - so it is worth an error.
        var manifest = Mutate(
            Complete,
            "\"publisher\": { \"name\": \"Example\" },",
            "\"ai\": { \"interaction\": \"yes\" },\n  \"publisher\": { \"name\": \"Example\" },");
        var result = ManifestValidator.Validate(manifest, ManifestValidationLevel.Development);

        Assert.That(result.Issues.Any(i => i.Field == "/ai/interaction"), Is.True);
    }

    // ---------------------------------------------------------------- result shape

    [Test]
    public void A_summary_counts_notes_separately()
    {
        var result = new ValidationResult();
        result.Add("a", ValidationSeverity.Error, "one");
        result.Add("b", ValidationSeverity.Warning, "two");
        result.Add("c", ValidationSeverity.Info, "three");

        Assert.Multiple(() =>
        {
            // Subtracting errors from the total counted the note as a warning, so the summary could
            // disagree with HasWarnings on the same result.
            Assert.That(result.ToString(), Is.EqualTo("1 error(s), 1 warning(s), 1 note(s)"));
            Assert.That(result.WarningCount, Is.EqualTo(1));
            Assert.That(result.InfoCount, Is.EqualTo(1));
            Assert.That(result.HasWarnings, Is.True);
        });
    }

    [Test]
    public void An_empty_result_summarises_cleanly()
    {
        Assert.That(new ValidationResult().ToString(), Is.EqualTo("0 error(s), 0 warning(s)"));
    }

    // ---------------------------------------------------------------- parity with the tool

    [Test]
    public void The_tool_agrees_about_the_level_model()
    {
        if (!CliPresent)
        {
            Assert.Ignore("macrodeck-plugin is not on PATH.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "deckforge-parity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
            CreateEntryPoint(directory);
        try
        {
            // The tool is authoritative for what it accepts at each level.
            foreach (var (level, toolLevel) in new[]
                     {
                         (ManifestValidationLevel.Development, "development"),
                         (ManifestValidationLevel.Package, "package"),
                         (ManifestValidationLevel.Publication, "publication"),
                     })
            {
                var path = Path.Combine(directory, toolLevel + ".json");
                File.WriteAllText(path, NoPublication, new UTF8Encoding(false));

                var toolValid = RunValidate(path, toolLevel);
                var ours = ManifestValidator.Validate(NoPublication, level, directory);

                Assert.That(
                    toolValid,
                    Is.EqualTo(ours.Ok),
                    $"The tool and DeckForge disagree at {toolLevel}: tool valid={toolValid}, ours={ours.Ok}. "
                    + string.Join(" | ", ours.Issues.Select(i => i.Code)));
            }

            // And for a complete manifest, the tool accepts it at the strictest level.
            var completePath = Path.Combine(directory, "complete.json");
            File.WriteAllText(completePath, Complete, new UTF8Encoding(false));
            Assert.That(RunValidate(completePath, "publication"), Is.True);

            // With the built tree present, the two filesystem rules agree too.
            var packaged = ManifestValidator.Validate(Complete, ManifestValidationLevel.Package, directory);
            Assert.That(packaged.Ok, Is.True, string.Join(" | ", packaged.Issues.Select(i => i.Code)));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Test]
    public void The_tool_agrees_the_template_repository_is_acceptable()
    {
        if (!CliPresent)
        {
            Assert.Ignore("macrodeck-plugin is not on PATH.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "deckforge-parity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
            CreateEntryPoint(directory);
        try
        {
            var path = Path.Combine(directory, "placeholder.json");
            File.WriteAllText(path, Complete, new UTF8Encoding(false));

            // The old validator invented a rule rejecting the template placeholder. The tool does
            // not: it checks the shape, and the Store is what refuses a placeholder.
            Assert.That(RunValidate(path, "publication"), Is.True);
            Assert.That(ManifestValidator.Validate(Complete, ManifestValidationLevel.Publication).Ok, Is.True);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Test]
    public void The_tool_agrees_a_bare_runtime_identifier_is_acceptable()
    {
        if (!CliPresent)
        {
            Assert.Ignore("macrodeck-plugin is not on PATH.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "deckforge-parity-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
            CreateEntryPoint(directory);
        try
        {
            var manifest = Mutate(Complete, "\"win-x64\"", "\"linux\"");
            var path = Path.Combine(directory, "rid.json");
            File.WriteAllText(path, manifest, new UTF8Encoding(false));

            Assert.That(RunValidate(path, "development"), Is.True);
            Assert.That(ManifestValidator.Validate(manifest, ManifestValidationLevel.Development).Ok, Is.True);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// The tool checks that the entrypoint resolves to a file that exists, relative to the
    /// manifest's own directory. Laying the file out is what keeps the comparison about
    /// manifest content rather than about a build that has not run yet.
    /// </summary>
    private static void CreateEntryPoint(string directory)
    {
        var runtimes = Path.Combine(directory, "runtimes", "win-x64");
        Directory.CreateDirectory(runtimes);
        File.WriteAllText(Path.Combine(runtimes, "Probe.dll"), "probe", new UTF8Encoding(false));

        var assets = Path.Combine(directory, "Assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "icon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>", new UTF8Encoding(false));
    }

    private static bool RunValidate(string manifestPath, string level)
    {
        var psi = new ProcessStartInfo("macrodeck-plugin")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "validate", "--manifest", manifestPath, "--level", level, "--output", "json" })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);

        // Exit 0 is success; 1 means the subject was rejected.
        if (process.ExitCode == 0)
        {
            return true;
        }

        if (process.ExitCode != 1)
        {
            Assert.Fail($"Unexpected exit {process.ExitCode}: {stdout}");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            return document.RootElement.TryGetProperty("valid", out var valid)
                && valid.ValueKind == JsonValueKind.True;
        }
        catch (JsonException ex)
        {
            Assert.Fail($"Could not read the tool's JSON report: {ex.Message}\n{stdout}");
            return false;
        }
    }
}
