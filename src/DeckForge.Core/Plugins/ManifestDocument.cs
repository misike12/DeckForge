using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckForge.Core.Plugins;

/// <summary>
/// Edits manifest.json on a raw JSON tree, so unknown properties (legal - Macro Deck ignores
/// them) survive every round-trip. The typed <see cref="PluginManifest"/> is the editor's
/// view; all writes go through this document.
/// </summary>
public sealed class ManifestDocument
{
    private JsonObject _root;

    /// <summary>Direct access to the underlying JSON tree for advanced edits (unknown fields survive).</summary>
    public JsonObject RawDocument => _root;

    private ManifestDocument(JsonObject root) => _root = root;

    /// <summary>Parses a manifest file. Throws FormatException on invalid JSON.</summary>
    public static ManifestDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static ManifestDocument Parse(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new FormatException("Manifest root must be a JSON object.");
        return new ManifestDocument(node);
    }

    public static ManifestDocument NewManifest(NewProjectOptions options, string version = "1.0.0")
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
            ["manifestVersion"] = 1,
            ["id"] = options.PluginId,
            ["name"] = options.PluginName,
            ["version"] = version,
            ["description"] = options.Description ?? "",
            ["icon"] = "Assets/icon.svg",
            ["entrypoints"] = new JsonObject(),
            ["publisher"] = new JsonObject { ["name"] = options.Publisher },
            ["license"] = options.License,
            ["repository"] = string.IsNullOrWhiteSpace(options.Repository) ? "https://github.com/example/my-plugin" : options.Repository,
            ["compatibility"] = new JsonObject { ["macroDeck"] = MacroDeckSdkInfo.DefaultMacroDeckRange },
        };
        if (!string.IsNullOrWhiteSpace(options.Homepage))
        {
            root["homepage"] = options.Homepage;
        }
        return new ManifestDocument(root);
    }

    // ----- identity -----

    public string Id => GetString("id") ?? "";
    public string Name => GetString("name") ?? "";
    public string Version => GetString("version") ?? "";

    public void SetId(string value) => SetString("id", value);
    public void SetName(string value) => SetString("name", value);
    public void SetVersion(string value) => SetString("version", value);
    public void SetDescription(string value) => SetString("description", value);
    public void SetIcon(string value) => SetString("icon", value);
    public void SetLicense(string? value) => SetString("license", value);
    public void SetRepository(string? value) => SetString("repository", value);
    public void SetHomepage(string? value) => SetString("homepage", value);

    public string PublisherName => _root["publisher"]?["name"]?.GetValue<string>() ?? "";
    public void SetPublisherName(string value)
    {
        var publisher = _root["publisher"] as JsonObject ?? new JsonObject();
        publisher["name"] = value;
        _root["publisher"] = publisher;
    }

    public IReadOnlyList<string> Permissions =>
        _root["permissions"] as JsonArray is { } arr
            ? [.. arr.OfType<JsonValue>().Select(v => v.GetValue<string>())]
            : [];

    public void SetPermissions(IReadOnlyList<string> permissions) =>
        _root["permissions"] = new JsonArray([.. permissions.Select(p => JsonValue.Create(p))]);

    public IReadOnlyList<string> Languages =>
        _root["languages"] as JsonArray is { } langs
            ? [.. langs.OfType<JsonValue>().Select(v => v.GetValue<string>())]
            : [];

    public void SetLanguages(IReadOnlyList<string> languages) =>
        _root["languages"] = new JsonArray([.. languages.Select(p => JsonValue.Create(p))]);

    // ----- entrypoints -----

    public IReadOnlyList<string> Platforms =>
        _root["entrypoints"] as JsonObject is { } eps ? [.. eps.Select(kv => kv.Key)] : [];

    /// <summary>Ensures an entrypoint row exists for a rid with the standard layout.</summary>
    public void EnsureEntrypoint(string rid, string projectName, bool selfContained, string dotnetVersion = MacroDeckSdkInfo.DefaultDotnetVersion)
    {
        var entrypoints = _root["entrypoints"] as JsonObject ?? new JsonObject();
        var exe = selfContained && rid.StartsWith("win", StringComparison.Ordinal)
            ? $"runtimes/{rid}/{projectName}.exe"
            : $"runtimes/{rid}/{projectName}.dll";
        var entry = new JsonObject { ["executable"] = exe };
        if (!selfContained)
        {
            entry["runtime"] = new JsonObject { ["kind"] = "FrameworkDependent", ["dotnetVersion"] = dotnetVersion };
        }
        entrypoints[rid] = entry;
        _root["entrypoints"] = entrypoints;
    }

    public void RemoveEntrypoint(string rid)
    {
        if (_root["entrypoints"] is JsonObject eps)
        {
            eps.Remove(rid);
        }
    }

    // ----- persistence -----

    public void Save(string path)
    {
        File.WriteAllText(path, ToJson());
    }

    public string ToJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private string? GetString(string name) =>
        _root[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private void SetString(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _root.Remove(name);
        }
        else
        {
            _root[name] = value;
        }
    }
}
