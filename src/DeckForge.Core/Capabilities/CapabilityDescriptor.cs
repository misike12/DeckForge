namespace DeckForge.Core.Capabilities;

public enum CapabilityCategory
{
    Buttons,
    Data,
    DeckAndClients,
    SetupAndMaintenance,
    HardwareAndSurfaces,
}

/// <summary>
/// Describes one capability a Macro Deck plugin can offer. Pure metadata - rendered by
/// the capability gallery and used by generators to pick templates and docs links.
/// </summary>
public sealed record CapabilityDescriptor
{
    /// <summary>Stable machine id, e.g. "actions".</summary>
    public required string Id { get; init; }

    /// <summary>Display name, e.g. "Actions".</summary>
    public required string Name { get; init; }

    /// <summary>One-line pitch shown on the gallery card.</summary>
    public required string Summary { get; init; }

    public required CapabilityCategory Category { get; init; }

    /// <summary>SDK interface to implement, e.g. "IVariableProvider".</summary>
    public string? Interface { get; init; }

    /// <summary>Suggested host permission ids, e.g. ["host:variables"].</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>Relative docs page, e.g. "features/variables".</summary>
    public required string DocsPath { get; init; }

    /// <summary>Default capability id, or null if none.</summary>
    public bool IsInDefaultPresets { get; init; }

    /// <summary>Segoe Fluent glyph for the gallery card.</summary>
    public string Glyph { get; init; } = "\uE945";
}
