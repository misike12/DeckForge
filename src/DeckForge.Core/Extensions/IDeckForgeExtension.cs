namespace DeckForge.Core.Extensions;

/// <summary>Phases where a DeckForge extension can contribute UI or behavior.</summary>
public enum DeckForgeHooks
{
    /// <summary>The main shell after navigation is built (add nav items, pages).</summary>
    Shell,
    /// <summary>The capability gallery (add custom capability cards).</summary>
    CapabilityGallery,
    /// <summary>The New Project wizard (add templates/options pages).</summary>
    NewProjectWizard,
    /// <summary>Manifest Studio (add tabs, fields, validators).</summary>
    ManifestStudio,
    /// <summary>Command palette (register custom commands).</summary>
    Commands,
    /// <summary>Toolbar (register build/run/pack style buttons).</summary>
    Toolbar,
    /// <summary>Diagnostics page (register validators/checks).</summary>
    Diagnostics,
    /// <summary>Docs browser (register doc sources).</summary>
    DocsSources,
}

/// <summary>
/// Contract every DeckForge extension implements. Extensions are discovered from the
/// app directory or user folder, instantiated via DI, and invoked per hook.
/// Keep extensions isolated: resolve services from <see cref="IServiceProvider"/>, never
/// reach into the shell's internals.
/// </summary>
public interface IDeckForgeExtension
{
    /// <summary>Stable id, e.g. "deckforge.samples-gallery".</summary>
    string Id { get; }

    /// <summary>Display name.</summary>
    string Name { get; }

    /// <summary>One-line description shown in the Extensions settings page.</summary>
    string Description => "";

    /// <summary>Hooks this extension wants; only these invocations arrive.</summary>
    IReadOnlyList<DeckForgeHooks> Hooks { get; }

    /// <summary>Called once when the extension is loaded. Return false to skip it.</summary>
    Task<bool> InitializeAsync(IServiceProvider services, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>Invoked for each hook the extension registered. May return nav/page payloads.</summary>
    Task<object?> InvokeAsync(DeckForgeHooks hook, object? payload, CancellationToken ct = default) => Task.FromResult<object?>(null);
}
