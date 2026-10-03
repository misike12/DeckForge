using System.Globalization;
using System.Text;

namespace DeckForge.Core.Visual;

/// <summary>
/// Renders a document as SVG, from the layout model.
/// </summary>
/// <remarks>
/// <para>
/// Part 25.4 asks for "a vector render produced from the layout model", and that phrase is the whole
/// design: the export draws the same <see cref="BlockRect"/>s the canvas draws, so an exported script and
/// the canvas cannot disagree about what the script looks like. A renderer that laid the blocks out itself
/// would be a second geometry, and the first change to either would show up as an export that does not match
/// the screen.
/// </para>
/// <para>
/// Deliberately not a PNG writer. A PNG needs a rasteriser, and the honest options are a WPF render or a
/// third-party encoder — neither of which belongs in Core, and both of which would make this untestable.
/// The SVG is the vector export; the PNG is the same geometry rasterised by the caller, which already has a
/// drawing surface.
/// </para>
/// <para>
/// What goes in is labels and literals only. §27.2's rule is that an export must not carry data the user
/// did not mean to share, and a host surface name is exactly that.
/// </para>
/// </remarks>
public static class SvgRenderer
{
    /// <summary>The margin left around the drawing, in workspace units.</summary>
    private const double Margin = 16;

    /// <summary>
    /// Renders one script as an SVG document.
    /// </summary>
    /// <param name="project">The document, for the targets its scripts belong to.</param>
    /// <param name="script">The script to draw.</param>
    /// <param name="background">The colour behind the blocks, or null for none.</param>
    public static string Render(VisualProject project, VisualScript script, string? background = null)
    {
        var rects = StackLayout.LayoutRun(script.Body);
        var (width, height) = Sized(rects);

        var svg = new StringBuilder();
        OpenDocument(svg, width, height, background);

        foreach (var group in rects.GroupBy(pair => BlockOf(script, pair.Key)))
        {
            svg.AppendLine($"  <g fill=\"{FillOf(group.Key)}\" stroke=\"{StrokeOf()}\" stroke-width=\"1\">");

            foreach (var (_, rect) in group)
            {
                svg.AppendLine($"    <rect {Box(rect, Margin, Margin)} rx=\"6\" />");
            }

            svg.AppendLine("  </g>");
        }

        // The same margin the rects were drawn with, not zero: at zero every label sat eight pixels to the
        // left of its own block, in the gutter, overlapping whatever was above it. The document path passes
        // `column.OffsetX + Margin`, which is why only this entry point was wrong - and only the page's
        // single-script preview ever used it, which is why nothing showed it.
        WriteLabels(svg, script, rects, Margin, Margin);

        CloseDocument(svg);

        return svg.ToString();
    }

    /// <summary>
    /// Renders a whole document's scripts, one column each, as one SVG.
    /// </summary>
    /// <remarks>
    /// Side by side in document order, so a document's shape is visible. Ten scripts all at x=0 stacked on
    /// top of each other is not a picture of anything.
    /// </remarks>
    /// <param name="project">The document.</param>
    /// <param name="background">The colour behind the blocks, or null for none.</param>
    public static string RenderDocument(VisualProject project, string? background = null)
    {
        var columns = new List<(VisualScript Script, IReadOnlyDictionary<string, BlockRect> Rects, double OffsetX)>();

        var offsetX = 0d;
        foreach (var script in project.Targets.SelectMany(target => target.Scripts))
        {
            var rects = StackLayout.LayoutRun(script.Body);
            var (width, _) = Sized(rects);

            columns.Add((script, rects, offsetX));
            offsetX += width;
        }

        var totalWidth = columns.Count == 0 ? Margin * 2 : offsetX + (Margin * 2);
        var tallest = columns.Count == 0
            ? 0
            : columns.Max(column => CanvasView.ExtentOf(column.Rects.Values).Height);

        var svg = new StringBuilder();
        OpenDocument(svg, totalWidth, tallest + (Margin * 2) + 24, background);
        svg.AppendLine("  <g font-family=\"Segoe UI, sans-serif\" font-size=\"12\">");

        foreach (var column in columns)
        {
            svg.AppendLine(
                $"    <text x=\"{Round(column.OffsetX + Margin)}\" y=\"{Round(Margin + 14)}\" "
                + $"font-size=\"13\" fill=\"#FFFFFF\">{Escape(column.Script.Name)}</text>");

            foreach (var (id, rect) in column.Rects)
            {
                var block = BlockOf(column.Script, id);

                svg.AppendLine(
                    $"    <rect {Box(rect, column.OffsetX + Margin, 24)} rx=\"6\" fill=\"{FillOf(block)}\" "
                    + $"stroke=\"{StrokeOf()}\" stroke-width=\"1\" />");
            }

            WriteLabels(svg, column.Script, column.Rects, column.OffsetX + Margin, 24, "    ");
        }

        svg.AppendLine("  </g>");
        CloseDocument(svg);

        return svg.ToString();
    }

    /// <summary>The document's size, plus a margin all round.</summary>
    private static (double Width, double Height) Sized(IReadOnlyDictionary<string, BlockRect> rects)
    {
        var (width, height) = CanvasView.ExtentOf(rects.Values);

        return (width + (Margin * 2), height + (Margin * 2));
    }

    /// <summary>Opens the document element and, if asked, the background behind it.</summary>
    private static void OpenDocument(StringBuilder svg, double width, double height, string? background)
    {
        svg.AppendLine(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Round(width)}\" height=\"{Round(height)}\" "
            + $"viewBox=\"0 0 {Round(width)} {Round(height)}\">");

        if (background is not null)
        {
            svg.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{background}\" />");
        }
    }

    private static void CloseDocument(StringBuilder svg) => svg.AppendLine("</svg>");

    /// <summary>
    /// A rectangle's attributes, offset into place.
    /// </summary>
    /// <param name="rect">The rect, in workspace units.</param>
    /// <param name="offsetX">Where to put it, in SVG pixels.</param>
    /// <param name="offsetY">Where to put it, in SVG pixels.</param>
    private static string Box(BlockRect rect, double offsetX, double offsetY) =>
        $"x=\"{Round(rect.X + offsetX)}\" y=\"{Round(rect.Y + offsetY)}\" "
        + $"width=\"{Round(rect.Width)}\" height=\"{Round(rect.Height)}\"";

    /// <summary>
    /// The labels, drawn in one pass on top of every fill.
    /// </summary>
    /// <remarks>
    /// Together and last on purpose. A label drawn with its own block and then covered by the next block's
    /// fill is the classic layering bug, and drawing them separately makes it impossible rather than
    /// unlikely.
    /// </remarks>
    private static void WriteLabels(
        StringBuilder svg,
        VisualScript script,
        IReadOnlyDictionary<string, BlockRect> rects,
        double offsetX,
        double offsetY,
        string indent = "  ")
    {
        svg.AppendLine($"{indent}<g font-family=\"Segoe UI, sans-serif\" font-size=\"12\" fill=\"#FFFFFF\">");

        foreach (var (id, rect) in rects)
        {
            var label = Escape(Label(BlockOf(script, id)));
            if (label.Length == 0)
            {
                continue;
            }

            svg.AppendLine(
                $"{indent}  <text x=\"{Round(rect.X + offsetX + 8)}\" "
                + $"y=\"{Round(rect.Y + offsetY + 18)}\">{label}</text>");
        }

        svg.AppendLine($"{indent}</g>");
    }

    /// <summary>The block a layout id names, or null when the document no longer has it.</summary>
    /// <remarks>
    /// The layout model keys its rects by block id, so the id is the way back to the block. Putting the id on
    /// the rect instead — the obvious thing, and what this did first — asks the layout to populate a field
    /// it has no reason to, and then every caller has to agree about it.
    /// </remarks>
    private static Block? BlockOf(VisualScript script, string id) =>
        script.Blocks().FirstOrDefault(block => string.Equals(block.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// A block's label with its own words, its dropdowns and the user's literals in their holes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user's literals have to be in here. §27.2 says an export carries "block labels and the user's
    /// literals", and an image of a script that says <c>log {template}</c> instead of <c>log Total 6</c> is a
    /// picture of a template rather than of a script.
    /// </para>
    /// <para>
    /// A nested reporter is shown as its own label rather than its value, because rendering the value would
    /// mean running the interpreter — and an export must not depend on whether a run would succeed.
    /// </para>
    /// </remarks>
    private static string Label(Block? block)
    {
        if (block is null || BlockCatalog.Find(block.Kind) is not { } descriptor)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            BlockLabel.Plan(descriptor).Select(run => run.Kind switch
            {
                BlockLabelRunKind.Text => run.Text,
                BlockLabelRunKind.Menu => block.Fields.TryGetValue(run.Menu!.Name, out var chosen) ? chosen : run.Menu.Default,
                _ when block.Inputs.TryGetValue(run.Slot!.Name, out var input) => Literal(block, input),
                _ => BlockLabel.Hole(run.Slot),
            }));
    }

    /// <summary>
    /// What one filled hole says, or what an empty one says.
    /// </summary>
    /// <remarks>
    /// A variable slot is shown as its *name* and not as its value, for the same reason the interpreter's
    /// table writes it down: a value is a run's business, and an export happens whether or not one has
    /// happened. A name is in the document and is what the reader recognises.
    /// </remarks>
    private static string Literal(Block block, BlockInput input)
    {
        if (input.Block is { } nested)
        {
            return BlockCatalog.Find(nested.Kind) is { } descriptor
                ? BlockLabel.PreviewText(descriptor) + "…"
                : nested.Kind;
        }

        if (input.Variable is { } variable)
        {
            return variable;
        }

        if (input.Number is { } number)
        {
            return number.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (input.Boolean is { } boolean)
        {
            return boolean ? "true" : "false";
        }

        return string.IsNullOrEmpty(input.Text) ? string.Empty : input.Text;
    }

    /// <summary>
    /// A category's fill, as an SVG colour.
    /// </summary>
    /// <remarks>
    /// Read from the catalogue's own hue and not from a theme resource: an export has to be identical on a
    /// machine that has never opened the editor, and a resource lookup only resolves inside a running
    /// application. The App derives its theme from the same catalogue row, so the two agree without either
    /// depending on the other.
    /// </remarks>
    private static string FillOf(Block? block)
    {
        var category = block is null || BlockCatalog.Find(block.Kind) is not { } descriptor
            ? BlockCategory.Control
            : descriptor.Category;

        return BlockCatalog.Category(category).Hue;
    }

    /// <summary>A block's outline, dark enough to read against a category fill.</summary>
    private static string StrokeOf() => "#00000040";

    /// <summary>
    /// Escapes text for XML.
    /// </summary>
    /// <remarks>
    /// A block label can hold anything the user typed, including a literal called <c>a &amp; b</c> or a
    /// parameter called <c>&lt;x&gt;</c>. Without this the export is not a document and not a picture, and
    /// the failure lands in whatever opens it.
    /// </remarks>
    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>A number with the invariant separator, because an SVG is not a locale.</summary>
    private static string Round(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}