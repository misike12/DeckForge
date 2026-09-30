namespace DeckForge.Core.Visual;

/// <summary>One edge of a block silhouette.</summary>
/// <remarks>
/// A closed contour is a sequence of these with no gaps: an arc records where it ended, so the next
/// segment always starts where the last one stopped. That is what lets the canvas hand the contour
/// straight to a <c>PathFigure</c> without re-deriving a point that was already computed.
/// </remarks>
public abstract record BlockOutlineSegment;

/// <summary>A straight edge, in the tile's own coordinates.</summary>
public sealed record BlockOutlineLine(double X1, double Y1, double X2, double Y2) : BlockOutlineSegment;

/// <summary>
/// An elliptical arc.
/// </summary>
/// <param name="CenterX">The centre's x.</param>
/// <param name="CenterY">The centre's y.</param>
/// <param name="RadiusX">The horizontal radius.</param>
/// <param name="RadiusY">The vertical radius.</param>
/// <param name="StartAngle">Where the arc starts, in degrees, where 0 is to the right and 90 is down.</param>
/// <param name="SweepAngle">
/// How far it travels, in degrees. Positive is clockwise on screen, which is the direction the block
/// outline is drawn in, so a corner is always a positive sweep.
/// </param>
/// <remarks>
/// The angles are recorded rather than the start and end points because a quarter circle of radius
/// <c>r</c> at the top-right of a block has both endpoints exactly <c>r</c> away from the corner, and
/// two different arcs pass through them. Naming the centre and the sweep removes the choice
/// altogether, which is the only way to be sure a corner curves the way the eye expects.
/// </remarks>
public sealed record BlockOutlineArc(
    double CenterX,
    double CenterY,
    double RadiusX,
    double RadiusY,
    double StartAngle,
    double SweepAngle) : BlockOutlineSegment;

/// <summary>
/// A block's silhouette, at one size: one or more closed contours plus where a block above connects.
/// </summary>
/// <param name="Shape">The silhouette that produced it.</param>
/// <param name="Contours">
/// Closed outlines. A container has two or more — its outer edge and one hole per mouth — which the
/// canvas fills even-odd, so the body is a hole rather than a second overlapping tile.
/// </param>
/// <param name="Width">The width the silhouette was built for.</param>
/// <param name="Height">The height it was built for, tab included.</param>
/// <param name="NotchX">
/// The notch's centre in the tile's coordinates, which is where the block above lands its tab and
/// where the block below hangs its own notch.
/// </param>
/// <param name="NotchY">The notch's depth: how far the silhouette dips in from the top edge.</param>
/// <param name="HeaderHeight">Where the first mouth starts, for a container; zero otherwise.</param>
/// <param name="ArmWidth">How far a container's body is indented from its left edge; zero otherwise.</param>
public sealed record BlockOutlineShape(
    BlockShape Shape,
    IReadOnlyList<IReadOnlyList<BlockOutlineSegment>> Contours,
    double Width,
    double Height,
    double NotchX,
    double NotchY,
    double HeaderHeight,
    double ArmWidth);

/// <summary>
/// The silhouette of every block shape, as numbers.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.3 of the design asks for "real notch and tab geometry drawn with <c>Path</c>", so a stack
/// reads as physically connected rather than as a list of rounded rectangles. The geometry itself is
/// here, in Core, and not in the XAML, for the reason every other piece of canvas intelligence in this
/// feature is in Core: <c>tests/DeckForge.Tests</c> cannot reference WPF, so geometry written in a
/// template is geometry nothing can check. A notch that silently stops lining up with the tab above it
/// is the defect this shape exists to make impossible — the stack reads as a list again, and nothing
/// says why.
/// </para>
/// <para>
/// The size is an argument rather than a property. WPF measures first and asks afterwards, so the
/// canvas hands over the rectangle the tile actually got; the result is exact rather than estimated.
/// The rough sizes live in <see cref="BlockMetrics"/> for the layout engine, which has to work before
/// anything has been measured.
/// </para>
/// </remarks>
public static class BlockOutline
{
    /// <summary>The width of the notch above and the tab below.</summary>
    public const double NotchWidth = 24;

    /// <summary>How deep the notch cuts in, and how far the tab stands out.</summary>
    public const double NotchDepth = 5;

    /// <summary>Where the notch starts, measured from the block's left edge.</summary>
    /// <remarks>
    /// Not zero, because a notch hard against the left edge collides with the rounded corner and stops
    /// reading as a notch. Scratch puts it about a fifth of the way in, which is what this matches.
    /// </remarks>
    public const double NotchInset = 14;

    /// <summary>The corner radius every shape uses, from Part 9.3.</summary>
    public const double CornerRadius = BlockMetrics.CornerRadius;

    /// <summary>How tall a container's header bar is, which is also where its first mouth starts.</summary>
    public const double HeaderHeight = 34;

    /// <summary>How tall a container's footer bar is.</summary>
    public const double FooterHeight = 16;

    /// <summary>How far a container's body is indented, matching <see cref="BlockMetrics.BodyIndent"/>.</summary>
    public const double ArmWidth = BlockMetrics.BodyIndent;

    /// <summary>How far a hat's top rises above the shoulders.</summary>
    public const double DomeHeight = 8;

    /// <summary>How far a boolean reporter's points stand out from its flat sides.</summary>
    public const double HexPoint = 10;

    /// <summary>
    /// The silhouette of <paramref name="shape"/> at <paramref name="width"/> by <paramref name="height"/>.
    /// </summary>
    /// <remarks>
    /// The height always includes the tab, so a stack of blocks whose heights sum to the block below's
    /// top edge lines up with no gap to tune: the tab fills the notch by construction. That is the whole
    /// reason the geometry is shared with the layout engine rather than drawn twice.
    /// </remarks>
    public static BlockOutlineShape Create(BlockShape shape, double width, double height)
    {
        var w = Math.Max(16, width);
        var h = Math.Max(16, height);

        return shape switch
        {
            BlockShape.Hat => Hat(w, h),
            BlockShape.Cap => Cap(w, h),
            BlockShape.C or BlockShape.CIf or BlockShape.CChain => Container(shape, w, h),
            BlockShape.Reporter => Oval(w, h),
            BlockShape.BooleanReporter => Hexagon(w, h),
            BlockShape.Modifier => RoundedBox(w, h),
            _ => Stack(w, h),
        };
    }

    /// <summary>How many mouths a container of this shape has.</summary>
    private static int Mouths(BlockShape shape) => shape switch
    {
        BlockShape.C => 1,
        BlockShape.CIf => 2,
        // CChain is the <c>if / else if / else</c> shape: three bars, and the middle one may be empty.
        // No catalog row uses it yet — the catalogue builds it as a fixed three-body CIf — so it is
        // drawn here rather than left as a shape nothing could render.
        _ => 3,
    };

    private static BlockOutlineShape Stack(double w, double h)
    {
        var r = Radius(w, h - NotchDepth);
        var contour = new Contour();
        TabbedTop(contour, r);
        contour.Arc(w - r, r, r, r, -90, 90);
        contour.LineTo(w, h - NotchDepth - r);
        contour.Arc(w - r, h - NotchDepth - r, r, r, 0, 90);
        TabBelow(contour, h - NotchDepth);
        contour.LineTo(r, h - NotchDepth);
        contour.Arc(r, h - NotchDepth - r, r, r, 180, 90);
        contour.LineTo(0, r);
        contour.Arc(r, r, r, r, 180, 90);

        return Finish(BlockShape.Stack, w, h, [contour.Build()]);
    }

    private static BlockOutlineShape Hat(double w, double h)
    {
        var r = Math.Min(CornerRadius, Math.Min(w / 2, (h - NotchDepth - DomeHeight) / 2));
        var contour = new Contour();

        // A dome rather than a flat top: the one place a block's silhouette is allowed to break the
        // rectangle, because it is how a user sees at a glance which end of a script is the start.
        contour.MoveTo(r, DomeHeight);
        contour.Arc(w / 2, DomeHeight, Math.Max(1, w / 2 - r), DomeHeight, 180, 180);
        contour.LineTo(w, h - NotchDepth - r);
        contour.Arc(w - r, h - NotchDepth - r, r, r, 0, 90);
        TabBelow(contour, h - NotchDepth);
        contour.LineTo(r, h - NotchDepth);
        contour.Arc(r, h - NotchDepth - r, r, r, 180, 90);
        contour.LineTo(0, DomeHeight + r);
        contour.Arc(r, DomeHeight + r, r, r, 180, 90);

        // Nothing sits above a hat, so there is no notch to line up: the connector is the top edge.
        return Finish(BlockShape.Hat, w, h, [contour.Build()], notchY: 0);
    }

    private static BlockOutlineShape Cap(double w, double h)
    {
        var bulge = Math.Min(12, h / 3);
        var r = Radius(w, h - bulge);
        var contour = new Contour();
        TabbedTop(contour, r);
        contour.Arc(w - r, r, r, r, -90, 90);
        contour.LineTo(w, h - bulge - r);
        contour.Arc(w - r, h - bulge - r, r, r, 0, 90);
        contour.LineTo(w - r, h - bulge);
        contour.Arc(w / 2, h - bulge, Math.Max(1, w / 2 - r), bulge, 0, 180);
        contour.LineTo(r, h - bulge);
        contour.Arc(r, h - bulge - r, r, r, 180, 90);
        contour.LineTo(0, r);
        contour.Arc(r, r, r, r, 180, 90);

        // A cap ends the flow, so nothing follows it: no tab below either.
        return Finish(BlockShape.Cap, w, h, [contour.Build()], tab: false);
    }

    private static BlockOutlineShape Container(BlockShape shape, double w, double h)
    {
        var bottom = h - NotchDepth;
        var r = Radius(w, bottom);
        var contour = new Contour();
        TabbedTop(contour, r);
        contour.Arc(w - r, r, r, r, -90, 90);
        contour.LineTo(w, bottom - r);
        contour.Arc(w - r, bottom - r, r, r, 0, 90);
        TabBelow(contour, bottom);
        contour.LineTo(r, bottom);
        contour.Arc(r, bottom - r, r, r, 180, 90);
        contour.LineTo(0, r);
        contour.Arc(r, r, r, r, 180, 90);

        var contours = new List<IReadOnlyList<BlockOutlineSegment>> { contour.Build() };
        var mouths = Mouths(shape);

        // A mouth narrower or shorter than the arm it sits inside would be a hole with no room in it,
        // which happens for a tile narrower than the layout engine asked for. Clamping keeps the
        // geometry well-formed rather than leaving the canvas to draw a sliver.
        var arm = Math.Min(ArmWidth, Math.Max(1, w / 2 - 1));
        var regionTop = Math.Min(HeaderHeight, h - 1);
        var regionBottom = Math.Max(regionTop + 1, bottom - FooterHeight);
        var share = (regionBottom - regionTop + FooterHeight) / mouths;

        for (var i = 0; i < mouths; i++)
        {
            // Clamped to the tile, because a chain of bars divides the region between them and a
            // minimum-height tile has room for the first mouth and not for the third.
            var top = Math.Min(regionTop + (i * share), Math.Max(regionTop, h - 2));
            var mouthBottom = Math.Max(top + 1, Math.Min(h - 1, Math.Min(regionBottom, top + share - FooterHeight)));
            var mouth = new Contour();
            mouth.MoveTo(arm, top);
            mouth.LineTo(w, top);
            mouth.LineTo(w, mouthBottom);
            mouth.LineTo(arm, mouthBottom);
            contours.Add(mouth.Build());
        }

        return Finish(shape, w, h, contours, headerHeight: HeaderHeight, armWidth: arm);
    }

    private static BlockOutlineShape Oval(double w, double h)
    {
        var ry = h / 2;
        var rx = Math.Min(ry, w / 2);
        var contour = new Contour();
        contour.MoveTo(rx, 0);
        contour.Arc(rx, ry, rx, ry, -90, 180);
        contour.LineTo(w - rx, h);
        contour.Arc(w - rx, ry, rx, ry, 90, 180);

        return Finish(BlockShape.Reporter, w, h, [contour.Build()], notchY: 0);
    }

    private static BlockOutlineShape Hexagon(double w, double h)
    {
        var point = Math.Min(HexPoint, w / 2);
        var contour = new Contour();
        contour.MoveTo(point, 0);
        contour.LineTo(w - point, 0);
        contour.LineTo(w, h / 2);
        contour.LineTo(w - point, h);
        contour.LineTo(point, h);
        contour.LineTo(0, h / 2);

        return Finish(BlockShape.BooleanReporter, w, h, [contour.Build()], notchY: 0);
    }

    private static BlockOutlineShape RoundedBox(double w, double h)
    {
        var r = Radius(w, h);
        var contour = new Contour();
        contour.MoveTo(r, 0);
        contour.LineTo(w - r, 0);
        contour.Arc(w - r, r, r, r, -90, 90);
        contour.LineTo(w, h - r);
        contour.Arc(w - r, h - r, r, r, 0, 90);
        contour.LineTo(r, h);
        contour.Arc(r, h - r, r, r, 180, 90);
        contour.LineTo(0, r);
        contour.Arc(r, r, r, r, 180, 90);

        return Finish(BlockShape.Modifier, w, h, [contour.Build()], tab: false, notchY: 0);
    }

    /// <summary>Where the notch starts on the top edge, and the two edges of the notch itself.</summary>
    private static void TabbedTop(Contour contour, double r)
    {
        contour.MoveTo(r, 0);
        contour.LineTo(NotchInset, 0);
        contour.LineTo(NotchInset + NotchWidth / 2, NotchDepth);
        contour.LineTo(NotchInset + NotchWidth, 0);
    }

    /// <summary>
///     The tab on the bottom edge, drawn right to left because that is the direction of travel.
/// </summary>
    /// <remarks>
    /// The bottom edge is passed in rather than read off the pen. Reading it between segments walks the
    /// tab three steps further down every time, so it ends up below the tile and the next block's notch
    /// no longer lines up with it.
    /// </remarks>
    private static void TabBelow(Contour contour, double bottom)
    {
        contour.LineTo(NotchInset + NotchWidth, bottom);
        contour.LineTo(NotchInset + NotchWidth, bottom + NotchDepth);
        contour.LineTo(NotchInset, bottom + NotchDepth);
        contour.LineTo(NotchInset, bottom);
    }

    private static double Radius(double w, double h) =>
        Math.Max(1, Math.Min(CornerRadius, Math.Min(w / 2, h / 2)));

    private static BlockOutlineShape Finish(
        BlockShape shape,
        double w,
        double h,
        IReadOnlyList<IReadOnlyList<BlockOutlineSegment>> contours,
        bool tab = true,
        double? notchY = null,
        double headerHeight = 0,
        double armWidth = 0) =>
        new(shape, contours, w, h, NotchInset + NotchWidth / 2, notchY ?? (tab ? NotchDepth : 0), headerHeight, armWidth);

    /// <summary>Builds one closed contour, keeping track of where the pen is.</summary>
    private sealed class Contour
    {
        private readonly List<BlockOutlineSegment> _segments = [];
        private double _x;

        private double _y;

        private double _startX;

        private double _startY;

        public void MoveTo(double x, double y)
        {
            _x = _startX = x;
            _y = _startY = y;
        }

        public void LineTo(double x, double y)
        {
            if (Math.Abs(x - _x) > 0.01 || Math.Abs(y - _y) > 0.01)
            {
                _segments.Add(new BlockOutlineLine(_x, _y, x, y));
            }

            _x = x;
            _y = y;
        }

        public void Arc(double centerX, double centerY, double radiusX, double radiusY, double start, double sweep)
        {
            var end = ToRadians(start + sweep);
            _segments.Add(new BlockOutlineArc(centerX, centerY, radiusX, radiusY, start, sweep));
            _x = centerX + radiusX * Math.Cos(end);
            _y = centerY + radiusY * Math.Sin(end);
        }

        /// <summary>
        /// The contour's segments, closed back to where it began.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The closing edge is added rather than left to the canvas. WPF's <c>PathFigure</c> closes a
        /// contour implicitly, so an omission would still look right on screen — but the geometry would
        /// not describe the shape it draws, and the first shape that leans on that (a reporter, whose
        /// outline is four segments with the fourth landing 100 units from the first) is invisible until
        /// somebody exports the outline or fills it even-odd.
        /// </para>
        /// <para>
        /// A contour that degenerated to a point is dropped rather than closed with a zero-length line.
        /// That happens when a caller hands over a tile narrower than its own corner radius, and drawing
        /// nothing is the honest answer.
        /// </para>
        /// </remarks>
        public IReadOnlyList<BlockOutlineSegment> Build()
        {
            if (_segments.Count == 0)
            {
                return [];
            }

            LineTo(_startX, _startY);
            return _segments.Count >= 2 ? _segments : [];
        }
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}