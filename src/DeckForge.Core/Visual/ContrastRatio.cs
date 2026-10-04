namespace DeckForge.Core.Visual;

/// <summary>
/// The WCAG relative-luminance contrast ratio between two colours, and the theme colours it is measured
/// against.
/// </summary>
/// <remarks>
/// <para>
/// This exists because §28.2 asked for label contrast "computed at 4.5:1 in a test" and the only way to
/// compute it is to have the numbers somewhere a test can reach. They were in the App's theme, behind
/// WPF colour structs and brush construction, which means the ratio could be looked at and never checked.
/// The colours are therefore computed here as <c>#RRGGBB</c> strings and the App converts them, so
/// there is exactly one set of numbers and the test measures those.
/// </para>
/// <para>
/// The ratios are WCAG 2.1 relative luminance: channels are linearised, weighted by
/// <c>0.2126/0.7152/0.0722</c>, and the ratio is the brighter luminance over the darker, plus a half
/// percent. Alpha is ignored, because every colour here is opaque and a ratio against a translucent
/// colour would need the backdrop as well - which is a different question, and one this design does not
/// have.
/// </para>
/// </remarks>
public static class ContrastRatio
{
    /// <summary>The ratio WCAG AA asks of body text against its background.</summary>
    public const double TextMinimum = 4.5;

    /// <summary>The ratio WCAG asks of a boundary or a glyph that is not text.</summary>
    public const double NonTextMinimum = 3.0;

    /// <summary>
    /// The contrast ratio between two colours, from 1 (identical) to 21 (black on white).
    /// </summary>
    /// <param name="first">One colour, as <c>#RGB</c> or <c>#RRGGBB</c>.</param>
    /// <param name="second">The other.</param>
    /// <remarks>
    /// <para>
    /// An unparseable colour gives 0, not 1 and not 21. A ratio of 0 fails every threshold, so a typo in
    /// a hex string shows up as a failing accessibility test rather than as a colour that mysteriously
    /// passes - which is the failure mode a threshold-based check has when it returns a comfortable
    /// default.
    /// </para>
    /// </remarks>
    public static double Between(string first, string second)
    {
        if (!TryParse(first, out var one) || !TryParse(second, out var other))
        {
            return 0;
        }

        var a = LuminanceOf(one);
        var b = LuminanceOf(other);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>WCAG relative luminance of a colour, from 0 (black) to 1 (white).</summary>
    /// <param name="hex">The colour, as <c>#RGB</c> or <c>#RRGGBB</c>.</param>
    public static double Luminance(string hex) => TryParse(hex, out var rgb) ? LuminanceOf(rgb) : 0;

    private static double LuminanceOf((byte R, byte G, byte B) rgb) =>
        (0.2126 * Channel(rgb.R))
        + (0.7152 * Channel(rgb.G))
        + (0.0722 * Channel(rgb.B));

    // sRGB is not linear, and using the encoded value directly is the single most common way a contrast
    // checker reports a comfortable number for a colour that is in fact hard to read.
    private static double Channel(byte value)
    {
        var encoded = value / 255d;

        return encoded <= 0.03928
            ? encoded / 12.92
            : Math.Pow((encoded + 0.055) / 1.055, 2.4);
    }

    /// <summary>Moves a colour towards black, as <c>#RRGGBB</c>.</summary>
    public static string Darken(string hex, double by) => Blend(hex, 0, by);

    /// <summary>Moves a colour towards white, as <c>#RRGGBB</c>.</summary>
    public static string Lighten(string hex, double by) => Blend(hex, 255, by);

    /// <summary>
    /// Moves a colour towards <paramref name="towards"/> until it reads against
    /// <paramref name="against"/> at <paramref name="minimum"/>, and returns where it stopped.
    /// </summary>
    /// <param name="hex">The colour to adjust.</param>
    /// <param name="towards">The end of the blend: 0 for black, 255 for white.</param>
    /// <param name="against">The colour it has to read against.</param>
    /// <param name="minimum">The ratio to reach.</param>
    /// <param name="start">Where to begin blending from.</param>
    /// <param name="step">How far to move each attempt.</param>
    /// <remarks>
    /// <para>
    /// This is what turned §28.2's number into a result. The fill and outline constants were hand-tuned
    /// per theme, and the first thing a computed ratio said about them was that Events' dark fill gave
    /// white text 3.89:1 and every light-theme outline sat between 1.5 and 2.8:1 against the fill it was
    /// supposed to be drawing the edge of. Both were wrong, and neither was visible - a slightly weak
    /// outline still looks like an outline.
    /// </para>
    /// <para>
    /// So the requirement decides the colour rather than a constant recording somebody's guess about it.
    /// Every category hue gets the *same* guaranteed contrast, whether or not its own luminance made that
    /// hard, and adding a category with a pale hue cannot quietly reintroduce the failure. The walk is
    /// bounded and starts from a fixed amount, so the result is deterministic: the same hue gives the
    /// same colour on every machine, every run.
    /// </para>
    /// </remarks>
    public static string AdjustUntil(
        string hex,
        int towards,
        string against,
        double minimum,
        double start = 0,
        double step = 0.04)
    {
        var candidate = Blend(hex, towards, start);

        // Enough steps to reach the far end from any starting amount. The last resort is the end colour
        // itself, which is the best contrast available in that direction - so if even that fails, the
        // honest answer is the colour we got to rather than a pretend pass.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (Between(candidate, against) >= minimum)
            {
                return candidate;
            }

            candidate = Blend(hex, towards, Math.Min(start + (step * (attempt + 1)), 1));
        }

        return candidate;
    }

    /// <summary>
    /// Blends every channel towards <paramref name="to"/>, which is what both theme directions are.
    /// </summary>
    /// <remarks>
    /// The App had its own <c>Darken</c> and <c>Lighten</c> over WPF colours, and the block palette was
    /// the only thing that used them for its per-category fills. Keeping both would have meant the
    /// numbers in a test were the numbers in the theme only by coincidence, and a coincidence is exactly
    /// what had been there all along.
    /// </remarks>
    private static string Blend(string hex, int to, double amount)
    {
        if (!TryParse(hex, out var rgb))
        {
            return hex;
        }

        byte Mix(byte from) => (byte)Math.Clamp(Math.Round(from + ((to - from) * amount)), 0, 255);

        return $"#{Mix(rgb.R):X2}{Mix(rgb.G):X2}{Mix(rgb.B):X2}";
    }

    /// <summary>Reads <c>#RGB</c> or <c>#RRGGBB</c>, and refuses anything else.</summary>
    private static bool TryParse(string hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var digits = hex.StartsWith('#') ? hex[1..] : hex;
        if (digits.Length is 3)
        {
            digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);
        }

        if (digits.Length != 6
            || !int.TryParse(
                digits,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var packed))
        {
            return false;
        }

        rgb = ((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
        return true;
    }
}
