using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeckForge.Core.Visual;

namespace DeckForge.App.Services;

/// <summary>
/// Draws a <see cref="PngExportPlan"/> and encodes it: the only WPF half of the PNG export.
/// </summary>
/// <remarks>
/// <para>
/// This class exists because of one decision recorded in §28.5. PNG export was "not built, by decision",
/// on the grounds that "rasterising belongs to the caller that already has a drawing surface, and a
/// third-party encoder in Core would make the export untestable". That was right about the encoder and
/// wrong to let it take the export with it: a <c>RenderTargetBitmap</c> is a WPF type, so it cannot be in
/// Core, but <em>no decision about the picture needs it</em>. What the blocks are, where they sit, how big
/// the image is, what scale it is drawn at and what colour each one is are all in
/// <see cref="PngExportPlan"/>, in a project the test suite can read.
/// </para>
/// <para>
/// So this is deliberately the driest adapter in the application: it multiplies nothing, rounds nothing,
/// decides nothing and falls back to nothing. Every number it draws is already in device pixels and every
/// colour is already <c>#RRGGBB</c>. If a future defect says an export drew the wrong block in the wrong
/// place, the fix belongs in Core where a test can fail about it, and not here.
/// </para>
/// </remarks>
public static class BlockImageRenderer
{
    /// <summary>
    /// The widest surface WPF will allocate.
    /// </summary>
    /// <remarks>
    /// 16384, which is the texture bound the software renderer is built on. Asking for more does not scale
    /// the drawing down and it does not fail politely: it throws from deep inside the renderer, after the
    /// drawing has already been walked, with a message about a bitmap rather than about the export. Refused
    /// here instead, with the number the caller asked for in the sentence, because a user who exported at
    /// 8x deserves to be told the size rather than to be told about a bitmap.
    /// </remarks>
    public const int MaxDimension = 16384;

    /// <summary>
    /// The typeface an export is drawn in when the caller has no theme to ask.
    /// </summary>
    /// <remarks>
    /// The same stack <c>LiquidTheme</c> registers as <c>Liquid.FontFamily</c>, spelled out rather than
    /// looked up: the plan may be rendered from a page, from the clipboard on a dispatcher with no window,
    /// or from anywhere else, and a resource lookup returns null in all but the first of those. A null
    /// typeface is not an error WPF can report usefully, so the default is the answer and a caller with a
    /// theme can pass its own.
    /// </remarks>
    public static readonly Typeface DefaultTypeface =
        new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>
    /// Draws a plan, or says why it could not be drawn.
    /// </summary>
    /// <param name="plan">The plan, from Core.</param>
    /// <param name="bitmap">The image, frozen, or null.</param>
    /// <param name="problem">What stopped it, in a sentence, or null.</param>
    /// <param name="typeface">The typeface to draw text in, or null for <see cref="DefaultTypeface"/>.</param>
    /// <returns>Whether there is a bitmap.</returns>
    /// <remarks>
    /// <para>
    /// A refusal rather than an exception because there are two callers with two different appetites: the
    /// export writes a file and must name the problem, and the clipboard puts a picture *alongside* the text
    /// it is already carrying, so a picture that will not draw may not stop the text from landing. A
    /// throwing renderer forces both to catch, and the one that must not care would have to catch anyway.
    /// </para>
    /// <para>
    /// The surface is left transparent because Part 25.3 asks for it: an export pasted into a chat client
    /// takes its background from the message, and one painted with the editor's own would arrive as a grey
    /// box around the blocks.
    /// </para>
    /// </remarks>
    public static bool TryRender(
        PngExportPlan plan,
        out BitmapSource? bitmap,
        out string? problem,
        Typeface? typeface = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        bitmap = null;
        problem = null;

        if (plan.Width < 1 || plan.Height < 1)
        {
            problem = $"the drawing came out {plan.Width}x{plan.Height} pixels, and there is no such image.";
            return false;
        }

        if (plan.Width > MaxDimension || plan.Height > MaxDimension)
        {
            problem = $"it would be {plan.Width}x{plan.Height} pixels at {plan.Scale:0.##}x, and WPF will "
                + $"not draw a surface wider than {MaxDimension}. Export at a smaller scale.";
            return false;
        }

        var visual = new DrawingVisual();
        var brushes = new Dictionary<string, Brush>(StringComparer.Ordinal);
        var pens = new Dictionary<string, Pen>(StringComparer.Ordinal);

        using (var context = visual.RenderOpen())
        {
            foreach (var item in plan.Items)
            {
                switch (item)
                {
                    case PngExportBox box:
                        DrawBox(context, box, brushes, pens);
                        break;

                    case PngExportText line:
                        DrawText(context, line, typeface ?? DefaultTypeface, brushes);
                        break;
                }
            }
        }

        try
        {
            var surface = new RenderTargetBitmap(
                plan.Width,
                plan.Height,
                96,
                96,
                PixelFormats.Pbgra32);

            surface.Render(visual);

            // Frozen so the bitmap can cross to the clipboard and to an encoder's worker thread: a
            // DrawingVisual-backed bitmap holds a dispatcher affinity, and handing one of those to the
            // clipboard is a cross-thread exception at the far end rather than here.
            surface.Freeze();
            bitmap = surface;
            return true;
        }
        catch (Exception error) when (error is OutOfMemoryException or NotSupportedException or ArgumentException)
        {
            problem = $"WPF could not allocate the {plan.Width}x{plan.Height} surface: {error.Message}";
            return false;
        }
    }

    /// <summary>
    /// The image as PNG bytes, with no file touched.
    /// </summary>
    /// <param name="bitmap">A frozen bitmap, as <see cref="TryRender"/> returns.</param>
    /// <remarks>
    /// Bytes rather than a file so the caller can write them with one try/catch and report a write failure
    /// the way it reports every other one. An encoder that had a path would have a second opinion about
    /// where the file goes, and an export is something the user names.
    /// </remarks>
    public static byte[] EncodePng(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);

        return buffer.ToArray();
    }

    /// <summary>Draws one rounded rectangle, reusing a brush or pen a previous box already made.</summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="box">The rectangle, in device pixels.</param>
    /// <param name="brushes">Colours already converted this pass.</param>
    /// <param name="pens">Outlines already converted this pass.</param>
    /// <remarks>
    /// The gradient is the one <c>LiquidTheme</c> builds for a block, at the same 0 to 0.15,1 direction, so
    /// an export is the canvas's colour rather than a flat version of it. Converting per box instead would
    /// allocate a hundred brushes and a hundred pens for a hundred blocks, and a frozen brush is cheaper
    /// than a lookup in a dictionary — but a hundred of them is still a hundred of somebody's decision.
    /// </remarks>
    private static void DrawBox(
        DrawingContext context,
        PngExportBox box,
        Dictionary<string, Brush> brushes,
        Dictionary<string, Pen> pens)
    {
        var fill = Fill(box, brushes);
        var pen = Pen(box, brushes, pens);
        var area = new Rect(box.X, box.Y, box.Width, box.Height);

        // The radius clamped to half the shorter side, because DrawRoundedRectangle throws rather than
        // flattening when the two cross: a very short block at 2x has a radius taller than itself.
        var radius = Math.Max(0, Math.Min(box.Radius, Math.Min(box.Width, box.Height) / 2));

        context.DrawRoundedRectangle(fill, pen, area, radius, radius);
    }

    /// <summary>A box's fill: flat when both stops are the same, a gradient when they are not.</summary>
    /// <param name="box">The rectangle.</param>
    /// <param name="brushes">Colours already converted this pass, keyed by the two stops together.</param>
    private static Brush Fill(PngExportBox box, Dictionary<string, Brush> brushes)
    {
        var key = $"{box.FillTop}|{box.FillBottom}";

        if (brushes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Brush brush;

        if (string.Equals(box.FillTop, box.FillBottom, StringComparison.Ordinal))
        {
            brush = Frozen(BrushOf(box.FillTop));
        }
        else
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0.15, 1),
            };

            gradient.GradientStops.Add(new GradientStop(ColourOf(box.FillTop), 0));
            gradient.GradientStops.Add(new GradientStop(ColourOf(box.FillBottom), 1));
            gradient.Freeze();
            brush = gradient;
        }

        brushes[key] = brush;
        return brush;
    }

    /// <summary>A box's outline, or null when it has none.</summary>
    /// <param name="box">The rectangle.</param>
    /// <param name="brushes">Colours already converted this pass.</param>
    /// <param name="pens">Outlines already converted this pass.</param>
    private static Pen? Pen(PngExportBox box, Dictionary<string, Brush> brushes, Dictionary<string, Pen> pens)
    {
        if (box.Stroke is not { Length: > 0 } stroke || box.StrokeThickness <= 0)
        {
            return null;
        }

        var key = $"{stroke}|{box.StrokeThickness}";

        if (pens.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var pen = new Pen(Solid(stroke, brushes), box.StrokeThickness);
        pen.Freeze();

        pens[key] = pen;
        return pen;
    }

    /// <summary>Draws one line of text with its left edge and its baseline.</summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="line">The text, in device pixels.</param>
    /// <param name="typeface">What to draw it in.</param>
    /// <param name="brushes">Colours already converted this pass.</param>
    /// <remarks>
    /// <para>
    /// Built as a <see cref="FormattedText"/> with the invariant culture rather than through the
    /// <c>DrawText(string, …)</c> overload, which formats with the current culture. That overload is how a
    /// machine set to a locale with different digits exports a picture whose numbers are not the ones in the
    /// document — the same reason <see cref="SvgRenderer"/> writes its coordinates with an invariant
    /// separator, and the reason the plan hashes the same on every machine.
    /// </para>
    /// <para>
    /// The origin is moved up by the formatted text's own baseline, because <c>DrawText</c> places the
    /// <em>top left</em> of the line where the point is while the plan — like an SVG's <c>y</c> — says where
    /// the baseline goes. Without that subtraction every label in the export sat a line below the block it
    /// belongs to, which is not a subtlety: the first one landed on the caption bar and the second on the
    /// top edge of the block underneath it.
    /// </para>
    /// </remarks>
    private static void DrawText(
        DrawingContext context,
        PngExportText line,
        Typeface typeface,
        Dictionary<string, Brush> brushes)
    {
        if (line.Text.Length == 0 || line.FontSize <= 0)
        {
            return;
        }

        var formatted = new FormattedText(
            line.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            line.FontSize,
            Solid(line.Ink, brushes),
            1.0);

        context.DrawText(formatted, new Point(line.X, line.Y - formatted.Baseline));
    }

    /// <summary>A solid brush for one colour, cached under its own name.</summary>
    /// <param name="hex">The colour, as <c>#RRGGBB</c>.</param>
    /// <param name="brushes">Colours already converted this pass.</param>
    private static Brush Solid(string hex, Dictionary<string, Brush> brushes)
    {
        if (brushes.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var brush = Frozen(BrushOf(hex));
        brushes[hex] = brush;

        return brush;
    }

    /// <summary>A frozen solid brush, so it can be handed to a drawing context without a dispatcher check.</summary>
    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>Reads a colour the Core palette writes, which is <c>#RRGGBB</c>.</summary>
    /// <remarks>
    /// Grey for anything unparseable rather than an exception. Core's own colours are measured and its
    /// fallback is a measured neutral, so a bad string here means a defect in the plan — and a grey
    /// rectangle beside a clear refusal names the block that caused it, where a thrown
    /// <see cref="ColorConverter"/> during an export names nothing at all.
    /// </remarks>
    private static Color ColourOf(string hex) =>
        ColorConverter.ConvertFromString(hex) is Color colour ? colour : Colors.Gray;

    /// <summary>A brush for one <c>#RRGGBB</c> colour.</summary>
    private static SolidColorBrush BrushOf(string hex) => new(ColourOf(hex));
}