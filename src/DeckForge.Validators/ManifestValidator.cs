using System.Text.Json;
using System.Text.RegularExpressions;
using DeckForge.Core.Utils;

namespace DeckForge.Validators;

/// <summary>
/// How strictly a manifest is judged. The levels are cumulative.
/// </summary>
/// <remarks>
/// The distinction is not cosmetic. <c>development</c> is the only level the host itself enforces
/// at install, so a manifest missing its publication metadata installs and runs perfectly well
/// locally. <c>package</c> adds the packaged-content checks and reports missing publication
/// metadata as a <em>warning</em>. <c>publication</c> reports the same findings as <em>errors</em>,
/// because at that point they block the Store.
/// </remarks>
public enum ManifestValidationLevel
{
    /// <summary>Every runtime field and the manifest/entrypoint/compatibility shape.</summary>
    Development = 0,

    /// <summary>Adds packaged-content checks. The host never runs this.</summary>
    Package = 1,

    /// <summary>Missing publication metadata becomes an error.</summary>
    Publication = 2,
}

/// <summary>
/// A manifest validator that mirrors the real <c>macrodeck-plugin validate</c>.
/// </summary>
/// <remarks>
/// <para>
/// The issue codes are the tool's own, so the editor and the CLI speak the same language. A
/// previous version invented its own: <c>missing-id</c>, <c>missing-name</c>,
/// <c>duplicate-permission</c>, <c>entrypoint-script</c>, and five separate
/// <c>*-missing</c> codes where the tool emits one <c>publication-metadata-missing</c>. It also
/// invented a rule the tool does not have - rejecting the <c>github.com/example/</c> template
/// placeholder, which the tool's <c>^https?://</c> check accepts - and warned on an unrecognized
/// runtime identifier, which the tool accepts as-is.
/// </para>
/// <para>
/// It had no notion of a level at all, so it over-reported at development and could never
/// escalate a finding to an error the way publication requires. And one rule was dead: the icon
/// check had an <c>if</c> with no block, so its body was the compatibility check, and no icon
/// finding was ever produced.
/// </para>
/// <para>
/// The tool also evaluates the manifest's JSON Schema, which this does not: that is what reports
/// <c>schema:type@/name</c> and <c>schema:pattern@/homepage</c> with an RFC 6901 pointer. The
/// shape and cross-field rules below cover most of the same ground; the schema pass remains the
/// tool's alone, which is why the CLI stays authoritative.
/// </para>
/// </remarks>
public static partial class ManifestValidator
{
    /// <summary>The permission vocabulary, transcribed from the tool's own list.</summary>
    public static IReadOnlyList<string> KnownPermissions { get; } =
    [
        "host:variables", "host:user-variables", "host:config", "host:deck", "host:scripts",
        "host:widgets", "host:notifications", "host:action-interactions", "host:devices",
        "host:variable-values", "host:layouts", "host:folder-views", "host:widget-types",
        "host:event-bindings", "host:screensavers", "host:adb", "host:messaging",
        "events:publish", "assets:upload", "net:outbound", "fs:user-files", "process:spawn", "device:usb",
    ];

    /// <summary>Runtime identifiers the host resolves directly. Others are accepted as written.</summary>
    public static IReadOnlyList<string> KnownRids { get; } =
    [
        "win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64",
        "linux-musl-x64", "linux-musl-arm64",
    ];

    /// <summary>The entrypoint key grammar. Wider than the RID list on purpose.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RidKeyPattern();

    /// <summary>An absolute http or https URL, which is what every URL field requires.</summary>
    [GeneratedRegex(@"^https?://[^\s]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteUrlPattern();

    /// <summary>A <c>major.minor</c> version, which is what a runtime's dotnetVersion must be.</summary>
    [GeneratedRegex(@"^\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex MajorMinorPattern();

    /// <summary>A <c>sha256:&lt;64 lowercase hex&gt;</c> digest.</summary>
    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();

    /// <summary>A <c>bundledIconPacks[].key</c>: lowercase, digits and inner hyphens, at most 64.</summary>
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex BundledKeyPattern();

    /// <summary>
    /// The version-range grammar: a bare version, or comparators joined by commas. No caret, tilde
    /// or wildcards.
    /// </summary>
    /// <remarks>
    /// A bare <c>3.0.0</c> was rejected because every alternative in the pattern required a
    /// comparator. That is the form the real CLI accepts at every level, and the CLI is the
    /// authority here - so DeckForge reported a manifest as invalid that the tool about to consume
    /// it would have loaded without complaint, which is the worst kind of validation error: the
    /// designer refuses to open a working plugin.
    /// </remarks>
    [GeneratedRegex(@"^\s*(\*|([<>]=?|=)\s*\S+(\s*,\s*[<>]=?|=)?|\d+\.\d+(\.\d+)?([-+][0-9A-Za-z.+-]+)?)+\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRangePattern();

    private static readonly string[] StandardLinkTypes =
    [
        "documentation", "wiki", "issues", "support", "community", "donate",
        "privacy", "terms", "changelog", "license", "custom",
    ];

    private static readonly string[] ScriptExtensions = [".sh", ".bat", ".cmd", ".ps1", ".command"];

    private const int MaxNameLength = 128;
    private const int MaxOwnerIdLength = 128;
    private const int MaxBundledIconPacks = 32;
    private const int MaxAiServices = 16;
    private const int MaxAiServiceLength = 64;

    /// <summary>Validates at the level the host itself enforces.</summary>
    public static ValidationResult Validate(string manifestJson) =>
        Validate(manifestJson, ManifestValidationLevel.Development);

    /// <summary>Validates at the given level, against the manifest's text alone.</summary>
    /// <remarks>
    /// This is what the editor uses, because a manifest is edited long before it is built. It
    /// therefore cannot perform the two checks the tool does against the filesystem - that the
    /// entrypoint resolves to a file, and that a declared icon is present. Both are reported by
    /// <c>validate</c> and both appear in <see cref="Package"/>, so a caller that has a built tree
    /// should use <see cref="Validate(string, ManifestValidationLevel, string?)"/> instead.
    /// </remarks>
    public static ValidationResult Validate(string manifestJson, ManifestValidationLevel level) =>
        Validate(manifestJson, level, baseDirectory: null);

    /// <summary>
    /// Validates at the given level, additionally checking the two filesystem-dependent rules when
    /// <paramref name="baseDirectory"/> is the directory the manifest lives in.
    /// </summary>
    public static ValidationResult Validate(string manifestJson, ManifestValidationLevel level, string? baseDirectory)
    {
        var result = new ValidationResult();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException ex)
        {
            result.Add("malformed", ValidationSeverity.Error, $"manifest.json is not valid JSON: {ex.Message}");
            return result;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                result.Add("malformed", ValidationSeverity.Error, "Manifest root must be a JSON object.");
                return result;
            }

            ValidateDocument(doc.RootElement, level, result);
            ValidatePackagedContent(doc.RootElement, level, baseDirectory, result);
        }

        return result;
    }

    /// <summary>
    /// The two rules that need a built tree. The tool emits them at package level, and both are
    /// errors there.
    /// </summary>
    private static void ValidatePackagedContent(
        JsonElement root,
        ManifestValidationLevel level,
        string? baseDirectory,
        ValidationResult result)
    {
        if (level < ManifestValidationLevel.Package || baseDirectory is null)
        {
            return;
        }

        if (TryString(root, "icon", out var icon) && !string.IsNullOrWhiteSpace(icon))
        {
            var path = Path.Combine(baseDirectory, icon.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                result.Add("icon-declared-not-present", ValidationSeverity.Error,
                    $"'{icon}' is declared as 'icon' but is not present in the packaged content.", "/icon");
            }
        }

        if (!root.TryGetProperty("entrypoints", out var entrypoints) || entrypoints.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("bundledIconPacks", out var packs) && packs.ValueKind == JsonValueKind.Object)
        {
            foreach (var pack in packs.EnumerateObject())
            {
                if (pack.Value.ValueKind == JsonValueKind.Object
                    && TryString(pack.Value, "path", out var packPath)
                    && !Path.IsPathRooted(packPath))
                {
                    // Checked against the filesystem, which is the only way to tell a declared pack
                    // from a bundled one. The previous version asserted "not present" for every
                    // pack at package level without looking, so it was wrong in both directions.
                    var resolved = Path.Combine(
                        baseDirectory,
                        packPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(resolved))
                    {
                        result.Add("bundled-icon-pack-missing", ValidationSeverity.Warning,
                            $"Bundled icon pack '{pack.Name}' resolves to '{resolved}', which does not exist.",
                            $"/bundledIconPacks/{pack.Name}/path");
                    }
                }
            }
        }

        foreach (var entry in entrypoints.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object
                || !TryString(entry.Value, "executable", out var executable)
                || Path.IsPathRooted(executable))
            {
                continue;
            }

            var path = Path.Combine(baseDirectory, executable.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                result.Add("entrypoint-missing", ValidationSeverity.Error,
                    $"Entrypoint '{entry.Name}' resolves to '{path}', which does not exist.",
                    $"/entrypoints/{entry.Name}/executable");
            }
        }
    }

    private static void ValidateDocument(JsonElement root, ManifestValidationLevel level, ValidationResult result)
    {
        ValidateManifestVersion(root, result);
        ValidateIdentity(root, result);
        ValidateName(root, result);
        ValidateVersion(root, result);
        ValidateEntrypoints(root, level, result);
        ValidateIcon(root, level, result);
        ValidatePublisherAndLinks(root, result);
        ValidateCompatibility(root, result);
        ValidateRelationships(root, result);
        ValidateBundledIconPacks(root, level, result);
        ValidatePermissions(root, result);
        ValidateLanguages(root, result);
        ValidateFiles(root, result);
        ValidateSignature(root, result);
        ValidateSettings(root, result);
        ValidateAi(root, result);
        ValidatePublicationMetadata(root, level, result);
    }

    private static void ValidateManifestVersion(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("manifestVersion", out var version))
        {
            result.Add("schema:required", ValidationSeverity.Error, "manifestVersion is required.", "/");
            return;
        }

        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var value))
        {
            result.Add("schema:type", ValidationSeverity.Error, "manifestVersion must be the number 1.", "/manifestVersion");
            return;
        }

        if (value != 1)
        {
            result.Add("unsupported-manifest-version", ValidationSeverity.Error,
                $"Manifest version {value} is not supported; only 1 is understood.", "/manifestVersion");
        }
    }

    private static void ValidateIdentity(JsonElement root, ValidationResult result)
    {
        if (!TryString(root, "id", out var id))
        {
            result.Add("schema:required", ValidationSeverity.Error, "id is required.", "/");
            return;
        }

        if (id.Length > MaxOwnerIdLength)
        {
            result.Add("invalid-plugin-id", ValidationSeverity.Error,
                $"'{id}' is not a valid plugin id. It must be at most {MaxOwnerIdLength} characters.", "/id");
            return;
        }

        if (id.Contains("::", StringComparison.Ordinal))
        {
            result.Add("invalid-plugin-id", ValidationSeverity.Error,
                $"'{id}' is not a valid plugin id. It must not contain '::'.", "/id");
            return;
        }

        if (!MacroDeckRules.IsValidPluginId(id))
        {
            result.Add("invalid-plugin-id", ValidationSeverity.Error,
                $"'{id}' is not a valid plugin id. It must be reverse-domain, lowercase and "
                + "hyphen-separated, with at least two segments (e.g. 'com.example.my-plugin').", "/id");
        }
    }

    private static void ValidateName(JsonElement root, ValidationResult result)
    {
        if (!TryString(root, "name", out var name))
        {
            result.Add("schema:required", ValidationSeverity.Error, "name is required.", "/");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            result.Add("invalid-name", ValidationSeverity.Error, "Name must not be empty.", "/name");
            return;
        }

        if (name.Any(char.IsControl))
        {
            result.Add("invalid-name", ValidationSeverity.Error, "Name must not contain control characters.", "/name");
        }

        if (name.Length > MaxNameLength)
        {
            result.Add("invalid-name", ValidationSeverity.Error,
                $"Name exceeds the {MaxNameLength}-character limit.", "/name");
        }
    }

    private static void ValidateVersion(JsonElement root, ValidationResult result)
    {
        if (!TryString(root, "version", out var version))
        {
            result.Add("schema:required", ValidationSeverity.Error, "version is required.", "/");
            return;
        }

        if (!MacroDeckRules.IsValidSemVer(version))
        {
            result.Add("invalid-version", ValidationSeverity.Error,
                $"'{version}' is not a valid SemVer 2.0 version.", "/version");
            return;
        }

        // A version becomes a directory name, so a separator in it would escape the install tree.
        if (version.Contains('/') || version.Contains('\\') || version is "." or "..")
        {
            result.Add("version-mismatch", ValidationSeverity.Error,
                $"'{version}' is not usable as a directory name.", "/version");
        }
    }

    private static void ValidateEntrypoints(JsonElement root, ManifestValidationLevel level, ValidationResult result)
    {
        if (!root.TryGetProperty("entrypoints", out var entrypoints) || entrypoints.ValueKind != JsonValueKind.Object)
        {
            result.Add("schema:required", ValidationSeverity.Error, "entrypoints is required.", "/");
            return;
        }

        var declared = entrypoints.EnumerateObject().ToList();
        if (declared.Count == 0)
        {
            result.Add("no-entrypoints", ValidationSeverity.Error, "Manifest declares no entrypoints.", "/entrypoints");
            return;
        }

        foreach (var property in declared)
        {
            var rid = property.Name;
            var entry = property.Value;

            if (!RidKeyPattern().IsMatch(rid))
            {
                result.Add("schema:propertyNames", ValidationSeverity.Error,
                    $"Entrypoint key '{rid}' may only contain letters, digits, dots and hyphens.",
                    $"/entrypoints/{rid}");
            }

            if (entry.ValueKind != JsonValueKind.Object)
            {
                result.Add("entrypoint-missing", ValidationSeverity.Error,
                    $"Entrypoint '{rid}' needs an executable.", $"/entrypoints/{rid}");
                continue;
            }

            if (!TryString(entry, "executable", out var executable))
            {
                result.Add("entrypoint-missing", ValidationSeverity.Error,
                    $"Entrypoint '{rid}' needs an executable.", $"/entrypoints/{rid}/executable");
                continue;
            }

            ValidateEntrypointPath(rid, executable, result);
            ValidateEntrypointRuntime(rid, entry, executable, result);

            if (entry.TryGetProperty("arguments", out var arguments) && arguments.ValueKind != JsonValueKind.Array)
            {
                result.Add("schema:type", ValidationSeverity.Error,
                    $"Entrypoint '{rid}' arguments must be an array of strings.", $"/entrypoints/{rid}/arguments");
            }

            _ = level;
        }
    }

    private static void ValidateEntrypointPath(string rid, string executable, ValidationResult result)
    {
        // Path safety is checked for every runtime on every platform: a manifest with an escaping
        // path is invalid everywhere, even though only the host's own RID is ever executed here.
        if (Path.IsPathRooted(executable))
        {
            result.Add("entrypoint-outside-version-directory", ValidationSeverity.Error,
                $"Entrypoint '{rid}' executable '{executable}' is not a safe relative path.",
                $"/entrypoints/{rid}/executable");
            return;
        }

        var segments = executable.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
        {
            result.Add("entrypoint-outside-version-directory", ValidationSeverity.Error,
                $"Entrypoint '{rid}' executable '{executable}' escapes the version directory.",
                $"/entrypoints/{rid}/executable");
        }
    }

    private static void ValidateEntrypointRuntime(string rid, JsonElement entry, string executable, ValidationResult result)
    {
        var pointer = $"/entrypoints/{rid}/executable";
        var extension = Path.GetExtension(executable);

        if (ScriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            result.Add("schema:not", ValidationSeverity.Error,
                $"Entrypoint '{rid}' executable '{executable}' looks like a script, which the "
                + "artifact format forbids.", pointer);
            return;
        }

        // An absent runtime block means self-contained, so a script entrypoint is an install
        // script in disguise and is rejected whether or not the block is present.
        string? kind = null;
        string? dotnetVersion = null;
        if (entry.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
        {
            if (TryString(runtime, "kind", out var k))
            {
                kind = k;
            }

            dotnetVersion = TryGetString(runtime, "dotnetVersion");
        }

        if (string.Equals(kind, "FrameworkDependent", StringComparison.OrdinalIgnoreCase))
        {
            if (dotnetVersion is null || !MajorMinorPattern().IsMatch(dotnetVersion))
            {
                result.Add("invalid-entrypoint-runtime", ValidationSeverity.Error,
                    $"Entrypoint '{rid}' has an invalid Runtime.DotnetVersion '{dotnetVersion}'; expected 'major.minor'.",
                    $"/entrypoints/{rid}/runtime/dotnetVersion");
            }

            if (!executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                result.Add("invalid-entrypoint-runtime", ValidationSeverity.Error,
                    $"Entrypoint '{rid}' is framework-dependent but its executable does not end in '.dll'.",
                    pointer);
            }

            return;
        }

        if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("invalid-entrypoint-runtime", ValidationSeverity.Error,
                $"Entrypoint '{rid}' is self-contained but its executable ends in '.dll'.", pointer);
        }
    }

    private static void ValidateIcon(JsonElement root, ManifestValidationLevel level, ValidationResult result)
    {
        // The previous version's icon rule had an if with no block, so its body was the
        // compatibility check and no icon finding was ever produced.
        if (!TryString(root, "icon", out var icon) || string.IsNullOrWhiteSpace(icon))
        {
            return;
        }

        if (Path.IsPathRooted(icon) || icon.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(s => s == ".."))
        {
            result.Add("invalid-icon", ValidationSeverity.Error,
                $"Icon path '{icon}' is not a safe relative path.", "/icon");
            return;
        }

        // Existence is never checked here: the reader gates every supervisor launch, so a
        // stripped icon must not be able to stop a working plugin.
        if (icon.Contains('\\', StringComparison.Ordinal))
        {
            result.Add("invalid-icon", ValidationSeverity.Error,
                $"Icon path '{icon}' must use forward slashes.", "/icon");
        }

        var extension = Path.GetExtension(icon);
        if (extension is not (".svg" or ".png" or ".jpg" or ".jpeg" or ".webp"))
        {
            result.Add("schema:enum", ValidationSeverity.Error,
                $"Icon '{icon}' has no known media type. Supported extensions are: "
                + ".svg, .png, .jpg, .jpeg, .webp.", "/icon");
        }

        _ = level;
    }

    private static void ValidatePublisherAndLinks(JsonElement root, ValidationResult result)
    {
        if (root.TryGetProperty("publisher", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
        {
            if (!TryString(publisher, "name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                result.Add("invalid-publisher", ValidationSeverity.Error,
                    "Publisher.Name must not be empty.", "/publisher/name");
            }

            if (TryString(publisher, "id", out var id) && !MacroDeckRules.IsValidPluginId(id))
            {
                result.Add("invalid-publisher", ValidationSeverity.Error,
                    $"Publisher id '{id}' is not a valid plugin id.", "/publisher/id");
            }

            if (TryString(publisher, "url", out var url) && !AbsoluteUrlPattern().IsMatch(url))
            {
                result.Add("invalid-publisher", ValidationSeverity.Error,
                    $"Publisher url '{url}' must be an absolute http or https URL.", "/publisher/url");
            }
        }

        foreach (var (field, pointer) in new[] { ("homepage", "/homepage"), ("repository", "/repository") })
        {
            if (TryString(root, field, out var value) && !AbsoluteUrlPattern().IsMatch(value))
            {
                result.Add("malformed", ValidationSeverity.Error,
                    $"'{field[..1].ToUpperInvariant()}{field[1..]}' '{value}' must be an absolute http or https URL.",
                    pointer);
            }
        }

        ValidateAdditionalLinks(root, result);
    }

    private static void ValidateAdditionalLinks(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("additionalLinks", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var standardTypes = new HashSet<string>(StringComparer.Ordinal);
        var customLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < links.GetArrayLength(); i++)
        {
            var pointer = $"/additionalLinks/{i}";
            if (links[i].ValueKind != JsonValueKind.Object)
            {
                result.Add("invalid-additional-link", ValidationSeverity.Error, "Each link must be an object.", pointer);
                continue;
            }

            var link = links[i];
            var type = TryGetString(link, "type");
            var url = TryGetString(link, "url");
            var label = TryGetString(link, "label");

            if (string.IsNullOrWhiteSpace(type))
            {
                result.Add("invalid-additional-link", ValidationSeverity.Error, "A link needs a type.", $"{pointer}/type");
                continue;
            }

            if (string.IsNullOrWhiteSpace(url) || !AbsoluteUrlPattern().IsMatch(url))
            {
                result.Add("invalid-additional-link", ValidationSeverity.Error,
                    "A link needs an absolute http or https URL.", $"{pointer}/url");
                continue;
            }

            if (type == "custom")
            {
                if (string.IsNullOrWhiteSpace(label))
                {
                    result.Add("invalid-additional-link", ValidationSeverity.Error,
                        "A custom link needs a label.", $"{pointer}/label");
                }
                else if (!customLabels.Add(label.Trim()))
                {
                    result.Add("invalid-additional-link", ValidationSeverity.Error,
                        $"Two custom links share the label '{label}'.", $"{pointer}/label");
                }
            }
            else if (!standardTypes.Add(type))
            {
                result.Add("invalid-additional-link", ValidationSeverity.Error,
                    $"Two links both use the type '{type}'.", $"{pointer}/type");
            }

            if (label is not null && type != "custom")
            {
                result.Add("schema:additionalProperties", ValidationSeverity.Error,
                    "Only a custom link may carry a label.", $"{pointer}/label");
            }

            if (!StandardLinkTypes.Contains(type, StringComparer.Ordinal))
            {
                result.Add("unknown-link-type", ValidationSeverity.Warning,
                    $"'{type}' is not a known link type.", $"{pointer}/type");
            }

            if (!urls.Add(url))
            {
                result.Add("invalid-additional-link", ValidationSeverity.Error,
                    "Two links share the same URL.", $"{pointer}/url");
            }
        }
    }

    private static void ValidateCompatibility(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("compatibility", out var compatibility)
            || compatibility.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in new[] { "sdk", "macroDeck" })
        {
            if (TryString(compatibility, field, out var range) && !VersionRangePattern().IsMatch(range))
            {
                result.Add("invalid-compatibility", ValidationSeverity.Error,
                    $"Compatibility.{field[..1].ToUpperInvariant()}{field[1..]} '{range}' is not a valid version range. "
                    + "Use comparators joined by commas - no caret, tilde or wildcards.", $"/compatibility/{field}");
            }
        }

        if (compatibility.TryGetProperty("protocol", out var protocol))
        {
            if (protocol.ValueKind != JsonValueKind.Object
                || !TryInt(protocol, "minimum", out var minimum)
                || !TryInt(protocol, "maximum", out var maximum))
            {
                result.Add("invalid-compatibility", ValidationSeverity.Error,
                    "Compatibility.protocol needs both a minimum and a maximum.", "/compatibility/protocol");
            }
            else if (minimum < 1 || maximum < minimum)
            {
                result.Add("invalid-compatibility", ValidationSeverity.Error,
                    $"Compatibility.Protocol range [{minimum}, {maximum}] is invalid.", "/compatibility/protocol");
            }
        }
    }

    private static void ValidateRelationships(JsonElement root, ValidationResult result)
    {
        var own = TryGetString(root, "id");

        foreach (var field in new[] { "dependencies", "conflicts", "iconPacks" })
        {
            if (!root.TryGetProperty(field, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var label = field switch
            {
                "dependencies" => "Dependency",
                "conflicts" => "Conflict",
                _ => "Icon pack",
            };

            for (var i = 0; i < list.GetArrayLength(); i++)
            {
                var pointer = $"/{field}/{i}";
                if (list[i].ValueKind != JsonValueKind.Object || !TryString(list[i], "id", out var id))
                {
                    result.Add("invalid-dependency", ValidationSeverity.Error,
                        $"{label} entries need an id.", pointer);
                    continue;
                }

                if (!MacroDeckRules.IsValidPluginId(id))
                {
                    result.Add("invalid-dependency", ValidationSeverity.Error,
                        $"{label} id '{id}' is not a valid plugin id.", $"{pointer}/id");
                }

                if (own is not null && string.Equals(id, own, StringComparison.Ordinal))
                {
                    result.Add("invalid-dependency", ValidationSeverity.Error,
                        $"{label} '{id}' cannot equal the manifest's own id.", $"{pointer}/id");
                }

                if (TryString(list[i], "versionRange", out var range) && !VersionRangePattern().IsMatch(range))
                {
                    result.Add("invalid-dependency", ValidationSeverity.Error,
                        $"{label} '{id}' has an invalid VersionRange.", $"{pointer}/versionRange");
                }

                if (!seen.Add(id))
                {
                    result.Add("invalid-dependency", ValidationSeverity.Error,
                        $"{label} id '{id}' is declared more than once.", $"{pointer}/id");
                }
            }
        }

        // A plugin that both requires and forbids the same id can never resolve.
        if (root.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Array
            && root.TryGetProperty("conflicts", out var conflicts) && conflicts.ValueKind == JsonValueKind.Array)
        {
            var dependencyIds = dependencies.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => TryGetString(e, "id"))
                .Where(id => id is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var conflict in conflicts.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var id = TryGetString(conflict, "id");
                if (id is not null && dependencyIds.Contains(id))
                {
                    result.Add("invalid-dependency", ValidationSeverity.Error,
                        $"'{id}' cannot be both a dependency and a conflict.", "/conflicts");
                }
            }
        }
    }

    private static void ValidateBundledIconPacks(JsonElement root, ManifestValidationLevel level, ValidationResult result)
    {
        if (!root.TryGetProperty("bundledIconPacks", out var packs) || packs.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        if (packs.GetArrayLength() > MaxBundledIconPacks)
        {
            result.Add("schema:maxItems", ValidationSeverity.Error,
                $"At most {MaxBundledIconPacks} bundled icon packs can be declared.", "/bundledIconPacks");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < packs.GetArrayLength(); i++)
        {
            var pointer = $"/bundledIconPacks/{i}";
            if (packs[i].ValueKind != JsonValueKind.Object)
            {
                result.Add("invalid-bundled-icon-pack", ValidationSeverity.Error, "Each entry must be an object.", pointer);
                continue;
            }

            var key = TryGetString(packs[i], "key");
            var path = TryGetString(packs[i], "path");

            if (key is null || !BundledKeyPattern().IsMatch(key))
            {
                result.Add("invalid-bundled-icon-pack", ValidationSeverity.Error,
                    $"Bundled icon pack key '{key}' must be lowercase letters, digits and inner hyphens, "
                    + "at most 64 characters.", $"{pointer}/key");
            }
            else if (!keys.Add(key))
            {
                result.Add("invalid-bundled-icon-pack", ValidationSeverity.Error,
                    $"Bundled icon pack key '{key}' is declared more than once.", $"{pointer}/key");
            }

            if (path is null || Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal)
                || !path.EndsWith(".macroDeckIconPack", StringComparison.OrdinalIgnoreCase)
                || path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(s => s == ".."))
            {
                result.Add("invalid-bundled-icon-pack", ValidationSeverity.Error,
                    $"Bundled icon pack '{key}' path '{path}' must be a safe relative path ending in "
                    + ".macroDeckIconPack.", $"{pointer}/path");
            }
            else if (!paths.Add(path))
            {
                result.Add("invalid-bundled-icon-pack", ValidationSeverity.Error,
                    $"Bundled icon pack path '{path}' is declared more than once.", $"{pointer}/path");
            }

            // Existence is checked in ValidatePackagedContent, alongside the icon and the
            // entrypoints. It used to be reported here for every declared pack at package level
            // without ever looking at the filesystem, so a plugin that bundled its packs correctly
            // carried a permanent "not present" warning - and one that had genuinely not bundled
            // them got the same warning, which is no warning at all.
        }
    }

    private static void ValidatePermissions(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < permissions.GetArrayLength(); i++)
        {
            var pointer = $"/permissions/{i}";
            if (permissions[i].ValueKind != JsonValueKind.String)
            {
                result.Add("schema:type", ValidationSeverity.Error, "A permission must be a string.", pointer);
                continue;
            }

            var permission = permissions[i].GetString() ?? string.Empty;
            if (permission.Length == 0)
            {
                result.Add("invalid-permission", ValidationSeverity.Error, "Permission entries must not be empty.", pointer);
                continue;
            }

            if (!seen.Add(permission))
            {
                result.Add("schema:uniqueItems", ValidationSeverity.Error,
                    $"Permission '{permission}' is declared more than once.", pointer);
            }

            // Unknown is a warning, never a rejection: a plugin built against a newer host
            // declares permissions this host has not heard of, and default-deny would break it.
            if (!KnownPermissions.Contains(permission, StringComparer.Ordinal))
            {
                result.Add("unknown-permission", ValidationSeverity.Warning,
                    $"Permission '{permission}' is not one this version of Macro Deck knows about.", pointer);
            }
        }
    }

    private static void ValidateLanguages(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("languages", out var languages) || languages.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < languages.GetArrayLength(); i++)
        {
            var pointer = $"/languages/{i}";
            if (languages[i].ValueKind != JsonValueKind.String)
            {
                result.Add("schema:type", ValidationSeverity.Error, "A language tag must be a string.", pointer);
                continue;
            }

            var tag = languages[i].GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tag))
            {
                result.Add("invalid-language", ValidationSeverity.Error, "Language entries must not be empty.", pointer);
                continue;
            }

            if (!MacroDeckRules.IsValidLanguageTag(tag))
            {
                result.Add("schema:pattern", ValidationSeverity.Error,
                    $"'{tag}' is not a well-formed BCP-47 language tag.", pointer);
            }

            if (!seen.Add(tag))
            {
                result.Add("schema:uniqueItems", ValidationSeverity.Error,
                    $"Language '{tag}' is declared more than once.", pointer);
            }
        }
    }

    private static void ValidateFiles(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.GetArrayLength(); i++)
        {
            var pointer = $"/files/{i}";
            if (files[i].ValueKind != JsonValueKind.Object)
            {
                result.Add("invalid-file-digest", ValidationSeverity.Error, "Each entry must be an object.", pointer);
                continue;
            }

            var path = TryGetString(files[i], "path");
            var sha = TryGetString(files[i], "sha256");

            if (path is null || Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal)
                || path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(s => s == ".."))
            {
                result.Add("invalid-file-digest", ValidationSeverity.Error,
                    $"File path '{path}' is not a safe relative path.", $"{pointer}/path");
            }
            else if (!seen.Add(path))
            {
                result.Add("invalid-file-digest", ValidationSeverity.Error,
                    $"File path '{path}' is declared more than once.", $"{pointer}/path");
            }

            if (sha is null || !DigestPattern().IsMatch(sha))
            {
                result.Add("invalid-file-digest", ValidationSeverity.Error,
                    $"File '{path}' has an invalid Sha256 digest; it must be 'sha256:' and 64 lowercase hex characters.",
                    $"{pointer}/sha256");
            }

            if (TryInt(files[i], "size", out var size) && size < 0)
            {
                result.Add("invalid-file-digest", ValidationSeverity.Error,
                    $"File '{path}' has a negative Size.", $"{pointer}/size");
            }
        }
    }

    private static void ValidateSignature(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in new[] { "algorithm", "keyId", "value" })
        {
            if (!TryString(signature, field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                result.Add("invalid-signature", ValidationSeverity.Error,
                    "Signature.Algorithm, Signature.KeyId and Signature.Value must not be empty.", $"/signature/{field}");
            }
        }

        // The algorithm is deliberately not checked against ed25519 here: a scheme this reader
        // does not know about is the verifier's business, not the manifest reader's.
        if (TryString(signature, "value", out var raw) && !IsBase64(raw))
        {
            result.Add("invalid-signature", ValidationSeverity.Error,
                "Signature.Value is not valid base64.", "/signature/value");
        }
    }

    /// <summary>
    /// Whether <paramref name="value"/> is base64.
    /// </summary>
    /// <remarks>
    /// The buffer used to be <c>stackalloc byte[value.Length]</c>, sized from the manifest. A
    /// hand-edited manifest with a signature value of a few hundred megabytes asked for that much
    /// stack, and a stack overrun kills the process outright - no catch, no message, and the app
    /// looked like it had crashed for no reason. Valid base64 is a quarter of its length in bytes, so
    /// the allocation is bounded by that, and a manifest-sized value is refused rather than attempted.
    /// </remarks>
    /// <summary>
    /// Whether <paramref name="range"/> matches the version-range grammar.
    /// </summary>
    /// <remarks>Public so the grammar can be tested directly, one range at a time.</remarks>
    public static bool IsVersionRangeSyntaxForTest(string range) =>
        VersionRangePattern().IsMatch(range);

    /// <summary>Whether <paramref name="value"/> is base64. Public so the size cap can be tested.</summary>
    public static bool IsBase64ForTest(string value) => IsBase64(value);

    private static bool IsBase64(string value)
    {
        // Base64 expands 3 bytes into 4 characters, so the decoded size is at most length / 4 * 3,
        // and is a multiple of 4 characters with padding. Anything far larger than a key or token is
        // not a signature, so it is rejected before an allocation is attempted at all.
        const int MaxDecodedBytes = 64 * 1024;
        if (value.Length > (MaxDecodedBytes / 3 * 4) + 4)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[value.Length / 4 * 3 + 3];
        return Convert.TryFromBase64String(value, buffer, out _);
    }

    private static void ValidateSettings(JsonElement root, ValidationResult result)
    {
        // These are clamped, never rejected, so anything is legal. The point is to tell the
        // designer when a value they set is being silently changed.
        if (root.TryGetProperty("shutdown", out var shutdown) && shutdown.ValueKind == JsonValueKind.Object
            && TryInt(shutdown, "gracefulTimeoutSeconds", out var graceful))
        {
            if (graceful is < 1 or > 60)
            {
                result.Add("invalid-settings", ValidationSeverity.Info,
                    $"shutdown.gracefulTimeoutSeconds is clamped to 1-60; {graceful} becomes "
                    + $"{Math.Clamp(graceful, 1, 60)}.", "/shutdown/gracefulTimeoutSeconds");
            }
        }

        if (root.TryGetProperty("health", out var health) && health.ValueKind == JsonValueKind.Object)
        {
            ClampCheck(health, "intervalSeconds", 5, 120, result);
            ClampCheck(health, "timeoutSeconds", 1, 10, result);
            ClampCheck(health, "unhealthyThreshold", 2, 10, result);
        }
    }

    private static void ClampCheck(JsonElement parent, string name, int min, int max, ValidationResult result)
    {
        if (TryInt(parent, name, out var value) && (value < min || value > max))
        {
            result.Add("invalid-settings", ValidationSeverity.Info,
                $"health.{name} is clamped to {min}-{max}; {value} becomes {Math.Clamp(value, min, max)}.",
                $"/health/{name}");
        }
    }

    private static void ValidateAi(JsonElement root, ValidationResult result)
    {
        if (!root.TryGetProperty("ai", out var ai) || ai.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in new[] { "interaction", "generatedContent", "generatedAssets" })
        {
            if (ai.TryGetProperty(field, out var flag) && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                // A non-boolean flag makes the whole declaration read as "not declared", never as
                // "uses no AI" - which is a materially different thing to the Store.
                result.Add("schema:type", ValidationSeverity.Error,
                    $"ai.{field} must be true or false; any other value makes the whole declaration "
                    + "read as not declared.", $"/ai/{field}");
            }
        }

        if (!ai.TryGetProperty("services", out var services))
        {
            return;
        }

        if (services.ValueKind != JsonValueKind.Array)
        {
            result.Add("schema:type", ValidationSeverity.Error,
                "ai.services must be an array; any other value makes the whole declaration read as not declared.",
                "/ai/services");
            return;
        }

        if (services.GetArrayLength() > MaxAiServices)
        {
            result.Add("schema:maxItems", ValidationSeverity.Error,
                $"ai.services lists {services.GetArrayLength()} names; at most {MaxAiServices} are allowed.",
                "/ai/services");
        }

        for (var i = 0; i < services.GetArrayLength(); i++)
        {
            var name = services[i].ValueKind == JsonValueKind.String ? services[i].GetString() : null;
            if (string.IsNullOrEmpty(name) || name.Length > MaxAiServiceLength)
            {
                result.Add("schema:maxLength", ValidationSeverity.Error,
                    "Each ai.services name must be 1 to 64 characters.", $"/ai/services/{i}");
            }
        }
    }

    /// <summary>
    /// The six publication-required fields, reported under one code at the level that requires
    /// them.
    /// </summary>
    /// <remarks>
    /// The previous version split these across five invented codes, hardcoded every one as a
    /// warning, had no level parameter at all, and added a seventh rule the tool does not have -
    /// rejecting the <c>github.com/example/</c> template placeholder, which passes the tool's
    /// <c>^https?://</c> check. Verified against the real tool at all three levels: development
    /// reports nothing, package reports six warnings, publication reports the same six as errors.
    /// </remarks>
    private static void ValidatePublicationMetadata(JsonElement root, ManifestValidationLevel level, ValidationResult result)
    {
        if (level < ManifestValidationLevel.Package)
        {
            return;
        }

        var severity = level >= ManifestValidationLevel.Publication
            ? ValidationSeverity.Error
            : ValidationSeverity.Warning;

        const string suffix = "is required to publish to the Macro Deck plugin ecosystem. "
                              + "It is not required to develop or run this plugin locally.";

        if (!TryString(root, "description", out var description) || string.IsNullOrWhiteSpace(description))
        {
            result.Add("publication-metadata-missing", severity, $"'description' {suffix}", "/description");
        }

        if (!TryString(root, "icon", out var icon) || string.IsNullOrWhiteSpace(icon))
        {
            result.Add("publication-metadata-missing", severity, $"'icon' {suffix}", "/icon");
        }

        if (!TryString(root, "license", out var license) || string.IsNullOrWhiteSpace(license))
        {
            result.Add("publication-metadata-missing", severity, $"'license' {suffix}", "/license");
        }

        if (!TryString(root, "repository", out var repository) || string.IsNullOrWhiteSpace(repository))
        {
            result.Add("publication-metadata-missing", severity, $"'repository' {suffix}", "/repository");
        }

        if (!root.TryGetProperty("compatibility", out var compatibility)
            || compatibility.ValueKind != JsonValueKind.Object
            || !compatibility.EnumerateObject().Any())
        {
            result.Add("publication-metadata-missing", severity,
                "'compatibility' is required to publish to the Macro Deck plugin ecosystem: declare at "
                + "least one of 'sdk', 'protocol' or 'macroDeck'. It is not required to develop or run "
                + "this plugin locally.", "/compatibility");
        }

        if (!root.TryGetProperty("publisher", out var publisher) || publisher.ValueKind != JsonValueKind.Object)
        {
            result.Add("publication-metadata-missing", severity, $"'publisher' {suffix}", "/publisher");
        }
        else if (!TryString(publisher, "name", out var publisherName) || string.IsNullOrWhiteSpace(publisherName))
        {
            result.Add("publication-metadata-missing", severity, $"'publisher.name' {suffix}", "/publisher/name");
        }
    }

    private static bool TryString(JsonElement e, string name, out string value)
    {
        value = string.Empty;
        return e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString() ?? string.Empty) is not null;
    }

    private static string? TryGetString(JsonElement e, string name) =>
        TryString(e, name, out var v) ? v : null;

    private static bool TryInt(JsonElement e, string name, out int value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }
}
