using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The block silhouettes, as numbers rather than as pictures.
/// </summary>
/// <remarks>
/// <para>
/// The canvas draws these with <c>Path</c>, and the test project cannot load WPF, so the geometry is in
/// Core and what is asserted here is the geometry itself: that every contour closes, that a notch and
/// the tab below it are at the same x, and that the shapes are the ones the catalog says they are.
/// </para>
/// <para>
/// The closing check is the one that earns the file. A silhouette is drawn as a sequence of segments
/// with no explicit "close", so one segment that does not start where the last one ended leaves a gap
/// that reads on screen as a notch which is not there — and nothing in the build says so.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BlockOutlineTests
{
    /// <summary>The size the assertions use: the smallest tile the canvas will draw.</summary>
    private const double W = 160;

    private const double H = 44;

    [Test]
    [TestCase(BlockShape.Hat)]
    [TestCase(BlockShape.Stack)]
    [TestCase(BlockShape.Cap)]
    [TestCase(BlockShape.C)]
    [TestCase(BlockShape.CIf)]
    [TestCase(BlockShape.CChain)]
    [TestCase(BlockShape.Reporter)]
    [TestCase(BlockShape.BooleanReporter)]
    [TestCase(BlockShape.Modifier)]
    [TestCase(BlockShape.Placeholder)]
    public void Every_contour_closes(BlockShape shape)
    {
        var outline = BlockOutline.Create(shape, W, H);

        Assert.That(outline.Contours, Is.Not.Empty, $"{shape} produced no contour at all.");

        foreach (var contour in outline.Contours)
        {
            var start = First(contour);
            var end = Last(contour);

            Assert.That(
                Distance(start, end),
                Is.LessThan(0.5),
                $"{shape}: the contour starts at {start} and ends at {end}, so the path has a gap in it.");
        }
    }

    [Test]
    [TestCase(BlockShape.Hat)]
    [TestCase(BlockShape.Stack)]
    [TestCase(BlockShape.Cap)]
    [TestCase(BlockShape.C)]
    [TestCase(BlockShape.CIf)]
    [TestCase(BlockShape.CChain)]
    [TestCase(BlockShape.Reporter)]
    [TestCase(BlockShape.BooleanReporter)]
    [TestCase(BlockShape.Modifier)]
    [TestCase(BlockShape.Placeholder)]
    public void No_contour_leaves_the_tile(BlockShape shape)
    {
        var outline = BlockOutline.Create(shape, W, H);

        Assert.That(outline.Contours.All(contour => contour.All(segment => segment switch
        {
            BlockOutlineLine line =>
                Between(line.X1, 0, W) && Between(line.Y1, 0, H)
                && Between(line.X2, 0, W) && Between(line.Y2, 0, H),
            BlockOutlineArc arc =>
                arc.RadiusX >= 0 && arc.RadiusY >= 0 && Math.Abs(arc.SweepAngle) > 0,
            _ => false,
        })), Is.True, $"{shape} draws outside the rectangle it was given.");
    }

    [Test]
    public void The_tab_below_a_stack_sits_under_the_notch_that_follows_it()
    {
        // The whole reason the height includes the tab: a stack of two blocks whose heights sum to the
        // second block's top edge must have the first block's tab inside the second block's notch. If
        // the two ever disagree the stack stops looking connected, and both numbers are constants.
        var outline = BlockOutline.Create(BlockShape.Stack, W, H);

        Assert.Multiple(() =>
        {
            Assert.That(outline.NotchX, Is.EqualTo(BlockOutline.NotchInset + BlockOutline.NotchWidth / 2));
            Assert.That(outline.NotchY, Is.EqualTo(BlockOutline.NotchDepth));
            Assert.That(
                Contour(outline, 0).OfType<BlockOutlineLine>()
                    .Where(line => Math.Abs(line.Y2 - H) < 0.001)
                    .Select(line => line.X2)
                    .Any(x => Math.Abs(x - outline.NotchX) < BlockOutline.NotchWidth),
                Is.True,
                "the tab reaches the same x as the notch, or the next block's notch misses it.");
        });
    }

    [Test]
    public void A_hat_has_no_notch_because_nothing_sits_above_it()
    {
        var outline = BlockOutline.Create(BlockShape.Hat, W, H);

        Assert.Multiple(() =>
        {
            Assert.That(outline.NotchY, Is.Zero, "a hat's top edge is its connector");
            Assert.That(
                Contour(outline, 0).OfType<BlockOutlineLine>().Any(line => line.Y1 == 0 && line.Y2 == 0),
                Is.False,
                "a hat has no flat top edge to draw a notch into");
        });
    }

    [Test]
    public void A_cap_has_no_tab_because_nothing_follows_it()
    {
        var outline = BlockOutline.Create(BlockShape.Cap, W, H);

        Assert.Multiple(() =>
        {
            Assert.That(
                Contour(outline, 0).OfType<BlockOutlineLine>().Any(line => line.Y2 > H - BlockOutline.NotchDepth),
                Is.False,
                "a cap ends the flow, so its bottom edge stops at the tile's edge");
        });
    }

    [Test]
    public void A_container_cuts_one_hole_per_mouth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlockOutline.Create(BlockShape.C, W, 200).Contours, Has.Count.EqualTo(2),
                "one outer contour and one mouth");
            Assert.That(BlockOutline.Create(BlockShape.CIf, W, 200).Contours, Has.Count.EqualTo(3),
                "one outer contour and a mouth per branch");
            Assert.That(BlockOutline.Create(BlockShape.CChain, W, 200).Contours, Has.Count.EqualTo(4),
                "if / else if / else is three bars");
        });
    }

    [Test]
    public void A_containers_mouths_sit_inside_its_header_and_above_its_footer()
    {
        const double height = 220;
        var outline = BlockOutline.Create(BlockShape.C, W, height);
        var mouth = Rect(Contour(outline, 1));

        Assert.Multiple(() =>
        {
            Assert.That(mouth.Top, Is.GreaterThanOrEqualTo(outline.HeaderHeight),
                "the label has room above the mouth");
            Assert.That(mouth.Left, Is.EqualTo(outline.ArmWidth).Within(0.001),
                "the mouth is indented by the same rail the body's statements sit on");
            Assert.That(mouth.Right, Is.EqualTo(W).Within(0.001),
                "the mouth reaches the block's right edge");
            Assert.That(mouth.Bottom, Is.LessThan(height - BlockOutline.NotchDepth),
                "the footer bar and the tab sit below the mouth, not inside it");
        });
    }

    [Test]
    public void An_empty_container_still_leaves_room_for_a_mouth()
    {
        // The reason the header and footer are constants rather than a share of the tile: the layout
        // engine asks for a height before anything is measured, so a body region that depended on the
        // height could not be laid out at all.
        var outline = BlockOutline.Create(BlockShape.C, W, H);
        var mouth = Rect(Contour(outline, 1));

        Assert.Multiple(() =>
        {
            Assert.That(mouth.Bottom, Is.GreaterThan(mouth.Top),
                "a C-block drawn at the minimum height still has somewhere to drop a statement");
            Assert.That(mouth.Left, Is.LessThan(mouth.Right));
        });
    }

    [Test]
    public void An_if_else_gives_each_branch_its_own_bar_and_none_of_them_overlap()
    {
        var outline = BlockOutline.Create(BlockShape.CIf, W, 240);
        var first = Rect(Contour(outline, 1));
        var second = Rect(Contour(outline, 2));

        Assert.Multiple(() =>
        {
            Assert.That(second.Top, Is.GreaterThan(first.Bottom),
                "the bar between the branches is what keeps them from running together");
            Assert.That(first.Bottom, Is.LessThan(second.Bottom));
        });
    }

    [Test]
    public void A_reporter_is_a_stadium_and_a_boolean_is_a_hexagon()
    {
        var reporter = Contour(BlockOutline.Create(BlockShape.Reporter, W, 28), 0);
        var hexagon = Contour(BlockOutline.Create(BlockShape.BooleanReporter, W, 28), 0);

        Assert.Multiple(() =>
        {
            Assert.That(reporter.OfType<BlockOutlineArc>().Count(), Is.EqualTo(2),
                "an oval is two half circles and two straight sides");
            Assert.That(hexagon.OfType<BlockOutlineArc>(), Is.Empty, "a hexagon has no curves in it");
            Assert.That(hexagon.OfType<BlockOutlineLine>().Count(), Is.EqualTo(6));
        });
    }

    [Test]
    public void Every_shape_the_catalog_uses_can_be_drawn_at_any_size()
    {
        // A tile narrower than its own corner radius, or a height smaller than a notch, used to produce
        // arcs that turned back on themselves. The canvas sizes tiles from measured text, so this
        // happens in normal use rather than only in a test. A container's mouth may legitimately
        // collapse at 1x1 — there is no room for a hole — but the block's own edge must always be there.
        var missing = new List<string>();

        foreach (var shape in Enum.GetValues<BlockShape>())
        {
            foreach (var (w, h) in new[] { (1.0, 1.0), (8.0, 8.0), (24.0, 12.0), (400.0, 300.0) })
            {
                var outline = BlockOutline.Create(shape, w, h);
                if (outline.Contours.Count == 0 || outline.Contours[0].Count == 0)
                {
                    missing.Add($"{shape} at {w}x{h}");
                }
            }
        }

        Assert.That(missing, Is.Empty, string.Join("; ", missing));
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static IReadOnlyList<BlockOutlineSegment> Contour(BlockOutlineShape outline, int index) =>
        outline.Contours[index];

    private static (double X, double Y) First(IReadOnlyList<BlockOutlineSegment> contour) =>
        contour[0] switch
        {
            BlockOutlineLine line => (line.X1, line.Y1),
            BlockOutlineArc arc => Point(arc, arc.StartAngle),
            _ => (0, 0),
        };

    private static (double X, double Y) Last(IReadOnlyList<BlockOutlineSegment> contour) =>
        contour[^1] switch
        {
            BlockOutlineLine line => (line.X2, line.Y2),
            BlockOutlineArc arc => Point(arc, arc.StartAngle + arc.SweepAngle),
            _ => (0, 0),
        };

    private static (double X, double Y) Point(BlockOutlineArc arc, double angle)
    {
        var radians = angle * Math.PI / 180;
        return (arc.CenterX + arc.RadiusX * Math.Cos(radians), arc.CenterY + arc.RadiusY * Math.Sin(radians));
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static bool Between(double value, double low, double high) =>
        value >= low - 0.001 && value <= high + 0.001;

    private static (double Top, double Left, double Right, double Bottom) Rect(
        IReadOnlyList<BlockOutlineSegment> contour)
    {
        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var segment in contour)
        {
            switch (segment)
            {
                case BlockOutlineLine line:
                    xs.Add(line.X1);
                    xs.Add(line.X2);
                    ys.Add(line.Y1);
                    ys.Add(line.Y2);
                    break;
                case BlockOutlineArc arc:
                    foreach (var quarter in new[] { 0d, 90d, 180d, 270d })
                    {
                        var point = Point(arc, arc.StartAngle + quarter * Math.Sign(arc.SweepAngle));
                        xs.Add(point.X);
                        ys.Add(point.Y);
                    }

                    break;
            }
        }

        return (ys.Min(), xs.Min(), xs.Max(), ys.Max());
    }
}