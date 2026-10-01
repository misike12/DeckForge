using System.Windows;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// One drag in progress, from the first pixel of movement to the drop or the cancel.
/// </summary>
/// <remarks>
/// <para>
/// The pointer half of Part 9.5, and nothing else. It tracks where the pointer is, when a press becomes a
/// drag, and which zone the resolver picked — and then hands the whole question to <see cref="DropPlan"/>,
/// which is in Core and knows nothing about pixels. The split is what lets the interesting half be tested
/// without a window: a drag whose snapping has only ever been checked by hand is a drag whose boundary
/// cases nobody has checked, and the boundary cases are the ones a user hits by accident.
/// </para>
/// <para>
/// The document is not touched until the drop. A drag that edited as it moved would have to undo its own
/// edits every time the pointer crossed a refusal boundary, and the resolver's answer changes as the
/// pointer crosses gaps; holding the run aside and applying one command at the end is what makes a drag
/// one history entry and therefore one <c>Ctrl+Z</c>.
/// </para>
/// </remarks>
public sealed class DragController
{
    /// <summary>How far the pointer must move before a press becomes a drag.</summary>
    /// <remarks>
    /// Part 9.5's press-versus-drag distinction, and the reason a plain click still selects a block.
    /// Four pixels: enough that a slightly shaky click is not a drag, small enough that the canvas does
    /// not feel like it ignored you.
    /// </remarks>
    public const double Threshold = 4;

    private readonly VisualEditorViewModel _vm;

    private IReadOnlyList<Block>? _preview;

    public DragController(VisualEditorViewModel vm) => _vm = vm;

    /// <summary>What the drag is carrying, or null when no press is down.</summary>
    public DragPayload? Payload { get; private set; }

    /// <summary>
    /// The blocks the ghost should draw.
    /// </summary>
    /// <remarks>
    /// The real run for a canvas drag, and a fresh miniature for a palette drag. The miniature is built
    /// once at the press rather than per render, because the ghost is redrawn on every pointer move and
    /// allocating a block per frame for something the user looks at for under a second is the sort of cost
    /// that only ever shows up as a stutter on a busy document.
    /// </remarks>
    public IReadOnlyList<Block> Ghost => Payload?.Run ?? _preview ?? [];

    /// <summary>Where the press was, in surface coordinates.</summary>
    public Point PressPoint { get; private set; }

    /// <summary>Where the pointer is now, in surface coordinates.</summary>
    public Point Pointer { get; private set; }

    /// <summary>
    /// The last resolution, or null when nothing in range accepts this shape.
    /// </summary>
    /// <remarks>
    /// Null is an ordinary state and not an error: a boolean over a stack gap has nowhere to go, and the
    /// indicator disappearing is the correct and only honest feedback. The message line explains why once,
    /// rather than the canvas nagging on every frame.
    /// </remarks>
    public DropResolution? Resolution { get; private set; }

    /// <summary>Whether the pointer has moved far enough to count as a drag.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Whether there is anything to drop.</summary>
    public bool IsActive => Payload is not null && IsDragging;

    /// <summary>Whether the drop would land somewhere.</summary>
    public bool CanDrop => Resolution is not null;

    /// <summary>The parent id of the candidate that last won, for the resolver's stability bonus.</summary>
    private string? _recentParentId;

    /// <summary>Records a press on a canvas block, or on a palette row.</summary>
    /// <param name="node">The block under the pointer.</param>
    /// <param name="point">The pointer, in surface coordinates.</param>
    /// <param name="single">Whether Alt narrowed the drag to this one block.</param>
    public void Press(BlockNodeViewModel node, Point point, bool single = false)
    {
        if (node.IsPaletteRow)
        {
            _preview = BlockCatalog.Find(node.Kind) is { } descriptor
                ? [BlockFactory.Preview(descriptor)]
                : [];
            Payload = DragPayload.FromPalette(node.Kind);
        }
        else if (_vm.Editor.Locate(node.Block) is { } where)
        {
            _preview = null;
            Payload = DragPayload.FromDocument(_vm.Editor, where, node.Block, wholeStack: !single);
        }
        else
        {
            // A block the editor cannot place: from a newer document, or a palette row that was never
            // marked as one. There is nothing to drag, and a drag with no payload would follow the pointer
            // forever.
            Payload = null;
            _preview = null;
        }

        PressPoint = point;
        Pointer = point;
        IsDragging = false;
        Resolution = null;
        _recentParentId = null;
    }

    /// <summary>Records a press on a palette row, which carries a kind rather than a document block.</summary>
    public void PressPalette(BlockNodeViewModel row, Point point) => Press(row, point);

    /// <summary>Moves the pointer, promoting the press to a drag once it passes the threshold.</summary>
    public void Move(Point point, IReadOnlyList<DropCandidate> candidates)
    {
        Pointer = point;

        if (Payload is not { } payload)
        {
            return;
        }

        if (!IsDragging)
        {
            var travelled = Math.Abs(point.X - PressPoint.X) + Math.Abs(point.Y - PressPoint.Y);
            if (travelled < Threshold)
            {
                return;
            }

            IsDragging = true;
        }

        Resolution = DropResolver.Resolve(payload.Kind, point.X, point.Y, candidates, _recentParentId);

        // Remember a winner only once the pointer has genuinely crossed out of its magnet radius. Doing it
        // on every move would make the stability bonus follow the pointer rather than the zones, and rule
        // 4 of the scoring exists to stop an indicator flickering between two equidistant gaps, not to
        // make it lag behind one.
        if (Resolution is { Magnetic: false } resolved)
        {
            _recentParentId = resolved.Candidate.ParentId;
        }
    }

    /// <summary>Applies the drop.</summary>
    /// <remarks>
    /// <para>
    /// Refuses when the pointer is outside the magnet radius of anything it could join.
    /// <see cref="DropResolver.Resolve"/> always names a nearest candidate, because a scorer with no
    /// winner cannot distinguish "nothing is near" from "nothing is near <em>that is acceptable</em>" —
    /// and the two need different answers. Without this rule a block released in the middle of an empty
    /// canvas snaps to whichever script happened to be closest, which is how a drag silently teleports a
    /// stack a foot away from where it was let go.
    /// </para>
    /// <para>
    /// The two zones that create rather than join are exempt, because "far from a block" is exactly what
    /// dropping a hat on empty canvas means.
    /// </para>
    /// </remarks>
    public DocumentEditResult Drop()
    {
        if (Payload is not { } payload || Resolution is not { } resolution)
        {
            Reset();
            return DocumentEditResult.Refused("Nothing to drop there.", "drop-nowhere");
        }

        if (!resolution.Magnetic && resolution.Candidate.Kind is not (DropTargetKind.Canvas or DropTargetKind.HatSlot))
        {
            Reset();
            return DocumentEditResult.Refused(
                "Drop it closer to a block. Let go over the gap it should go into.",
                "drop-out-of-range");
        }

        var result = DropPlan.Apply(
            _vm.Editor, _vm.Target, payload, resolution.Candidate, Pointer.X, Pointer.Y);

        Reset();
        return result;
    }

    /// <summary>Abandons the drag without touching the document.</summary>
    public void Cancel() => Reset();

    /// <summary>Forgets the drag, whatever state it was in.</summary>
    public void Reset()
    {
        Payload = null;
        Resolution = null;
        IsDragging = false;
        _preview = null;
        _recentParentId = null;
    }
}
