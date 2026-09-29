namespace DeckForge.Core.Visual;

/// <summary>
/// The measured size of one block, at a zoom and font scale, as the canvas draws it.
/// </summary>
/// <remarks>
/// <para>
/// Pure numbers, no WPF: the same values the canvas uses for its <c>Path</c> geometry are what the
/// layout engine and the drop resolver need, so they are computed once here. A layout that guesses and
/// a canvas that measures would eventually disagree by two pixels, and two pixels is exactly the gap a
/// magnetic snap is supposed to close.
/// </para>
/// <para>
/// Everything is deterministic from (descriptor, zoom, scale): the same inputs always produce the same
/// rectangle, which is what makes the layout cache (Part 9.9) safe and the stopwatch test in
/// Appendix H meaningful.
/// </para>
/// </remarks>
public static class BlockMetrics
{
    /// <summary>The minimum height of any tile, from Part 9.3.</summary>
    public const double MinTileHeight = 44;

    /// <summary>The tile corner radius, from Part 9.3.</summary>
    public const double CornerRadius = 10;

    /// <summary>The width of the indent rail a C-block draws down the left of its body.</summary>
    public const double BodyIndent = 16;

    /// <summary>Vertical space between two statements in a stack.</summary>
    public const double StackGap = 4;

    /// <summary>Horizontal padding inside a tile, either side of the label.</summary>
    public const double TilePaddingX = 10;

    /// <summary>Height added by a body's top bar and bottom bar beyond its statements.</summary>
    public const double ContainerChrome = MinTileHeight;

    /// <summary>
    /// Estimates a tile's rendered width.
    /// </summary>
    /// <remarks>
    /// The real canvas measures text with the actual typeface. Core cannot — and should not: a
    /// per-character estimate that errs wide is enough for column allocation, for palette miniatures
    /// and for hit testing, and it means layout never depends on which fonts happen to be installed.
    /// The App layer may substitute a measured value; the geometry contract is the height, which the
    /// snapping arithmetic depends on.
    /// </remarks>
    public static double EstimateWidth(BlockDescriptor descriptor, double zoom = 1, double fontScale = 1)
    {
        var characters = descriptor.Label.Length;
        var slotExtra = (descriptor.Slots?.Count ?? 0) * 24;
        var menuExtra = (descriptor.Menus?.Count ?? 0) * 48;
        var width = TilePaddingX * 2 + characters * 7.2 * fontScale + slotExtra + menuExtra;
        return Math.Max(96, width) * zoom;
    }

    /// <summary>
    /// A tile's rendered height: the label line, or the label line plus every body.
    /// </summary>
    public static double Height(BlockDescriptor descriptor, double zoom = 1, double fontScale = 1)
    {
        var line = Math.Max(MinTileHeight, 24 * fontScale) * zoom;
        return descriptor.Shape switch
        {
            BlockShape.C or BlockShape.CIf or BlockShape.CChain =>
                line * (1 + (descriptor.Bodies?.Count ?? 0)) + ContainerChrome * zoom * (descriptor.Bodies?.Count ?? 0),
            BlockShape.Hat => line * 1.25,
            _ => line,
        };
    }

    /// <summary>
    /// The height of a C-block's body, given the height of the statements inside it.
    /// </summary>
    public static double BodyHeight(IReadOnlyList<double> statementHeights, double zoom = 1)
    {
        if (statementHeights.Count == 0)
        {
            return MinTileHeight * zoom;
        }

        var total = statementHeights.Sum() + StackGap * zoom * (statementHeights.Count - 1);
        return total + StackGap * zoom;
    }

    /// <summary>
    /// The y-offset, within a stack, of each statement's top edge, and the stack's total height.
    /// </summary>
    /// <remarks>
    /// One pass, no allocation per statement beyond the returned arrays. This is the number the drop
    /// resolver binary-searches for gaps, so it lives here rather than in the canvas.
    /// </remarks>
    public static (double[] Tops, double Total) StackOffsets(IReadOnlyList<double> heights, double zoom = 1)
    {
        var tops = new double[heights.Count];
        var y = 0.0;
        for (var i = 0; i < heights.Count; i++)
        {
            tops[i] = y;
            y += heights[i] + StackGap * zoom;
        }

        return (tops, y);
    }
}
