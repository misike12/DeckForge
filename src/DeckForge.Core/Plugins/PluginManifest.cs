namespace DeckForge.Core.Plugins;

/// <summary>One per-platform launch target of a manifest.</summary>
public sealed record ManifestEntrypoint
{
    public required string Executable { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
    public ManifestRuntime? Runtime { get; init; }
}

/// <summary>Runtime kind of an entrypoint. FrameworkDependent requires a .dll executable.</summary>
public sealed record ManifestRuntime
{
    /// <summary>"SelfContained" (default when omitted) or "FrameworkDependent".</summary>
    public string Kind { get; init; } = "SelfContained";
    /// <summary>"10.0" for the current Macro Deck runtime; required when FrameworkDependent.</summary>
    public string? DotnetVersion { get; init; }
}

/// <summary>Publisher block of the manifest.</summary>
public sealed record ManifestPublisher
{
    public string Name { get; init; } = "";
    public string? Id { get; init; }
    public string? Email { get; init; }
    public string? Url { get; init; }
}

/// <summary>Compatibility ranges; absent members declare nothing.</summary>
public sealed record ManifestCompatibility
{
    /// <summary>Version range like "&gt;=3.0.0-0".</summary>
    public string? MacroDeck { get; init; }
    public string? Sdk { get; init; }
    public ManifestProtocolRange? Protocol { get; init; }
}

public sealed record ManifestProtocolRange
{
    public required int Minimum { get; init; }
    public required int Maximum { get; init; }
}

/// <summary>Self-declaration about AI use shown on the Store page.</summary>
public sealed record ManifestAi
{
    public bool Interaction { get; init; }
    public bool GeneratedContent { get; init; }
    public bool GeneratedAssets { get; init; }
    /// <summary>At most 16 names of at most 64 characters.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];
}

/// <summary>
/// Strongly typed view of manifest.json (manifestVersion 1) fields DeckForge edits.
/// Unknown properties are legal - Macro Deck ignores them - so persistence always
/// round-trips the raw JSON document; this record is only the editor's view.
/// </summary>
public sealed record PluginManifest
{
    public const int CurrentVersion = 1;

    public int ManifestVersion { get; init; } = CurrentVersion;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public string Description { get; init; } = "";
    /// <summary>Forward-slash path relative to the version directory, e.g. "Assets/icon.svg".</summary>
    public string Icon { get; init; } = "Assets/icon.svg";
    public IReadOnlyDictionary<string, ManifestEntrypoint> Entrypoints { get; init; } =
        new Dictionary<string, ManifestEntrypoint>();
    public ManifestPublisher? Publisher { get; init; }
    public string? License { get; init; }
    public string? Repository { get; init; }
    public string? Homepage { get; init; }
    public ManifestCompatibility? Compatibility { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
    public ManifestAi? Ai { get; init; }
    /// <summary>Graceful shutdown timeout, seconds (clamped 1-60 by the host).</summary>
    public int? GracefulTimeoutSeconds { get; init; }
}
