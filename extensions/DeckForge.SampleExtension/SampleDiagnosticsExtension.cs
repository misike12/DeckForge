using System.Runtime.InteropServices;
using DeckForge.Core.Extensions;

namespace DeckForge.SampleExtension;

/// <summary>
/// A worked example of a DeckForge extension, and the thing that proves the extension point works
/// from outside the application.
/// </summary>
/// <remarks>
/// <para>
/// It contributes a diagnostics check, which is the smallest hook that does real work: the app calls
/// it, shows what it returns, and nothing about the result is special-cased. The shell's own built-in
/// diagnostics extension does the same thing from inside the app; this one is here to be dropped into
/// <c>%LOCALAPPDATA%\DeckForge\extensions</c> by someone who has never seen DeckForge's source.
/// </para>
/// <para>
/// Extensions run inside DeckForge with DeckForge's own permissions, so the boundary is a decision
/// rather than a sandbox. It therefore resolves services from the provider it is handed and never
/// reaches into the shell: a rule here, because the alternative - touching a WPF control directly -
/// is what makes an extension a fork of the app instead of an addition to it.
/// </para>
/// </remarks>
public sealed class SampleDiagnosticsExtension : IDeckForgeExtension
{
    /// <inheritdoc />
    public string Id => "deckforge.sample-diagnostics";

    /// <inheritdoc />
    public string Name => "Sample diagnostics";

    /// <inheritdoc />
    public string Description => "Reports the machine and toolchain an extension can see, as a worked example.";

    /// <inheritdoc />
    public IReadOnlyList<DeckForgeHooks> Hooks { get; } = [DeckForgeHooks.Diagnostics];

    /// <inheritdoc />
    public Task<object?> InvokeAsync(DeckForgeHooks hook, object? payload, CancellationToken ct = default)
    {
        if (hook != DeckForgeHooks.Diagnostics)
        {
            return Task.FromResult<object?>(null);
        }

        // The payload type is the app's, and it is deliberately not named here. An extension that
        // casts the payload has coupled itself to a version; one that returns a value the app knows
        // how to display has not. Anything the app does not recognise is shown as-is, so a mismatch
        // degrades to plain text rather than throwing inside a hook.
        return Task.FromResult<object?>(new SampleReport(
            $"Sample extension running on {Environment.OSVersion} ({RuntimeInformation.FrameworkDescription})."));
    }
}

/// <summary>One line this extension contributes to the diagnostics list.</summary>
/// <param name="Summary">What to show.</param>
public sealed record SampleReport(string Summary);
