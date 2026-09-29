namespace DeckForge.Core.Visual;

/// <summary>
/// What the plugin declares, which is what the reference checks run against.
/// </summary>
/// <remarks>
/// <para>
/// The validator deliberately knows nothing about where these names come from. In the App they are
/// read from the workspace's action editor and capability scaffolds; in tests they are lists. Keeping
/// the source out of Core is what makes <c>vis-param-unknown</c> testable without a workspace.
/// </para>
/// <para>
/// <see cref="Empty"/> is the "assume everything exists" mode: with no declarations supplied, no
/// unknown-name warning can fire, so an editor that has not loaded the action's parameters yet shows
/// a canvas without a wall of false positives rather than one with a wall of wrong ones.
/// </para>
/// </remarks>
public sealed record VisualValidationContext(
    IReadOnlySet<string> Parameters,
    IReadOnlySet<string> HostVariables)
{
    /// <summary>No declarations: the reference checks are switched off.</summary>
    public static readonly VisualValidationContext Empty = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
}
