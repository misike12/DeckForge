using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The canvas: the scripts of the open target, side by side, and the host for dragging them.
/// </summary>
/// <remarks>
/// <para>
/// One column per script, laid out left to right. Part 9.4 describes a <c>ScrollViewer</c> around a zoom
/// <c>ScaleTransform</c> around a <c>Canvas</c> holding one view per script; the transform and the absolute
/// positions are deliberately not here yet. Zoom and pan are Phase 10, and building them now means a
/// second layout that Phase 10 throws away — plus a coordinate system nothing is tested against, since the
/// drop resolver's rectangles only become real here, where the blocks actually are.
/// </para>
/// <para>
/// This control owns the pointer half of Part 9.5 and nothing else: it translates mouse events into
/// surface coordinates, hands them to <see cref="DragController"/>, and shows the result. Every decision
/// about what a drop <em>means</em> lives in the controller and every decision about <em>where things
/// are</em> lives in <see cref="CanvasHitTest"/>, so the drag can be tested with no window at all.
/// </para>
/// <para>
/// Pointer capture, not OLE drag, for canvas to canvas. Part 9.5 asks for it for a reason that turned out
/// to be the deciding one: OLE hands the drag image to the operating system and gives back only "here is
/// a position", which is enough for a palette drag and not enough for a ghost that has to line its notch
/// up with a tab sixty times a second.
/// </para>
/// </remarks>
public partial class BlockWorkspace : UserControl
{
    private VisualEditorViewModel? _vm;
    private DragController? _drag;
    private DragAdorner? _adorner;
    private AutoScroll? _scroll;

    public BlockWorkspace()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Surface.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        Surface.PreviewMouseMove += OnPreviewMouseMove;
        Surface.PreviewMouseLeftButtonUp += OnPreviewMouseUp;

        // Losing capture only stops the auto-scroller. It is not an abort: WPF releases the capture as
        // part of handling the button-up, and that notification can arrive before the up event reaches
        // the element - so aborting here discarded the payload and the drop silently never happened.
        // Abandoning a drag is detected by the pointer leaving, or by Escape, both of which are gestures
        // rather than consequences of WPF's own bookkeeping.
        Surface.LostMouseCapture += (_, _) => _scroll?.Stop();
    }

    /// <summary>
    /// Wires a drag controller up when the page's view model arrives.
    /// </summary>
    /// <remarks>
    /// Built here rather than passed in, because the adorner has to be created from the
    /// <c>AdornerLayer</c> and the layer does not exist until the control is in a window. The control is
    /// therefore the only thing that can own the ghost, and the controller stays free of WPF so it can be
    /// tested.
    /// </remarks>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not VisualEditorViewModel vm)
        {
            return;
        }

        _vm = vm;
        _drag = new DragController(vm);
        _scroll = new AutoScroll(Scroller);
    }

    /// <summary>Starts a drag from a palette row, which begins outside this control.</summary>
    /// <remarks>
    /// <para>
    /// Public because Part 9.5's two input paths meet here: a palette press crosses a control boundary, so
    /// the row hands over the press point in surface coordinates and this control takes over the pointer.
    /// The palette cannot capture the pointer itself and have the events arrive here, and it cannot run the
    /// drag either — the ghost needs this control's adorner layer.
    /// </para>
    /// <para>
    /// The capture is the part that matters. Without it a drag out of the palette only reaches this control
    /// once the pointer happens to cross the canvas, so a drag that stayed in the palette, or one that went
    /// straight past the canvas to the inspector, received no move events at all and silently did nothing —
    /// no error, no refusal, no ghost. Capturing here rather than in the palette is still correct: this is
    /// called from inside the routed press, which is when WPF will honour a capture for an element that did
    /// not receive the button-down itself.
    /// </para>
    /// </remarks>
    public void BeginDragFromPalette(BlockNodeViewModel row, Point surfacePoint)
    {
        if (_drag is null || _vm is null)
        {
            return;
        }

        _drag.PressPalette(row, surfacePoint);
        Attach();
        Surface.CaptureMouse();
    }

    /// <summary>The drag in progress, for the page's undo button and the workspace's own state.</summary>
    public DragController? Drag => _drag;

    /// <summary>
    /// The element every pointer position is expressed in.
    /// </summary>
    /// <remarks>
    /// Public because the palette's press arrives in the palette's coordinates and has to be translated
    /// into these before anything else looks at it. Exposing the surface rather than doing the
    /// translation inside <see cref="BeginDragFromPalette"/> would leave the caller passing a point in a
    /// coordinate system the method does not know, which is how a drag ends up starting 180 pixels off.
    /// </remarks>
    public FrameworkElement SurfaceElement => Surface;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        var point = ToSurface(args.GetPosition(Surface));
        var hit = CanvasHitTest.BlockAt(Surface, point);

        if (_drag is null || _vm is null)
        {
            return;
        }

        var single = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        if (hit is not { } node)
        {
            return;
        }

        _drag.Press(node, point, single);
        _vm.Select(node);
        Attach();
        Surface.CaptureMouse();

        // Focus the tile here rather than leaving it to the tile's own MouseLeftButtonDown, because this
        // is a *preview* handler and it marks the event handled - which stops the bubbling one from being
        // raised at all. The tile's focus-on-click therefore never ran, the keyboard went wherever it had
        // been, and every arrow key addressed something other than the block the user had just clicked.
        // Selecting and focusing belong together, and this is the only place both can happen.
        if (CanvasHitTest.TileAt(Surface, point) is { } tile)
        {
            tile.TakeFocus();
        }

        args.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs args)
    {
        if (_drag is { Payload: not null } && _vm is not null)
        {
            var point = ToSurface(args.GetPosition(Surface));
            _drag.Move(point, Candidates());
            _adorner?.InvalidateVisual();
            _scroll?.Update(args.GetPosition(Scroller), _drag.IsDragging);
            args.Handled = true;
            return;
        }

        _scroll?.Update(args.GetPosition(Scroller), dragging: false);
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (_drag is not { Payload: not null } || _vm is null)
        {
            return;
        }

        if (_drag.IsDragging)
        {
            // Re-resolve against the position of the release rather than trusting the last move: on a fast
            // drag the release can arrive with no move event after the last one, and dropping into the gap
            // the pointer passed through 80ms ago is not what anybody means.
            _drag.Move(ToSurface(args.GetPosition(Surface)), Candidates());

            var result = _drag.Drop();
            if (!result.Applied && result.Problem is { } problem)
            {
                _vm.Report(problem);
            }
        }

        Abort();
        args.Handled = true;
    }


    /// <summary>
    /// How far sideways a gap may be from the pointer and still count as a candidate at all.
    /// </summary>
    /// <remarks>
    /// Wider than a block so that a pointer anywhere over a stack still has its gaps, and narrower than
    /// the gap between two script columns so that a pointer over one script never has to be told apart
    /// from the neighbouring one. Filtering first also improves the refusal: with nothing in reach the
    /// drop says "nothing to drop there" rather than silently choosing whichever script happened to share
    /// a y coordinate.
    /// </remarks>
    private const double HorizontalReach = 240;

    /// <summary>
    /// Every candidate the resolver may choose from, in surface coordinates.
    /// </summary>
    /// <remarks>
    /// Rebuilt on every pointer move, which is what the Appendix H budget of 2ms is about, and it is why
    /// the canvas enumerates only what is on screen: O(blocks on screen), not O(document). At 500 tiles
    /// this is a few hundred rectangles and a list concatenation.
    /// </remarks>
    private IReadOnlyList<DropCandidate> Candidates()
    {
        if (_vm is null)
        {
            return [];
        }

        // Only gaps the pointer could plausibly mean. Part 9.5 lists "horizontal overlap between the
        // dragged footprint and the target stack" as a scoring rule and Part 9.5.1 says where it lives:
        // enforced here when enumerating candidates, not in the scorer. It matters because of the magnet -
        // the scorer measures it with a weighted distance of |dy| + |dx| / 2 against a forty-pixel
        // radius, so a pointer sitting sixty pixels to the right of a stack's left edge, which is still
        // unambiguously over that stack, spends half its budget on the horizontal term and falls outside
        // the magnet. The scorer cannot see the mistake: it is handed every gap in the document and asked
        // for the closest, and half of that distance is not a thing it can weigh differently.
        var pointerX = _drag?.Pointer.X ?? 0;
        var gaps = CanvasHitTest.Candidates(Surface, _vm.Target.Scripts)
            .Where(candidate => Math.Abs(candidate.X - pointerX) <= HorizontalReach);

        var slots = CanvasHitTest.SlotCandidates(Surface);

        // Free canvas: a hat dropped on empty space starts a script, which is the only thing that can.
        // Placed at the pointer rather than at a fixed corner, because the script's own position is
        // recorded from it and a script that lands somewhere other than where it was dropped is a
        // nuisance to find.
        if (_drag is { IsDragging: true, Payload.Kind: var kind } && BlockCatalog.Find(kind) is { IsHat: true })
        {
            var free = new DropCandidate(DropTargetKind.Canvas, string.Empty, X: _drag.Pointer.X, Y: _drag.Pointer.Y);
            return [.. gaps, .. slots, free];
        }

        return [.. gaps, .. slots];
    }

    /// <summary>
    /// Brings a block into view and gives it keyboard focus.
    /// </summary>
    /// <param name="blockId">The block's id.</param>
    /// <remarks>
    /// Both halves matter and they are one method because they are one gesture. The arrow keys move the
    /// selection, and a selection that has walked off the edge of a scroll viewer is a selection the user
    /// cannot see; and a block that is selected but not focused is a block the next keystroke will not
    /// reach, because the keyboard goes to focus rather than to selection.
    /// </remarks>
    public void ScrollTo(string blockId)
    {
        var tile = CanvasHitTest.Tiles(Surface)
            .FirstOrDefault(candidate => candidate.DataContext is BlockNodeViewModel node
                && string.Equals(node.Id, blockId, StringComparison.Ordinal));

        if (tile is null)
        {
            return;
        }

        tile.BringIntoView();
        tile.TakeFocus();
    }

    /// <summary>
    /// Shows or hides the drag adornments for whatever the editor says is in progress.
    /// </summary>
    /// <remarks>
    /// Called from the page on every change notification, because the keyboard's carry mode is owned by the
    /// editor and not by any pointer event. One adorner serves both paths and its <c>OnRender</c> decides
    /// which of them to draw, so "is anything in hand" is one question with one answer rather than two
    /// adorners that have to be kept in step.
    /// </remarks>
    public void Refresh()
    {
        // Re-focus the selected block, because every edit rebuilds the canvas and the rebuild replaces
        // every tile. Without this the keyboard has focus on an element that no longer exists, WPF drops
        // it, and the *next* keystroke goes to whatever else claims focus - which is how "Ctrl+Z did
        // nothing" happens one keystroke after a drag that worked perfectly.
        //
        // Guarded on "something is selected" and on the tile actually being found, so a selection that
        // has left the document does not spin here trying to focus nothing.
        if (_vm?.Selected is { } selected)
        {
            var tile = CanvasHitTest.Tiles(Surface)
                .FirstOrDefault(candidate => candidate.DataContext is BlockNodeViewModel node
                    && string.Equals(node.Id, selected.Id, StringComparison.Ordinal));

            if (tile is not null && !tile.IsKeyboardFocused)
            {
                tile.TakeFocus();
            }
        }

        var wanted = _vm is not null && InHand();

        if (wanted)
        {
            Attach();
            _adorner?.InvalidateVisual();
        }
        else if (_adorner is not null && AdornerLayer.GetAdornerLayer(Surface) is { } layer)
        {
            layer.Remove(_adorner);
            _adorner = null;
        }
    }

    /// <summary>Whether a pointer drag or a keyboard carry is in progress.</summary>
    private bool InHand() => _drag is { IsActive: true } || _vm is { IsCarrying: true };

    /// <summary>Puts the ghost in the adorner layer, once one exists.</summary>
    private void Attach()
    {
        if (_adorner is not null || _drag is null || _vm is null)
        {
            return;
        }

        if (AdornerLayer.GetAdornerLayer(Surface) is not { } layer)
        {
            return;
        }

        _adorner = new DragAdorner(Surface, _drag, _vm);
        layer.Add(_adorner);
    }

    /// <summary>Ends a drag without applying anything and takes the ghost away.</summary>
    private void Abort()
    {
        _drag?.Cancel();
        _scroll?.Stop();

        if (_adorner is not null && AdornerLayer.GetAdornerLayer(Surface) is { } layer)
        {
            layer.Remove(_adorner);
            _adorner = null;
        }

        if (Surface.IsMouseCaptureWithin)
        {
            Surface.ReleaseMouseCapture();
        }
    }

    /// <summary>A point in this control's coordinates, expressed on the drag surface.</summary>
    private Point ToSurface(Point point) => Surface.TranslatePoint(point, Surface);

    /// <summary>
    /// Cancels a drag in progress when the pointer leaves the canvas entirely.
    /// </summary>
    /// <remarks>
    /// The alternative is a drag that survives its own mouse capture: the pointer is released outside the
    /// window, the capture ends, and a drag controller still holding a run would apply it on the next
    /// click anywhere in the application.
    /// </remarks>
    protected override void OnMouseLeave(MouseEventArgs args)
    {
        if (_drag is { IsDragging: true })
        {
            Abort();
        }

        base.OnMouseLeave(args);
    }
}