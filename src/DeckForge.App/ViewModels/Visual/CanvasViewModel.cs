using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// The canvas's zoom, pan and minimap: the toolbar's numbers, and the rectangles the minimap draws.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is Core's <see cref="CanvasView"/>; this holds the current value and tells the workspace
/// about it. A view model that computed the zoom itself would be a second implementation of a rule the
/// drop resolver also uses, and the canvas would drop a block where the ghost is not at one zoom and not at
/// another.
/// </para>
/// <para>
/// Zoom is a slider *and* Ctrl+wheel, because they answer different questions: the slider says how far in
/// or out, the wheel says "a little more" while the pointer is where the reader is looking.
/// </para>
/// </remarks>
public sealed partial class CanvasViewModel : ObservableObject
{
    private readonly BlockWorkspaceHost _host;
    private CanvasView _view = new();
    private Minimap? _minimap;
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;

    public CanvasViewModel(BlockWorkspaceHost host)
    {
        _host = host;
        Refresh();
    }

    /// <summary>What the workspace needs from the view model, so neither has to know the other's type.</summary>
    public interface BlockWorkspaceHost
    {
        /// <summary>The view, for the canvas to draw through.</summary>
        CanvasView View { get; }

        /// <summary>Tells the canvas the view changed.</summary>
        void ApplyView(CanvasView view);

        /// <summary>Every block's rect, in workspace units, for the minimap.</summary>
        IReadOnlyList<BlockRect> Rects { get; }

        /// <summary>The canvas viewport's size in pixels.</summary>
        (double Width, double Height) Viewport { get; }
    }

    /// <summary>
    /// Applies a view the minimap asked for.
    /// </summary>
    /// <remarks>
    /// A method rather than a setter, because the view is derived: changing the pan has to move the
    /// transform, republish the minimap and tell the toolbar, and a setter that did all three would be doing
    /// four things behind one assignment.
    /// </remarks>
    /// <param name="view">The view to apply.</param>
    public void Apply(CanvasView view) => View = view;

    /// <summary>The current view: zoom and pan, in Core's terms.</summary>
    public CanvasView View
    {
        get => _view;
        private set
        {
            _view = value;
            _host.ApplyView(value);
            OnPropertyChanged();
            RefreshMinimap();
        }
    }

    /// <summary>The zoom, as the slider shows it.</summary>
    public double Zoom
    {
        get => View.ClampedZoom;
        set
        {
            if (Math.Abs(value - Zoom) < 0.001)
            {
                return;
            }

            // Zoomed about the viewport's middle, which is what every canvas does and the only place the
            // user is not already looking: zooming about the pointer would be nicer, but this control does
            // not see pointer positions and the wheel handler does its own centring.
            var centre = View.ToWorkspace(_viewportWidth / 2, _viewportHeight / 2);

            View = View.AtZoom(value).CentredOn(centre.X, centre.Y, _viewportWidth, _viewportHeight);
        }
    }

    /// <summary>The zoom as a percentage, for the label beside the slider.</summary>
    public string ZoomText => $"{Zoom * 100:N0}%";

    /// <summary>The zoom the slider offers, from Core's own bounds.</summary>
    public double MinimumZoom => CanvasView.MinZoom;

    /// <summary>The zoom the slider offers, from Core's own bounds.</summary>
    public double MaximumZoom => CanvasView.MaxZoom;

    /// <summary>Whether the minimap has anything to draw.</summary>
    public bool HasNothing => (_minimap?.Rects.Count ?? 0) == 0;

    /// <summary>The minimap, as Core computed it.</summary>
    public Minimap? Minimap => _minimap;

    /// <summary>The rectangles to draw, for the minimap control.</summary>
    public ObservableCollection<(BlockRect Block, double X, double Y, double Width, double Height)> MinimapRects
    {
        get;
    } = [];

    /// <summary>The viewport rectangle, in the minimap's own pixels.</summary>
    public (double X, double Y, double Width, double Height) ViewportRect =>
        _minimap?.Viewport ?? (0, 0, 0, 0);

    /// <summary>Zooms by a wheel notch, about the pointer.</summary>
    /// <param name="notches">Positive zooms in.</param>
    /// <param name="pointerX">The pointer's x in the viewport.</param>
    /// <param name="pointerY">The pointer's y in the viewport.</param>
    /// <remarks>
    /// About the pointer, because that is the whole reason to use a wheel rather than the slider: the user
    /// is looking at a block and wants that block bigger. Zooming about the viewport's middle instead moves
    /// whatever is under the pointer out from under it, which feels like the canvas ignoring them.
    /// </remarks>
    public void ZoomByWheel(int notches, double pointerX, double pointerY)
    {
        var step = Math.Exp(notches * 0.15);
        var under = View.ToWorkspace(pointerX, pointerY);
        var zoomed = View.AtZoom(View.ClampedZoom * step);

        // Pan so the same workspace point stays under the pointer after the zoom.
        View = zoomed with
        {
            PanX = under.X - (pointerX / zoomed.ClampedZoom),
            PanY = under.Y - (pointerY / zoomed.ClampedZoom),
        };
    }

    /// <summary>Pans by a pointer delta in screen pixels.</summary>
    /// <param name="dx">Pixels right.</param>
    /// <param name="dy">Pixels down.</param>
    public void PanBy(double dx, double dy) => View = View.PannedBy(dx, dy);

    /// <summary>Zooms to fit everything, which is the command's whole behaviour.</summary>
    public void ZoomToFit() => View = CanvasView.FitTo(CanvasView.ExtentOf(_host.Rects), _viewportWidth, _viewportHeight);

    /// <summary>Back to 1:1, keeping the middle of the viewport where it is.</summary>
    public void ZoomToActualSize() => Zoom = 1;

    /// <summary>Moves the view so a block is in the middle of the viewport.</summary>
    /// <param name="blockId">The block's id.</param>
    /// <returns>Whether the block is on the canvas at all.</returns>
    public bool Show(string blockId)
    {
        foreach (var rect in _host.Rects)
        {
            if (!string.Equals(rect.Id, blockId, StringComparison.Ordinal))
            {
                continue;
            }

            View = View.CentredOn(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2), _viewportWidth, _viewportHeight);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Re-reads the document and the viewport.
    /// </summary>
    /// <remarks>
    /// On every rebuild, because the canvas's extent changes when a script is added and a minimap that
    /// still shows the old extent is a thumbnail of a document that no longer exists.
    /// </remarks>
    public void Refresh()
    {
        var (width, height) = _host.Viewport;
        if (width > 0 && height > 0)
        {
            _viewportWidth = width;
            _viewportHeight = height;
        }

        RefreshMinimap();
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomText));
    }

    /// <summary>Recomputes the minimap and republishes its rectangles.</summary>
    private void RefreshMinimap()
    {
        var rects = _host.Rects;
        _minimap = Minimap.For(rects, View, _viewportWidth, _viewportHeight, 240, 150);

        MinimapRects.Clear();
        foreach (var rect in rects)
        {
            var (x, y, blockWidth, blockHeight) = _minimap.Place(rect);
            MinimapRects.Add((rect, x, y, blockWidth, blockHeight));
        }

        OnPropertyChanged(nameof(Minimap));
        OnPropertyChanged(nameof(HasNothing));
        OnPropertyChanged(nameof(ViewportRect));
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomText));
    }
}