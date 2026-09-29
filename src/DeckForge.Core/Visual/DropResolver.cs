namespace DeckForge.Core.Visual;

/// <summary>What kind of place a drop candidate is.</summary>
public enum DropTargetKind
{
    /// <summary>A gap between two statements, or before the first, or after the last.</summary>
    StackGap,

    /// <summary>A C-block's mouth: dropping here wraps the dragged stack inside the C-block.</summary>
    Mouth,

    /// <summary>A value slot on a block: reporters land here.</summary>
    ValueSlot,

    /// <summary>The top of a script: only hats may land here.</summary>
    HatSlot,

    /// <summary>Free canvas: stacks land here to start a new script.</summary>
    Canvas,
}

/// <summary>One place the dragged block could land.</summary>
/// <param name="Kind">What kind of place it is.</param>
/// <param name="ParentId">The block that owns the slot or body, or the id of the block the gap sits after.</param>
/// <param name="BodyName">The body the gap or mouth opens into, when one does.</param>
/// <param name="SlotName">The slot, for a value-slot candidate.</param>
/// <param name="Index">The insertion index within the body, for a stack gap.</param>
/// <param name="X">The notch's x, in canvas coordinates.</param>
/// <param name="Y">The notch's y, in canvas coordinates.</param>
public sealed record DropCandidate(
    DropTargetKind Kind,
    string ParentId,
    string? BodyName = null,
    string? SlotName = null,
    int Index = 0,
    double X = 0,
    double Y = 0);

/// <summary>The best place to land, and where the ghost should sit while approaching it.</summary>
/// <param name="Candidate">The winning candidate.</param>
/// <param name="SnapX">Where the ghost's top-left should be drawn.</param>
/// <param name="SnapY">Where the ghost's top-left should be drawn.</param>
/// <param name="Magnetic">Whether the pointer is inside the magnet radius, so free-follow is suspended.</param>
public sealed record DropResolution(DropCandidate Candidate, double SnapX, double SnapY, bool Magnetic);

/// <summary>
/// Scores drop candidates and picks the winner: the drop resolver, pure and window-free.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.5's scoring, in one place so the canvas, the keyboard drop mode and the tests all ask the
/// same question. The order of the rules is the order of the design: shape first (a hard filter — a
/// boolean cannot land in a stack gap however close it is), then distance, then horizontal overlap,
/// then the stability bonus that stops an indicator flickering between two equidistant gaps.
/// </para>
/// <para>
/// The magnet radius is 40px at 100% zoom, scaled by the zoom factor. Inside it the ghost's notch
/// aligns exactly with the target's notch and free-follow is suspended — the feeling Scratch users
/// describe as the block "wanting" to connect.
/// </para>
/// </remarks>
public static class DropResolver
{
    /// <summary>The magnet radius at 100% zoom, from Part 9.5.</summary>
    public const double MagnetRadius = 40;

    /// <summary>
    /// Resolves the best candidate for a pointer position, or null when nothing in range accepts the
    /// dragged shape.
    /// </summary>
    /// <param name="draggedKind">The catalog kind of the block (or top block of the stack) being dragged.</param>
    /// <param name="pointerX">The pointer's x, in canvas coordinates.</param>
    /// <param name="pointerY">The pointer's y, in canvas coordinates.</param>
    /// <param name="candidates">The zones the canvas has enumerated for the visible scripts.</param>
    /// <param name="recentParentId">The candidate most recently won, for the stability bonus.</param>
    /// <param name="zoom">Canvas zoom, 1 at 100%.</param>
    public static DropResolution? Resolve(
        string draggedKind,
        double pointerX,
        double pointerY,
        IReadOnlyList<DropCandidate> candidates,
        string? recentParentId = null,
        double zoom = 1)
    {
        var dragged = BlockCatalog.Find(draggedKind);
        if (dragged is null)
        {
            return null;
        }

        DropCandidate? best = null;
        var bestScore = double.NegativeInfinity;
        var bestDistance = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (!Accepts(candidate, dragged))
            {
                continue;
            }

            var distance = Math.Abs(pointerY - candidate.Y) + Math.Abs(pointerX - candidate.X) * 0.5;
            var score = -distance;

            // The stability bonus: the zone most recently entered wins ties, so an indicator cannot
            // flicker between two equidistant gaps while the pointer hovers on the boundary.
            if (recentParentId is not null
                && string.Equals(candidate.ParentId, recentParentId, StringComparison.Ordinal))
            {
                score += 12 * zoom;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                bestDistance = distance;
            }
        }

        if (best is null)
        {
            return null;
        }

        var magnet = MagnetRadius * zoom;
        var magnetic = bestDistance <= magnet;
        var snapX = magnetic ? best.X : pointerX;
        var snapY = magnetic ? best.Y : pointerY;
        return new DropResolution(best, snapX, snapY, magnetic);
    }

    /// <summary>Whether a zone accepts a block, by shape. A hard filter, never a weight.</summary>
    /// <remarks>
    /// The table is Part 9.5's. The one deliberate subtlety is the mouth: a C-block's mouth accepts
    /// stacks — and a mouth that accepts a C-block accepts wrapping a C-block, which Scratch allows
    /// and which the design keeps, because "put all of this in a repeat" is one gesture worth having.
    /// Reporters are refused everywhere except value slots, and caps are refused as anything but the
    /// last block of a run only by the emitter, not here: the resolver is about geometry, and the
    /// validator already owns "finish in the middle".
    /// </remarks>
    public static bool Accepts(DropCandidate candidate, BlockDescriptor dragged)
    {
        switch (candidate.Kind)
        {
            case DropTargetKind.ValueSlot:
                return dragged.IsReporter;

            case DropTargetKind.HatSlot:
                return dragged.IsHat;

            case DropTargetKind.Mouth:
            case DropTargetKind.StackGap:
                return !dragged.IsReporter && !dragged.IsHat;

            case DropTargetKind.Canvas:
                return dragged.IsHat || !dragged.IsReporter;

            default:
                return false;
        }
    }

    /// <summary>
    /// Enumerates the drop candidates of one laid-out script.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="rects">The layout from <see cref="StackLayout.Layout"/>.</param>
    /// <param name="zoom">Canvas zoom.</param>
    /// <remarks>
    /// Gaps sit at the notch of every laid-out block (landing "after" it) plus one at the top of the
    /// body. The canvas calls this per pointer-move over the visible scripts only, which is how the
    /// Appendix H budget of 2ms holds: candidates are O(visible blocks), not O(document).
    /// </remarks>
    public static IReadOnlyList<DropCandidate> CandidatesFor(
        VisualScript script,
        IReadOnlyDictionary<string, BlockRect> rects,
        double zoom = 1)
    {
        var candidates = new List<DropCandidate>();

        // The hat slot: only hats, always at the top.
        if (rects.TryGetValue(script.Hat.Id, out var hatRect))
        {
            candidates.Add(new DropCandidate(
                DropTargetKind.HatSlot, script.Hat.Id, X: hatRect.X, Y: hatRect.Y));
        }

        var y = hatRect.Height;
        var first = true;
        foreach (var statement in script.Body)
        {
            Collect(statement, x: 0, ref y, script.Hat.Id, null, first, rects, candidates, zoom);
            first = false;
        }

        return candidates;
    }

    private static void Collect(
        Block block,
        double x,
        ref double y,
        string parentId,
        string? bodyName,
        bool isFirst,
        IReadOnlyDictionary<string, BlockRect> rects,
        List<DropCandidate> candidates,
        double zoom)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is null || !rects.TryGetValue(block.Id, out var rect))
        {
            return;
        }

        // The gap above this statement: landing here inserts before it. The first statement of a body
        // is also the mouth's landing spot.
        candidates.Add(new DropCandidate(
            isFirst && bodyName is not null ? DropTargetKind.Mouth : DropTargetKind.StackGap,
            parentId,
            BodyName: bodyName,
            Index: 0,
            X: x,
            Y: y));

        if (descriptor.IsContainer)
        {
            var innerY = y + rect.Height;
            var indent = x + BlockMetrics.BodyIndent * zoom;
            foreach (var (name, body) in block.Bodies)
            {
                var first = true;
                foreach (var child in body)
                {
                    Collect(child, indent, ref innerY, block.Id, name, first, rects, candidates, zoom);
                    first = false;
                }
            }
        }

        // The gap below this statement: landing here inserts after it.
        y = rect.NotchY;
        candidates.Add(new DropCandidate(
            DropTargetKind.StackGap, block.Id, X: x, Y: y));
    }
}
