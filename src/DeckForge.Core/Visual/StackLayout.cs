namespace DeckForge.Core.Visual;

/// <summary>Where one block sits, in canvas coordinates at the given zoom.</summary>
/// <param name="X">Left edge, relative to the script's origin.</param>
/// <param name="Y">Top edge, relative to the script's origin.</param>
/// <param name="Width">Rendered width.</param>
/// <param name="Height">Rendered height, bodies included.</param>
/// <param name="NotchY">
/// The y of the block's notch — where the next block's tab wants to sit. For a C-block this is the
/// bottom edge of the whole block, not the label line, because that is where a drop lands.
/// </param>
public readonly record struct BlockRect(double X, double Y, double Width, double Height, double NotchY);

/// <summary>
/// The geometry of one script: a rectangle per block, computed from the document.
/// </summary>
/// <remarks>
/// <para>
/// Layout is a pure function of (document, zoom, font scale). Nothing here touches the canvas: the
/// App's <c>ScriptView</c> positions tiles from this tree, the export path (Part 25) renders from it,
/// and the drop resolver scores gaps against it. Three consumers, one geometry — which is the whole
/// reason it lives in Core rather than in a WPF panel's measure pass.
/// </para>
/// <para>
/// Heights are cached by block id by the caller when a script is large (Appendix H: 200 blocks under
/// 8ms cold, under 0.5ms warm); this class stays allocation-honest so the cached path is a dictionary
/// lookup per block, nothing more.
/// </para>
/// </remarks>
public static class StackLayout
{
    /// <summary>
    /// Lays out one script at its document position.
    /// </summary>
    /// <param name="script">The script to lay out.</param>
    /// <param name="zoom">Canvas zoom, 1 at 100%.</param>
    /// <param name="fontScale">System font scale, 1 at the default size.</param>
    /// <param name="heightOf">
    /// An optional height override, keyed by block id, from the caller's cache. A block absent from
    /// the cache is estimated from its descriptor.
    /// </param>
    public static IReadOnlyDictionary<string, BlockRect> Layout(
        VisualScript script,
        double zoom = 1,
        double fontScale = 1,
        IReadOnlyDictionary<string, double>? heightOf = null)
    {
        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal);
        var y = 0.0;

        var hatDescriptor = BlockCatalog.Find(script.Hat.Kind);
        var hatHeight = heightOf is not null && heightOf.TryGetValue(script.Hat.Id, out var cached)
            ? cached
            : hatDescriptor is null ? BlockMetrics.MinTileHeight * zoom : BlockMetrics.Height(hatDescriptor, zoom, fontScale);
        rects[script.Hat.Id] = new BlockRect(0, 0, WidthOf(hatDescriptor, zoom, fontScale), hatHeight, hatHeight);
        y = hatHeight;

        foreach (var statement in script.Body)
        {
            y = LayoutStatement(statement, x: 0, y, zoom, fontScale, heightOf, rects);
        }

        return rects;
    }

    /// <summary>The total height of a laid-out script, for scroll bounds and the script strip.</summary>
    public static double TotalHeight(
        VisualScript script,
        double zoom = 1,
        double fontScale = 1,
        IReadOnlyDictionary<string, double>? heightOf = null)
    {
        var rects = Layout(script, zoom, fontScale, heightOf);
        var max = 0.0;
        foreach (var rect in rects.Values)
        {
            max = Math.Max(max, rect.Y + rect.Height);
        }

        return max;
    }

    /// <summary>
    /// Lays out a loose run of statements, with no hat, starting at the origin.
    /// </summary>
    /// <param name="blocks">The statements to lay out, in order.</param>
    /// <param name="zoom">Canvas zoom, 1 at 100%.</param>
    /// <param name="fontScale">System font scale, 1 at the default size.</param>
    /// <param name="heightOf">An optional measured-height override, keyed by block id.</param>
    /// <remarks>
    /// <para>
    /// For the things that are not a script: the drag ghost, and the canvas thumbnails of Part 25. Both
    /// need the pitch of a run, and both need it to include the bodies — a ghost that drew a C-block at
    /// its header height would show a run whose spacing did not match the stack it is about to join, which
    /// is precisely the impression a snapping ghost exists to remove.
    /// </para>
    /// <para>
    /// A separate entry point rather than a flag on <see cref="Layout"/>, because "the first block is a
    /// hat" is a statement about a script and not about a layout: there is no such thing as a hatless run
    /// with a script's origin, and pretending otherwise is how a caller ends up with an off-by-one.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, BlockRect> LayoutRun(
        IReadOnlyList<Block> blocks,
        double zoom = 1,
        double fontScale = 1,
        IReadOnlyDictionary<string, double>? heightOf = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal);
        var y = 0.0;

        foreach (var block in blocks)
        {
            y = LayoutStatement(block, x: 0, y, zoom, fontScale, heightOf, rects);
        }

        return rects;
    }

    /// <summary>Lays out one statement and everything inside it, returning the next free y.</summary>
    private static double LayoutStatement(
        Block block,
        double x,
        double y,
        double zoom,
        double fontScale,
        IReadOnlyDictionary<string, double>? heightOf,
        Dictionary<string, BlockRect> rects)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        var labelHeight = BlockMetrics.MinTileHeight * zoom;

        switch (descriptor?.Shape)
        {
            case BlockShape.C or BlockShape.CIf or BlockShape.CChain:
            {
                rects[block.Id] = new BlockRect(x, y, WidthOf(descriptor, zoom, fontScale), labelHeight, y + labelHeight);
                var innerY = y + labelHeight;
                var indent = x + BlockMetrics.BodyIndent * zoom;
                var bodyBottom = innerY;

                var bodies = descriptor.Bodies ?? [];
                foreach (var bodyDescriptor in bodies)
                {
                    if (!block.Bodies.TryGetValue(bodyDescriptor.Name, out var body))
                    {
                        // An absent body is an empty one: it still takes a mouth's height, because the
                        // canvas draws an openable gap there.
                        bodyBottom += BlockMetrics.MinTileHeight * zoom;
                        continue;
                    }

                    var statementY = bodyBottom;
                    foreach (var child in body)
                    {
                        statementY = LayoutStatement(child, indent, statementY, zoom, fontScale, heightOf, rects);
                    }

                    bodyBottom = Math.Max(bodyBottom + BlockMetrics.MinTileHeight * zoom, statementY);
                }

                rects[block.Id] = rects[block.Id] with { Height = bodyBottom - y, NotchY = bodyBottom };
                return bodyBottom;
            }

            default:
            {
                var height = heightOf is not null && heightOf.TryGetValue(block.Id, out var cached)
                    ? cached
                    : descriptor is null ? labelHeight : BlockMetrics.Height(descriptor, zoom, fontScale);
                rects[block.Id] = new BlockRect(x, y, WidthOf(descriptor, zoom, fontScale), height, y + height);
                return y + height + BlockMetrics.StackGap * zoom;
            }
        }
    }

    private static double WidthOf(BlockDescriptor? descriptor, double zoom, double fontScale) =>
        descriptor is null ? 120 * zoom : BlockMetrics.EstimateWidth(descriptor, zoom, fontScale);
}
