namespace DeckForge.Core.Visual;

/// <summary>
/// A drag's rectangle on the canvas, with its corners already the right way round.
/// </summary>
/// <remarks>
/// <para>
/// A type rather than four doubles passed about, because the normalisation has to happen exactly once and
/// in one place. A band whose width is negative — a drag leftwards, which is half of all of them — has an
/// <c>X</c> greater than its right edge, and every intersection test written straight against the raw
/// corners returns false for it. The result is a rubber band that selects nothing when you drag left and
/// everything when you drag right, which reads as "the selection only works one way round".
/// </para>
/// <para>
/// Deliberately not <c>System.Windows.Rect</c>: Core does not reference WPF and never will, because
/// <c>tests/DeckForge.Tests</c> may not reference the App either, so a geometry type that needed a window
/// would put this test out of reach.
/// </para>
/// </remarks>
/// <param name="X">The smaller of the two x values.</param>
/// <param name="Y">The smaller of the two y values.</param>
/// <param name="Width">How wide the drag was, never negative.</param>
/// <param name="Height">How tall the drag was, never negative.</param>
public readonly record struct BlockBand(double X, double Y, double Width, double Height)
{
    /// <summary>The right edge, inclusive.</summary>
    public double Right => X + Width;

    /// <summary>The bottom edge, inclusive.</summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// Whether the drag was too small to have been one.
    /// </summary>
    /// <remarks>
    /// The same question <c>DragController.Threshold</c> asks about a block drag, asked here about the
    /// other half of the same press. A press and a release at the same pixel is a click, and Part 18.1
    /// gives the click on empty canvas its own meaning: it clears the selection. Without a threshold the
    /// band would also select whatever a zero-width rectangle happens to touch, so every click on the
    /// canvas would select the block under it and then clear the selection — which is what "the canvas
    /// ignores clicks on empty space" looks like from the outside.
    /// </remarks>
    public bool IsTiny => Width < RubberBand.Threshold && Height < RubberBand.Threshold;

    /// <summary>
    /// Whether the band and the block share at least one point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Edges count. Part 18.1 says the band "selects every script intersecting" it, and the band a user
    /// draws along the bottom edge of a block is aiming at that block. An exclusive test on every edge is
    /// the difference between a band that selects what it visibly touches and one that needs a pixel more
    /// than the eye can judge.
    /// </para>
    /// <para>
    /// Which also makes the containment test unnecessary, and that is not a simplification: the order
    /// matters. A block entirely inside the band intersects it; a band entirely inside a huge C-block
    /// intersects it too; a block containing the whole band intersects. All three are selected, because a
    /// user who drags a band across a loop has said something about the loop.
    /// </para>
    /// </remarks>
    /// <param name="rect">The block's rectangle, in the same coordinates.</param>
    public bool Touches(BlockRect rect) =>
        rect.X <= Right && rect.X + rect.Width >= X
        && rect.Y <= Bottom && rect.Y + rect.Height >= Y;
}

/// <summary>
/// Which blocks a rubber band touches, decided in Core.
/// </summary>
/// <remarks>
/// <para>
/// The hit test, and nothing else. Part 18.1's gesture is a pointer gesture and Part 9.5's argument for
/// putting the drop resolver in Core applies verbatim: the interesting half — "does a rectangle from here
/// to there select that block" — is a question about rectangles, and a question about rectangles that can
/// only be answered by dragging a mouse across a window is a question nobody has checked the corners of.
/// The drawing of the band, and the capture of the pointer, stay in the App.
/// </para>
/// <para>
/// The input is a dictionary of rectangles keyed by block id because that is what the canvas already has:
/// <c>CanvasHitTest.Rects</c> builds one by walking the live tiles, and having the band select from
/// <c>StackLayout</c>'s computed geometry instead would select blocks the canvas is not showing, at
/// positions the user cannot see.
/// </para>
/// </remarks>
public static class RubberBand
{
    /// <summary>
    /// How far the pointer must move before a press on empty canvas becomes a band.
    /// </summary>
    /// <remarks>
    /// Four pixels, the same figure <c>DragController.Threshold</c> uses for a block drag, and for the
    /// same reason: a slightly shaky click is not a drag. Two separate numbers would be two separate
    /// answers to "how far is far enough", and a canvas where the block drag starts at three pixels and
    /// the band at eight is a canvas that feels broken in a way nobody can describe.
    /// </remarks>
    public const double Threshold = 4;

    /// <summary>
    /// The band between two points, whatever order they were dragged in.
    /// </summary>
    /// <param name="pressX">Where the press began.</param>
    /// <param name="pressY">Where the press began.</param>
    /// <param name="nowX">Where the pointer is now.</param>
    /// <param name="nowY">Where the pointer is now.</param>
    public static BlockBand Between(double pressX, double pressY, double nowX, double nowY) =>
        new(
            Math.Min(pressX, nowX),
            Math.Min(pressY, nowY),
            Math.Abs(nowX - pressX),
            Math.Abs(nowY - pressY));

    /// <summary>
    /// The ids of the blocks the band touches, in the order the canvas gave them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Document order rather than distance from the press, so that the block the user drew the band *from*
    /// — if they started on one, which a band begun exactly on a tile's edge can be — is not treated as
    /// more selected than the rest. Selection order is not something the user can see, and anything
    /// reproducible is better than anything that depends on dictionary iteration.
    /// </para>
    /// <para>
    /// A null or empty id is skipped rather than selected: a rectangle whose caller did not know the
    /// block's id cannot be announced, cannot be focused, and cannot be acted on, so counting it would
    /// make the count disagree with what happens.
    /// </para>
    /// </remarks>
    /// <param name="band">The rectangle, already normalised.</param>
    /// <param name="rects">Every block's rectangle, keyed by id.</param>
    public static IReadOnlyList<string> Hits(
        BlockBand band,
        IReadOnlyDictionary<string, BlockRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);

        var hits = new List<string>();

        foreach (var (id, rect) in rects)
        {
            if (id.Length > 0 && band.Touches(rect))
            {
                hits.Add(id);
            }
        }

        return hits;
    }

    /// <summary>
    /// The ids a band would select, or nothing at all when it was too small to be one.
    /// </summary>
    /// <remarks>
    /// The threshold applied here rather than in the caller, because every caller would otherwise have to
    /// remember it and one of them would not. A click is not a band, and the two callers — a release and
    /// a live preview — have to agree about that or the selection flickers as the pointer crosses the
    /// threshold.
    /// </remarks>
    /// <param name="band">The rectangle, already normalised.</param>
    /// <param name="rects">Every block's rectangle, keyed by id.</param>
    public static IReadOnlyList<string> Selected(BlockBand band, IReadOnlyDictionary<string, BlockRect> rects) =>
        band.IsTiny ? [] : Hits(band, rects);
}
