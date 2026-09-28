using System.Reflection;
using DeckForge.Core.Extensions;

namespace DeckForge.Core.Extensions.BuiltIn;

/// <summary>
/// Contributes a Diagnostics check that reports the extensions DeckForge found.
/// </summary>
/// <remarks>
/// <para>
/// The extension point had an interface, eight hooks, and nothing behind them. A point with no
/// implementation is indistinguishable from no point at all, and a hook nothing subscribes to is
/// indistinguishable from a broken one - so the only way to tell whether the plumbing works is for
/// something to actually travel through it.
/// </para>
/// <para>
/// This one is deliberately built in and deliberately small. It reads no user content and changes
/// nothing; it reports what the loader found. A third-party extension then has a working example to
/// copy, and a failing one has a way to show up in the product rather than only in a log.
/// </para>
/// </remarks>
public sealed class ExtensionDiagnosticsExtension : IDeckForgeExtension
{
    public string Id => "deckforge.extension-diagnostics";

    public string Name => "Extension diagnostics";

    public string Description =>
        "Reports which extensions are loaded, disabled or failed, and where they were found.";

    public IReadOnlyList<DeckForgeHooks> Hooks { get; } = [DeckForgeHooks.Diagnostics];

    public Task<object?> InvokeAsync(DeckForgeHooks hook, object? payload, CancellationToken ct = default)
    {
        if (hook is not DeckForgeHooks.Diagnostics)
        {
            return Task.FromResult<object?>(null);
        }

        var service = payload as ExtensionService;
        if (service is null)
        {
            return Task.FromResult<object?>(null);
        }

        return Task.FromResult<object?>(new ExtensionReport(service.Found));
    }
}

/// <summary>What the diagnostics extension found.</summary>
/// <param name="Extensions">One entry per extension the loader reported.</param>
public sealed record ExtensionReport(IReadOnlyList<ExtensionInfo> Extensions)
{
    /// <summary>The lines a diagnostics page would show.</summary>
    public IReadOnlyList<string> Lines =>
    [
        .. Extensions.Select(e => $"{e.Id} [{e.State}] {e.Name}{(e.Error is null ? "" : $" - {e.Error}")}"),
    ];
}
