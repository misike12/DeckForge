using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// One block, drawn: the silhouette in <see cref="OnRender"/> and the label and bodies in markup.
/// </summary>
/// <remarks>
/// <para>
/// The split is deliberate. The geometry needs the size the tile turned out to be, and a
/// <c>ControlTemplate</c> cannot ask the element it is templating how big that was — so the outline is
/// drawn in <see cref="OnRender"/>, where <c>ActualWidth</c> and <c>ActualHeight</c> are already known
/// and exact. What the markup holds is the header row and one <c>ItemsControl</c> per body, which is
/// what lets a body contain a tile that contains a body.
/// </para>
/// <para>
/// Nothing here decides anything about a block. The shape comes from the catalogue row, the geometry
/// from <see cref="BlockOutline"/> in Core, the label's split from <see cref="BlockLabel"/> in Core. The
/// control's whole job is to draw a rectangle of the right colour and let WPF lay out the words.
/// </para>
/// </remarks>
public partial class BlockTile : UserControl
{
    /// <summary>How wide the stroke around a block is.</summary>
    /// <remarks>
    /// Slightly over one pixel, because a hairline at exactly one device pixel disappears at some zoom
    /// levels and the notch stops reading as a notch.
    /// </remarks>
    private const double StrokeThickness = 1.25;

    /// <summary>How tall the lit band along the top of a block is.</summary>
    private const double SheenHeight = BlockOutline.HeaderHeight / 2;

    public BlockTile()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
        MouseLeftButtonDown += OnMouseLeftButtonDown;

        // Focusable, and taking focus on click, because the keyboard gestures in Part 9.5 are routed
        // through whatever holds keyboard focus. A `UserControl` is not focusable by default, so without
        // this a click selects a block and focus stays wherever it was - on the navigation list, or
        // nowhere - and every arrow key then goes to a control that has never heard of a block. The
        // symptom is a canvas where selection visibly works and the keyboard visibly does nothing.
        Focusable = true;
        IsTabStop = true;
    }

    /// <summary>
    /// Moves keyboard focus onto this block, for a click or for Tab.
    /// </summary>
    /// <remarks>
    /// <see cref="Focus"/> and not <c>Keyboard.Focus</c> on an inner element: focus has to land on the tile
    /// so that the page's <c>PreviewKeyDown</c> sees an event whose route passes through it, and so the
    /// selection ring is drawn by the element that actually has focus.
    /// </remarks>
    public void TakeFocus() => Focus();

    /// <summary>Whether the pointer is over this tile.</summary>
    /// <remarks>
    /// Part 9.3 lists hover beside focus and selection. It is the only one of the three a tile can know
    /// about on its own, and drawing it here rather than in a <c>ControlTemplate</c> trigger is what lets
    /// it be a thin stroke over the real silhouette instead of a rounded rectangle that does not match it.
    /// </remarks>
    public bool IsHovered => IsMouseOver;

    /// <summary>
    /// Raised when the block is clicked, bubbling so one handler on the page catches every tile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bubbling rather than a command because the tiles are created inside <c>DataTemplate</c>s the page
    /// never sees, and a command would have to be threaded through four nested templates to reach them.
    /// The page is an ancestor of all of them, so one handler there is enough.
    /// </para>
    /// <para>
    /// The block travels in its own event-argument type rather than as <c>Source</c>. WPF builds an
    /// event's route by walking up the visual tree from <c>Source</c>, and <c>Source</c> has to be the
    /// element raising it: put a view model there and the route is built from an object that is not in
    /// the tree, so the event is delivered to nobody at all — with no error anywhere, which is what made
    /// this look like the click simply not arriving.
    /// </para>
    /// </remarks>
    public static readonly RoutedEvent SelectionRequestedEvent = EventManager.RegisterRoutedEvent(
        "SelectionRequested",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(BlockTile));

    /// <summary>
    /// Raised when the breakpoint dot in the gutter is clicked, bubbling to the page.
    /// </summary>
    /// <remarks>
    /// A separate event from <see cref="SelectionRequestedEvent"/> rather than a flag on the selection
    /// event, because clicking the dot is not clicking the block: it must not move the selection, or the
    /// inspector would jump to a block the user did not choose while they were aiming at the gutter.
    /// </remarks>
    public static readonly RoutedEvent BreakpointRequestedEvent = EventManager.RegisterRoutedEvent(
        "BreakpointRequested",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(BlockTile));

    /// <summary>Raises <see cref="BreakpointRequestedEvent"/> for this block.</summary>
    private void Breakpoint_Click(object sender, RoutedEventArgs args)
    {
        if (Node is { } node)
        {
            // Handled, so the page does not also read the press as a click on the block itself: the press
            // lands on the gutter, and treating that as a selection is how a breakpoint dot becomes a
            // mysterious selection change.
            args.Handled = true;
            RaiseEvent(new BlockSelectedEventArgs(BreakpointRequestedEvent, this, node));
        }
    }

    private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
    {
        // Focus, then raise. Both are here and neither is enough alone: the click that selects a block has
        // to hand the keyboard to it as well, or "click a block, press Down, nothing moves" is what the
        // canvas does.
        TakeFocus();

        if (Node is { } node)
        {
            RaiseEvent(new BlockSelectedEventArgs(SelectionRequestedEvent, this, node));
        }
    }

    /// <summary>The text colour for this block's category.</summary>
    /// <remarks>
    /// Resolved here rather than bound through a converter, because the key is computed from the
    /// category: neither a <c>Path</c> nor a <c>TextBlock</c> can name a resource whose name it does not
    /// know.
    /// </remarks>
    public Brush Ink
    {
        get => (Brush)GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public static readonly DependencyProperty InkProperty =
        DependencyProperty.Register(
            nameof(Ink),
            typeof(Brush),
            typeof(BlockTile),
            new PropertyMetadata(Brushes.White));

    /// <summary>Whether the block is drawn switched off.</summary>
    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public static readonly DependencyProperty IsMutedProperty =
        DependencyProperty.Register(
            nameof(IsMuted),
            typeof(bool),
            typeof(BlockTile),
            new PropertyMetadata(false, OnLookChanged));

    /// <summary>Whether this tile is the selected block, which draws a ring in the accent colour.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(BlockTile),
            new PropertyMetadata(false, OnLookChanged));

    /// <summary>
    /// Whether the stage is standing on this block, which draws the pulse ring.
    /// </summary>
    /// <remarks>
    /// A ring rather than an opacity change: the stage has to be findable on a canvas where a hundred
    /// blocks are the same colour, and an accent ring around the one that is running is findable in a way
    /// a slightly paler block is not.
    /// </remarks>
    public bool IsCurrent
    {
        get => (bool)GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public static readonly DependencyProperty IsCurrentProperty =
        DependencyProperty.Register(
            nameof(IsCurrent),
            typeof(bool),
            typeof(BlockTile),
            new PropertyMetadata(false, OnLookChanged));

    /// <summary>The node this tile draws, or null before a DataContext has arrived.</summary>
    private BlockNodeViewModel? Node => DataContext as BlockNodeViewModel;

    /// <summary>The outline drawn most recently, cached because a render is not free.</summary>
    private Geometry? _outline;

    /// <summary>The node whose selection state this tile is currently mirroring.</summary>
    private BlockNodeViewModel? _watched;

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        // The outline is in the tile's own coordinates, so it is only correct for one size. Dropping it
        // rather than scaling it is what keeps a resize exact instead of a fraction of a pixel out.
        _outline = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        if (Node is not { } node || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var outline = BlockOutline.Create(node.Shape, ActualWidth, ActualHeight);
        _outline ??= BlockShapeGeometry.ToGeometry(outline);
        if (_outline is null)
        {
            return;
        }

        var stroke = new Pen(Resolve(node.StrokeKey) ?? Brushes.Transparent, StrokeThickness);
        context.DrawGeometry(Resolve(node.FillKey), stroke, _outline);

        DrawSheen(context, node);
        DrawState(context);
    }
    /// <summary>
    /// The lit band along the top of a block.
    /// </summary>
    /// <remarks>
    /// Part 9.3 asks for an inner top highlight. It is the silhouette again, clipped to a strip, rather
    /// than a rectangle, because a rectangle would spill across a C-block's mouth and wash out the body
    /// the mouth exists to show. Reporters are skipped: their whole shape is a pill and a band across it
    /// reads as a rendering fault.
    /// </remarks>
    private void DrawSheen(DrawingContext context, BlockNodeViewModel node)
    {
        if (node.Shape is BlockShape.Reporter or BlockShape.BooleanReporter)
        {
            return;
        }

        if (Resolve("Liquid.BlockSheenBrush") is not { } sheen || _outline is null)
        {
            return;
        }

        var band = new RectangleGeometry(new Rect(0, 0, ActualWidth, Math.Min(ActualHeight, SheenHeight)));
        band.Freeze();

        var clipped = new CombinedGeometry(GeometryCombineMode.Intersect, band, _outline);
        clipped.Freeze();
        context.DrawGeometry(sheen, null, clipped);
    }

    /// <summary>The selected and disabled states, which Part 9.3 lists beside hover and focus.</summary>
    private void DrawState(DrawingContext context)
    {
        if (IsHovered && Resolve("Liquid.BlockSheenBrush") is { } hover)
        {
            context.PushOpacity(0.30);
            context.DrawGeometry(hover, null, _outline!);
            context.Pop();
        }

        if (IsMuted)
        {
            context.PushOpacity(0.45);
            context.DrawGeometry(Resolve("Liquid.BlockRailBrush"), null, _outline!);
            context.Pop();
        }

        if (IsSelected && Resolve("Liquid.AccentBrush") is { } accent)
        {
            var ring = new Pen(accent, 2) { LineJoin = PenLineJoin.Round };
            context.DrawGeometry(null, ring, _outline!);
        }

        if (IsCurrent && Resolve("Liquid.DangerBrush") is { } current)
        {
            // Drawn outside the silhouette as well as on it, so it reads on a block at the far left of the
            // canvas with nothing around it to contrast with.
            var outer = new Pen(current, 4) { LineJoin = PenLineJoin.Round };
            context.DrawGeometry(null, outer, _outline!);

            var inner = new Pen(Brushes.White, 1.5) { LineJoin = PenLineJoin.Round };
            context.DrawGeometry(null, inner, _outline!);
        }
    }

    /// <summary>Re-reads everything that comes from the theme after it changes underneath the page.</summary>
    private void Refresh()
    {
        Watch(Node);

        if (Node is { } node)
        {
            Ink = Resolve(node.InkKey) ?? Brushes.White;
            IsMuted = node.IsDisabled;
            IsSelected = node.IsSelected;
            IsCurrent = node.IsCurrent;
            ToolTip = $"{node.Title}\n{node.Summary}";
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Follows a node's selection state.
    /// </summary>
    /// <remarks>
    /// Selection is owned by the page and arrives as a change notification on the node, so a tile has to
    /// listen for it: a tile that read the flag once would never show the ring again after the first
    /// click. The previous node is released first, because the palette rebuilds its rows on every search
    /// and a tile left holding an old one would keep a live subscription to an object nothing shows.
    /// </remarks>
    private void Watch(BlockNodeViewModel? node)
    {
        if (ReferenceEquals(_watched, node))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnNodeChanged;
        }

        _watched = node;

        if (_watched is not null)
        {
            _watched.PropertyChanged += OnNodeChanged;
        }
    }

    private void OnNodeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BlockNodeViewModel.IsSelected) && sender is BlockNodeViewModel node)
        {
            IsSelected = node.IsSelected;
            IsCurrent = node.IsCurrent;
            InvalidateVisual();
        }
    }

    private static void OnLookChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is BlockTile tile)
        {
            tile.InvalidateVisual();
        }
    }

    /// <summary>
    /// A theme brush by key, or null when the theme does not have one.
    /// </summary>
    /// <remarks>
    /// Null rather than a grey stand-in. A block whose category has no tokens is a document written by a
    /// newer build, and drawing it grey says "not themed yet" while drawing nothing says "not drawn" —
    /// two different bugs to chase.
    /// </remarks>
    private static Brush? Resolve(string key) => Application.Current?.TryFindResource(key) as Brush;
}