using DeckForge.Core.Plugins;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Extends project generation without touching the core generator. Registered contributors
/// run in <see cref="Order"/> after the stock Macro Deck template files exist in the builder
/// and can add files, resx keys, or adjust the manifest view.
/// Capability scaffolders (variables, events, config flows, ...) are implemented as
/// contributors; new ones are a new class + one DI registration.
/// </summary>
public interface IProjectContentContributor
{
    /// <summary>Stable id for diagnostics and dedup.</summary>
    string Id { get; }

    /// <summary>Run order; lower runs earlier. Stock template files are order 0.</summary>
    int Order => 100;

    /// <summary>Capability preset ids this contributor implements, e.g. ["variables"].</summary>
    IReadOnlyList<string> PresetIds => [];

    /// <summary>Returns true when this contributor should run for the given options.</summary>
    bool AppliesTo(NewProjectOptions options) => PresetIds.Any(p => options.CapabilityPresets.Contains(p));

    void Contribute(ProjectContentBuilder builder, NewProjectOptions options);
}
