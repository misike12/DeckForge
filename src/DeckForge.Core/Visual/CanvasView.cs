namespace DeckForge.Core.Visual;

/// <summary>
/// The canvas's zoom and pan, plus the arithmetic that turns a document's extent into a view that fits it.
/// </summary>
/// <remarks>
/// <para>
/// A value in Core rather than a <c>ScaleTransform</c> in a control, for the reason this file exists: three
/// things need to agree about where a block is — the canvas that draws it, the drop resolver that scores
/// gaps against it, and the minimap that shows a thumbnail of it. A zoom that lives in a control can only
/// be read by the control, so the other two would have to ask it, and asking a control for a number during
/// a drag is how a canvas ends up dropping a block where the ghost was not.
/// </para>
/// <para>
/// Workspace units are unscaled. Everything the user writes down — "the block is 40 pixels wide" — is in
/// those units, so the zoom is applied once, at the end, on the way to the screen.
/// </para>
/// </remarks>
public sealed record CanvasView
{
    /// <summary>The smallest zoom a user can reach.</summary>
    /// <remarks>
    /// 0.25 rather than 0.1. Below a quarter the labels stop being readable and the canvas stops being
    /// anything but coloured rectangles, which is a worse thing to offer than not offering it.
    /// </remarks>
    public const double MinZoom = 0.25;

    /// <summary>The largest zoom.</summary>
    /// <remarks>
    /// 2.5. A block's own layout is a font size and a notch depth; past about double, the text is larger
    /// than the notch it sits in and the shape the whole design is built on stops reading as a shape.
    /// </remarks>
    public const double MaxZoom = 2.5;

    /// <summary>
    /// The zoom, always clamped.
    /// </summary>
    /// <remarks>
    /// Clamped on the way in rather than trusted, because a pinch gesture or a Ctrl+wheel sends deltas and
    /// a canvas that obeys them all the way to zero has a hit test with nothing to hit.
    /// </remarks>
    public double Zoom { get; init; } = 1;

    /// <summary>How far the document is shifted left and up, in workspace units.</summary>
    public double PanX { get; init; }

    /// <summary>How far the document is shifted up, in workspace units.</summary>
    public double PanY { get; init; }

    /// <summary>The zoom, clamped to what the canvas will draw.</summary>
    public double ClampedZoom => Math.Clamp(Zoom, MinZoom, MaxZoom);

    /// <summary>A view at a given zoom, with the pan kept.</summary>
    /// <param name="zoom">The zoom asked for.</param>
    public CanvasView AtZoom(double zoom) => this with { Zoom = zoom };

    /// <summary>A view panned by a delta in *screen* pixels.</summary>
    /// <param name="dx">Pixels right.</param>
    /// <param name="dy">Pixels down.</param>
    /// <remarks>
    /// Divided by the zoom, because a drag that moves the pointer five pixels has to move the document five
    /// pixels whatever the zoom. Taking the delta as workspace units instead makes the document race the
    /// pointer at 200% and crawl at 50%, which is the classic zoom-pan bug and the reason this takes pixels.
    /// </remarks>
    public CanvasView PannedBy(double dx, double dy) =>
        this with { PanX = PanX + (dx / ClampedZoom), PanY = PanY + (dy / ClampedZoom) };

    /// <summary>A view centred on a point in workspace units.</summary>
    /// <param name="x">The point's x.</param>
    /// <param name="y">The point's y.</param>
    /// <param name="viewportWidth">The viewport's width in pixels.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    public CanvasView CentredOn(double x, double y, double viewportWidth, double viewportHeight) => this with
    {
        PanX = x - (viewportWidth / (2 * ClampedZoom)),
        PanY = y - (viewportHeight / (2 * ClampedZoom)),
    };

    /// <summary>
    /// The view that fits an extent into a viewport, with a margin.
    /// </summary>
    /// <remarks>
    /// Never larger than 1. "Zoom to fit" on a small script must not *enlarge* it: doubling a three-block
    /// script so it fills the window is the wrong answer to "show me everything", and it is what a naive
    /// scale-to-fit does.
    /// </remarks>
    /// <param name="extent">The document's extent in workspace units.</param>
    /// <param name="viewportWidth">The viewport's width in pixels.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    /// <param name="margin">Pixels to leave around it.</param>
    public static CanvasView FitTo(
        (double Width, double Height) extent,
        double viewportWidth,
        double viewportHeight,
        double margin = 24)
    {
        if (extent.Width <= 0 || extent.Height <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return new CanvasView();
        }

        var usableWidth = Math.Max(1, viewportWidth - (margin * 2));
        var usableHeight = Math.Max(1, viewportHeight - (margin * 2));

        var zoom = Math.Min(usableWidth / extent.Width, usableHeight / extent.Height);

        return new CanvasView
        {
            Zoom = Math.Clamp(Math.Min(zoom, 1), MinZoom, MaxZoom),
            PanX = (extent.Width / 2) - (viewportWidth / (2 * Math.Clamp(Math.Min(zoom, 1), MinZoom, MaxZoom))),
            PanY = (extent.Height / 2) - (viewportHeight / (2 * Math.Clamp(Math.Min(zoom, 1), MinZoom, MaxZoom))),
        };
    }

    /// <summary>The extent of a set of rects, or an empty one when there are none.</summary>
    /// <param name="rects">The rects, in workspace units.</param>
    public static (double Width, double Height) ExtentOf(IEnumerable<BlockRect> rects)
    {
        double right = 0;
        double bottom = 0;
        var any = false;

        foreach (var rect in rects)
        {
            any = true;
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }

        return any ? (right, bottom) : (0, 0);
    }

    /// <summary>
    /// A point in workspace units, from a point on screen.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="BlockRect.Project"/>, and the reason the hit test can ask where the
    /// pointer is over a block while zoomed. Without it the hit test has to walk every block and invert
    /// each one, which is O(blocks) per mouse move at a hundred blocks and visibly lags at five hundred.
    /// </remarks>
    /// <param name="screenX">The pointer's x in the viewport.</param>
    /// <param name="screenY">The pointer's y in the viewport.</param>
    public (double X, double Y) ToWorkspace(double screenX, double screenY) =>
        ((screenX / ClampedZoom) + PanX, (screenY / ClampedZoom) + PanY);
}

/// <summary>
/// The minimap: the document drawn small, plus the rectangle showing the part of it that is on screen.
/// </summary>
/// <remarks>
/// A value in Core for the same reason <see cref="CanvasView"/> is. The minimap's only interesting
/// computation is "how do I draw the whole document in this box, and where does the viewport land inside
/// it", and both of those are arithmetic on a document's extent — arithmetic that is much easier to be
/// wrong about in a <c>Canvas</c> than in a method with a test.
/// </remarks>
/// <param name="Document">The view that fits the whole document into the box.</param>
/// <param name="Rects">Every block's rect, in workspace units.</param>
/// <param name="Viewport">Where the viewport sits, in the minimap's own pixels.</param>
/// <param name="BoxWidth">The minimap's width in pixels.</param>
/// <param name="BoxHeight">The minimap's height in pixels.</param>
public sealed record Minimap(
    CanvasView Document,
    (double Width, double Height) Extent,
    IReadOnlyList<BlockRect> Rects,
    (double X, double Y, double Width, double Height) Viewport,
    double BoxWidth,
    double BoxHeight)
{
    /// <summary>
    /// Builds a minimap for a document at a viewport.
    /// </summary>
    /// <remarks>
    /// The viewport is clamped into the document rather than allowed to hang off it, because a viewport
    /// larger than the document — which is what happens when the user is zoomed out past the whole canvas —
    /// would otherwise draw a rectangle that starts outside the map and is therefore not clickable.
    /// </remarks>
    /// <param name="rects">Every block's rect, in workspace units.</param>
    /// <param name="view">The live view.</param>
    /// <param name="viewportWidth">The canvas viewport's width in pixels.</param>
    /// <param name="viewportHeight">The canvas viewport's height in pixels.</param>
    /// <param name="boxWidth">The minimap's width in pixels.</param>
    /// <param name="boxHeight">The minimap's height in pixels.</param>
    public static Minimap For(
        IReadOnlyList<BlockRect> rects,
        CanvasView view,
        double viewportWidth,
        double viewportHeight,
        double boxWidth,
        double boxHeight)
    {
        var extent = CanvasView.ExtentOf(rects);
        var document = CanvasView.FitTo(extent, boxWidth, boxHeight, margin: 4);
        var zoom = document.ClampedZoom;

        // The viewport in workspace units first, because that is the unit the extent is in: the pan is how
        // far the document has been shifted, so the visible region starts at the negation of it.
        var width = Math.Min(viewportWidth / zoom, extent.Width);
        var height = Math.Min(viewportHeight / zoom, extent.Height);
        var left = Math.Clamp(-view.PanX * zoom, 0, Math.Max(0, extent.Width - width));
        var top = Math.Clamp(-view.PanY * zoom, 0, Math.Max(0, extent.Height - height));

        var (_, offsetY) = Offsets(extent, zoom, boxHeight);
        var offsetX = (boxWidth - (extent.Width * zoom)) / 2;

        return new Minimap(
            document,
            extent,
            rects,
            (offsetX + (left * zoom), offsetY + (top * zoom), width * zoom, height * zoom),
            boxWidth,
            boxHeight);
    }

    /// <summary>Where one block is drawn in the minimap, in the box's pixels.</summary>
    /// <param name="rect">The block's rect in workspace units.</param>
    public (double X, double Y, double Width, double Height) Place(BlockRect rect)
    {
        var zoom = Document.ClampedZoom;
        var offsetX = (BoxWidth - (Extent.Width * zoom)) / 2;
        var (_, offsetY) = Offsets(Extent, zoom, BoxHeight);

        return (
            offsetX + (rect.X * zoom),
            offsetY + (rect.Y * zoom),
            Math.Max(1, rect.Width * zoom),
            Math.Max(1, rect.Height * zoom));
    }

    /// <summary>
    /// The view that puts a point in the minimap back where the user clicked.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="Place"/>, and the reason clicking the minimap works. A minimap that only
    /// shows where you are is a picture; one you can click is a scrollbar with a map on it, which is the
    /// whole point of putting it beside a canvas that is wider than the window.
    /// </remarks>
    /// <param name="boxX">Where the user clicked, in the box's pixels.</param>
    /// <param name="boxY">Where the user clicked, in the box's pixels.</param>
    /// <param name="viewportWidth">The canvas viewport's width in pixels.</param>
    /// <param name="viewportHeight">The canvas viewport's height in pixels.</param>
    public CanvasView ViewAt(double boxX, double boxY, double viewportWidth, double viewportHeight)
    {
        var zoom = Document.ClampedZoom;
        var offsetX = (BoxWidth - (Extent.Width * zoom)) / 2;
        var (_, offsetY) = Offsets(Extent, zoom, BoxHeight);

        var workspaceX = (boxX - offsetX) / zoom;
        var workspaceY = (boxY - offsetY) / zoom;

        return Document.CentredOn(
            Math.Clamp(workspaceX, 0, Math.Max(0, Extent.Width)),
            Math.Clamp(workspaceY, 0, Math.Max(0, Extent.Height)),
            viewportWidth,
            viewportHeight);
    }

    /// <summary>
    /// The vertical offset the map centres its document at.
    /// </summary>
    /// <remarks>
    /// Horizontal and vertical are treated differently on purpose: a wide canvas is usually taller than
    /// the window and centred horizontally, so the x offset is negative and means "the document starts
    /// off the left edge" — which is correct, and is why x and y cannot share one formula.
    /// </remarks>
    private static (double X, double Y) Offsets((double Width, double Height) extent, double zoom, double boxHeight)
    {
        var y = (boxHeight - (extent.Height * zoom)) / 2;

        return ((boxHeight - (extent.Height * zoom)) / 2, y);
    }
}