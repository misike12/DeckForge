using System.Windows;
using System.Windows.Controls;
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
        }
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/actions");
}