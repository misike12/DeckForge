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
/// One column per script, laid out left to right, inside Part 9.4's <c>ScrollViewer</c> -&gt; zoom
/// <c>ScaleTransform</c> -&gt; <c>PanTransform</c> -&gt; <c>Canvas</c>. This paragraph used to say the
/// transform and the absolute positions were "deliberately not here yet" and that zoom and pan were a
/// later phase - on a class that now has <see cref="View"/>, <see cref="ApplyView"/>, a scale transform, a
/// pan transform and three pan events. A header that tells the next contributor a feature is absent is
/// how a second implementation gets built beside the first.
/// </para>
/// <para>
/// The arithmetic is still Core's. This control applies a <see cref="Core.Visual.CanvasView"/> it is given
/// and measures what is on screen; it does not decide what a zoom or a pan means, because the drop
/// resolver, the minimap and the stage all need the same answer and three implementations of it would
/// disagree the first time any one of them changed.
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

        // The viewport's own size changing is the one measurement the minimap cannot do without: Core
        // scales the whole document into 240 by 150, so a viewport that has not been measured yet produces
        // no rectangles at all - and the page's first SizeChanged arrives before the surface has a size,
        // so nothing else would ever ask again. Driving the window found this as a thumbnail reading
        // "Nothing on the canvas yet" over a canvas full of blocks.
        //
        // Only on a size that actually changed: WPF raises SizeChanged more than once per layout pass, and
        // the subscriber redraws the minimap, which changes the minimap's layout, which raises SizeChanged
        // again. Left unguarded that is a feedback loop, and it ends in a stack overflow inside the visual
        // tree walk rather than in anything that names the cause.
        Surface.SizeChanged += (_, args) =>
        {
            var width = args.NewSize.Width;
            var height = args.NewSize.Height;

            if (Math.Abs(width - _viewportWidth) < 0.5 && Math.Abs(height - _viewportHeight) < 0.5)
            {
                return;
            }

            _viewportWidth = width;
            _viewportHeight = height;
            ViewportChanged?.Invoke();
        };
    }

    private double _viewportWidth;
    private double _viewportHeight;

    /// <summary>
    /// Raised when the canvas viewport's size changed, for anything that scales against it.
    /// </summary>
    /// <remarks>
    /// The same compromise as the static zoom and pan events, and for the same reason: a control cannot
    /// hold a reference to a view model that does not exist yet, and the page is what knows which one.
    /// </remarks>
    public event Action? ViewportChanged;

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
        Scroller.PreviewMouseDown += (_, args) => OnViewportInput(this, args);
        Scroller.PreviewMouseMove += (_, args) =>
        {
            if (PanOrigin is not null)
            {
                PanTo(args.GetPosition(Scroller));
                args.Handled = true;
            }
        };
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

    /// <summary>
    /// The canvas's zoom and pan, as Core's value.
    /// </summary>
    /// <remarks>
    /// The transform and the arithmetic are one object rather than two, because a <c>ScaleTransform</c> and a
    /// zoom factor that disagree by a factor of the zoom is exactly the bug that makes a canvas drop a block
    /// where the ghost is not. The workspace implements the host interface so the view model never has to
    /// ask a control where anything is.
    /// </remarks>
    public CanvasView View { get; private set; } = new();

    /// <summary>Applies a view to the canvas.</summary>
    /// <param name="view">The view to draw through.</param>
    public void ApplyView(CanvasView view)
    {
        View = view;

        ZoomTransform.ScaleX = view.ClampedZoom;
        ZoomTransform.ScaleY = view.ClampedZoom;
        PanTransform.X = -view.PanX * view.ClampedZoom;
        PanTransform.Y = -view.PanY * view.ClampedZoom;
    }

    /// <summary>
    /// Every block's rect in workspace units, for the minimap.
    /// </summary>
    /// <remarks>
    /// Read from the live tiles rather than recomputed from the document, because the minimap has to
    /// agree with what is on screen including anything the canvas has since adjusted — and because the
    /// tiles already paid for their own measurement to be drawn. The transform is taken to the surface
    /// rather than to the window, so the canvas's own pan and zoom do not leak into the numbers: Core works
    /// in workspace units, and a rect that arrived already scaled would be scaled twice.
    /// <para>
    /// This delegates to <see cref="CanvasHitTest.Rects"/>, which is the one walk. It was a second
    /// implementation of "where is everything", differing from the first in how it measured the origin and
    /// - worse - in what it put in <c>NotchY</c>: the block's height here, its bottom edge there. Two
    /// answers to one question is how the minimap and the drop resolver end up disagreeing about the same
    /// tile, and only the fact that the minimap does not read <c>NotchY</c> has been hiding it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<BlockRect> Rects => [.. CanvasHitTest.Rects(Surface).Values];

    /// <summary>
    /// What the user can see, in pixels.
    /// </summary>
    /// <remarks>
    /// The scroller's viewport, not the surface's own size. The surface is content-sized, so on a canvas
    /// narrower than the window its width is the content's and its height is the tallest column's - and
    /// zoom is centred on this number. Centring on the content instead of the window put the centre of the
    /// document off screen as soon as the zoom changed, and the canvas went blank on the first press of the
    /// zoom-out button. It also mis-drew the minimap's "you are here" rectangle, which is meant to be the
    /// part of the document on screen.
    /// </remarks>
    public (double Width, double Height) Viewport => (Scroller.ViewportWidth, Scroller.ViewportHeight);

    /// <summary>
    /// Zooms about the pointer on Ctrl+wheel, and pans on middle-drag.
    /// </summary>
    /// <remarks>
    /// Attached to the scroller rather than the surface, because the pointer may well be over a tile and a
    /// tile that swallows the wheel is a canvas that cannot be zoomed with the mouse over the thing being
    /// looked at. Middle-drag pans because it is the one drag a pointer has that means "move the view" and
    /// nothing else claims it.
    /// </remarks>
    private void OnViewportInput(object sender, MouseEventArgs args)
    {
        if (args is MouseWheelEventArgs wheel && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ZoomByWheel?.Invoke(this, new CanvasZoomEventArgs(wheel.Delta / 120, wheel.GetPosition(Scroller)));
            wheel.Handled = true;
            return;
        }

        if (args is not MouseButtonEventArgs button)
        {
            return;
        }

        if (button.ChangedButton == MouseButton.Middle)
        {
            // A press starts the pan and a release ends it; both arrive as the same event type, so the
            // state is the only thing that tells them apart. Reading a property that only exists on
            // MouseButtonEventArgs through MouseEventArgs is a compile error, which is the good outcome.
            if (button.ButtonState == MouseButtonState.Pressed)
            {
                PanStart?.Invoke(this, button.GetPosition(Scroller));
            }
            else
            {
                PanEnd?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised for a Ctrl+wheel notch, carrying the notch count and where the pointer is.</summary>
    /// <remarks>
    /// Instance events, not static ones. They were static because a control cannot hold a reference to a
    /// view model that does not exist yet - but a static event outlives every instance that subscribed, and
    /// the page subscribed with lambdas it held nowhere, so there was no way to unsubscribe. A second page
    /// (a re-created shell, a test host) doubled every notch and every pan delta, and each stale handler
    /// drove a canvas that was no longer on screen. <see cref="ViewportChanged"/> on this class was already
    /// the right shape; these four now match it, and the page adds on Loaded and removes on Unloaded.
    /// </remarks>
    public event EventHandler<CanvasZoomEventArgs>? ZoomByWheel;

    /// <summary>Raised when a middle-drag begins, with where it began.</summary>
    public event EventHandler<Point>? PanStart;

    /// <summary>Raised when a middle-drag ends.</summary>
    public event EventHandler? PanEnd;

    /// <summary>Where a middle-drag began, for the panner.</summary>
    public Point? PanOrigin { get; private set; }

    /// <summary>Moves the view with a middle-drag.</summary>
    /// <param name="position">Where the pointer is now, in the scroller's coordinates.</param>
    public void PanTo(Point position)
    {
        if (PanOrigin is not { } origin)
        {
            return;
        }

        PanBy?.Invoke(this, new Point(position.X - origin.X, position.Y - origin.Y));
        PanOrigin = position;
    }

    /// <summary>Raised while a middle-drag moves, with the delta in pixels.</summary>
    public event EventHandler<Point>? PanBy;

    /// <summary>Captures the pointer for a middle-drag, so the drag survives leaving the canvas.</summary>
    public void BeginPan(Point position)
    {
        PanOrigin = position;
        Surface.CaptureMouse();
    }

    /// <summary>Ends a middle-drag and gives the pointer back.</summary>
    public void EndPan()
    {
        PanOrigin = null;
        Surface.ReleaseMouseCapture();
    }

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
        var gaps = CanvasHitTest.Candidates(Surface, _vm.Columns)
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
    /// Brings a column into view, without selecting anything in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed because procedures are columns after the scripts, and the sample has eleven scripts before
    /// the first procedure — so choosing a procedure from the strip would otherwise select something the
    /// user cannot see, in a column past the right-hand edge of a window that does not scroll with the
    /// keyboard. The strip's whole job is to be an index into the canvas, and an index into something
    /// off-screen is not one.
    /// </para>
    /// <para>
    /// The element with the column as its data context is its own panel rather than its first tile,
    /// because a procedure with an empty body has no tile to find.
    /// </para>
    /// </remarks>
    public void ScrollToColumn(ColumnViewModel column)
    {
        var found = CanvasHitTest.DescendantsOf<FrameworkElement>(Surface)
            .FirstOrDefault(element => ReferenceEquals(element.DataContext, column));

        (found is not null ? found : Surface).BringIntoView();
    }
/// <summary>
    /// Puts the keyboard back on the canvas.
    /// </summary>
    /// <remarks>
    /// The selection if there is one, the canvas itself otherwise. A dismissed overlay has to leave focus
    /// somewhere the arrow keys do something, or "Esc closed the palette and now the canvas is dead" is
    /// the first thing a keyboard user concludes - and it is true, because the focused element is gone.
    /// </remarks>
    public void FocusCanvas()
    {
        if (_vm?.Selected is { } selected)
        {
            var tile = CanvasHitTest.Tiles(Surface)
                .FirstOrDefault(candidate => candidate.DataContext is BlockNodeViewModel node
                    && string.Equals(node.Id, selected.Id, StringComparison.Ordinal));

            if (tile is not null)
            {
                tile.TakeFocus();
                return;
            }
        }

        Focus();
    }

    /// <summary>
    /// Shows or hides the drag adornments for whatever the editor says is in progress.
    /// </summary>
    /// <remarks>
    /// Called from the page on every change notification, because the keyboard's carry mode is owned by the
    /// editor and the adorners are owned by this control.
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

        FitSurfaceToColumns();
    }

    /// <summary>
    /// Makes the drag surface as wide as its columns, so every one of them can be scrolled to.
    /// </summary>
    /// <remarks>
    /// A <c>Canvas</c> does not grow to its children the way a panel does, and the surface's fixed 1600
    /// pixels was therefore the hard limit on how far the scroll viewer could scroll — which is why
    /// choosing a procedure from the strip scrolled part of the way and stopped. Measured rather than
    /// computed, because the columns are 400 pixels wide plus a margin by layout and a block label that
    /// wraps would make one of them wider; guessing from the column count is the same estimate that was
    /// wrong to begin with.
    ///
    /// Skipped when nothing has been measured yet, because a rebuild lands here before the first layout
    /// pass and a width of zero would collapse the canvas for a frame.
    /// </remarks>
    private void FitSurfaceToColumns()
    {
        if (Surface.ActualWidth <= 0 || Surface.DesiredSize.Width <= 0)
        {
            return;
        }

        var widest = 0.0;

        foreach (var column in CanvasHitTest.DescendantsOf<FrameworkElement>(Surface))
        {
            if (column.DataContext is not ColumnViewModel || column.ActualWidth <= 0)
            {
                continue;
            }

            var right = column.TranslatePoint(new Point(column.ActualWidth, 0), Surface).X + 24;
            widest = Math.Max(widest, right);
        }

        if (widest > 0)
        {
            Surface.Width = widest;
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

/// <summary>A Ctrl+wheel notch: how many, and where the pointer was.</summary>
/// <param name="Notches">Positive zooms in.</param>
/// <param name="At">The pointer's position in the viewport's coordinates.</param>
public sealed class CanvasZoomEventArgs(int notches, Point at) : EventArgs
{
    /// <summary>Wheel notches, positive for zooming in.</summary>
    public int Notches { get; } = notches;

    /// <summary>Where the pointer is, so the zoom can be about it.</summary>
    public Point At { get; } = at;
}