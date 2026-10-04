using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The rubber band: which blocks a drag's rectangle touches.
/// </summary>
/// <remarks>
/// <para>
/// Part 18.1 gives the gesture a pointer and Part 9.5's argument for putting the drop resolver in Core
/// applies to it exactly: the drawing of a rectangle needs WPF and the *question* of which blocks it covers
/// does not, and a question that can only be answered by dragging a mouse across a window is a question
/// nobody has checked the corners of. Four of the cases below are corners.
/// </para>
/// <para>
/// The rects are the canvas's own, measured from the live tiles — that is what
/// <c>CanvasHitTest.Rects</c> produces — so the unit under test is screen pixels on the drag surface
/// rather than workspace units. Nothing here scales, because nothing here should: the surface already has
/// the zoom applied, and a second scale is how a band ends up selecting the blocks two columns over.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RubberBandTests
{
    [Test]
    public void A_band_selects_the_blocks_it_covers_and_leaves_the_rest_alone()
    {
        var rects = Rects(
            ("left", 0, 0, 100, 100),
            ("middle", 200, 0, 100, 100),
            ("right", 400, 0, 100, 100));

        var hits = RubberBand.Selected(RubberBand.Between(150, -10, 350, 200), rects);

        Assert.That(hits, Is.EqualTo(new[] { "middle" }),
            "and only the middle one, because the band stops short of both neighbours");
    }

    [Test]
    public void A_band_dragged_up_and_to_the_left_selects_the_same_blocks_as_one_dragged_down_and_right()
    {
        var rects = Rects(("a", 40, 40, 60, 60));

        var forwards = RubberBand.Between(0, 0, 200, 200);
        var backwards = RubberBand.Between(200, 200, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(backwards.X, Is.EqualTo(0));
            Assert.That(backwards.Width, Is.EqualTo(200),
                "normalised, because a band whose X is past its right edge intersects nothing and half of "
                + "all drags are leftwards - which reads as a selection that only works one way round");
            Assert.That(RubberBand.Selected(backwards, rects), Is.EqualTo(RubberBand.Selected(forwards, rects)));
        });
    }

    [Test]
    public void A_block_the_band_only_touches_along_one_edge_is_selected()
    {
        // Right edge of the band exactly at the block's left edge. Part 18.1 says the band selects what it
        // intersects, and the block a user is drawing along the edge of is the block they mean.
        var rects = Rects(("edge", 100, 0, 100, 100));

        Assert.That(
            RubberBand.Hits(RubberBand.Between(0, 0, 100, 200), rects),
            Is.EqualTo(new[] { "edge" }),
            "an exclusive test on every edge would make the band need a pixel more than the eye can judge, "
            + "and a selection that appears to miss the block you drew against is the worst kind of wrong");
    }

    [Test]
    public void A_band_drawn_inside_a_big_block_selects_that_block()
    {
        // Containment in both directions: a huge C-block that swallows the band, and a band that swallows a
        // small block. Both are intersections and both are what the user drew.
        var rects = Rects(("container", 0, 0, 600, 600), ("small", 20, 20, 40, 40));

        var hits = RubberBand.Hits(RubberBand.Between(25, 25, 30, 30), rects);

        Assert.That(hits, Is.EquivalentTo(new[] { "container", "small" }));
    }

    [Test]
    public void A_click_is_not_a_band()
    {
        var rects = Rects(("under", 90, 90, 100, 100));

        Assert.Multiple(() =>
        {
            Assert.That(
                RubberBand.Selected(RubberBand.Between(100, 100, 100, 100), rects),
                Is.Empty,
                "Part 18.1 gives the click on empty canvas its own meaning - it clears the selection. "
                + "Without a threshold it would also select whatever a zero-width rectangle touches, so "
                + "every click would select the block under it and then clear the selection");
            Assert.That(
                RubberBand.Selected(RubberBand.Between(100, 100, 103, 101), rects),
                Is.Empty,
                "and a shaky click is still a click");
            Assert.That(
                RubberBand.Selected(RubberBand.Between(100, 100, 120, 100), rects).Count,
                Is.EqualTo(1),
                "but once the pointer has really moved it is a drag again");
        });
    }

    [Test]
    public void The_threshold_is_the_same_figure_a_block_drag_uses()
    {
        // Two numbers would be two answers to "how far is far enough", and a canvas where a block drag
        // starts at three pixels and a band at eight feels broken in a way nobody can describe.
        Assert.That(
            RubberBand.Threshold,
            Is.EqualTo(DragControllerThreshold),
            "Part 9.5's press-versus-drag figure, shared so the two halves of one press agree");
    }

    [Test]
    public void A_rect_without_an_id_is_not_selected_because_it_could_not_be_acted_on()
    {
        var rects = Rects(("kept", 0, 0, 10, 10));
        rects[string.Empty] = new BlockRect(0, 0, 10, 10, 10);

        Assert.That(
            RubberBand.Hits(RubberBand.Between(-5, -5, 50, 50), rects),
            Is.EqualTo(new[] { "kept" }),
            "a hit with no id could not be announced, focused or selected afterwards, so counting it would "
            + "make the count disagree with what happens");
    }

    [Test]
    public void The_hits_come_back_in_the_order_the_canvas_gave_them()
    {
        var rects = Rects(("second", 0, 0, 10, 10), ("first", 100, 100, 10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(RubberBand.Hits(RubberBand.Between(-10, -10, 500, 500), rects),
                Is.EqualTo(new[] { "second", "first" }),
                "document order, so the block a band was dragged from is not treated as more selected than "
                + "the rest and the selection does not depend on dictionary iteration");
        });
    }

    [Test]
    public void No_rectangles_is_an_empty_selection_rather_than_a_crash()
    {
        Assert.That(
            RubberBand.Hits(RubberBand.Between(0, 0, 100, 100), new Dictionary<string, BlockRect>()),
            Is.Empty,
            "a canvas with nothing on it is a state the user can reach by deleting everything");
    }

    /// <summary>
    /// The canvas's own figure, spelled out here rather than imported.
    /// </summary>
    /// <remarks>
    /// <c>DragController</c> is in the App and this project may not reference it, so the number is
    /// written down rather than read. Duplicating a constant is normally the wrong answer; here it is the
    /// only one available, and the assertion above is what keeps the two from drifting apart silently —
    /// which is better than a test that could not exist.
    /// </remarks>
    private const int DragControllerThreshold = 4;

    /// <summary>Rectangles keyed by block id, as <c>CanvasHitTest.Rects</c> produces them.</summary>
    private static Dictionary<string, BlockRect> Rects(params (string Id, double X, double Y, double W, double H)[] rects)
    {
        var built = new Dictionary<string, BlockRect>(StringComparer.Ordinal);

        foreach (var (id, x, y, width, height) in rects)
        {
            built[id] = new BlockRect(x, y, width, height, y + height, id);
        }

        return built;
    }
}
