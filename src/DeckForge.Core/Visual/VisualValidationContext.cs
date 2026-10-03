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
/// An empty set used to mean two opposite things: "nobody has told me what exists yet" and "the plugin
/// declares nothing". <see cref="Empty"/> is the first and <see cref="None"/> is the second, so which one
/// you meant is stated rather than inferred from an empty collection. <see cref="NamesResolved"/> is the
/// flag that tells them apart.
/// </para>
/// </remarks>
public sealed record VisualValidationContext(
    IReadOnlySet<string> Parameters,
    IReadOnlySet<string> HostVariables,
    bool NamesResolved = true)
{
    /// <summary>Nobody has said yet: no unknown-name warning can fire.</summary>
    /// <remarks>
    /// What the Visual page validates against until it has resolved the open workspace's action
    /// parameters and host variables. Without it, a canvas full of perfectly good references was reported
    /// as undeclared names, because the app had no source of names to check them against - and an editor
    /// that opens with a wall of red is one nobody reads.
    /// </remarks>
    public static readonly VisualValidationContext Empty = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        NamesResolved: false);

    /// <summary>The plugin declares nothing, and that is known: every reference is unknown.</summary>
    /// <remarks>
    /// The opposite of <see cref="Empty"/>, and the one a test wants when it is checking that an undeclared
    /// name is reported. Two empty sets with two meanings was the bug; this is the second meaning, named.
    /// </remarks>
    public static readonly VisualValidationContext None = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
}
