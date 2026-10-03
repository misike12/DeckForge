using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The minimap control: draws Core's rectangles and takes a click back to a view.
/// </summary>
/// <remarks>
/// <para>
/// It draws rectangles and nothing else. Every number it draws came from <see cref="Minimap"/>, which is
/// where the arithmetic is tested; a control that computed its own would be a second implementation of the
/// same rule and would drift the first time either changed.
/// </para>
/// <para>
/// The click is turned into a view by asking Core for the inverse of the mapping it just used, rather than
/// by inverting the mapping again here — which is the mistake that makes a minimap scroll to somewhere
/// slightly wrong and nobody can say why.
/// </para>
/// </remarks>
public partial class CanvasMinimap : UserControl
{
    /// <summary>Raised when the user clicks the map, carrying the view they asked for.</summary>
    public static readonly RoutedEvent ViewRequestedEvent = EventManager.RegisterRoutedEvent(
        "ViewRequested",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(CanvasMinimap));

    /// <summary>Raised when the user clicks the map, carrying the view they asked for.</summary>
    public event RoutedEventHandler? ViewRequested
    {
        add => AddHandler(ViewRequestedEvent, value);
        remove => RemoveHandler(ViewRequestedEvent, value);
    }

    public CanvasMinimap()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Draw();
    }

    /// <summary>Redraws from the view model's current minimap.</summary>
    public void Draw()
    {
        Map.Children.Clear();

        if (DataContext is not CanvasViewModel model || model.Minimap is not { } minimap)
        {
            return;
        }

        var fill = Resolve("Liquid.BlockRailBrush") ?? Brushes.Gray;

        foreach (var (block, x, y, width, height) in model.MinimapRects)
        {
            Map.Children.Add(new Rectangle
            {
                Width = width,
                Height = height,
                RadiusX = 1,
                RadiusY = 1,
                Fill = fill,
                Opacity = 0.9,
                ToolTip = block.Id,

                // RenderTransform rather than Canvas.Left/Top, because a minimap rectangle a tenth of a
                // pixel wide snapped to the pixel grid disappears entirely.
                RenderTransform = new TranslateTransform(x, y),
            });
        }

        var viewport = new Rectangle
        {
            Width = Math.Max(2, minimap.Viewport.Width),
            Height = Math.Max(2, minimap.Viewport.Height),
            StrokeThickness = 1.5,
            Stroke = Resolve("Liquid.AccentBrush") ?? Brushes.DodgerBlue,
            Fill = Brushes.Transparent,
            RenderTransform = new TranslateTransform(minimap.Viewport.X, minimap.Viewport.Y),
        };

        Map.Children.Add(viewport);
    }

    /// <summary>
    /// Turns a click into the view that centres the canvas there.
    /// </summary>
    private void Map_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CanvasViewModel model || model.Minimap is not { } minimap)
        {
            return;
        }

        var point = e.GetPosition(Map);
        var view = minimap.ViewAt(point.X, point.Y, 240, 150);

        RaiseEvent(new ViewRequestedEventArgs(ViewRequestedEvent, this, view));
        e.Handled = true;
    }

    /// <summary>A theme brush by key, or null when the theme does not have one.</summary>
    private static Brush? Resolve(string key) => Application.Current?.TryFindResource(key) as Brush;
}

/// <summary>The view a click on the minimap asked for.</summary>
public sealed class ViewRequestedEventArgs(RoutedEvent routedEvent, object source, CanvasView view)
    : RoutedEventArgs(routedEvent, source)
{
    /// <summary>The view to apply.</summary>
    public CanvasView View { get; } = view;
}