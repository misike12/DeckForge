using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// Turns the shapes <see cref="BlockOutline"/> describes into something WPF can draw.
/// </summary>
/// <remarks>
/// <para>
/// The whole conversion, and it is deliberately that small. Everything that decides where the notch is,
/// how deep the corner is and how many mouths a C-block has was decided in Core and tested there; this
/// file only walks a list of segments. A canvas whose geometry lived in a XAML template could not be
/// tested at all, and geometry that cannot be tested is exactly where a stack stops looking connected.
/// </para>
/// <para>
/// Contours are separate figures in one geometry and the tile fills it even-odd, so a container's body
/// is a hole rather than a second tile drawn on top. That is what lets one <c>Path</c> draw a C-block
/// and its border without the edge showing through the mouth.
/// </para>
/// <para>
/// Arcs become short lines rather than <c>ArcTo</c> segments. WPF's arc API measures angles its own way
/// round from Core's, and a silent half-circle facing the wrong way is invisible in a build and obvious
/// on screen; sampling the ellipse removes the question. One sample every three degrees is smooth at
/// any zoom this editor offers and costs about a hundred points for a block, which a cached, frozen
/// geometry draws without noticing.
/// </para>
/// </remarks>
public static class BlockShapeGeometry
{
    /// <summary>How many degrees of arc one sampled segment covers.</summary>
    private const double ArcStep = 3;

    /// <summary>The geometry for one block silhouette, or null when there is nothing to draw.</summary>
    /// <remarks>
    /// Null rather than an empty geometry: a <c>Path</c> with empty <c>Data</c> still costs a render
    /// pass, and an empty tile is exactly what a silhouette that degenerated to a point means.
    /// </remarks>
    public static Geometry? ToGeometry(BlockOutlineShape outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var geometry = new StreamGeometry();
        var drawn = 0;

        using (var context = geometry.Open())
        {
            foreach (var contour in outline.Contours)
            {
                if (contour.Count == 0)
                {
                    continue;
                }

                drawn++;
                context.BeginFigure(Start(contour[0]), isFilled: true, isClosed: true);
                foreach (var segment in contour)
                {
                    switch (segment)
                    {
                        case BlockOutlineLine line:
                            context.LineTo(new Point(line.X2, line.Y2), isStroked: true, isSmoothJoin: false);
                            break;

                        case BlockOutlineArc arc:
                            foreach (var point in Sample(arc))
                            {
                                context.LineTo(point, isStroked: true, isSmoothJoin: false);
                            }

                            break;
                    }
                }
            }
        }

        geometry.Freeze();
        return drawn > 0 ? geometry : null;
    }

    /// <summary>Where a contour begins.</summary>
    private static Point Start(BlockOutlineSegment segment) => segment switch
    {
        BlockOutlineLine line => new(line.X1, line.Y1),
        BlockOutlineArc arc => Point(arc, arc.StartAngle),
        _ => new(0, 0),
    };

    /// <summary>The arc's own points, from just after its start to its end.</summary>
    /// <remarks>
    /// The final step lands on the arc's end point exactly, because the path's next segment starts there
    /// and a fraction of a pixel off is a visible hairline at the corner.
    /// </remarks>
    private static IEnumerable<Point> Sample(BlockOutlineArc arc)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.SweepAngle) / ArcStep));

        for (var step = 1; step <= steps; step++)
        {
            yield return Point(arc, arc.StartAngle + (arc.SweepAngle * step / steps));
        }
    }

    private static Point Point(BlockOutlineArc arc, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(
            arc.CenterX + (arc.RadiusX * Math.Cos(radians)),
            arc.CenterY + (arc.RadiusY * Math.Sin(radians)));
    }

    /// <summary>A readable trace of an outline, for the inspector and for failing tests.</summary>
    internal static string Describe(BlockOutlineShape outline) => string.Join(
        " | ",
        outline.Contours.Where(contour => contour.Count > 0).Select(contour => string.Join(
            " ",
            contour.Select(segment => segment switch
            {
                BlockOutlineLine line => string.Create(CultureInfo.InvariantCulture, $"L{line.X2:0.#},{line.Y2:0.#}"),
                BlockOutlineArc arc => string.Create(
                    CultureInfo.InvariantCulture,
                    $"A{arc.RadiusX:0.#} {arc.StartAngle:0.#} {arc.SweepAngle:0.#}"),
                _ => "?",
            }))));
}