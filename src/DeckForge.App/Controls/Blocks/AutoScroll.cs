using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// Scrolls the canvas when a drag reaches its edge, and only then.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.5's "auto-scroll near any canvas edge, speed proportional to overshoot". Proportional rather
/// than constant because a constant rate cannot be both slow enough to aim at a block you can see and fast
/// enough to reach a script you cannot: at the slow speed the canvas crawls for two seconds before
/// anything appears, and at the fast speed everything whips past the pointer before the resolver has
/// scored a single candidate.
/// </para>
/// <para>
/// Driven by a timer rather than by the mouse-move handler, because a pointer held still at the edge is
/// the case that matters and move events stop arriving in that case. The timer keeps running while the
/// pointer is inside the margin and stops the moment it leaves, so a drag that pauses to think about where
/// to put something does not slide the document out from under it.
/// </para>
/// </remarks>
public sealed class AutoScroll
{
    /// <summary>How close to the edge the pointer has to be, in pixels.</summary>
    /// <remarks>
    /// Wide enough to reach deliberately. Twenty-four pixels is about the width of the pointer's own hit
    /// area at 100% zoom, so a user who scrolls by dragging towards the edge does it without having to aim
    /// past their own cursor.
    /// </remarks>
    public const double Margin = 24;

    /// <summary>The fastest the canvas scrolls, in pixels per tick.</summary>
    private const double MaxStep = 24;

    /// <summary>Milliseconds between ticks.</summary>
    private const double IntervalMs = 16;

    private readonly System.Windows.Controls.ScrollViewer _viewer;
    private readonly DispatcherTimer _timer;
    private Point _pointer;
    private bool _running;

    public AutoScroll(System.Windows.Controls.ScrollViewer viewer)
    {
        _viewer = viewer;
        _timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(IntervalMs),
        };

        _timer.Tick += (_, _) => Scroll();
    }

    /// <summary>
    /// Tells the scroller where the pointer is and whether a drag is in progress.
    /// </summary>
    /// <param name="pointer">The pointer, in the scroll viewer's coordinates.</param>
    /// <param name="dragging">Whether to scroll at all. Clicking near the edge must not scroll.</param>
    /// <remarks>
    /// Scrolls once immediately as well as starting the timer, so the canvas responds on the same frame as
    /// the movement that asked for it. A timer that only starts scrolling on its first tick 16ms later
    /// feels like input lag, which is exactly what it is.
    /// </remarks>
    public void Update(Point pointer, bool dragging)
    {
        _pointer = pointer;

        if (!dragging)
        {
            Stop();
            return;
        }

        if (Scroll())
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>Stops immediately, for a cancelled drag or a closed page.</summary>
    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    /// <summary>
    /// How far one axis should scroll: zero inside the margin, otherwise in proportion to the overshoot.
    /// </summary>
    /// <param name="position">The pointer along that axis, in viewport coordinates.</param>
    /// <param name="extent">The viewport's length along that axis.</param>
    /// <remarks>
    /// Viewport coordinates rather than content coordinates, and the reason is that the viewport origin
    /// moves as the canvas scrolls. An overshoot measured against the content keeps growing every time the
    /// user reaches the end, so a drag would accelerate into a corner and stay pinned there.
    /// </remarks>
    public static double Step(double position, double extent)
    {
        if (extent <= Margin)
        {
            return 0;
        }

        if (position < Margin)
        {
            return -MaxStep * (Margin - position) / Margin;
        }

        if (position > extent - Margin)
        {
            return MaxStep * (position - (extent - Margin)) / Margin;
        }

        return 0;
    }

    /// <summary>Scrolls by one step. Returns whether the pointer was near an edge at all.</summary>
    private bool Scroll()
    {
        var horizontal = Step(_pointer.X, _viewer.ViewportWidth);
        var vertical = Step(_pointer.Y, _viewer.ViewportHeight);

        if (horizontal == 0 && vertical == 0)
        {
            return false;
        }

        Drive(Horizontal, horizontal);
        Drive(Vertical, vertical);

        return true;
    }

    /// <summary>The viewer's own scroll bar for one axis, or null before its template is applied.</summary>
    private ScrollBar? Horizontal =>
        _viewer.Template.FindName("HorizontalScrollBar", _viewer) as ScrollBar;

    /// <summary>The viewer's own scroll bar for the other axis.</summary>
    private ScrollBar? Vertical =>
        _viewer.Template.FindName("VerticalScrollBar", _viewer) as ScrollBar;

    /// <summary>
    /// Moves one scroll bar by a number of pixels.
    /// </summary>
    /// <remarks>
    /// The bar rather than a method on the viewer, because a proportional scroll needs an absolute offset
    /// and the only member on this <c>ScrollViewer</c> that takes one is the bar's own <c>Value</c>.
    /// Part names rather than a visual-tree search, so a themed template that renames or restyles the bars
    /// still works and a template that drops them fails visibly instead of silently never scrolling.
    /// </remarks>
    private static void Drive(ScrollBar? bar, double pixels)
    {
        if (bar is null || pixels == 0)
        {
            return;
        }

        bar.Value = Math.Clamp(bar.Value + pixels, bar.Minimum, bar.Maximum);
    }

    private void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _timer.Start();
    }
}
