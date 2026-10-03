using System.Windows;
using System.Windows.Media;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// What the pointer is over on the canvas, found by walking the live visual tree.
/// </summary>
/// <remarks>
/// <para>
/// Hit testing reads the tiles that are actually on screen rather than the rectangles
/// <see cref="StackLayout"/> computes, and the difference is the whole reason this class exists. The
/// canvas draws blocks with WPF's own measure and arrange pass: a label holding a long value makes its
/// tile wider than any estimate, and a nested body indents by whatever the tile's own margin says. A drop
/// indicator computed from the pure geometry would then sit somewhere the user can see is not where the
/// block is going, which reads as "the snapping is broken" rather than as "the estimate was wrong".
/// </para>
/// <para>
/// The tiles are still walked depth-first and the deepest match wins, so a nested reporter beats the tile
/// that owns it. That matters because the gap around the outer tile is only reachable when the pointer is
/// not over the reporter, and a reporter dropped from a palette should find its own hole rather than the
/// statement's shoulder.
/// </para>
/// </remarks>
internal static class CanvasHitTest
{
    /// <summary>The innermost block under the point, or null.</summary>
    /// <param name="root">The element the point is expressed in — the drag surface.</param>
    /// <param name="point">The pointer, in <paramref name="root"/>'s coordinates.</param>
    public static BlockNodeViewModel? BlockAt(FrameworkElement root, Point point) =>
        TileAt(root, point)?.DataContext as BlockNodeViewModel;

    /// <summary>The innermost tile under the point, or null.</summary>
    public static BlockTile? TileAt(FrameworkElement root, Point point)
    {
        if (VisualTreeHelper.HitTest(root, point) is not { } result)
        {
            return null;
        }

        // The *innermost* tile, and therefore the first one found walking up. This walked the whole chain
        // and kept the last match, which is the outermost: clicking the words of a reporter nested in a
        // statement's slot selected the statement that owned it, and dragging then picked up the whole
        // statement. The gap around the outer tile is still reachable - the pointer is only over the
        // reporter when it is genuinely over the reporter - and the reporter is what the user aimed at.
        for (var current = result.VisualHit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is BlockTile tile)
            {
                return tile;
            }
        }

        return null;
    }

    /// <summary>
    /// A value slot under the point and the block that owns it, or null.
    /// </summary>
    /// <remarks>
    /// Walked rather than read off the hit test, because a slot's border sits inside its tile's: a
    /// pointer over the hole's own edge would otherwise resolve to the block, which is the opposite of
    /// what someone lining a reporter up with a hole is aiming at.
    /// </remarks>

    /// <summary>Every tile under <paramref name="root"/>, outermost first.</summary>
    public static IReadOnlyList<BlockTile> Tiles(DependencyObject root) => [.. DescendantsOf<BlockTile>(root)];
    /// <summary>
    /// Where every block under <paramref name="root"/> sits, in <paramref name="root"/>'s coordinates.
    /// </summary>
    /// <param name="root">The drag surface.</param>
    /// <remarks>
    /// Measured, not computed. <see cref="DropResolver.CandidatesFor"/> needs rectangles and notches, and
    /// both are facts about what is on screen, so the resolver stays pure and the canvas hands it
    /// observations rather than predictions.
    /// </remarks>
    internal static IReadOnlyDictionary<string, BlockRect> Rects(FrameworkElement root)
    {
        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal);

        foreach (var tile in Tiles(root))
        {
            if (tile.DataContext is not BlockNodeViewModel node || tile.ActualWidth <= 0 || tile.ActualHeight <= 0)
            {
                continue;
            }

            var origin = tile.TranslatePoint(new Point(0, 0), root);
            // NotchY is the bottom edge, which is what the drop resolver's gap candidates are measured
            // from. The other copy of this walk passed the height here instead, so the same block had two
            // different notches depending on which caller asked - and the minimap does not read it today,
            // which is the only reason nobody has seen a drop indicator in the wrong place.
            rects[node.Id] = new BlockRect(
                origin.X,
                origin.Y,
                tile.ActualWidth,
                tile.ActualHeight,
                origin.Y + tile.ActualHeight);
        }

        return rects;
    }

    /// <summary>
    /// Every visible value slot, as candidates for the resolver.
    /// </summary>
    /// <param name="root">The drag surface.</param>
    /// <remarks>
    /// The slot's own measured top-left. Slots that have collapsed to nothing are left out, because a
    /// target a user cannot see is a target they will never aim at and a drop indicator will never appear
    /// near — the resolver would be choosing between candidates nobody could have picked on purpose.
    /// </remarks>
    public static IReadOnlyList<DropCandidate> SlotCandidates(FrameworkElement root)
    {
        var slots = new List<(string BlockId, string SlotName, double X, double Y)>();

        foreach (var view in DescendantsOf<InputSlotView>(root))
        {
            if (view.Data is not { } slot || !Visible(view, root))
            {
                continue;
            }

            var origin = view.TranslatePoint(new Point(0, 0), root);
            slots.Add((slot.Owner.Id, slot.Name, origin.X, origin.Y));
        }

        return DropResolver.ValueSlotCandidates(slots);
    }

    /// <summary>
    /// Every gap, mouth and hat slot on the canvas, in the resolver's own terms.
    /// </summary>
    /// <param name="root">The drag surface.</param>
    /// <param name="scripts">The scripts on it.</param>
    /// <remarks>
    /// One script at a time, because <see cref="DropResolver.CandidatesFor"/> walks a script and everything
    /// nested inside it and needs to know which hat a script body belongs to. Flattening the results is
    /// the only canvas-side bookkeeping, and it is a list concatenation.
    /// </remarks>

    /// <summary>
    /// Every gap, mouth and hat slot across every column on the canvas.
    /// </summary>
    /// <param name="root">The drag surface.</param>
    /// <param name="columns">The columns on it, scripts and procedures alike.</param>
    /// <remarks>
    /// One column at a time, because <see cref="DropResolver.CandidatesFor"/> walks a body and everything
    /// nested inside it and needs to know which body it is walking. Flattening the results is the only
    /// canvas-side bookkeeping, and it is a list concatenation.
    /// </remarks>
    public static IReadOnlyList<DropCandidate> Candidates(
        FrameworkElement root,
        IEnumerable<ColumnViewModel> columns) =>
        Candidates(root, columns.Select(column => (
            column.Address,
            column.StatementBlocks,
            (column as ScriptViewModel)?.Hat.Block)));

    private static IReadOnlyList<DropCandidate> Candidates(
        FrameworkElement root,
        IEnumerable<(BodyRef Address, IReadOnlyList<Block> Body, Block? Hat)> columns)
    {
        var rects = Rects(root);
        var candidates = new List<DropCandidate>();

        foreach (var column in columns)
        {
            candidates.AddRange(DropResolver.CandidatesFor(column.Address, column.Body, column.Hat, rects));
        }

        return candidates;
    }

    /// <summary>Whether the point falls inside the element, allowing for its layout margin.</summary>
    private static bool Within(FrameworkElement element, FrameworkElement root, Point point)
    {
        if (!Visible(element, root))
        {
            return false;
        }

        var local = element.TranslatePoint(point, element);
        return local.X >= 0 && local.Y >= 0 && local.X <= element.ActualWidth && local.Y <= element.ActualHeight;
    }

    /// <summary>
    /// Whether the element has been measured and is not collapsed to nothing.
    /// </summary>
    /// <remarks>
    /// WPF measures zero-sized elements before their content arrives, and an element with a zero size is
    /// still hit-testable in the sense that <c>IsVisible</c> says yes. Offering candidates for it is how
    /// an indicator ends up drawn at the origin of the canvas, on top of an unrelated script.
    /// </remarks>
    private static bool Visible(FrameworkElement element, FrameworkElement root) =>
        element.ActualWidth > 0 && element.ActualHeight > 0 && element.IsVisible && root.IsAncestorOf(element);

    /// <summary>Every element of a type below the root, in visual order.</summary>
    /// <remarks>
    /// <para>
    /// An explicit stack rather than recursion, because a nested iterator per node costs two stack frames
    /// per element of the tree and this is the one walk every refresh makes. A canvas holding a few hundred
    /// blocks puts tens of thousands of frames under it - the walk is depth-first through data templates,
    /// and a block with a nested body is four or five elements deep - and a stack overflow in the middle of
    /// a redraw is not a failure anything can report usefully.
    /// </para>
    /// <para>
    /// Visited by reference, so an element that reports itself as its own descendant costs one visit rather
    /// than the rest of the walk. It should not happen; a WPF tree that does it is still a tree somebody
    /// has to be able to walk.
    /// </para>
    /// </remarks>
    public static IEnumerable<T> DescendantsOf<T>(DependencyObject root)
        where T : DependencyObject
    {
        var pending = new Stack<(DependencyObject Node, int Index)>();
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance)
        {
            root,
        };

        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (node, index) = pending.Pop();

            if (index >= VisualTreeHelper.GetChildrenCount(node))
            {
                continue;
            }

            var child = VisualTreeHelper.GetChild(node, index);

            if (!seen.Add(child))
            {
                continue;
            }

            // Re-pushed with the next index so the walk comes back to this node's remaining children
            // after the child's own subtree, which is what makes the order depth-first.
            pending.Push((node, index + 1));

            if (child is T match)
            {
                yield return match;
            }

            pending.Push((child, 0));
        }
    }
}
