using System.Windows;
using System.Windows.Automation.Peers;
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

    /// <summary>
    /// The menu this tile opens on a right-click, or null until one is needed.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FrameworkElement.ContextMenu"/> on purpose. That property is left in place
    /// because removing it would change what a right-click means for assistive technology and for the
    /// automation tree, but nothing reads it: the tile opens <em>this</em> one from
    /// <see cref="OnPreviewMouseRightButtonDown"/>. Two menus on one route is the ambiguity this whole
    /// arrangement exists to avoid, so only one of them is ever populated and only one is ever shown.
    /// </remarks>
    private ContextMenu? _menu;

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

        // A context menu, empty. Not built here and not built in markup, because the tile lives four
        // template levels down and cannot reach the editor that owns the commands from its own data context.
        // The menu raises a routed event and the page — the one ancestor of every tile — fills it in. The
        // same shape as SelectionRequestedEvent, and for the same reason: WPF builds a routed event's route
        // from Source, and an object that is not in the tree is delivered to nobody, silently.
        //
        // Created here rather than on the first right-click because a lazy one races: WPF decides which
        // element to open a menu for as part of handling the right-button release, and a menu assigned
        // during the press is a menu this build cannot prove opens. The cost is one empty ContextMenu per
        // tile, which is a Popup that owns no window until it is shown — small beside the UserControl,
        // the two ItemsControls and the label row the tile already allocates.
        ContextMenu = new ContextMenu();
        ContextMenu.ContextMenuOpening += OnContextMenuOpening;

        // ...and the menu is opened from here rather than left to ContextMenuService, because relying on
        // the service was a guess and the guess was wrong. Driving the real window: right-clicking a block
        // did nothing at all - no menu, and the block was not even selected, so OnContextMenuOpening had
        // plainly never run. The service was never reaching this control's menu, and the reason is that
        // BlockWorkspace puts *its* menu on three elements that are all ancestors of every tile (the
        // scroller, the grid, and the drag surface the tiles are children of). Which of two ContextMenus
        // on the same route wins is not something this code controls, and no test can see it: a menu that
        // does not open fails silently and identically to a menu with nothing in it.
        //
        // So the tile handles the right button itself, asks the page to fill a menu it owns, and opens it.
        // One event, one menu, no dependence on which ancestor wins - and the block is selected first, so
        // a right-click behaves like a left-click for selection as well as for the menu, which is what
        // every other editor does and what Part 9.5's "right click | block | the tile's menu" assumes.
        PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
    }

    /// <summary>
    /// Fills the tile's own menu and opens it, on the right button.
    /// </summary>
    /// <param name="sender">The tile.</param>
    /// <param name="e">The press. Handled, because the menu this opens is the whole point of the gesture.</param>
    /// <remarks>
    /// The routed event is raised first so the page can populate <see cref="_menu"/>, and the menu is
    /// opened afterwards rather than from <c>ContextMenuOpening</c> - a handler that opens a menu from
    /// inside the opening event is opening it while WPF is still deciding how big it is, which is how a
    /// menu appears with no items in it.
    /// </remarks>
    private void OnPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Node is not { } node)
        {
            return;
        }

        // One menu per tile, reused. A fresh ContextMenu per right-click would be a Popup per gesture, and
        // the reason the original code allocated one per tile in the constructor was to avoid exactly that.
        _menu ??= new ContextMenu();
        _menu.Items.Clear();

        RaiseEvent(new BlockMenuRequestedEventArgs(MenuRequestedEvent, this, node, _menu));

        if (_menu.Items.Count == 0)
        {
            return;
        }

        _menu.PlacementTarget = this;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;

        e.Handled = true;
    }

    /// <summary>
    /// Asks the page to fill in this tile's context menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised from <see cref="ContextMenu.ContextMenuOpening"/> and deliberately <em>not</em> marking the
    /// event handled. WPF opens the menu itself once this returns, and the argument type is
    /// <see cref="ContextMenuEventArgs"/> rather than a plain routed one precisely so a handler can veto it —
    /// so marking it handled here would have been the one thing guaranteed to stop the menu opening, with no
    /// error anywhere.
    /// </para>
    /// <para>
    /// The event is not marked handled because there is nothing to stop. The tile's own menu is the one WPF
    /// is about to open and the one this event is about; a sibling handler that wanted a different menu
    /// would have replaced <see cref="Control.ContextMenu"/> before this fired, not fought over the event.
    /// </para>
    /// </remarks>
    private void OnContextMenuOpening(object? sender, ContextMenuEventArgs args)
    {
        if (Node is not { } node)
        {
            return;
        }

        RaiseEvent(new BlockMenuRequestedEventArgs(MenuRequestedEvent, this, node, (ContextMenu)sender!));
    }

    /// <summary>
    /// Raised when a tile's context menu is about to open, bubbling so one handler on the page can fill in
    /// every tile's.
    /// </summary>
    /// <remarks>
    /// A bubbling event carrying the menu itself rather than a request the page has to find, because the
    /// menu is a fresh object per tile and the only thing that knows which one is opening is the tile.
    /// </remarks>
    public static readonly RoutedEvent MenuRequestedEvent = EventManager.RegisterRoutedEvent(
        "MenuRequested",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(BlockTile));

    /// <summary>
    /// Selects this block the way a click does, for a keyboard or an automation client.
    /// </summary>
    /// <remarks>
    /// Public because <see cref="BlockTileAutomationPeer"/> is not in the visual tree and cannot raise a
    /// routed event by walking up from a <c>Source</c> it does not own — and a UI-automation client pressing
    /// a tile is a user, not a test.
    /// </remarks>
    public void RequestSelection()
    {
        TakeFocus();

        if (Node is { } node)
        {
            RaiseEvent(new BlockSelectedEventArgs(SelectionRequestedEvent, this, node));
        }
    }

    /// <summary>
    /// Raised when something that is not a click wants the selection changed, carrying which way.
    /// </summary>
    /// <remarks>
    /// A separate event from <see cref="SelectionRequestedEvent"/> rather than a flag on it, because "add
    /// this to what is selected" is a different gesture from "this is now the selection". The page's click
    /// handler must keep meaning the second, and a flag it had to read would put one boolean in the path of
    /// every click on the canvas.
    /// </remarks>
    public static readonly RoutedEvent SelectionModeRequestedEvent = EventManager.RegisterRoutedEvent(
        "SelectionModeRequested",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(BlockTile));

    /// <summary>
    /// Adds this block to the selection without dropping what is already selected.
    /// </summary>
    /// <remarks>
    /// The selection-item pattern's second verb, and it needs the editor to own a multi-selection at all.
    /// It did not: Part 18.1's rubber band has nowhere to put the blocks it touches, so this is what makes
    /// the peer's pattern more than a decoration.
    /// </remarks>
    public void RequestAddToSelection() => RaiseSelectionMode(BlockSelectionMode.Add);

    /// <summary>Removes this block from the selection.</summary>
    public void RequestRemoveFromSelection() => RaiseSelectionMode(BlockSelectionMode.Remove);

    /// <summary>Raises <see cref="SelectionModeRequestedEvent"/> with the mode asked for.</summary>
    private void RaiseSelectionMode(BlockSelectionMode mode)
    {
        if (Node is { } node)
        {
            RaiseEvent(new BlockSelectionModeEventArgs(SelectionModeRequestedEvent, this, node, mode));
        }
    }

    /// <summary>
    /// This tile's peer, so a screen reader and a UI-automation test can find the block by name.
    /// </summary>
    /// <remarks>
    /// Part 9.8 asks for a peer per tile and this is the whole of it. Without the override WPF supplies a
    /// peer for <see cref="UserControl"/>, which reports the element's type name as its name — so a screen
    /// reader read "user control" a hundred times down a stack, and a UI-automation test could not tell one
    /// block from another except by its position on screen.
    /// </remarks>
    protected override AutomationPeer OnCreateAutomationPeer() => new BlockTileAutomationPeer(this);

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