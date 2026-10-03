namespace DeckForge.Core.Visual;

/// <summary>
/// What a workspace remembers about how its author uses the palette: the blocks they reach for most, and
/// the ones they have pinned.
/// </summary>
/// <remarks>
/// <para>
/// In Core because the *rules* are the interesting part and they are easy to get subtly wrong: recency has
/// to be most-recent-first without duplicates, the list has to be capped, and pinning must not be affected
/// by using a block. A view model that grew its own list would get the ordering right for a week and then
/// quietly wrong.
/// </para>
/// <para>
/// Not document state. A canvas says what a script does; this says what a *person* does, and it belongs to
/// the workspace rather than to the plugin, because two actions in one plugin can be written by two
/// people who reach for different blocks.
/// </para>
/// </remarks>
public sealed class PaletteMemory
{
    /// <summary>How many recent blocks are kept.</summary>
    /// <remarks>
    /// Twelve. Enough to cover a session's handful of blocks and to fill a small strip; more than that and
    /// the row stops being "what I used" and starts being a second palette the user has to read past.
    /// </remarks>
    public const int RecentsLimit = 12;

    // Both lists, not a list and a set: the order is the feature in each case — most recent first for one,
    // the order they were pinned for the other — and a HashSet promises neither. That is not a
    // theoretical difference: a set that happened to keep insertion order is a set that reorders when
    // something removes and re-adds, which is exactly what re-pinning does.
    private readonly List<string> _recents = [];
    private readonly List<string> _favourites = [];

    /// <summary>The recently used block kinds, most recent first.</summary>
    public IReadOnlyList<string> Recents => _recents;

    /// <summary>The pinned block kinds, in the order they were pinned.</summary>
    public IReadOnlyList<string> Favourites => _favourites;

    /// <summary>Whether a block is pinned.</summary>
    /// <param name="kind">The catalogue kind.</param>
    public bool IsFavourite(string kind) => _favourites.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// Records that a block was used, moving it to the front if it was already there.
    /// </summary>
    /// <remarks>
    /// The move-to-front is the whole behaviour. Without it the list would be the order a user first
    /// touched each block in, which stops being "recent" within a minute of work and is only interesting
    /// to somebody reading a history.
    /// </remarks>
    /// <param name="kind">The catalogue kind.</param>
    public void Note(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        _recents.Remove(kind);
        _recents.Insert(0, kind);

        // Trimmed from the end, so the cap keeps the newest rather than the oldest.
        while (_recents.Count > RecentsLimit)
        {
            _recents.RemoveAt(_recents.Count - 1);
        }
    }

    /// <summary>Pins or unpins a block.</summary>
    /// <param name="kind">The catalogue kind.</param>
    /// <returns>Whether it is pinned now.</returns>
    public bool ToggleFavourite(string kind)
    {
        if (!_favourites.Remove(kind))
        {
            _favourites.Add(kind);
        }

        return _favourites.Contains(kind, StringComparer.Ordinal);
    }

    /// <summary>Forgets a block from both lists, for a catalogue that no longer has it.</summary>
    /// <param name="known">The kinds the catalogue still has.</param>
    /// <remarks>
    /// A block removed from the catalogue must not stay in the palette's memory, or the strip shows a row
    /// that cannot be dragged from and the star unpins nothing. The stored file is left alone — this is a
    /// read-side prune, and rewriting the user's file because a catalogue changed would be rude.
    /// </remarks>
    public void PruneTo(IReadOnlySet<string> known)
    {
        _recents.RemoveAll(kind => !known.Contains(kind));
        _favourites.RemoveAll(kind => !known.Contains(kind));
    }

    /// <summary>The state to store, as a plain record so the file format is not this class's problem.</summary>
    public PaletteMemoryState ToState() => new([.. _recents], [.. _favourites]);

    /// <summary>
    /// Rebuilds from stored state, keeping only what the catalogue still has.
    /// </summary>
    /// <remarks>
    /// Loading through the same pruning as everything else, because the file on disk was written by an
    /// older build and a block that has since been dropped must not come back as a dead row.
    /// </remarks>
    /// <param name="state">What was stored, or null for a workspace with no palette memory yet.</param>
    /// <param name="known">The kinds the catalogue has.</param>
    public static PaletteMemory Restore(PaletteMemoryState? state, IReadOnlySet<string> known)
    {
        var memory = new PaletteMemory();

        if (state is null)
        {
            return memory;
        }

        // Reversed, because `Note` puts the newest at the front and the file already has the newest first.
        // Restoring in file order would reverse the row — the one thing a recency list must never do, and
        // the kind of wrongness that only shows up once there are more than two entries to look at.
        foreach (var kind in state.Recents.Take(RecentsLimit).Reverse())
        {
            memory.Note(kind);
        }

        foreach (var kind in state.Favourites)
        {
            memory._favourites.Add(kind);
        }

        memory.PruneTo(known);

        return memory;
    }
}

/// <summary>What is stored on disk: two lists of block kinds.</summary>
/// <param name="Recents">Most recent first.</param>
/// <param name="Favourites">In the order they were pinned.</param>
public sealed record PaletteMemoryState(IReadOnlyList<string> Recents, IReadOnlyList<string> Favourites);