using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DeckForge.Core.Visual;

/// <summary>
/// Everything a PNG export is, as numbers: the size of the image, and the drawing instructions for it.
/// </summary>
/// <remarks>
/// <para>
/// Part 25.3 asks for a render "from the same geometry the canvas uses (<c>StackLayout</c> plus
/// <c>BlockMetrics</c>) … because it is drawn from the layout model rather than by screenshotting the
/// window, it works for a script that is scrolled off screen and it is deterministic". All three of those
/// are properties of *this type*, and all three are checkable without a window: the geometry comes from the
/// layout model, there is no view anywhere in the arithmetic, and the same document produces the same
/// <see cref="Fingerprint"/> every time.
/// </para>
/// <para>
/// What is deliberately not here is the raster. §28.5's "13 - PNG export" row refused PNG on the grounds
/// that "a third-party encoder in Core would make the export untestable", and that objection was right
/// about the encoder and wrong to throw away the export with it. This type is the half that can be tested:
/// the geometry, the scale, the padding, the caption and the colours are all decided here, in Core, where a
/// test reads them. The App's
/// <c>BlockImageRenderer</c> is left with drawing rounded rectangles and strings — work with no decision in
/// it that a test could contradict.
/// </para>
/// <para>
/// One list, in draw order, rather than a tree of blocks: a rasteriser wants a sequence, and a sequence is
/// the one shape a test can assert against. The order is fixed by the plan itself rather than left to a
/// dictionary's enumeration, which is what makes <see cref="Fingerprint"/> a fingerprint of the drawing and
/// not of a hash table.
/// </para>
/// </remarks>
public sealed record PngExportPlan
{
    /// <summary>How tall a caption's row is, in workspace units.</summary>
    /// <remarks>
    /// The vector export reserves 24 units above each column for the script's name, so a PNG of the same
    /// document puts its caption in the same place rather than one line lower.
    /// </remarks>
    public const double CaptionLineHeight = 24;

    /// <summary>How far below the top of the caption row its baseline sits, in workspace units.</summary>
    /// <remarks>
    /// The vector export's number, kept. A caption 4 units lower than the vector export's is a caption the
    /// two renders cannot be compared against each other on, which is the whole point of both walking the
    /// same model.
    /// </remarks>
    public const double CaptionBaseline = 14;

    /// <summary>How far below a block's top edge its label's baseline sits, in workspace units.</summary>
    /// <remarks>
    /// The vector export's number, for the same reason. It also happens to sit inside
    /// <see cref="BlockOutline.HeaderHeight"/>, so a container's label is in its header rather than in its
    /// body.
    /// </remarks>
    public const double LabelBaseline = 18;

    /// <summary>How far left of a block's edge its label starts, in workspace units.</summary>
    /// <remarks>
    /// The vector export's 8, rather than <see cref="BlockMetrics.TilePaddingX"/>. That constant is the
    /// padding inside a tile's own border and an export draws no border, so borrowing it would make the
    /// label sit somewhere neither render agrees on.
    /// </remarks>
    public const double LabelInset = 8;

    /// <summary>The caption's font size, in workspace units, before the scale.</summary>
    public const double CaptionFontSize = 13;

    /// <summary>A block label's font size, in workspace units, before the scale.</summary>
    /// <remarks>
    /// The same 12 the vector export writes, for the same reason: two exports of one script that set their
    /// labels at different sizes are two exports of two different scripts.
    /// </remarks>
    public const double LabelFontSize = 12;

    /// <summary>The least space two columns are kept apart, in workspace units.</summary>
    /// <remarks>
    /// 32, the vector export's margin doubled — so an export with no padding still has a gutter, and two
    /// columns never touch. Without it, a document of two scripts at zero padding drew as one grey block with
    /// two stacks in it.
    /// </remarks>
    public const double MinColumnGap = 32;

    /// <summary>The plate behind a caption in the dark palette, as <c>#RRGGBB</c>.</summary>
    /// <remarks>
    /// The same value <c>LiquidTheme</c> gives the rail brush, so a caption's plate and the editor's own
    /// chrome are the same colour. The number lives here because the App cannot be asked for it during an
    /// export, and an export that invented its own grey would be one more colour nobody measured.
    /// </remarks>
    public const string CaptionPlateDark = "#16181E";

    /// <summary>The plate behind a caption in the light palette, as <c>#RRGGBB</c>.</summary>
    public const string CaptionPlateLight = "#F4F6F9";

    /// <summary>The image's width, in device pixels.</summary>
    /// <remarks>
    /// Never below 1. A document with no scripts lays out to nothing, and "nothing" would be handed to a
    /// rasteriser as a zero-pixel surface — which is the one number in this whole export that has to be
    /// wrong on purpose to stay legal.
    /// </remarks>
    public int Width { get; init; } = 1;

    /// <summary>The image's height, in device pixels.</summary>
    /// <remarks>
    /// Never below 1, for the reason <see cref="Width"/> gives.
    /// </remarks>
    public int Height { get; init; } = 1;

    /// <summary>The scale the instructions below were multiplied by.</summary>
    /// <remarks>
    /// The clamped one, not <see cref="PngExportOptions.Scale"/> as asked for. A plan that reported the
    /// unclamped number would let a caller believe it was drawing at a scale the plan has already refused.
    /// </remarks>
    public double Scale { get; init; } = PngExportOptions.DefaultScale;

    /// <summary>Every instruction, in the order it is drawn.</summary>
    public IReadOnlyList<PngExportItem> Items { get; init; } = [];

    /// <summary>The rectangles: one per block, plus one per caption's plate.</summary>
    public IEnumerable<PngExportBox> Boxes => Items.OfType<PngExportBox>();

    /// <summary>The text: one per block that has a label, plus one per caption.</summary>
    public IEnumerable<PngExportText> Texts => Items.OfType<PngExportText>();

    /// <summary>
    /// A hash of the whole plan: its size and every instruction, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what "deterministic, so a test can compare hashes" means in practice, and it is deliberately
    /// a hash of the <em>plan</em> rather than of the pixels. Pixels cannot be compared across machines — a
    /// font rasteriser, a GPU and a rounding decision all differ — so a test over a real PNG would be a test
    /// of WPF. Over the plan, "the same document produces the same drawing" is a fact this build can assert.
    /// </para>
    /// <para>
    /// Computed on demand rather than cached, because a cached value in a record is carried along by
    /// <c>with</c> and would outlive the copy it described.
    /// </para>
    /// </remarks>
    public string Fingerprint =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Describe())));

    /// <summary>
    /// The plan as text: one line per instruction, in draw order.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than serialised, so that it is stable across a change to this project's
    /// serializer settings and readable when a hash does not match — which is the moment somebody needs to
    /// read it. Numbers are invariant, and text is XML-escaped through the same helper the vector export
    /// uses, because a label with a newline in it would otherwise become two lines and two plans could hash
    /// the same.
    /// </remarks>
    public string Describe()
    {
        var text = new StringBuilder();

        // '\n' rather than AppendLine, which ends a line with Environment.NewLine: a fingerprint that
        // changes with the operating system is not a fingerprint.
        text.Append("png ").Append(Number(Scale)).Append(' ')
            .Append(Width).Append('x').Append(Height).Append('\n');

        foreach (var item in Items)
        {
            switch (item)
            {
                case PngExportBox box:
                    text.Append(Row(
                        "box",
                        box.BlockId,
                        Number(box.X),
                        Number(box.Y),
                        Number(box.Width),
                        Number(box.Height),
                        Number(box.Radius),
                        box.FillTop,
                        box.FillBottom,
                        box.Stroke,
                        Number(box.StrokeThickness))).Append('\n');
                    break;

                case PngExportText line:
                    text.Append(Row(
                        "text",
                        line.BlockId,
                        Number(line.X),
                        Number(line.Y),
                        Number(line.FontSize),
                        line.Ink,
                        OneLine(line.Text))).Append('\n');
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// A plan for a whole document: one column per script, in document order.
    /// </summary>
    /// <param name="project">The document to draw.</param>
    /// <param name="options">The scale, padding and captions, or null for the defaults.</param>
    /// <remarks>
    /// <para>
    /// The same columns, in the same order and at the same offsets as <see cref="SvgRenderer.RenderDocument"/>
    /// — the layout model's <see cref="StackLayout.LayoutRun"/> for each script's body, laid out left to
    /// right. The two can therefore be compared position by position, which is a test and not a claim:
    /// <c>PngExportPlanTests</c> parses the vector export and checks every rectangle and every word against
    /// this plan. They differ in one place, and only one: the vector export puts its caption row where this
    /// plan puts its top margin, so at equal padding the two drawings sit one row apart vertically and
    /// nowhere else.
    /// </para>
    /// <para>
    /// Each column is captioned with the script's name in its target by default, for the reason Part 25.3
    /// gives for a caption at all: ten stacks of blocks with nothing above them are a picture of a document
    /// nobody can navigate.
    /// </para>
    /// </remarks>
    public static PngExportPlan ForDocument(VisualProject project, PngExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new PngExportOptions();

        var columns = new List<Column>();

        foreach (var target in project.Targets)
        {
            foreach (var script in target.Scripts)
            {
                columns.Add(ColumnOf(
                    options.CaptionOr(CaptionFor(target, script)),
                    StackLayout.LayoutRun(script.Body),
                    BlockExport.Index(script)));
            }
        }

        return Compose(columns, options);
    }

    /// <summary>
    /// A plan for one script, hat and all.
    /// </summary>
    /// <param name="project">The document, for the target the script belongs to.</param>
    /// <param name="script">The script to draw.</param>
    /// <param name="options">The scale, padding and caption, or null for the defaults.</param>
    /// <remarks>
    /// <para>
    /// The hat is included, which <see cref="SvgRenderer.Render"/> does not include — it lays the body out
    /// on its own, from the origin. So this column is the vector export's column with one block above it,
    /// and the spacing inside is identical: the body lands the same distance from the block before it, at
    /// the same x, because it is the same <see cref="StackLayout"/>. What differs is the origin, by the
    /// hat's height, and that is the trade. A picture of a script with its hat cut off is a picture of a
    /// stack, and a reader who has never seen the canvas would not know what starts it.
    /// </para>
    /// <para>
    /// The target is found by reference first and by id second, because a caller holding a script out of a
    /// document is holding the same object and a caller who rebuilt the document has rebuilt the ids too.
    /// </para>
    /// </remarks>
    public static PngExportPlan ForScript(
        VisualProject project,
        VisualScript script,
        PngExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(script);
        options ??= new PngExportOptions();

        var target = project.Targets.FirstOrDefault(candidate => candidate.Scripts.Any(owned =>
            ReferenceEquals(owned, script)
            || (script.Id.Length > 0 && string.Equals(owned.Id, script.Id, StringComparison.Ordinal))));

        return Compose(
            [ColumnOf(
                options.CaptionOr(CaptionFor(target, script)),
                StackLayout.Layout(script),
                BlockExport.Index(script))],
            options);
    }

    /// <summary>
    /// A plan for a loose run of blocks, with no hat and no script around it.
    /// </summary>
    /// <param name="run">The blocks, in order.</param>
    /// <param name="options">The scale, padding and caption, or null for the defaults.</param>
    /// <remarks>
    /// <para>
    /// The clipboard's picture: the run the user copied, drawn the way the canvas draws it. This is the case
    /// Part 25.2's "also a bitmap for pasting into a chat app" is about, and it is why the plan exists at
    /// all — a screenshot would have been the obvious way to get a bitmap and it would have been a picture
    /// of the window, at the window's zoom, cropped to the window.
    /// </para>
    /// <para>
    /// No caption by default, and not because the option was forgotten: a run has no name and no target, and
    /// a caption naming one would be a claim the document cannot back up. A caller who knows what the run is
    /// — the clipboard does not, because a run can come from any of a dozen targets — says so in
    /// <see cref="PngExportOptions.Caption"/>.
    /// </para>
    /// </remarks>
    public static PngExportPlan ForRun(IReadOnlyList<Block> run, PngExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        options ??= new PngExportOptions();

        // Walked, not indexed as given: the layout lays out the statements *inside* a container too, so an
        // index of the run's own blocks leaves every nested statement with a rectangle and no label — which
        // is the shape of a picture where a repeat's body has vanished into a bare grey box.
        return Compose(
            [ColumnOf(options.Caption ?? string.Empty, StackLayout.LayoutRun(run), IndexOf(run))],
            options);

        static IReadOnlyDictionary<string, Block> IndexOf(IReadOnlyList<Block> blocks) =>
            BlockExport.IndexOf(blocks.SelectMany(block => block.Walk()));
    }

    /// <summary>
    /// The caption a script gets: what it is called, in the target it belongs to.
    /// </summary>
    /// <param name="target">The target, or null when the script is not in one.</param>
    /// <param name="script">The script.</param>
    /// <remarks>
    /// Part 25.3's "a caption line naming the target", in that order — the script first, because that is
    /// what the reader is looking for, and the target second because it is the context. A script with no
    /// name, or a document with no target around it, gets whatever of the two exists: an export is not the
    /// place to invent a name for something the document never had.
    /// </remarks>
    public static string CaptionFor(VisualTarget? target, VisualScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var scriptName = string.IsNullOrWhiteSpace(script.Name) ? "script" : script.Name;
        var targetName = string.IsNullOrWhiteSpace(target?.Name) ? string.Empty : target!.Name;

        return targetName.Length == 0 ? scriptName : $"{scriptName} in {targetName}";
    }

    /// <summary>
    /// Lays the columns out side by side and turns them into instructions.
    /// </summary>
    /// <param name="columns">The columns, in document order.</param>
    /// <param name="options">What to include.</param>
    /// <remarks>
    /// The only place the size is computed, so <see cref="Width"/> and <see cref="Height"/> cannot be
    /// anything other than what the instructions below them draw.
    /// </remarks>
    private static PngExportPlan Compose(IReadOnlyList<Column> columns, PngExportOptions options)
    {
        var padding = options.ClampedPadding;
        var scale = options.ClampedScale;
        var gap = Math.Max(padding * 2, MinColumnGap);

        var items = new List<PngExportItem>();
        var offsetX = 0d;
        var contentHeight = 0d;

        foreach (var column in columns)
        {
            var originX = offsetX + padding;
            var originY = padding;

            if (column.CaptionHeight > 0)
            {
                items.Add(new PngExportBox
                {
                    X = originX * scale,
                    Y = originY * scale,
                    Width = column.Width * scale,
                    Height = column.CaptionHeight * scale,
                    Radius = BlockMetrics.CornerRadius * scale,
                    FillTop = options.Dark ? CaptionPlateDark : CaptionPlateLight,
                    FillBottom = options.Dark ? CaptionPlateDark : CaptionPlateLight,
                });

                items.Add(new PngExportText
                {
                    X = originX * scale,
                    Y = (originY + CaptionBaseline) * scale,
                    Text = column.Caption,
                    FontSize = CaptionFontSize * scale,
                    Ink = options.Dark ? BlockThemePalette.DarkInk : BlockThemePalette.LightInk,
                });
            }

            var top = originY + column.CaptionHeight;

            foreach (var painted in column.Blocks)
            {
                var tokens = BlockExport.Tokens(painted.Block, options.Dark);

                items.Add(new PngExportBox
                {
                    X = (originX + painted.Rect.X) * scale,
                    Y = (top + painted.Rect.Y) * scale,
                    Width = painted.Rect.Width * scale,
                    Height = painted.Rect.Height * scale,
                    Radius = BlockMetrics.CornerRadius * scale,
                    FillTop = tokens.FillTop,
                    FillBottom = tokens.FillBottom,
                    Stroke = tokens.Stroke,
                    StrokeThickness = StrokeWidth * scale,
                    BlockId = painted.Block?.Id,
                });

                var label = BlockExport.Label(painted.Block);
                if (label.Length > 0)
                {
                    items.Add(new PngExportText
                    {
                        X = (originX + painted.Rect.X + LabelInset) * scale,
                        Y = (top + painted.Rect.Y + LabelBaseline) * scale,
                        Text = label,
                        FontSize = LabelFontSize * scale,
                        Ink = tokens.Ink,
                        BlockId = painted.Block?.Id,
                    });
                }
            }

            offsetX += column.Width + gap;
            contentHeight = Math.Max(contentHeight, column.CaptionHeight + column.ExtentHeight);
        }

        var contentWidth = columns.Count == 0 ? 0 : offsetX - gap;

        return new PngExportPlan
        {
            Width = Pixels(contentWidth + (padding * 2), scale),
            Height = Pixels(contentHeight + (padding * 2), scale),
            Scale = scale,
            Items = items,
        };
    }

    /// <summary>
    /// A block's outline width, before the scale.
    /// </summary>
    /// <remarks>
    /// The standard contrast's width rather than the window's. <see cref="BlockTheme"/> reads the system's
    /// contrast preference because a window can ask for it; an export is a file written for somebody else's
    /// screen, and the colour it draws with is the one it was asked for in
    /// <see cref="PngExportOptions.Dark"/>.
    /// </remarks>
    private static double StrokeWidth => BlockContrast.StrokeWidthFor(ContrastLevel.Standard);

    /// <summary>One column's worth of drawing, before it knows where it sits.</summary>
    /// <param name="Caption">The line above the blocks, or empty for none.</param>
    /// <param name="Rects">Every block's rectangle, as the layout model gave it.</param>
    /// <param name="Index">The blocks those rectangles name.</param>
    /// <returns>The column.</returns>
    /// <remarks>
    /// Sorted by where blocks sit rather than kept in the order the layout dictionary happened to yield
    /// them. That order is insertion order in practice and unspecified in the contract, and a plan whose
    /// instruction order moved between two runs of the same document would hash differently — which would
    /// make <see cref="Fingerprint"/> a hash of a hash table rather than of a drawing. Sorting by
    /// <em>position</em> also makes it the right order to draw in, since a container is always above the
    /// blocks inside it.
    /// </remarks>
    private static Column ColumnOf(
        string caption,
        IReadOnlyDictionary<string, BlockRect> rects,
        IReadOnlyDictionary<string, Block> index)
    {
        var blocks = rects
            .OrderBy(pair => pair.Value.Y)
            .ThenBy(pair => pair.Value.X)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new Painted(index.GetValueOrDefault(pair.Key), pair.Value))
            .ToList();

        var (extentWidth, extentHeight) = CanvasView.ExtentOf(rects.Values);
        var captionHeight = caption.Length > 0 ? CaptionLineHeight : 0;

        // The column is at least as wide as its caption, because a caption drawn over nothing is a caption
        // on a transparent background, which is the failure Part 25.3's transparency makes possible.
        return new Column(
            caption,
            blocks,
            Math.Max(extentWidth, caption.Length > 0 ? EstimateWidth(caption, CaptionFontSize) : 0),
            extentHeight,
            captionHeight);
    }

    /// <summary>
    /// A width for a run of text, in workspace units.
    /// </summary>
    /// <param name="text">The text to be measured.</param>
    /// <param name="fontSize">The size it will be drawn at.</param>
    /// <remarks>
    /// The same seven-and-a-bit pixels per character <see cref="BlockMetrics.EstimateWidth"/> uses, scaled
    /// by the font size. Core cannot measure text and must not pretend to: an estimate that errs wide costs
    /// a few units of margin, and an estimate that errs narrow clips the caption off the edge of the
    /// export. The App may substitute a measured width; the contract is that the plan is bigger than the
    /// text, never smaller.
    /// </remarks>
    private static double EstimateWidth(string text, double fontSize) =>
        Math.Max(
            BlockMetrics.TilePaddingX * 2,
            text.Length * (7.2 * (fontSize / LabelFontSize)));

    /// <summary>A dimension in whole device pixels, rounded up, and never zero.</summary>
    /// <param name="size">The size in workspace units.</param>
    /// <param name="scale">The scale it is drawn at.</param>
    /// <remarks>
    /// Up rather than to the nearest, because rounding down would clip the last row of pixels off the
    /// bottom block, and a rounded-up edge is transparent padding rather than a missing edge. At least one
    /// pixel, because a rasteriser will not accept a surface of nothing — the only place in this export where
    /// the number is deliberately not the geometry.
    /// </remarks>
    private static int Pixels(double size, double scale) =>
        (int)Math.Max(1, Math.Ceiling((Finite(size) * Finite(scale))));

    /// <summary>A number that is a number, whatever the layout handed over.</summary>
    /// <remarks>
    /// A document hand-edited or half-written can carry a position that is not a number, and
    /// <c>Ceiling</c> would pass it straight on to a cast — where it becomes an arbitrary integer rather
    /// than an error. Here it becomes zero, which the caller then bounds by one, and the export is a small
    /// wrong picture rather than a crash with no message.
    /// </remarks>
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    /// <summary>One number, invariant, with no negative zero and no trailing noise.</summary>
    /// <remarks>
    /// Four decimals, which is well past what a rasteriser can act on: the alternative is a fingerprint that
    /// moves when a layout difference changes the fifteenth decimal place, and a hash that cries wolf is
    /// switched off.
    /// </remarks>
    private static string Number(double value) =>
        (Math.Round(Finite(value), 4) + 0d).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>One line of the description, with an absent field written as a dash.</summary>
    private static string Row(params string?[] fields) =>
        string.Join(' ', fields.Select(field => field ?? "-"));

    /// <summary>
    /// Text as it can sit inside one line of the description.
    /// </summary>
    /// <remarks>
    /// XML-escaped through the same helper the vector export uses, and then made one line: a label can
    /// carry anything the user typed, and a literal containing a newline would otherwise split one
    /// instruction across two lines — so a plan with that label would hash as though it had one more
    /// instruction than it has, and two different labels could produce the same description. The backslash
    /// goes first, so the escape this adds is reversible and <c>"a\nb"</c> cannot be typed as
    /// <c>"a\\nb"</c>.
    /// </remarks>
    private static string OneLine(string text) =>
        BlockExport.EscapeXml(text)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    /// <summary>One block and where the layout put it.</summary>
    /// <param name="Block">The block, or null when the layout knows a rectangle nothing in the document does.</param>
    /// <param name="Rect">Its rectangle, in workspace units.</param>
    /// <remarks>
    /// The block is nullable because the layout's keys and the document's blocks are separate collections
    /// and a run can arrive from elsewhere — a pasted fragment, a provider's template. The rectangle is
    /// still drawn, in Control's neutral grey, because an export that leaves a hole where a block was is
    /// worse than one that admits it cannot describe it.
    /// </remarks>
    private sealed record Painted(Block? Block, BlockRect Rect);

    /// <summary>One column's blocks, its caption and how big it is.</summary>
    /// <param name="Caption">The line above the blocks, or empty for none.</param>
    /// <param name="Blocks">Every block, in draw order.</param>
    /// <param name="Width">How wide the column is, in workspace units.</param>
    /// <param name="ExtentHeight">How tall the blocks are, in workspace units.</param>
    /// <param name="CaptionHeight">How tall the caption's row is, in workspace units.</param>
    private sealed record Column(
        string Caption,
        IReadOnlyList<Painted> Blocks,
        double Width,
        double ExtentHeight,
        double CaptionHeight);
}