namespace DeckForge.Core.Visual;

/// <summary>
/// How much of a block's colour survives, for a canvas that has to stay readable.
/// </summary>
/// <remarks>
/// <para>
/// §9.8's rule: "tiles fall back to border-only fills with strong outlines when the system asks". The
/// decision is in Core because it is a decision about the *design* — which colours carry meaning, which are
/// decoration — and a design that lives in a WPF theme cannot be checked, only looked at.
/// </para>
/// <para>
/// Two levels rather than a switch, because "high contrast" and "no fill at all" are not the same thing.
/// <see cref="Standard"/> is what the design is drawn for; <see cref="Strong"/> is the outline-only form for
/// a system that asks for it; <see cref="Monochrome"/> drops colour entirely for a user who reads the canvas
/// by shape, and it is the only level at which the palette's *text* labels stop being redundant.
/// </para>
/// </remarks>
public enum ContrastLevel
{
    /// <summary>The colours the design was drawn for.</summary>
    Standard,

    /// <summary>Category fills kept, outlines strengthened.</summary>
    Strong,

    /// <summary>No fills, strong outlines, and the category is read from the label rather than the colour.</summary>
    Monochrome,
}

/// <summary>
/// What a block looks like at a given contrast: its fill, its outline and how thick the outline is.
/// </summary>
/// <remarks>
/// A record rather than a brush, because a brush is an App type and this decision has to be testable without
/// one. The App resolves it into brushes; nothing here knows what a brush is.
/// </remarks>
/// <param name="Fill">The block's fill, or null for none.</param>
/// <param name="Stroke">Its outline colour.</param>
/// <param name="StrokeThickness">How wide the outline is.</param>
/// <param name="Muted">Whether the block is drawn switched off.</param>
public sealed record BlockContrast(
    string? Fill,
    string Stroke,
    double StrokeThickness,
    bool Muted)
{
    /// <summary>The outline's width, in pixels, for a level.</summary>
    /// <remarks>
    /// 2 and then 3. A one-pixel outline on a white background is a hairline that disappears at some zoom
    /// levels and on some panels — the same reason the block outline is 1.25 rather than 1.
    /// </remarks>
    public static double StrokeWidthFor(ContrastLevel level) => level switch
    {
        ContrastLevel.Strong => 2,
        ContrastLevel.Monochrome => 3,
        _ => 1.25,
    };
}

/// <summary>
/// The contrast a block is drawn at.
/// </summary>
/// <remarks>
/// Deliberately a static table over the catalogue's own hues rather than a theme resource. A canvas that
/// asks the running application what colour to draw has already lost the property that matters: the answer
/// is then the same on every machine, which is what makes it checkable, and checkable is what makes it
/// correct on a machine nobody is looking at.
/// </remarks>
public static class BlockContrastRules
{
    /// <summary>What a block of this category looks like at this contrast.</summary>
    /// <param name="category">The block's category.</param>
    /// <param name="level">How much colour survives.</param>
    /// <param name="disabled">Whether the block is switched off.</param>
    public static BlockContrast For(BlockCategory category, ContrastLevel level, bool disabled = false)
    {
        var hue = BlockCatalog.Category(category).Hue;

        return level switch
        {
            ContrastLevel.Strong => new BlockContrast(hue, Darken(hue, 0.35), BlockContrast.StrokeWidthFor(level), disabled),

            // No fill at all: the shape carries the category - stack, reporter, boolean, hat - and the
            // palette's text labels carry the category name, which is §9.8's "colour is never the only
            // signal" taken literally rather than aspirationally.
            ContrastLevel.Monochrome => new BlockContrast(null, "#FFFFFF", BlockContrast.StrokeWidthFor(level), disabled),

            _ => new BlockContrast(hue, Darken(hue, 0.2), BlockContrast.StrokeWidthFor(level), disabled),
        };
    }

    /// <summary>Whether a category's colour is what identifies it at this level.</summary>
    /// <param name="level">The contrast.</param>
    /// <remarks>
    /// Used by the palette to decide whether the category's *name* is redundant. At monochrome it is the
    /// only signal there is, so hiding it there would leave a colour-blind-safe palette that says nothing.
    /// </remarks>
    public static bool CategoryColourCarriesMeaning(ContrastLevel level) => level != ContrastLevel.Monochrome;

    /// <summary>
    /// Darkens a hex colour by a fraction, for an outline that reads against its own fill.
    /// </summary>
    /// <remarks>
    /// A hard-coded palette of "outline colours" was the first version, and it went stale the first time a
    /// category's hue changed — the outline quietly stopped matching and nobody would have noticed, because
    /// a slightly-wrong outline still looks like an outline. Deriving it means it cannot.
    /// </remarks>
    private static string Darken(string hex, double by)
    {
        if (!TryParse(hex, out var red, out var green, out var blue))
        {
            return hex;
        }

        int Scale(int channel) => (int)Math.Clamp(channel * (1 - by), 0, 255);

        return $"#{Scale(red):X2}{Scale(green):X2}{Scale(blue):X2}";
    }

    /// <summary>Reads <c>#RGB</c> or <c>#RRGGBB</c>, and refuses anything else.</summary>
    private static bool TryParse(string hex, out int red, out int green, out int blue)
    {
        red = green = blue = 0;

        var digits = hex.StartsWith('#') ? hex[1..] : hex;
        if (digits.Length is not (3 or 6))
        {
            return false;
        }

        if (!int.TryParse(
                digits.Length == 3 ? string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]) : digits,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var packed))
        {
            return false;
        }

        red = (packed >> 16) & 0xFF;
        green = (packed >> 8) & 0xFF;
        blue = packed & 0xFF;

        return true;
    }
}
