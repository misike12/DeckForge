using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DeckForge.App.Services;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Commands;

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

        AddHandler(
            Controls.Blocks.BlockTile.BreakpointRequestedEvent,
            new RoutedEventHandler(Tile_BreakpointRequested));

        // Part 18.5's tile menu, and the selection-item verbs an automation peer needs. Both bubble from
        // every tile, so one handler here covers the whole canvas; the alternative — each tile reaching for
        // the editor — cannot work, because a tile holds a block and the editor lives four template levels
        // above it.
        AddHandler(
            Controls.Blocks.BlockTile.MenuRequestedEvent,
            new RoutedEventHandler(Tile_MenuRequested));

        AddHandler(
            Controls.Blocks.BlockTile.SelectionModeRequestedEvent,
            new RoutedEventHandler(Tile_SelectionModeRequested));

        // The canvas and stage menus are built here and handed to the two controls that host them, because
        // a menu's items are commands and the commands are reached through Dispatch below - one table, one
        // key handler, one command palette and now three menus. See VisualMenus for why they are built in
        // code rather than declared in markup, and why they are factories rather than menus.
        Workspace.CanvasMenuFactory = () => Controls.Blocks.VisualMenus.ForCanvas(_vm, Dispatch);
        Stage.StageMenuFactory = () => Controls.Blocks.VisualMenus.ForStage(_vm.Stage, _vm, Dispatch);

        // Two events rather than four handlers: a chosen command and a dismissal, whatever raised them.
        PaletteOverlay.CommandChosen += Palette_CommandChosen;
        PaletteOverlay.Dismissed += Palette_Dismissed;

        Palette.RowPressed += Palette_RowPressed;
        PreviewKeyDown += OnPreviewKeyDown;
        _vm.PropertyChanged += OnEditorPropertyChanged;
        _vm.Stage.PropertyChanged += OnStagePropertyChanged;

// The zoom's input handlers, subscribed here because the workspace raises them and this page is the
        // only thing that knows which view model should answer. Named fields rather than lambdas, and
        // attached in AttachCanvasHandlers rather than here, because these used to be static events the
        // page subscribed to with lambdas it held nowhere: there was no way to unsubscribe, so a second
        // page doubled every notch and every pan delta and drove a canvas that was no longer on screen.
        AttachCanvasHandlers();

        SizeChanged += (_, args) => OnPageResized(args.NewSize.Width);
        Loaded += (_, _) => AttachCanvasHandlers();
        Unloaded += (_, _) => DetachCanvasHandlers();
        ApplyLayout(ActualWidth);
    }

    private void OnCanvasZoom(object? sender, Controls.Blocks.CanvasZoomEventArgs args)
    {
        Canvas.ZoomByWheel(args.Notches, args.At.X, args.At.Y);
        Minimap.Draw();
    }

    private void OnCanvasPanBy(object? sender, Point delta)
    {
        Canvas.PanBy(delta.X, delta.Y);
        Minimap.Draw();
    }

    private void OnCanvasPanStart(object? sender, Point point) => Workspace.BeginPan(point);

    private void OnCanvasPanEnd(object? sender, EventArgs args) => Workspace.EndPan();

    /// <summary>
    /// The surface has a size before the page does, and Core's minimap cannot scale anything without one.
    /// </summary>
    /// <remarks>
    /// Subscribing to the workspace rather than polling is what makes the thumbnail right the first time
    /// the page opens rather than after the first edit. Added and removed with the page's lifetime for the
    /// same reason as the canvas handlers: a handler that outlives its page is a handler that fires.
    /// </remarks>
    private void OnViewportChanged()
    {
        Canvas.Refresh();
        Minimap.Draw();
    }

    private bool _canvasHandlersAttached;

    private void AttachCanvasHandlers()
    {
        if (_canvasHandlersAttached)
        {
            return;
        }

        _canvasHandlersAttached = true;
        Workspace.ZoomByWheel += OnCanvasZoom;
        Workspace.PanStart += OnCanvasPanStart;
        Workspace.PanBy += OnCanvasPanBy;
        Workspace.PanEnd += OnCanvasPanEnd;
        Workspace.ViewportChanged += OnViewportChanged;
    }

    private void DetachCanvasHandlers()
    {
        if (!_canvasHandlersAttached)
        {
            return;
        }

        _canvasHandlersAttached = false;
        Workspace.ZoomByWheel -= OnCanvasZoom;
        Workspace.PanStart -= OnCanvasPanStart;
        Workspace.PanBy -= OnCanvasPanBy;
        Workspace.PanEnd -= OnCanvasPanEnd;
        Workspace.ViewportChanged -= OnViewportChanged;
    }

    /// <summary>
    /// Re-arranges the panels, then republishes the canvas view and the minimap.
    /// </summary>
    /// <remarks>
    /// The two go together because the minimap's scale is the canvas's viewport: a viewport that has just
    /// changed size has to be measured before anything can be drawn to scale. Left out of the first cut, and
    /// the page opened saying "Nothing on the canvas yet" over a canvas full of blocks - which is what
    /// driving the window found and reading the markup could not, because every number in it is correct and
    /// only the order the things happen in is wrong.
    /// </remarks>
    private void OnPageResized(double width)
    {
        ApplyLayout(width);
        Canvas.Refresh();
        Minimap.Draw();
    }

    /// <summary>
    /// Keeps the canvas in step with the editor's state.
    /// </summary>
    /// <remarks>
    /// Refreshing on every property change is what makes an edit reach the canvas at all: the editor
    /// raises <c>Changed</c>, the view model rebuilds, and the workspace has to be told to re-measure
    /// because the tiles are new objects at new sizes. The procedure case is folded in here rather than
    /// given its own subscription, because it is the same "something on screen is now different" event —
    /// and two subscriptions to one view model is how a page ends up refreshing twice per edit.
    /// </remarks>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        Workspace.Refresh();

        // The canvas's own view of the document, and then the thumbnail of it - once per rebuild, not once
        // per notification. A rebuild raises a dozen of them (scripts, columns, diagnostics, dirty state,
        // undo labels, the file line), and each one used to re-walk the whole visual tree and rebuild the
        // minimap's rectangles: thirteen full passes to repaint one frame. Deferred to the dispatcher
        // rather than guarded with a flag, so the pass happens after the layout those notifications asked
        // for and reads the tiles as they now are.
        if (!_canvasRepaintQueued)
        {
            _canvasRepaintQueued = true;

            Dispatcher.BeginInvoke(
                () =>
                {
                    _canvasRepaintQueued = false;
                    Canvas.Refresh();
                    Minimap.Draw();
                },
                DispatcherPriority.Background);
        }

        if (args.PropertyName == nameof(ViewModels.Visual.VisualEditorViewModel.SelectedProcedure)
            && _vm.SelectedProcedure is { } procedure)
        {
            Workspace.ScrollToColumn(procedure);
        }
    }

    /// <summary>
    /// Moves the stage's "here" mark onto the matching tile.
    /// </summary>
    /// <remarks>
    /// One subscription, on the stage rather than on the editor, and only for the one property that
    /// changes per block. The stage runs on a timer, so a page that also refreshed the whole workspace on
    /// every tick would re-measure every tile sixty times a second to move one ring.
    /// </remarks>
    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModels.Visual.StageViewModel.CurrentBlockId))
        {
            _vm.StampCurrent(_vm.Stage.CurrentBlockId);
        }
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
    /// <para>
    /// Phase 8 added a third row rather than a fifth column. The stage needs width for its trace and the
    /// canvas needs what little height it can get, so the stage took the one thing nothing else wanted:
    /// the bottom of the page.
    /// </para>
    /// </remarks>
    private const double WideLayoutWidth = 1080;

    private bool? _wide;

    /// <summary>
    /// How tall the stage is, as a share of what is left.
    /// </summary>
    /// <remarks>
    /// A third of the page rather than half, and not fixed. A stage tall enough for a hundred trace lines
    /// leaves the canvas with a hundred pixels of canvas, which is the panel a user spends the most time
    /// looking at; and a stage at a fixed height would take a third of a small window and all of a large
    /// one.
    /// </remarks>
    private const double StageRowWeight = 0.34;

    /// <summary>
    /// Places the five regions, one way for a wide page and another for a narrow one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A breakpoint rather than five collapse buttons, because a button that hides a panel has to be
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
            Height = new GridLength(wide ? 1 : 0.5, GridUnitType.Star),
        });
        Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (!wide)
        {
            Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StageRowWeight, GridUnitType.Star) });
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

            // A trailing star column, which the narrow layout did not have. Every element that spans the
            // grid - the header row, the canvas, the stage - spans the *columns*, and three fixed columns
            // add up to six hundred and ten pixels. In a viewport over nine hundred wide, that left three
            // hundred pixels of dead space down the right of the editor and squeezed the header's text
            // column to ninety-six, so its sentences trimmed to "Drag blocks...". The star column costs
            // the fixed panels nothing: it simply absorbs what is left.
            Layout.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 0,
            });
        }

        Place(HeaderRow, 0, 0, Layout.ColumnDefinitions.Count);

        if (wide)
        {
            Place(RailPanel, 1, 0);
            Place(PalettePanel, 1, 1);
            Place(CanvasPanel, 1, 2);
            Place(InspectorPanel, 1, 3);
            Place(StagePanel, 2, 0, Layout.ColumnDefinitions.Count);
        }
        else
        {
            Place(RailPanel, 1, 0);
            Place(PalettePanel, 1, 1);

            // The inspector takes the trailing star column as well. It is the one panel of the three whose
            // content - raw expressions, and a diagnostics list that runs long - gets narrower rather than
            // taller as the window shrinks, and pinned to its fixed column it left a third of a wide
            // window empty beside it.
            Place(InspectorPanel, 1, 2, 2);
            Place(CanvasPanel, 2, 0, Layout.ColumnDefinitions.Count);
            Place(StagePanel, 3, 0, Layout.ColumnDefinitions.Count);
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

    /// <summary>
    /// Reloads the canvas only when the workspace behind it is not the one already on screen.
    /// </summary>
    /// <remarks>
    /// Part 9.1's rule: reload only when the workspace behind the canvas is not the one already on screen.
    /// The document on this page is the user's unsaved work, so a plain navigation must not touch it; a
    /// genuine workspace switch must, because the file behind it is now somebody else's.
    /// </remarks>
    public void RefreshOnNavigate() => _vm.RefreshOnNavigate();

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
    /// Fills in a tile's context menu from Core's command table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler is on the page because the page is the one ancestor of every tile and the one thing that
    /// knows how a command is carried out — the same reason <see cref="Tile_SelectionRequested"/> is here.
    /// The tile creates the menu and asks; it does not build it, because a tile's data context is a block
    /// and a menu's items are commands, and those two live in different objects entirely.
    /// </para>
    /// <para>
    /// The right-click selects the block first, which is the opposite of what a menu usually does and the
    /// only useful behaviour here: "Duplicate block" in a menu opened on a block that is not the selection
    /// would duplicate whatever happened to be selected, and the user has no way to see which one that was.
    /// A click does not select a tile in WPF — the handler is on <c>MouseLeftButtonDown</c> — so without
    /// this the tile menu would act on a stale selection.
    /// </para>
    /// <para>
    /// Rebuilt on every open rather than once, because the greyed rows depend on whether anything is
    /// selected and a menu built when the page opened has nothing selected.
    /// </para>
    /// </remarks>
    private void Tile_MenuRequested(object sender, RoutedEventArgs args)
    {
        if (args is not Controls.Blocks.BlockMenuRequestedEventArgs requested)
        {
            return;
        }

        _vm.Select(requested.Block);

        requested.Menu.Items.Clear();
        foreach (var item in Controls.Blocks.VisualMenus.ForTile(requested.Block, _vm, Dispatch).Items)
        {
            requested.Menu.Items.Add(item);
        }
    }

    /// <summary>
    /// Changes the selection from something that is not a click: the automation peer's verbs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 9.8's peer can find a block by name, but a peer that can only <em>select</em> a block is half a
    /// selection pattern — a UI-automation client has to be able to add one to what is already selected,
    /// which is what the rubber band does for a person.
    /// </para>
    /// <para>
    /// No scrolling, unlike the click handler. A client that adds forty tiles to the selection would
    /// otherwise scroll the canvas forty times, ending up looking at the last one it touched rather than at
    /// the selection.
    /// </para>
    /// </remarks>
    private void Tile_SelectionModeRequested(object sender, RoutedEventArgs args)
    {
        if (args is not Controls.Blocks.BlockSelectionModeEventArgs mode)
        {
            return;
        }

        switch (mode.Mode)
        {
            case Controls.Blocks.BlockSelectionMode.Only:
                _vm.Select(mode.Block);
                break;

            case Controls.Blocks.BlockSelectionMode.Add:
                _vm.AddToSelection(mode.Block);
                break;

            case Controls.Blocks.BlockSelectionMode.Remove:
                _vm.RemoveFromSelection(mode.Block);
                break;
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
    /// Sets or clears a breakpoint from a tile's gutter.
    /// </summary>
    /// <remarks>
    /// Part 10.3's marquee feature, and it is the one thing on the canvas that is not an edit: a
    /// breakpoint is a fact about running the script rather than about what the script says, so it never
    /// goes near the <see cref="DocumentEditor"/> and never appears on the undo stack. Putting it there
    /// would also be wrong in a way a user would notice immediately — Ctrl+Z after setting a breakpoint
    /// would undo the block they had just written.
    /// </remarks>
    private void Tile_BreakpointRequested(object sender, RoutedEventArgs args)
    {
        if (args is Controls.Blocks.BlockSelectedEventArgs selected)
        {
            _vm.Stage.ToggleBreakpoint(selected.Block.Id);
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
    /// Writes the canvas as a vector image, beside the workspace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A save dialog rather than a fixed path: an export is something the user is going to put somewhere, and
    /// a tool that decides where its output goes is a tool that eventually overwrites something.
    /// </para>
    /// <para>
    /// What lands in the file is labels and the user's literals, and nothing else — §27.2's rule, and the
    /// reason the renderer is in Core where it can be tested rather than in a control where it cannot.
    /// </para>
    /// </remarks>
    private void ExportSvg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export the canvas as SVG",
            FileName = "canvas.svg",
            DefaultExt = ".svg",
            Filter = "SVG image (*.svg)|*.svg",
        };

        if (dialog.ShowDialog() is not true)
        {
            _vm.Report("Export cancelled. Nothing was written.");
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, DeckForge.Core.Visual.SvgRenderer.RenderDocument(_vm.Document), new UTF8Encoding(false));
            _vm.Report($"Exported {_vm.Document.Targets.Sum(target => target.Scripts.Count)} script(s) to {dialog.FileName}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The refusal has to name the path: "could not save" with no file name leaves the user looking
            // for whatever it tried to write.
            _vm.Report($"Could not write {dialog.FileName}: {error.Message}");
        }
    }

    /// <summary>
    /// Writes the canvas as a raster image, beside the vector one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A button beside "Export SVG" rather than a replacement for it, because they answer different
    /// questions: SVG is what to embed or print, PNG is what to paste into a chat app. The same save dialog,
    /// the same reporting, and the same refusal — including naming the path, because "could not save" with no
    /// file name beside it leaves the user looking for whatever it tried to write.
    /// </para>
    /// <para>
    /// What is planned here and drawn in <c>BlockImageRenderer</c> is the whole of it. The layout, the
    /// scale, the padding, the captions and the colours are Core's, which is the only reason this can be
    /// tested at all: §28.5 refused PNG because "a third-party encoder in Core would make the export
    /// untestable", and the answer was to keep the encoder out of Core rather than to keep the export out of
    /// the product.
    /// </para>
    /// </remarks>
    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export the canvas as PNG",
            FileName = "canvas.png",
            DefaultExt = ".png",
            Filter = "PNG image (*.png)|*.png",
        };

        if (dialog.ShowDialog() is not true)
        {
            _vm.Report("Export cancelled. Nothing was written.");
            return;
        }

        var plan = PngExportPlan.ForDocument(_vm.Document, PngOptions());

        if (!BlockImageRenderer.TryRender(plan, out var bitmap, out var problem))
        {
            // The path, then the reason. The user has a dialog open on a filename and deserves both, and the
            // reason alone ("the surface is too big") would be a complaint about the application rather than
            // about what they just asked for.
            _vm.Report($"Could not draw {dialog.FileName}: {problem} Nothing was written.");
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, BlockImageRenderer.EncodePng(bitmap!));

            _vm.Report(
                $"Exported {_vm.Document.Targets.Sum(target => target.Scripts.Count)} script(s) to "
                + $"{dialog.FileName} ({plan.Width}x{plan.Height} at {plan.Scale:0.##}x).");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _vm.Report($"Could not write {dialog.FileName}: {error.Message}");
        }
    }

    /// <summary>
    /// The settings the page's two raster paths draw with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vector export's 16-unit margin, so the two exports of one document differ by a file format rather
    /// than by a border. The scale is left at <see cref="PngExportOptions.DefaultScale"/> and the caption at
    /// Core's default, which is the script's name in the target it belongs to — Part 25.3's "a caption line
    /// naming the target", and something a user pasting into a chat app needs more than a colour does.
    /// </para>
    /// <para>
    /// Named rather than written twice because the file export and the clipboard's picture are the same
    /// picture, and a second copy of these numbers is a second place for the two to differ — which is how
    /// somebody ends up pasting a picture into a channel and exporting a different one to a folder.
    /// </para>
    /// </remarks>
    private static PngExportOptions PngOptions() => new() { Padding = 16 };

    /// <summary>
    /// Saves the canvas, or says why it could not be saved.
    /// </summary>
    /// <remarks>
    /// A handler rather than a command binding because the button lives in the page's own header, where a
    /// binding would work perfectly well — and this is written as a handler so the Save keyboard shortcut
    /// and the button share one path. There is no Ctrl+S binding yet; Part 9.5 does not list one, and a
    /// shortcut nobody documented is a shortcut nobody finds.
    /// </remarks>
    private void Save_Click(object sender, RoutedEventArgs e) => _vm.SaveCommand.Execute(null);

    /// <summary>
    /// Discards the canvas and reads the file back.
    /// </summary>
    /// <remarks>
    /// Not undoable, and that is deliberate: the editor is replaced rather than rewound, because the
    /// document on disk is not one of the states the undo stack was built from. The buttons are placed
    /// either side of Undo and Redo on purpose — the one action that throws work away should be the hardest
    /// of the three to press by accident.
    /// </remarks>
    private void Revert_Click(object sender, RoutedEventArgs e) => _vm.RevertCommand.Execute(null);

    /// <summary>Commits a procedure's new name when the box loses focus.</summary>
    /// <remarks>
    /// Handlers rather than bindings, because the box lives inside an <c>Expander</c> whose
    /// <c>DataContext</c> is the procedure column and which therefore cannot reach the page's view model
    /// through a binding path. The row is named by <c>Tag</c> for the same reason the inspector's steppers
    /// are.
    /// </remarks>
    private void ProcedureName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProcedureViewModel procedure } element
            && element is TextBox box)
        {
            _vm.RenameProcedure(procedure, box.Text);
        }
    }

    /// <summary>Commits a procedure's name on Enter.</summary>
    private void ProcedureName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { Tag: ProcedureViewModel procedure } element
            && element is TextBox box)
        {
            _vm.RenameProcedure(procedure, box.Text);
            e.Handled = true;
            Keyboard.Focus(box);
        }
    }

    /// <summary>
    /// Says whether the procedure returns a value.
    /// </summary>
    /// <remarks>
    /// The checkbox is put back the way the document is after the click, because the editor can refuse the
    /// change — a call already using this procedure as a value, or as a statement — and a checkbox that
    /// stays ticked after being refused is a control lying about the document. Guarded so that a click
    /// that agrees with the document does not write anything: a write here is an edit, and the panel must
    /// not put one on the undo stack for a control the user set back where it was.
    /// </remarks>
    private void Returns_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProcedureViewModel procedure } element
            && element is CheckBox box
            && box.IsChecked is { } wanted
            && wanted != procedure.Procedure.Returns)
        {
            _vm.SetProcedureReturns(procedure, wanted);

            if (ReferenceEquals(box, ReturnsBox))
            {
                ReturnsBox.IsChecked = procedure.Procedure.Returns;
            }
        }
    }

    /// <summary>Adds a parameter.</summary>
    private void ParameterAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProcedureViewModel procedure })
        {
            _vm.AddParameter(procedure);
        }
    }

    /// <summary>
    /// Removes a parameter.
    /// </summary>
    /// <remarks>
    /// The parameter is the button's own data context — the row template puts it there — and the procedure
    /// is whichever one the panel is showing. Nothing is remembered between events, so a rebuilt row cannot
    /// leave a handler holding a parameter that is no longer there.
    ///
    /// The procedure is read from the editor rather than from a <c>Tag</c> on the control because a row
    /// inside a DataTemplate cannot reach one: the panel's DataContext is outside the template's name
    /// scope, so <c>ElementName</c> finds nothing and an ancestor search finds the page instead. Both were
    /// tried, and both produced a button that looked wired and did nothing.
    /// </remarks>
    private void ParameterRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcedureParameter parameter }
            && _vm.SelectedProcedure is { } procedure)
        {
            _vm.RemoveParameter(procedure, parameter);
        }
    }

    /// <summary>Commits a parameter's new name when its box loses focus.</summary>
    private void Parameter_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcedureParameter parameter } element
            && element is TextBox box
            && _vm.SelectedProcedure is { } procedure)
        {
            _vm.RenameParameter(procedure, parameter, box.Text);
        }
    }

    /// <summary>Removes the selected procedure.</summary>
    private void ProcedureDelete_Click(object sender, RoutedEventArgs e) =>
        _vm.DeleteProcedure(_vm.SelectedProcedure);

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

        // Recorded here, at the press rather than at the drop: the recency row is a list of what the user
        // reached for, and a drag they abandoned with Escape was still a reach.
        _vm.Palette.NoteUse(row.Kind);

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

    /// <summary>The canvas's zoom, pan and minimap.</summary>
    /// <remarks>
    /// The view model takes the workspace as its host rather than reaching into the control, so the zoom's
    /// arithmetic is Core's and the canvas only ever applies a view it is given. A zoom the canvas computed
    /// for itself would be a second implementation of a rule the drop resolver also uses.
    /// </remarks>
    private ViewModels.Visual.CanvasViewModel Canvas => _canvas ??= CreateCanvas();

    private ViewModels.Visual.CanvasViewModel? _canvas;

    private bool _canvasRepaintQueued;

    /// <summary>
    /// Builds the canvas view model and hands it the workspace as its host.
    /// </summary>
    /// <remarks>
    /// The host interface exists so the view model never names the control: it asks for a view, a list of
    /// rects and a viewport size, and the workspace supplies all three. Without it the view model would hold
    /// a <c>BlockWorkspace</c>, and every test of the zoom would need a window.
    /// </remarks>
private ViewModels.Visual.CanvasViewModel CreateCanvas()
    {
        var canvas = new ViewModels.Visual.CanvasViewModel(new WorkspaceHost(Workspace));

        // Cached before the view model is told, and the order is the whole point. Assigning _vm.Canvas
        // raises a notification that comes straight back here, this handler reads Canvas, and the getter
        // would build *another* one because _canvas is still null - forever, which is how a stack of 3,000
        // identical frames gets to be the report. Found by driving the window: the page simply never came
        // up, and the crash named a tree walk that was only the last thing the runaway loop touched.
        _canvas = canvas;
        _vm.Canvas = canvas;

        return canvas;
    }

    /// <summary>The workspace, seen as the host the canvas view model asks for.</summary>
    /// <param name="workspace">The real one.</param>
    private sealed class WorkspaceHost(Controls.Blocks.BlockWorkspace workspace)
        : ViewModels.Visual.CanvasViewModel.BlockWorkspaceHost
    {
        /// <inheritdoc />
        public Core.Visual.CanvasView View
        {
            get => workspace.View;
            set => workspace.ApplyView(value);
        }

        /// <inheritdoc />
        public void ApplyView(Core.Visual.CanvasView view) => workspace.ApplyView(view);

        /// <inheritdoc />
        public IReadOnlyList<Core.Visual.BlockRect> Rects => workspace.Rects;

        /// <inheritdoc />
        public (double Width, double Height) Viewport => workspace.Viewport;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Zoom += 0.25;
        Minimap.Draw();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Zoom -= 0.25;
        Minimap.Draw();
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        Workspace.Refresh();
        Canvas.ZoomToFit();
    }

    private void ZoomActual_Click(object sender, RoutedEventArgs e)
    {
        Canvas.ZoomToActualSize();
        Minimap.Draw();
    }

    /// <summary>Moves the canvas where the user clicked on the minimap.</summary>
    private void Minimap_ViewRequested(object sender, Controls.Blocks.ViewRequestedEventArgs e)
    {
        Canvas.Apply(e.View);
        Minimap.Draw();
    }
    /// <summary>Redoes, for the same reason.</summary>
    private void Redo_Click(object sender, RoutedEventArgs e) => _vm.RedoCommand.Execute(null);

    /// <summary>
    /// Every keyboard gesture, on one handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the page rather than on the tiles, for the same reason the click is: the tiles live inside data
    /// templates four levels down and each one cannot reach the editor from its own data context. A page
    /// that is an ancestor of all of them needs exactly one handler.
    /// </para>
    /// <para>
    /// The order is Part 18.3's rules in its order: a modal swallows everything, a focused text field wins
    /// every single-key shortcut but not a modified one, and Escape cancels the innermost thing first. The
    /// stage keys are looked up in Core's command table rather than written out again, which is what keeps
    /// the shortcut sheet honest: F5 is "run" in the sheet because it *is* run here, both read from one row.
    /// </para>
    /// </remarks>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // A modal takes every key, and stops there. Anything below this line belongs to the canvas.
        if (IsOverlayOpen)
        {
            if (e.Key == Key.Escape)
            {
                CloseOverlays();
                e.Handled = true;
            }

            return;
        }

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

        // The palette and the sheet, before the field rule: both are modified keys, and documentation a
        // user cannot reach while a text field has focus is documentation only mouse users read.
        if (ctrl && e.Key == Key.K)
        {
            OpenPalette();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.OemQuestion)
        {
            OpenShortcuts();
            e.Handled = true;
            return;
        }

        // A focused text field wins over every single-key shortcut. Ctrl chords and function keys still
        // fire, which is why this sits below the Ctrl checks and above the plain-key switch.
        var editing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

        if (StageGesture(e.Key, shift) is { } stage)
        {
            RunStageCommand(stage);
            e.Handled = true;
            return;
        }

        // Undo and redo come before the carrying check, and they work whether or not a block is in hand.
        //
        // The carrying check is the reason this is not simply further down the list: a user who picks a
        // block up, moves the cursor a few zones, and then realises they wanted to undo the edit they made
        // before picking it up has no way to ask, because the carry swallows every key. Being able to undo
        // while holding something is what makes the carry mode safe to explore in - you can always get back
        // to where you were.
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

        if (ctrl && e.Key == Key.S)
        {
            _vm.SaveCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D && !editing)
        {
            _vm.DuplicateSelected();
            e.Handled = true;
            return;
        }

        // The keyboard's half of Part 18.1's rubber band. §18.3 gives the band no key of its own, and a
        // gesture only the pointer can perform is a defect this design names; Ctrl+A is the chord every
        // other editor on the machine already means by it, and Esc - already "Cancel" in the table - clears
        // it again, so the pair behaves like the band and a click on empty canvas do.
        //
        // Refused while a field has focus, like every other canvas chord: Ctrl+A inside a text box is
        // select-all-text, and a canvas that swallowed it would make the field's own shortcut unreachable.
        if (ctrl && e.Key == Key.A && !editing)
        {
            _vm.SelectAllBlocks();
            e.Handled = true;
            return;
        }

        // The target picker, for the keyboard. Tab reaches the ComboBox and the arrows move inside it, so
        // this is a shortcut rather than the only route — and it is here because §17.2 item 6 promises every
        // operation is reachable without a pointer, and the cheapest reading of that is a chord.
        if (ctrl && e.Key == Key.Tab)
        {
            _vm.SelectNextTarget();
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

        if (ctrl)
        {
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
                // Refused while a field has focus, so Backspace in a text box deletes a character rather
                // than the block whose slot is being edited. Before this, typing a single character into a
                // slot and pressing Backspace deleted the block instead.
                if (!editing)
                {
                    _vm.DeleteSelected();
                    e.Handled = true;
                }

                break;

            case Key.Space:
                if (!editing)
                {
                    // Pick up. The only key that needs something to be selected first, so it declines
                    // silently rather than doing nothing with an error.
                    _vm.PickUpSelected();
                    e.Handled = true;
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The stage command a key names, or null.
    /// </summary>
    /// <remarks>
    /// Looked up rather than switched on, which is what keeps the sheet from lying: written out twice, the
    /// handler and the sheet would agree until somebody added a key to one of them.
    /// </remarks>
    private static string? StageGesture(Key key, bool shift)
    {
        var name = key switch
        {
            Key.F5 => "F5",
            Key.F6 => "F6",
            Key.F9 => "F9",
            Key.F10 => "F10",
            Key.F11 => "F11",
            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        return VisualCommands.ForGesture(shift ? "Shift+" + name : name, CommandScope.Stage)?.Id;
    }

    /// <summary>Runs one stage command.</summary>
    private void RunStageCommand(string id)
    {
        switch (id)
        {
            case VisualCommands.StageRun:
                _vm.Stage.RunCommand.Execute(null);
                break;

            case VisualCommands.StageStep:
                _vm.Stage.StepCommand.Execute(null);
                break;

            case VisualCommands.StageStepInto:
                _vm.Stage.StepIntoCommand.Execute(null);
                break;

            case VisualCommands.StageStop:
                _vm.Stage.StopCommand.Execute(null);
                break;

            case VisualCommands.StagePause:
                // One command, because the table cannot hold two commands sharing a gesture and the
                // transport has one button. The session decides which of the two it is.
                _vm.Stage.PauseCommand.Execute(null);
                break;

            case VisualCommands.StageReset:
                _vm.Stage.ResetCommand.Execute(null);
                break;

            case VisualCommands.ToggleBreakpoint:
                // Only with a block selected, and silently otherwise: F9 is pressed while looking at the
                // canvas, and focus is not always where the eye is.
                if (_vm.Selected is { } selected)
                {
                    _vm.Stage.ToggleBreakpoint(selected.Block.Id);
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Whether either overlay is up.</summary>
    private bool IsOverlayOpen =>
        PaletteOverlay.Visibility == Visibility.Visible || ShortcutOverlay.Visibility == Visibility.Visible;

    /// <summary>
    /// Puts text on the system clipboard, and survives it being closed.
    /// </summary>
    /// <param name="text">What to copy.</param>
    /// <param name="image">A picture of the same run, or null when there is none.</param>
    /// <returns>Whether it landed.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="Clipboard"/> is a single process-wide lock that any other application may hold, and every
    /// access can fail with it. The failure is not exceptional in practice - another app opening the
    /// clipboard twice in a row makes it ordinary - and an unhandled one out of a key handler takes the
    /// dispatcher with it, which loses the whole session rather than one copy.
    /// </para>
    /// <para>
    /// Retried once, because the common case really is the transient one: another process had it a
    /// moment ago and has let go by the time the second attempt runs.
    /// </para>
    /// <para>
    /// Both payloads go in through one <see cref="DataObject"/> rather than a <c>SetText</c> followed by a
    /// <c>SetImage</c>, because the clipboard is replaced rather than added to: two writes means two
    /// round trips in which another process can take the lock, and the second one would clear the text the
    /// first had put there. Part 25.2 asks for both at once, and the text is the primary one — it is what a
    /// paste back into DeckForge reads — so it is written first and the picture only rides along.
    /// </para>
    /// <para>
    /// The image is a parameter and not something looked up here, because the caller has to be able to say
    /// it has none rather than have this decide: the picture is drawn from a layout walk that can be refused
    /// (a document too large to rasterise), and a refusal must not stop the text.
    /// </para>
    /// </remarks>
    private bool WriteClipboard(string text, BitmapSource? image)
    {
        var payload = new DataObject();

        // Text first, and unconditionally: a caller that has no picture gets exactly what it had before any
        // of this, and a caller whose picture could not be drawn still pastes into DeckForge.
        payload.SetText(text);

        if (image is not null)
        {
            payload.SetImage(image);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // copy: true, so the clipboard outlives the window. A chat app is a different process that
                // may ask for the image long after DeckForge has been closed, and an unflushed clipboard
                // would be empty by then — which is the one place a copy has to be told to persist.
                Clipboard.SetDataObject(payload, true);
                return true;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(60);
            }
        }

        return false;
    }

    /// <summary>
    /// A picture of a run, for the clipboard to carry alongside the envelope.
    /// </summary>
    /// <param name="run">The blocks being copied.</param>
    /// <returns>The image, and why there is not one if there is not.</returns>
    /// <remarks>
    /// <para>
    /// Drawn from the layout model by <see cref="PngExportPlan"/>, not captured from the canvas: a capture
    /// would be a picture of the window at the window's zoom, cropped to the window, and would show nothing
    /// at all of a run the user had scrolled off screen to reach. This is the same geometry the vector
    /// export uses, so the picture in a chat message and the file in a folder are the same drawing.
    /// </para>
    /// <para>
    /// A null bitmap is an ordinary answer here, and it is allowed to be. Part 25.2's picture is the second
    /// payload — the envelope is what a paste back into DeckForge reads — so a run too large to rasterise
    /// must not stop the text copy. The reason comes back with it so the caller can say what happened rather
    /// than leaving the user to discover that a chat app pasted words.
    /// </para>
    /// </remarks>
    private static (BitmapSource? Bitmap, string? Problem) ClipboardImage(
        IReadOnlyList<Block> run) =>
        BlockImageRenderer.TryRender(
            PngExportPlan.ForRun(run, PngOptions()),
            out var bitmap,
            out var problem)
            ? (bitmap, null)
            : (null, problem);

    /// <summary>
    /// Ctrl+C and Ctrl+X: one copy, and in the cut's case a delete afterwards.
    /// </summary>
    /// <param name="cut">Whether the blocks are removed once the clipboard has them.</param>
    /// <remarks>
    /// <para>
    /// One method for two commands, because they are one operation: a cut is a copy that has already been
    /// agreed to, and the parts that matter — the run that is chosen, the envelope, the picture, and the
    /// order they happen in — must not be allowed to differ between them.
    /// </para>
    /// <para>
    /// The order is the whole design. The run is read once and both payloads are built from that one read,
    /// so the envelope and the picture cannot be of different runs; the picture is drawn first, so a
    /// document too large to rasterise cannot stop the envelope; the clipboard is written before anything is
    /// deleted, so a copy that did not land leaves the blocks where they are; and the delete is last, so a
    /// cut reports what actually happened rather than what it meant to. A cut reads the run a second time,
    /// inside <c>CutSelected</c>, and cannot come back with a different one: drawing a picture does not move
    /// a selection.
    /// </para>
    /// </remarks>
    private void CopySelectedRun(bool cut)
    {
        if (_vm.SelectedRun() is not { Count: > 0 } run)
        {
            _vm.ReportProblem(cut ? "Select a block to cut." : "Select a block to copy.");
            return;
        }

        var (picture, drawing) = ClipboardImage(run);

        string? text;
        bool deleted;

        if (cut)
        {
            (text, deleted) = _vm.CutSelected();
        }
        else
        {
            text = VisualClipboard.Write(run);
            deleted = false;
        }

        if (text is null)
        {
            _vm.ReportProblem(cut ? "Select a block to cut." : "Select a block to copy.");
            return;
        }

        if (!WriteClipboard(text, picture))
        {
            _vm.ReportProblem(cut
                ? "The clipboard would not take it. The blocks are still here - undo is one Ctrl+Z away."
                : "The clipboard would not take it, so nothing was copied.");
            return;
        }

        // Said after the copy, not before it, and as an answer rather than a refusal: the copy happened and
        // the user should know what they got. Part 25.2 asks for a bitmap as well as the text, so a copy
        // that quietly produced only text is not what was promised.
        if (picture is null)
        {
            _vm.Report($"Copied the blocks, but there was no picture to go with them: {drawing}");
        }

        if (cut && !deleted)
        {
            _vm.ReportProblem("Copied, but the blocks could not be removed.");
        }
    }

    /// <summary>The clipboard's text, or null when it holds none or cannot be read.</summary>
    private static string? ReadClipboard()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(60);
            }
        }

        return null;
    }

    /// <summary>Writes the selected run to a <c>.dfblock</c> the user names.</summary>
    /// <remarks>
    /// The same text the clipboard carries, so a file and a copy cannot drift apart - which is the whole
    /// argument for the envelope being one format rather than two.
    /// </remarks>
    private void ExportBlock()
    {
        if (_vm.CopySelected() is not { } text)
        {
            _vm.ReportProblem("Select a block to export.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export this block",
            FileName = $"{_vm.Selected!.Block.Kind}{Core.Visual.VisualClipboard.Extension}",
            DefaultExt = Core.Visual.VisualClipboard.Extension,
            Filter = $"DeckForge block (*{Core.Visual.VisualClipboard.Extension})|*{Core.Visual.VisualClipboard.Extension}"
                + "|Text (*.txt)|*.txt",
        };

        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(false));
            _vm.Report($"Exported {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The refusal names the path, for the reason the SVG export's does: "could not save" leaves the
            // user looking for whatever it tried to write.
            _vm.ReportProblem($"Could not write {dialog.FileName}: {error.Message}");
        }
    }

    /// <summary>Reads a run from a <c>.dfblock</c> the user names, and drops it in.</summary>
    private void ImportBlock()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a block",
            Filter = $"DeckForge block (*{Core.Visual.VisualClipboard.Extension})|*{Core.Visual.VisualClipboard.Extension}"
                + "|Text (*.txt)|*.txt",
        };

        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            _vm.PasteText(File.ReadAllText(dialog.FileName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _vm.ReportProblem($"Could not read {dialog.FileName}: {error.Message}");
        }
    }


    /// <summary>Opens the command palette.</summary>
    /// <summary>Opens the command palette from the header button, so Ctrl+K is not the only way in.</summary>
    private void OpenPalette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    /// <summary>Opens the shortcut sheet from the header button.</summary>
    private void OpenShortcuts_Click(object sender, RoutedEventArgs e) => OpenShortcuts();

    private void OpenPalette()
    {
        CloseOverlays();
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteOverlay.Open();
    }

    /// <summary>Opens the shortcut sheet.</summary>
    private void OpenShortcuts()
    {
        CloseOverlays();
        ShortcutOverlay.Visibility = Visibility.Visible;
        ShortcutOverlay.Focus();
    }

    /// <summary>Hides both overlays and gives the keyboard back to the canvas.</summary>
    private void CloseOverlays()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        ShortcutOverlay.Visibility = Visibility.Collapsed;

        // The keyboard back on the canvas, or a dismissed palette leaves focus on a collapsed element and
        // the arrow keys go nowhere - which reads as "the canvas stopped working".
        Workspace.Refresh();
        Workspace.FocusCanvas();
    }

    /// <summary>Runs the command the palette returned.</summary>
    private void Palette_CommandChosen(object sender, RoutedEventArgs e)
    {
        CloseOverlays();

        if (e is Controls.Blocks.CommandChosenEventArgs chosen)
        {
            Dispatch(chosen.Command.Id);
        }
    }

    /// <summary>Closes the palette without running anything.</summary>
    private void Palette_Dismissed(object sender, RoutedEventArgs e) => CloseOverlays();

    /// <summary>Closes the shortcut sheet.</summary>
    private void Shortcut_Closed(object sender, RoutedEventArgs e) => CloseOverlays();

    /// <summary>
    /// Runs one command by id, whichever way it was chosen.
    /// </summary>
    /// <remarks>
    /// One dispatcher for the keys and the palette, so a command reachable only from the palette is a
    /// different bug from one reachable only from the keyboard - and there is only one of either.
    /// </remarks>
    private void Dispatch(string id)
    {
        switch (id)
        {
            case VisualCommands.OpenPalette:
                OpenPalette();
                break;

            case VisualCommands.AddBlock:
                // The same overlay, opened with a query already in it: "add block" and the palette are one
                // thing, and a second dialog for it would be a second thing to learn.
                OpenPalette();
                break;

            case VisualCommands.ShowShortcuts:
                OpenShortcuts();
                break;

            case VisualCommands.Undo:
                _vm.UndoCommand.Execute(null);
                break;

            case VisualCommands.Redo:
                _vm.RedoCommand.Execute(null);
                break;

            case VisualCommands.Duplicate:
                _vm.DuplicateSelected();
                break;

            case VisualCommands.Copy:
                CopySelectedRun(cut: false);
                break;

            case VisualCommands.Cut:
                CopySelectedRun(cut: true);
                break;

            case VisualCommands.Paste:
                _vm.PasteText(ReadClipboard());
                break;

            case VisualCommands.ExportBlock:
                ExportBlock();
                break;

            case VisualCommands.ImportBlock:
                ImportBlock();
                break;


            case VisualCommands.Delete:
                _vm.DeleteSelected();
                break;

            case VisualCommands.PickUpOrDrop:
                if (_vm.IsCarrying)
                {
                    _vm.DropCarried();
                }
                else
                {
                    _vm.PickUpSelected();
                }

                break;

            case VisualCommands.Cancel:
                _vm.CancelKeyboardDrag();
                break;

            case VisualCommands.SelectAll:
                _vm.SelectAllBlocks();
                break;

            case VisualCommands.Save:
                _vm.SaveCommand.Execute(null);
                break;

            case VisualCommands.OpenDocs:
                ShellMessenger.NavigateTo("docs::features/actions");
                break;

            case VisualCommands.FocusPaletteSearch:
                CloseOverlays();
                Palette.FocusSearch();
                break;

            case VisualCommands.StageRun:
            case VisualCommands.StageStep:
            case VisualCommands.StageStepInto:
            case VisualCommands.StageStop:
            case VisualCommands.StagePause:
            case VisualCommands.StageReset:
            case VisualCommands.ToggleBreakpoint:
                RunStageCommand(id);
                break;

            default:
                // A command in the table that nothing dispatches is a shortcut-sheet row that lies, so it
                // says so rather than pressing a key that does nothing.
                _vm.Report($"'{id}' is in the shortcut sheet but nothing runs it yet.");
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