using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.Services;

namespace DeckForge.App.Pages;

/// <summary>
/// The Visual page: a category rail, a searchable palette, the canvas and a diagnostics pane.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton, like every other page, so the sample document on the canvas survives
/// navigation. That matters more here than on most pages: a canvas is the one thing a user is most
/// likely to lose work in, and the defect this repository already recorded — a canvas silently
/// reloaded, and discarded what was on it, on plain navigation — is why <see cref="IRefreshOnNavigate"/>
/// exists and why nothing here reloads.
/// </para>
/// <para>
/// Phase 3 is a viewer. There is nothing to refresh, so <see cref="RefreshOnNavigate"/> is empty by
/// design rather than unfinished: Phase 5 replaces it with the rule from Part 9.1, reload only when the
/// workspace actually changed.
/// </para>
/// </remarks>
public partial class VisualEditorPage : Page, IRefreshOnNavigate
{
    private readonly ViewModels.Visual.VisualEditorViewModel _vm;

    public VisualEditorPage(ViewModels.Visual.VisualEditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Attached from code rather than markup. The tiles live inside data templates four levels down,
        // so the event-syntax on the Page root would be the only place markup could reach them - and the
        // markup compiler cannot see a routed event declared in the assembly it is compiling, which is a
        // build failure with a message about a property that plainly exists.
        AddHandler(
            Controls.Blocks.BlockTile.SelectionRequestedEvent,
            new RoutedEventHandler(Tile_SelectionRequested));

        AddHandler(
            Controls.Blocks.InputSlotView.SlotClickedEvent,
            new RoutedEventHandler(Slot_Clicked));

        Palette.RowPressed += Palette_RowPressed;
        PreviewKeyDown += OnPreviewKeyDown;
        _vm.PropertyChanged += (_, args) => Workspace.Refresh();
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width);
        ApplyLayout(ActualWidth);
    }

    /// <summary>
    /// The page width at which the canvas gets a column of its own beside the other three panels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured against the page, not the window: the navigation rail takes 220 of whatever the window
    /// is, so a 1240-pixel window leaves this page 1020 and the four-column layout does not fit in it.
    /// </para>
    /// <para>
    /// 1080 is the narrowest page that still has room for every panel's minimum — 150 for the rail, 280
    /// for the palette, 240 for the canvas and 300 for the diagnostics pane is 970, and the canvas keeps
    /// the rest. Below it the canvas drops to a row under the other three, where the same three panels
    /// still fit inside the application's minimum window of 960 less the rail and the page margin.
    /// </para>
    /// </remarks>
    private const double WideLayoutWidth = 1080;

    private bool? _wide;

    /// <summary>
    /// Places the four panels, one way for a wide page and another for a narrow one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A breakpoint rather than four collapse buttons, because a button that hides a panel has to be
    /// found again to bring the panel back, and at the size this layout exists for there is no room to
    /// put one per panel. Below the breakpoint the canvas drops to a row of its own under the rail, the
    /// palette and the diagnostics pane — which is the arrangement Part 9.2's drawing describes once the
    /// panels stop fitting side by side.
    /// </para>
    /// <para>
    /// This was found by driving the window, not by reading the markup: with the four columns written
    /// out as the design has them, the inspector was drawn past the right-hand edge of a 960-pixel
    /// window with no way to reach it, because this application has no horizontal scrolling. The
    /// fixed-column budget in <c>XamlMarkupTests</c> cannot see it either — every column here is
    /// proportional — which is why the arrangement is asserted by looking at it.
    /// </para>
    /// <para>
    /// Re-applied only when the mode changes, so dragging the window edge across the breakpoint does not
    /// rebuild the grid on every frame.
    /// </para>
    /// </remarks>
    private void ApplyLayout(double width)
    {
        var wide = width >= WideLayoutWidth;
        if (_wide == wide)
        {
            return;
        }

        _wide = wide;

        Layout.RowDefinitions.Clear();
        Layout.ColumnDefinitions.Clear();

        Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Layout.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(wide ? 1 : 0.58, GridUnitType.Star),
        });

        if (!wide)
        {
            Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        // The title and its Docs button share one row of their own, so the title wraps to whatever is
        // left rather than competing with the panels for columns. A star column here would have been the
        // obvious thing and is exactly what broke it: the panels' fixed columns took everything, the star
        // column was left with sixteen pixels, and the heading rendered one letter per line.

        void Panel(double pixels, int minimum) => Layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(pixels),
            MinWidth = minimum,
        });

        if (wide)
        {
            Panel(150, 130);
            Panel(280, 200);
            Layout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 240,
            });
            Panel(300, 220);
        }
        else
        {
            Panel(130, 120);
            Panel(230, 180);
            Panel(250, 200);
        }

        Place(HeaderRow, 0, 0, Layout.ColumnDefinitions.Count);

        if (wide)
        {
            Place(RailPanel, 1, 0);
            Place(PalettePanel, 1, 1);
            Place(CanvasPanel, 1, 2);
            Place(InspectorPanel, 1, 3);
        }
        else
        {
            Place(RailPanel, 1, 0);
            Place(PalettePanel, 1, 1);
            Place(InspectorPanel, 1, 2);
            Place(CanvasPanel, 2, 0, Layout.ColumnDefinitions.Count);
        }
    }

    /// <summary>Puts one panel in a cell, clearing the cell properties it is not using.</summary>
    private static void Place(FrameworkElement element, int row, int column, int columnSpan = 1, int rowSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        Grid.SetRowSpan(element, rowSpan);
    }

    /// <summary>Does nothing yet, on purpose.</summary>
    /// <remarks>
    /// The document here is the sample, not a file, so there is nothing to reload and nothing that could
    /// be stale. When Phase 5 loads from a workspace this becomes a reload guarded by "the workspace
    /// changed", which is the shape <see cref="BlockActionViewModel"/> already uses.
    /// </remarks>
    public void RefreshOnNavigate()
    {
    }

    /// <summary>
    /// Selects the clicked block for the inspector.
    /// </summary>
    /// <remarks>
    /// One handler for the whole page, because the tiles are created inside data templates four levels
    /// down and each one cannot reach the page's view model through its own data context — it holds a
    /// block, not the editor. Part 9.6's inspector, Part 9.7's "click a diagnostic to select the block"
    /// and Phase 8's breakpoints all start here.
    /// </remarks>
    private void Tile_SelectionRequested(object sender, RoutedEventArgs args)
    {
        if (args is Controls.Blocks.BlockSelectedEventArgs selected)
        {
            _vm.Select(selected.Block);
            Workspace.ScrollTo(selected.Block.Id);
        }
    }

    /// <summary>
    /// Selects the block that owns a clicked hole, and asks the inspector for that slot's row.
    /// </summary>
    /// <remarks>
    /// The one case where a click on the canvas names something more specific than a block. The argument
    /// carries the slot, because the bubbling event's <c>Source</c> is the hole's inner border and knows
    /// nothing about which slot it is standing in for.
    /// </remarks>
    private void Slot_Clicked(object sender, RoutedEventArgs args)
    {
        if (args is Controls.Blocks.SlotClickedEventArgs clicked)
        {
            _vm.SelectSlot(clicked.Block.Id, clicked.Slot.Name);
        }
    }

    /// <summary>
    /// Drives a numeric slot's spinner, from a button whose <c>Tag</c> is the row.
    /// </summary>
    /// <remarks>
    /// The arithmetic is on the view model rather than in the markup because there are two steppers and a
    /// toggle, and three copies of "read the number, add one, clamp, write it back" is three chances to get
    /// the clamping wrong. The row is named by <c>Tag</c> rather than by a binding because the binding
    /// would have to reach up out of the template to find the list.
    /// </remarks>
    private void Slot_Increment(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ViewModels.Visual.SlotEditor row })
        {
            row.Number = row.Number + 1;
        }
    }

    /// <summary>Steps a numeric slot down, by the same route.</summary>
    private void Slot_Decrement(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ViewModels.Visual.SlotEditor row })
        {
            row.Number = row.Number - 1;
        }
    }

    /// <summary>Flips a boolean slot from the inspector's toggle.</summary>
    private void Slot_Toggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ViewModels.Visual.SlotEditor row })
        {
            row.Boolean = !row.Boolean;
        }
    }

    /// <summary>Empties a slot from the inspector's clear button.</summary>
    private void Slot_Clear(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ViewModels.Visual.SlotEditor row })
        {
            row.ClearCommand.Execute(null);
        }
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/actions");

    /// <summary>
    /// Starts a drag from a palette row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The press arrives here rather than being handled inside the palette because the drag has to be
    /// drawn in the workspace's adorner layer — the canvas is the only element that can draw over both
    /// panels without either knowing the other exists, and a ghost clipped to the palette is a ghost that
    /// disappears the moment the pointer leaves the panel, which is the one place it is needed.
    /// </para>
    /// <para>
    /// The point arrives in the palette's coordinates and is translated into the workspace's surface,
    /// because that is the coordinate system the resolver, the hit test and the ghost all speak.
    /// Translating here rather than inside the workspace keeps the workspace's entry point in one
    /// coordinate system no matter which surface the drag came from.
    /// </para>
    /// </remarks>
    private void Palette_RowPressed(ViewModels.Visual.BlockNodeViewModel row, Point point)
    {
        _vm.Select(row);

        if (Workspace.SurfaceElement is not { } surface)
        {
            return;
        }

        Workspace.BeginDragFromPalette(row, Palette.TranslatePoint(point, surface));
    }

    /// <summary>The palette, named so a press on a row can find the workspace.</summary>
    private Controls.Blocks.PaletteList Palette => (Controls.Blocks.PaletteList)PalettePanel.Child;

    /// <summary>Undoes. The editor raises Changed, the page rebuilds, and the inspector follows.</summary>
    private void Undo_Click(object sender, RoutedEventArgs e) => _vm.UndoCommand.Execute(null);

    /// <summary>Redoes, for the same reason.</summary>
    private void Redo_Click(object sender, RoutedEventArgs e) => _vm.RedoCommand.Execute(null);

    /// <summary>
    /// Every keyboard gesture in Part 9.5, on one handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the page rather than on the tiles, for the same reason the click is: the tiles live inside data
    /// templates four levels down and each one cannot reach the editor from its own data context. A page
    /// that is an ancestor of all of them needs exactly one handler.
    /// </para>
    /// <para>
    /// <c>Preview</c> rather than bubbling, because the arrow keys have to be ours before the palette's
    /// search box or a scroll viewer sees them — a Ctrl+Down that a <c>ScrollViewer</c> consumed as "page
    /// down" would move the block and scroll the canvas at once.
    /// </para>
    /// <para>
    /// Every decision is delegated to <see cref="KeyboardMoves"/> in Core, which is where they can be
    /// tested. This handler's whole job is turning keystrokes into method calls and marking them handled.
    /// </para>
    /// </remarks>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (e.Key == Key.Escape)
        {
            // Escape cancels a keyboard drag before anything else, which is the one key that has to work
            // with the worst possible state: a user holding a block they cannot get rid of.
            if (_vm.CancelKeyboardDrag())
            {
                e.Handled = true;
            }

            return;
        }

        // Undo and redo come before the carrying check, and they work whether or not a block is in hand.
        //
        // The carrying check is the reason this is not simply further down the list: a user who picks a
        // block up, moves the cursor a few zones, and then realises they wanted to undo the edit they made
        // before picking it up has no way to ask, because the carry swallows every key. Being able to undo
        // while holding something is what makes the carry mode safe to explore in — you can always get
        // back to where you were.
        if (ctrl && e.Key == Key.Z)
        {
            if (shift)
            {
                _vm.RedoCommand.Execute(null);
            }
            else
            {
                _vm.UndoCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Y)
        {
            _vm.RedoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_vm.IsCarrying)
        {
            if (HandleCarryingKey(e))
            {
                e.Handled = true;
            }

            return;
        }

        if (ctrl && e.Key == Key.D)
        {
            _vm.DuplicateSelected();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up when ctrl:
                _vm.MoveSelected(up: true);
                e.Handled = true;
                break;

            case Key.Down when ctrl:
                _vm.MoveSelected(up: false);
                e.Handled = true;
                break;

            case Key.Up:
                _vm.StepSelection(up: true);
                e.Handled = true;
                break;

            case Key.Down:
                _vm.StepSelection(up: false);
                e.Handled = true;
                break;

            case Key.Delete:
            case Key.Back:
                _vm.DeleteSelected();
                e.Handled = true;
                break;

            case Key.Space:
                // Pick up. The only key that needs something to be selected first, so it declines
                // silently rather than doing nothing with an error.
                _vm.PickUpSelected();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    /// <summary>The keys that mean something while a block is in hand.</summary>
    /// <returns>Whether the key was a gesture and should be marked handled.</returns>
    private bool HandleCarryingKey(KeyEventArgs e)
    {
        var backwards = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                return _vm.DropCarried();

            case Key.Escape:
                return _vm.CancelKeyboardDrag();

            case Key.Tab:
                _vm.StepKeyboardDropZone(backwards ? -1 : 1);
                return true;

            // Left and right walk the same cursor as up and down. A keyboard has no pointer, so the only
            // thing a direction can mean is "the next zone that way through the list", and pretending the
            // cursor is two-dimensional would be a lie about what the arrows do.
            case Key.Up:
            case Key.Left:
                _vm.StepKeyboardDropZone(-1);
                return true;

            case Key.Down:
            case Key.Right:
                _vm.StepKeyboardDropZone(1);
                return true;

            default:
                return false;
        }
    }
}