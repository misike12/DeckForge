namespace DeckForge.Core.Visual;

/// <summary>
/// One instruction in a PNG export: either a filled rounded rectangle, or a line of text.
/// </summary>
/// <remarks>
/// <para>
/// Every coordinate on these is in <em>device pixels</em>, already multiplied by the plan's scale. That is
/// the whole reason the scale is decided in Core: the App's job is to hand each number to
/// <c>DrawRoundedRectangle</c> and <c>DrawText</c> without doing arithmetic of its own, so there is no
/// second place for "2x" to be applied at a different moment, half a size away from where it was applied.
/// </para>
/// <para>
/// Colours are <c>#RRGGBB</c> strings rather than WPF brushes. Core cannot name a brush — and a plan full
/// of them could not be compared, hashed or asserted on by a test, which is the only reason any of this
/// exists.
/// </para>
/// </remarks>
public abstract record PngExportItem
{
    /// <summary>The left edge, in device pixels.</summary>
    public double X { get; init; }

    /// <summary>The top edge, in device pixels.</summary>
    public double Y { get; init; }
}

/// <summary>A filled rounded rectangle: a block, or a plate behind a caption.</summary>
/// <remarks>
/// <para>
/// Two fills rather than one because the canvas draws a gradient and the App's theme already derives both
/// stops from the same <see cref="BlockThemePalette"/> call this plan makes. A single flat colour would be
/// one more number to keep in step with the screen, and an export that is subtly flatter than the canvas is
/// exactly the kind of difference nobody can report.
/// </para>
/// <para>
/// <see cref="BlockId"/> is null for a plate and set for a block, which is what lets a caller — and a test —
/// tell them apart without a discriminator that could itself disagree with the geometry.
/// </para>
/// </remarks>
public sealed record PngExportBox : PngExportItem
{
    /// <summary>The width, in device pixels.</summary>
    public double Width { get; init; }

    /// <summary>The height, in device pixels.</summary>
    public double Height { get; init; }

    /// <summary>The corner radius, in device pixels.</summary>
    public double Radius { get; init; }

    /// <summary>The fill at the top of the gradient, as <c>#RRGGBB</c>.</summary>
    public string FillTop { get; init; } = string.Empty;

    /// <summary>The fill at the bottom of the gradient, as <c>#RRGGBB</c>.</summary>
    public string FillBottom { get; init; } = string.Empty;

    /// <summary>The outline's colour, or null for none.</summary>
    public string? Stroke { get; init; }

    /// <summary>The outline's width, in device pixels.</summary>
    /// <remarks>
    /// Already scaled, like everything else here. Zero and a null <see cref="Stroke"/> both mean no
    /// outline, and the App is not asked to decide which.
    /// </remarks>
    public double StrokeThickness { get; init; }

    /// <summary>The block this rectangle is, or null when it is a caption's plate.</summary>
    public string? BlockId { get; init; }
}

/// <summary>A line of text, drawn with its left edge and its baseline.</summary>
/// <remarks>
/// A baseline rather than a top edge because that is what a drawing context takes, and a plan that stored a
/// top edge would leave the App computing an ascent from a font size — which is the rasteriser's estimate
/// of the renderer, and would put every label a few pixels high.
/// </remarks>
public sealed record PngExportText : PngExportItem
{
    /// <summary>What the text says, unescaped.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The font size, in device pixels.</summary>
    public double FontSize { get; init; }

    /// <summary>The colour of the text, as <c>#RRGGBB</c>.</summary>
    public string Ink { get; init; } = string.Empty;

    /// <summary>The block whose label this is, or null for a caption.</summary>
    public string? BlockId { get; init; }
}