using System.Globalization;

namespace DeckForge.Core.Visual;

/// <summary>
/// The parts of an export that are the same whichever format it is written in.
/// </summary>
/// <remarks>
/// <para>
/// Two exports walk the same layout model: <see cref="SvgRenderer"/> for Part 25.4 and
/// <see cref="PngExportPlan"/> for Part 25.3. Both have to say the same words about a block, in the same
/// colours — and while each of them had its own copy of that, the copies were free to drift. A PNG that
/// says <c>wait {seconds}</c> where the SVG beside it says <c>wait 3</c> is not a rendering difference
/// nobody would notice; it is an export that can no longer be trusted to mean what the canvas means, and
/// the only way to tell which of the two is wrong is to open both.
/// </para>
/// <para>
/// So the shared half lives here, in Core, where a test can read it: what a block is called, what its label
/// says with the user's literals in its holes, which category's colours it wears, and how text is escaped
/// on the way out. What is deliberately *not* here is anything about pixels or markup: a PNG's encoder is
/// the caller's business, and the XML escaping belongs to the one format that has XML.
/// </para>
/// </remarks>
public static class BlockExport
{
    /// <summary>
    /// A script's blocks by id, so a layout rectangle can be traced back to the block it came from.
    /// </summary>
    /// <param name="script">The script, hat included.</param>
    /// <remarks>
    /// <para>
    /// The layout model keys its rectangles by block id, so the id is the only way back from a rectangle to
    /// a block. Both exports looked the block up per rectangle with a linear search — which is a pass over
    /// the whole script for every block, so a two-hundred-block script paid for forty thousand comparisons
    /// per export, and the PNG renderer was about to copy that.
    /// </para>
    /// <para>
    /// First one wins on a duplicated id, which is what the per-block search did. It has to keep doing so:
    /// a document can arrive from a hand-edited file or an older paste with two blocks sharing one id, and
    /// an export that throws on it would be refusing to draw a document the canvas draws without complaint.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, Block> Index(VisualScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return IndexOf(script.Blocks());
    }

    /// <summary>
    /// Any set of blocks by id, including the ones nested inside a body or a slot.
    /// </summary>
    /// <param name="blocks">The blocks to index.</param>
    /// <remarks>
    /// Over <see cref="Index"/> for a set that is not a script's — a run on its way to the clipboard, which
    /// is a fragment of a document and is laid out by id like everything else. Two entry points over one
    /// implementation rather than one method with a nullable script, because a nullable parameter here would
    /// read as "a script that may be missing" rather than as "not a script".
    /// </remarks>
    public static IReadOnlyDictionary<string, Block> IndexOf(IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var index = new Dictionary<string, Block>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            index.TryAdd(block.Id, block);
        }

        return index;
    }

    /// <summary>
    /// The category a block is coloured by.
    /// </summary>
    /// <param name="block">The block, or null when the document no longer has the one a rect names.</param>
    /// <remarks>
    /// Control for a block nobody can describe, because Control is the category the neutral grey belongs to
    /// and a neutral rectangle says "this is not a block I recognise" without saying "error". An export that
    /// refused to draw it would leave a hole where a block was, which is a worse lie than a grey one.
    /// </remarks>
    public static BlockCategory Category(Block? block) =>
        block is null || BlockCatalog.Find(block.Kind) is not { } descriptor
            ? BlockCategory.Control
            : descriptor.Category;

    /// <summary>
    /// The colours one block is drawn in, from the measured palette.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="dark">Whether the dark palette is wanted.</param>
    /// <remarks>
    /// <see cref="BlockThemePalette"/> rather than the catalogue's raw hue, because §28.2's 4.5:1 is a
    /// property of the palette and a raw hue is not one of its values. The App's theme derives the very same
    /// tokens from the very same call, so a block's colour in an export and on the canvas cannot be two
    /// decisions.
    /// </remarks>
    public static BlockThemeTokens Tokens(Block? block, bool dark) =>
        BlockThemePalette.For(Category(block), dark);

    /// <summary>
    /// A block's label with its own words, its dropdowns and the user's literals in their holes.
    /// </summary>
    /// <param name="block">The block, or null.</param>
    /// <remarks>
    /// <para>
    /// The user's literals have to be in here. §27.2 says an export carries "block labels and the user's
    /// literals", and an image of a script that says <c>log {template}</c> instead of <c>log Total 6</c> is
    /// a picture of a template rather than of a script.
    /// </para>
    /// <para>
    /// A nested reporter is shown as its own label rather than its value, because rendering the value would
    /// mean running the interpreter — and an export must not depend on whether a run would succeed. A
    /// variable slot is shown as its <em>name</em> for the same reason the interpreter's table writes it
    /// down: a value is a run's business, and an export happens whether or not one has happened.
    /// </para>
    /// </remarks>
    public static string Label(Block? block)
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
    /// Escapes text for XML.
    /// </summary>
    /// <param name="text">Whatever the user typed.</param>
    /// <remarks>
    /// A block label can hold anything the user typed, including a literal called <c>a &amp; b</c> or a
    /// parameter called <c>&lt;x&gt;</c>. Without this the export is not a document and not a picture, and
    /// the failure lands in whatever opens it. Shared with the PNG plan's canonical form, which is hashed:
    /// a label containing a newline would otherwise split one line of the fingerprint into two and make two
    /// different plans hash the same.
    /// </remarks>
    public static string EscapeXml(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// What one filled hole says, or what an empty one says.
    /// </summary>
    /// <param name="block">The block holding the hole.</param>
    /// <param name="input">What is in it.</param>
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
}