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

        BlockTile? found = null;

        for (var current = result.VisualHit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is BlockTile tile)
            {
                found = tile;
            }
        }

        return found;
    }

    /// <summary>
    /// A value slot under the point and the block that owns it, or null.
    /// </summary>
    /// <remarks>
    /// Walked rather than read off the hit test, because a slot's border sits inside its tile's: a
    /// pointer over the hole's own edge would otherwise resolve to the block, which is the opposite of
    /// what someone lining a reporter up with a hole is aiming at.
    /// </remarks>
    public static (BlockNodeViewModel Owner, SlotViewModel Slot)? SlotAt(FrameworkElement root, Point point)
    {
        foreach (var view in Descendants<InputSlotView>(root))
        {
            if (view.Data is { } slot && Within(view, root, point))
            {
                return (slot.Owner, slot);
            }
        }

        return null;
    }

    /// <summary>Every tile under <paramref name="root"/>, outermost first.</summary>
    public static IReadOnlyList<BlockTile> Tiles(DependencyObject root) => [.. Descendants<BlockTile>(root)];
    /// <summary>
    /// Where every block under <paramref name="root"/> sits, in <paramref name="root"/>'s coordinates.
    /// </summary>
    /// <param name="root">The drag surface.</param>
    /// <remarks>
    /// Measured, not computed. <see cref="DropResolver.CandidatesFor"/> needs rectangles and notches, and
    /// both are facts about what is on screen, so the resolver stays pure and the canvas hands it
    /// observations rather than predictions.
    /// </remarks>
    public static IReadOnlyDictionary<string, BlockRect> Rects(FrameworkElement root)
    {
        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal);

        foreach (var tile in Tiles(root))
        {
            if (tile.DataContext is not BlockNodeViewModel node || tile.ActualWidth <= 0 || tile.ActualHeight <= 0)
            {
                continue;
            }

            var origin = tile.TranslatePoint(new Point(0, 0), root);
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

        foreach (var view in Descendants<InputSlotView>(root))
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
    public static IReadOnlyList<DropCandidate> Candidates(
        FrameworkElement root,
        IEnumerable<VisualScript> scripts)
    {
        var rects = Rects(root);
        var candidates = new List<DropCandidate>();

        foreach (var script in scripts)
        {
            candidates.AddRange(DropResolver.CandidatesFor(script, rects));
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
    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
