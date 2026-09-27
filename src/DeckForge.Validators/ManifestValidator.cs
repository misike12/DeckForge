using System.Text.Json;
using DeckForge.Core.Utils;

namespace DeckForge.Validators;

/// <summary>
/// Native manifest validator mirroring macrodeck-plugin validate's Development-level rules
/// and the documented manifest constraints, so the editor shows errors as you type - before
/// any CLI run. The CLI remains the authoritative final check.
/// </summary>
public static class ManifestValidator
{
    private static readonly string[] KnownPermissions =
    [
        "host:variables", "host:user-variables", "host:config", "host:deck", "host:scripts",
        "host:widgets", "host:notifications", "host:action-interactions", "host:devices",
        "host:variable-values", "host:layouts", "host:folder-views", "host:widget-types",
        "host:event-bindings", "host:screensavers", "host:adb", "host:messaging",
        "events:publish", "assets:upload", "net:outbound", "fs:user-files", "process:spawn", "device:usb",
    ];

    private static readonly string[] KnownRids =
    [
        "win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64",
        "linux-musl-x64", "linux-musl-arm64",
    ];

    public static ValidationResult Validate(string manifestJson)
    {
        var result = new ValidationResult();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException ex)
        {
            result.Add("json", ValidationSeverity.Error, $"manifest.json is not valid JSON: {ex.Message}");
            return result;
        }
        using (doc)
        {
            ValidateDocument(doc.RootElement, result);
        }
        return result;
    }

    private static void ValidateDocument(JsonElement root, ValidationResult result)
    {

        if (root.ValueKind != JsonValueKind.Object)
        {
            result.Add("malformed", ValidationSeverity.Error, "Manifest root must be a JSON object.");
            return;
        }

        // manifestVersion: runtime required, must be exactly 1.
        if (!TryInt(root, "manifestVersion", out var manifestVersion))
        {
            result.Add("invalid-manifest-version", ValidationSeverity.Error, "manifestVersion must be the number 1.");
        }
        else if (manifestVersion != 1)
        {
            result.Add("unsupported-manifest-version", ValidationSeverity.Error, $"manifestVersion {manifestVersion} is not supported (expected 1).");
        }

        // id: reverse-domain pattern, max 128 chars.
        if (TryString(root, "id", out var id))
        {
            if (!MacroDeckRules.IsValidPluginId(id))
            {
                result.Add("invalid-id", ValidationSeverity.Error, $"'{id}' is not a valid reverse-domain id (com.example.my-plugin).", "id");
            }
            if (id.Length > 128)
            {
                result.Add("invalid-id", ValidationSeverity.Error, "Plugin id exceeds 128 characters.", "id");
            }
        }
        else
        {
            result.Add("missing-id", ValidationSeverity.Error, "id is required.", "id");
        }

        // name: 1-128 chars, no control characters.
        if (TryString(root, "name", out var name))
        {
            if (name.Length == 0 || name.Length > 128)
            {
                result.Add("invalid-name", ValidationSeverity.Error, "name must be 1-128 characters.", "name");
            }
            if (name.Any(char.IsControl))
            {
                result.Add("invalid-name", ValidationSeverity.Error, "name must not contain control characters.", "name");
            }
        }
        else
        {
            result.Add("missing-name", ValidationSeverity.Error, "name is required.", "name");
        }

        // version: SemVer 2.0.
        if (TryString(root, "version", out var version))
        {
            if (!MacroDeckRules.IsValidSemVer(version))
            {
                result.Add("invalid-version", ValidationSeverity.Error, $"'{version}' is not a valid SemVer 2.0 version.", "version");
            }
        }
        else
        {
            result.Add("missing-version", ValidationSeverity.Error, "version is required.", "version");
        }

        // entrypoints: at least one, each executable a relative path inside the package.
        if (root.TryGetProperty("entrypoints", out var entrypoints) && entrypoints.ValueKind == JsonValueKind.Object)
        {
            if (entrypoints.EnumerateObject().Count() == 0)
            {
                result.Add("missing-entrypoint", ValidationSeverity.Error, "At least one entrypoint is required.", "entrypoints");
            }
            foreach (var ep in entrypoints.EnumerateObject())
            {
                if (!KnownRids.Contains(ep.Name))
                {
                    result.Add("unknown-rid", ValidationSeverity.Warning, $"Entrypoint '{ep.Name}' is not a known runtime identifier (still installs on matching hosts).", "entrypoints");
                }
                if (ep.Value.ValueKind == JsonValueKind.Object && ep.Value.TryGetProperty("executable", out var exe))
                {
                    var exePath = exe.GetString() ?? "";
                    var ext = Path.GetExtension(exePath);
                    if (ext is ".sh" or ".bat" or ".cmd" or ".ps1" or ".command")
                    {
                        result.Add("entrypoint-script", ValidationSeverity.Error, $"Entrypoint '{ep.Name}' uses a script ({exePath}); scripts are rejected.", "entrypoints");
                    }
                    if (exePath.Contains("..") || Path.IsPathRooted(exePath))
                    {
                        result.Add("entrypoint-layout-invalid", ValidationSeverity.Error, $"Entrypoint executable '{exePath}' must be relative and inside the package.", "entrypoints");
                    }
                    var runtimeKind = ep.Value.TryGetProperty("runtime", out var rt) && rt.TryGetProperty("kind", out var kind)
                        ? kind.GetString()
                        : null;
                    var isSelfContained = runtimeKind is null
                        || string.Equals(runtimeKind, "SelfContained", StringComparison.OrdinalIgnoreCase);
                    if (isSelfContained && string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add("entrypoint-layout-invalid", ValidationSeverity.Error, $"Self-contained entrypoint '{ep.Name}' must launch an executable, not a .dll.", "entrypoints");
                    }
                    if (string.Equals(runtimeKind, "FrameworkDependent", StringComparison.OrdinalIgnoreCase) && ext != ".dll")
                    {
                        result.Add("entrypoint-layout-invalid", ValidationSeverity.Error, $"FrameworkDependent entrypoint '{ep.Name}' must end in .dll.", "entrypoints");
                    }
                    if (runtimeKind is null && string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add("entrypoint-layout-invalid", ValidationSeverity.Warning, $"Entrypoint '{ep.Name}' ends in .dll but declares no runtime kind; omitting runtime means self-contained.", "entrypoints");
                    }
                }
                else
                {
                    result.Add("missing-entrypoint", ValidationSeverity.Error, $"Entrypoint '{ep.Name}' needs an executable.", "entrypoints");
                }
            }
        }
        else
        {
            result.Add("missing-entrypoint", ValidationSeverity.Error, "entrypoints is required.", "entrypoints");
        }

        // Publication metadata (publication-required, tooling-side).
        if (!TryString(root, "description", out var description) || string.IsNullOrWhiteSpace(description))
        {
            result.Add("description-missing", ValidationSeverity.Warning, "description is required to publish to the Store.", "description");
        }
        if (!root.TryGetProperty("publisher", out var publisher) || publisher.ValueKind != JsonValueKind.Object
            || !publisher.TryGetProperty("name", out var pubName) || string.IsNullOrWhiteSpace(pubName.GetString()))
        {
            result.Add("publisher-missing", ValidationSeverity.Warning, "publisher.name is required to publish to the Store.", "publisher");
        }
        if (!TryString(root, "license", out var license) || string.IsNullOrWhiteSpace(license))
        {
            result.Add("license-missing", ValidationSeverity.Warning, "license (SPDX id, e.g. MIT) is required to publish.", "license");
        }
        if (!TryString(root, "repository", out var repository) || string.IsNullOrWhiteSpace(repository))
        {
            result.Add("repository-missing", ValidationSeverity.Warning, "repository is required to publish to the Store.", "repository");
        }
        else if (repository.StartsWith("https://github.com/example/", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("publication-metadata-missing", ValidationSeverity.Warning, "repository is still the template placeholder; the Store refuses it.", "repository");
        }

        if (!TryString(root, "icon", out var icon) || string.IsNullOrWhiteSpace(icon))
        if (!root.TryGetProperty("compatibility", out var compat) || compat.ValueKind != JsonValueKind.Object
            || compat.EnumerateObject().IsEmpty())
        {
            result.Add("compatibility-missing", ValidationSeverity.Warning, "compatibility should declare at least one member (sdk, protocol or macroDeck).", "compatibility");
        }

        // permissions: unique; unknown values warn (never error).
        if (root.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in perms.EnumerateArray())
            {
                var perm = p.GetString() ?? "";
                if (!seen.Add(perm))
                {
                    result.Add("duplicate-permission", ValidationSeverity.Error, $"Permission '{perm}' is listed twice; duplicates are rejected.", "permissions");
                }
                if (!KnownPermissions.Contains(perm))
                {
                    result.Add("unknown-permission", ValidationSeverity.Warning, $"Permission '{perm}' is not in the known vocabulary (still installs).", "permissions");
                }
            }
        }

        // languages: unique BCP-47-ish tags, case-insensitively unique.
        if (root.TryGetProperty("languages", out var langs) && langs.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in langs.EnumerateArray())
            {
                var tag = l.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(tag))
                {
                    result.Add("invalid-language", ValidationSeverity.Error, "languages must not contain blank tags.", "languages");
                }
                else if (!MacroDeckRules.IsValidLanguageTag(tag))
                {
                    result.Add("invalid-language", ValidationSeverity.Warning, $"'{tag}' does not look like a well-formed BCP-47 tag.", "languages");
                }
                else if (!seen.Add(tag))
                {
                    result.Add("duplicate-language", ValidationSeverity.Error, $"Language '{tag}' is listed twice (case-insensitive).", "languages");
                }
            }
        }

        // ai: shape checks mirroring the manifest reference.
        if (root.TryGetProperty("ai", out var ai) && ai.ValueKind == JsonValueKind.Object)
        {
            if (ai.TryGetProperty("services", out var services))
            {
                if (services.ValueKind != JsonValueKind.Array)
                {
                    result.Add("invalid-ai", ValidationSeverity.Warning, "ai.services must be an array; the whole declaration reads as not declared otherwise.", "ai");
                }
                else
                {
                    var names = services.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => s.Length > 0).ToList();
                    if (names.Count > 16)
                    {
                        result.Add("invalid-ai", ValidationSeverity.Warning, "ai.services lists more than 16 service names.", "ai");
                    }
                    if (names.Any(n => n.Length > 64))
                    {
                        result.Add("invalid-ai", ValidationSeverity.Warning, "ai.service names must be at most 64 characters.", "ai");
                    }
                }
            }
        }
    }

    private static bool TryString(JsonElement e, string name, out string value)
    {
        value = "";
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return true;
        }
        return false;
    }

    private static string? TryGetString(JsonElement e, string name) =>
        TryString(e, name, out var v) ? v : null;

    private static bool TryInt(JsonElement e, string name, out int value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value);
    }
}

file static class EnumerableExtensions
{
    public static bool IsEmpty<T>(this IEnumerable<T> source) => !source.Any();
}
