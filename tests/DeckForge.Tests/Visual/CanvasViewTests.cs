using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Zoom, pan and the minimap: three consumers that must agree about where a block is.
/// </summary>
/// <remarks>
/// The canvas draws from a view, the drop resolver scores gaps against it, and the minimap thumbnails it.
/// Written three times — which is how it was written once already, in a control nobody could test — these
/// are the same arithmetic three times, and the tests are mostly about the two directions agreeing: a view
/// that projects a point and then cannot invert it is a canvas whose hit test is quietly wrong at every
/// zoom except one.
/// </remarks>
[TestFixture]
public sealed class CanvasViewTests
{
    [Test]
    public void The_zoom_is_clamped_because_a_pinch_sends_deltas_and_a_control_cannot_obey_them_all()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new CanvasView { Zoom = 0.01 }.ClampedZoom, Is.EqualTo(CanvasView.MinZoom),
                "below a quarter the labels stop being readable and the canvas becomes coloured rectangles");
            Assert.That(new CanvasView { Zoom = 40 }.ClampedZoom, Is.EqualTo(CanvasView.MaxZoom),
                "and past about double the text is larger than the notch it sits in");
            Assert.That(new CanvasView().ClampedZoom, Is.EqualTo(1));
        });
    }

    [Test]
    public void A_drag_moves_the_document_by_the_number_of_pixels_the_pointer_moved()
    {
        // The whole point of taking the delta in pixels: at 200% a workspace-unit delta moves the document
        // twice as far as the pointer, which is the classic zoom-pan bug.
        foreach (var zoom in new[] { 0.25, 0.5, 1, 1.75, 2.5 })
        {
            var view = new CanvasView { Zoom = zoom }.PannedBy(40, 25);

            var moved = view.ToWorkspace(40, 25);

            Assert.Multiple(() =>
            {
                Assert.That((moved.X - view.PanX) * view.ClampedZoom, Is.EqualTo(40).Within(0.001),
                    $"x tracks the pointer at {zoom:P0}");
                Assert.That((moved.Y - view.PanY) * view.ClampedZoom, Is.EqualTo(25).Within(0.001),
                    $"y tracks the pointer at {zoom:P0}");
            });
        }
    }

    [Test]
    public void A_projected_point_and_its_inverse_agree_at_every_zoom()
    {
        var rect = new BlockRect(X: 120, Y: 64, Width: 200, Height: 40, NotchY: 40, Id: "b-1");

        foreach (var zoom in new[] { CanvasView.MinZoom, 0.5, 1, 1.5, CanvasView.MaxZoom })
        {
            var view = new CanvasView { Zoom = zoom, PanX = -37, PanY = 12 };
            var (x, y, _, _) = rect.Project(view);

            var back = view.ToWorkspace(x, y);

            Assert.Multiple(() =>
            {
                Assert.That(back.X, Is.EqualTo(rect.X).Within(0.001),
                    $"x survives the round trip at {zoom:P0}");
                Assert.That(back.Y, Is.EqualTo(rect.Y).Within(0.001),
                    $"y survives the round trip at {zoom:P0}");
            });
        }
    }

    [Test]
    public void Centring_puts_the_point_in_the_middle_of_the_viewport()
    {
        var view = new CanvasView { Zoom = 2 }.CentredOn(400, 300, 800, 600);

        var centre = view.ToWorkspace(400, 300);

        Assert.Multiple(() =>
        {
            Assert.That(centre.X, Is.EqualTo(400).Within(0.001));
            Assert.That(centre.Y, Is.EqualTo(300).Within(0.001));
        });
    }

    [Test]
    public void Fit_to_never_enlarges_a_small_document()
    {
        // "Show me everything" answered by doubling a three-block script is the wrong answer, and it is what
        // a naive scale-to-fit does.
        var view = CanvasView.FitTo((Width: 100, Height: 100), 1200, 800);

        Assert.That(view.ClampedZoom, Is.EqualTo(1),
            "a document smaller than the window is shown at 1:1, not blown up to fill it");
    }

    [Test]
    public void Fit_to_shrinks_a_document_that_does_not_fit_and_keeps_both_edges_inside()
    {
        var view = CanvasView.FitTo((Width: 2000, Height: 1500), 800, 600, margin: 24);

        Assert.Multiple(() =>
        {
            Assert.That(view.ClampedZoom, Is.LessThan(1));
            Assert.That(2000 * view.ClampedZoom, Is.LessThanOrEqualTo(800), "the wide edge fits");
            Assert.That(
                1500 * view.ClampedZoom,
                Is.LessThanOrEqualTo(600),
                "and the tall one does too - a fit-to-width that crops vertically is not a fit");
        });
    }

    [Test]
    public void Fit_to_a_document_too_big_for_the_smallest_zoom_returns_that_zoom_rather_than_an_unusable_one()
    {
        // A 4000-wide document cannot fit an 800-pixel viewport at a quarter scale, and there is no honest
        // answer except to stop at the floor. Returning the *computed* 0.18 would hand every caller a zoom
        // it cannot use, and the one place that matters - the minimap - would then draw with a scale the
        // canvas is not using.
        var view = CanvasView.FitTo((Width: 4000, Height: 3000), 800, 600, margin: 24);

        Assert.That(view.ClampedZoom, Is.EqualTo(CanvasView.MinZoom));
    }

    [Test]
    public void Fit_to_an_empty_document_is_the_default_view_rather_than_a_division_by_zero()
    {
        foreach (var extent in new[] { (0d, 0d), (-5d, 10d) })
        {
            var view = CanvasView.FitTo(extent, 800, 600);

            Assert.That(view.ClampedZoom, Is.EqualTo(1), $"extent {extent} leaves the view alone");
        }
    }

    [Test]
    public void The_extent_of_nothing_is_nothing()
    {
        var (width, height) = CanvasView.ExtentOf([]);

        Assert.Multiple(() =>
        {
            Assert.That(width, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(0));
        });
    }

    [Test]
    public void The_extent_reaches_further_right_and_lower_than_any_single_block()
    {
        var rects = new[]
        {
            new BlockRect(0, 0, 100, 40, 40),
            new BlockRect(20, 500, 300, 40, 540),
        };

        var (width, height) = CanvasView.ExtentOf(rects);

        Assert.Multiple(() =>
        {
            Assert.That(width, Is.EqualTo(320), "the widest block's right edge, not its width");
            Assert.That(height, Is.EqualTo(540), "and the lowest block's bottom edge");
        });
    }

    // ---- the minimap ----------------------------------------------------------------------------------

    private static readonly BlockRect[] Script =
    [
        new(0, 0, 220, 44, 44, "b-1"),
        new(0, 44, 260, 44, 88, "b-2"),
        new(0, 88, 300, 44, 132, "b-3"),
    ];

    [Test]
    public void Every_block_gets_a_rectangle_inside_the_minimap()
    {
        var minimap = Minimap.For(Script, new CanvasView(), 800, 600, 240, 160);

        foreach (var block in Script)
        {
            var (x, y, width, height) = minimap.Place(block);

            Assert.Multiple(() =>
            {
                Assert.That(x, Is.GreaterThanOrEqualTo(-1), $"{block.Id} starts inside the box");
                Assert.That(width, Is.GreaterThan(0), $"{block.Id} is at least a pixel wide");
                Assert.That(height, Is.GreaterThan(0), $"{block.Id} is at least a pixel tall");
            });
        }
    }

    [Test]
    public void The_viewport_rectangle_stays_inside_the_minimap_even_when_the_view_is_zoomed_further_out()
    {
        // Zoomed out past the whole document, the visible region is larger than the document — and a rect
        // that starts off the map is a rect the user cannot click.
        var minimap = Minimap.For(Script, new CanvasView { Zoom = 0.25 }, 4000, 4000, 240, 160);

        Assert.Multiple(() =>
        {
            Assert.That(minimap.Viewport.X, Is.GreaterThanOrEqualTo(-0.001));
            Assert.That(minimap.Viewport.Y, Is.GreaterThanOrEqualTo(-0.001));
            Assert.That(
                minimap.Viewport.X + minimap.Viewport.Width,
                Is.LessThanOrEqualTo(minimap.BoxWidth + 0.001),
                "and it does not run off the right edge");
        });
    }

    [Test]
    public void Clicking_the_minimap_goes_where_the_user_clicked()
    {
        var minimap = Minimap.For(Script, new CanvasView(), 800, 600, 240, 160);

        // The block's own place, clicked.
        var (x, y, _, _) = minimap.Place(Script[2]);
        var view = minimap.ViewAt(x, y, 800, 600);

        var centre = view.ToWorkspace(400, 300);

        Assert.Multiple(() =>
        {
            Assert.That(centre.X, Is.EqualTo(Script[2].X).Within(1.5),
                "the middle of the viewport lands on the block that was clicked");
            Assert.That(centre.Y, Is.EqualTo(Script[2].Y).Within(1.5));
        });
    }

    [Test]
    public void Clicking_outside_the_document_lands_on_the_document_rather_than_off_it()
    {
        var minimap = Minimap.For(Script, new CanvasView(), 800, 600, 240, 160);

        var view = minimap.ViewAt(-500, -500, 800, 600);
        var centre = view.ToWorkspace(400, 300);

        Assert.Multiple(() =>
        {
            Assert.That(centre.X, Is.GreaterThanOrEqualTo(-0.001), "clamped to the left edge");
            Assert.That(centre.Y, Is.GreaterThanOrEqualTo(-0.001), "and the top");
        });
    }

    [Test]
    public void A_minimap_of_an_empty_document_does_not_throw()
    {
        Minimap minimap = Minimap.For([], new CanvasView(), 800, 600, 240, 160);

        Assert.Multiple(() =>
        {
            Assert.That(minimap.Place(new BlockRect(0, 0, 10, 10, 10)).Width, Is.GreaterThan(0));
            Assert.That(minimap.ViewAt(120, 80, 800, 600).ClampedZoom, Is.GreaterThan(0));
        });
    }
}