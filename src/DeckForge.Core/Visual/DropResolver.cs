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

    /// <summary>
    /// On top of a statement, which replaces it.
    /// </summary>
    /// <remarks>
    /// Part 9.5's last zone. Offered only by the canvas, and only for a block the pointer is genuinely
    /// over the body of rather than beside, because the gap above and below the same statement are two
    /// ordinary gaps and stealing either of them would make ordinary drops unpredictable.
    /// </remarks>
    OntoStatement,

    /// <summary>The top of a script: only hats may land here.</summary>
    HatSlot,

    /// <summary>Free canvas: stacks land here to start a new script.</summary>
    Canvas,
}

/// <summary>
/// One place the dragged block could land.
/// </summary>
/// <param name="Kind">What kind of place it is.</param>
/// <param name="ParentId">
/// The block that owns the body, slot or script, or the id of the block the gap sits against.
/// </param>
/// <param name="BodyName">
/// The body the gap opens into. Empty for a script body, which a hat's id addresses on its own.
/// </param>
/// <param name="SlotName">The slot, for a value-slot candidate.</param>
/// <param name="Index">
/// Where in the body. A candidate is therefore self-sufficient: the canvas turns one into a
/// <see cref="BodyRef"/> and an index without having to remember where it was standing.
/// </param>
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
            case DropTargetKind.OntoStatement:
                return !dragged.IsReporter && !dragged.IsHat;

            case DropTargetKind.Canvas:
                return dragged.IsHat || !dragged.IsReporter;

            default:
                return false;
        }
    }

    /// <summary>
    /// Enumerates the drop candidates of one laid-out script: the hat slot, and a gap above and below
    /// every laid-out statement including the ones nested inside containers.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="rects">
    /// Where every block of the script is. Measured off the live tiles rather than computed, because the
    /// candidates' only job is to be compared with a pointer position and the pointer is looking at the
    /// tiles WPF actually laid out. A block whose label holds a long value is wider than any estimate, and
    /// a gap computed from the estimate is then not over the block the user is aiming at.
    /// </param>
    /// <remarks>
    /// <para>
    /// Every candidate carries the body it belongs to and the index it would insert at, which is what
    /// makes a candidate self-sufficient: the canvas turns one into a <see cref="BodyRef"/> and an index
    /// without keeping its own record of where it was walking. P1c's version left the index at zero
    /// throughout, which was fine while nothing acted on a candidate and is not now.
    /// </para>
    /// <para>
    /// The canvas calls this per pointer-move over the visible scripts only, which is how the Appendix H
    /// budget of 2ms holds: candidates are O(visible blocks), not O(document).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DropCandidate> CandidatesFor(
        VisualScript script,
        IReadOnlyDictionary<string, BlockRect> rects,
        double zoom = 1)
    {
        var candidates = new List<DropCandidate>();

        // The hat slot: only hats, always at the top.
        if (!rects.TryGetValue(script.Hat.Id, out var hatRect))
        {
            return candidates;
        }

        candidates.Add(new DropCandidate(
            DropTargetKind.HatSlot, script.Hat.Id, X: hatRect.X, Y: hatRect.Y));

        var body = BodyRef.ScriptBody(script.Hat.Id);

        // The index is the block's own position in the body, because that is what the candidate has to
        // carry: an index of zero for every statement makes every gap in the script mean the same place.
        for (var index = 0; index < script.Body.Count; index++)
        {
            Collect(script.Body[index], body, index, rects, candidates);
        }

        return candidates;
    }

    /// <summary>
    /// Turns the value slots the canvas located into candidates.
    /// </summary>
    /// <param name="slots">Each slot's owning block, its name, and its top-left in canvas coordinates.</param>
    /// <remarks>
    /// <para>
    /// A slot is exactly as wide as the text in it, which Core cannot know and should not guess: the
    /// canvas measures, and this turns those measurements into candidates the scorer understands. The
    /// scoring, the shape filter and the magnet radius stay in one place either way, and that is the
    /// property that matters — a reporter refused from a value slot has to be refused the same way
    /// whether the hole was measured on screen or estimated.
    /// </para>
    /// <para>
    /// Slots inside a collapsed body are simply not passed in, which is correct: there is nowhere on
    /// screen to drop into them.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DropCandidate> ValueSlotCandidates(
        IEnumerable<(string BlockId, string SlotName, double X, double Y)> slots) =>
        [.. slots.Select(slot => new DropCandidate(
            DropTargetKind.ValueSlot,
            slot.BlockId,
            SlotName: slot.SlotName,
            X: slot.X,
            Y: slot.Y))];

    /// <summary>
    /// Adds the gaps around one statement and, if it wraps anything, inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every coordinate comes from the block's own rectangle: the gap above at <see cref="BlockRect.Y"/>,
    /// the gap below at <see cref="BlockRect.NotchY"/>, and the x from <see cref="BlockRect.X"/>. Nothing
    /// here adds a height to a running total.
    /// </para>
    /// <para>
    /// That is a correction rather than a simplification, and it is worth being explicit about why the
    /// arithmetic was wrong in two different ways. It walked y down from the hat, which meant a statement's
    /// top was whatever the previous one ended up being — so a container's inner gaps were reported at the
    /// container's <em>bottom</em>, because that is where the walk started, and an indicator for the top of
    /// a loop's mouth appeared at the foot of it. And it reported x as an indent from the script's origin,
    /// which is zero, so every script's gaps claimed the same x and the scorer — which weighs x at half a
    /// point per pixel — had no way to prefer the script under the pointer over its neighbour. Both were
    /// invisible to a test and obvious in a screenshot: a drop put a block in the wrong script, and a
    /// second attempt at the right place was refused because the nearest candidate was a hundred pixels
    /// away vertically.
    /// </para>
    /// <para>
    /// The rectangles are measured from the live canvas, so they already know where WPF put everything. A
    /// second arithmetic pass over the same geometry can only ever disagree with it, and does.
    /// </para>
    /// </remarks>
    private static void Collect(
        Block block,
        BodyRef where,
        int index,
        IReadOnlyDictionary<string, BlockRect> rects,
        List<DropCandidate> candidates)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is null || !rects.TryGetValue(block.Id, out var rect))
        {
            return;
        }

        // The gap above this statement: landing here inserts before it. The first statement of a wrapped
        // body is also the mouth's landing spot, which is what makes "drop a stack into an empty loop" one
        // gesture rather than a special case.
        candidates.Add(new DropCandidate(
            index == 0 && !where.IsScriptBody ? DropTargetKind.Mouth : DropTargetKind.StackGap,
            where.OwnerId,
            BodyName: where.BodyName,
            Index: index,
            X: rect.X,
            Y: rect.Y));

        if (descriptor.IsContainer)
        {
            foreach (var (name, children) in block.Bodies)
            {
                var childWhere = new BodyRef(block.Id, name);

                for (var childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    Collect(children[childIndex], childWhere, childIndex, rects, candidates);
                }

                // The gap below the last statement of a body, which is the mouth's own bottom edge. Without
                // it a C-block's mouth can only ever be filled from the top, which is not how Scratch
                // behaves: a run dropped at the foot of a loop's body belongs inside the loop.
                //
                // Its y is the last statement's notch, which is where the footer bar begins - the same
                // place the block below it wants to sit. Using the container's own bottom instead put this
                // gap 44px lower than the one above it and made the top and the foot of every mouth
                // disagree by the height of the footer.
                if (children.Count > 0
                    && rects.TryGetValue(children[^1].Id, out var lastChild))
                {
                    candidates.Add(new DropCandidate(
                        DropTargetKind.Mouth,
                        childWhere.OwnerId,
                        BodyName: name,
                        Index: children.Count,
                        X: lastChild.X,
                        Y: lastChild.NotchY));
                }
            }
        }

        // The gap below this statement: landing here inserts after it.
        candidates.Add(new DropCandidate(
            DropTargetKind.StackGap, where.OwnerId, BodyName: where.BodyName, Index: index + 1, X: rect.X, Y: rect.NotchY));
    }
}
