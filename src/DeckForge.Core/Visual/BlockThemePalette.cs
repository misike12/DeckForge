namespace DeckForge.Core.Visual;

/// <summary>
/// The colours a block of a given category is drawn in, as <c>#RRGGBB</c>.
/// </summary>
/// <remarks>
/// A record of strings rather than brushes or WPF colours, so a test can read the numbers and compute a
/// ratio from them. <c>LiquidTheme</c> converts them to brushes; nothing here knows WPF exists.
/// </remarks>
/// <param name="FillTop">The gradient's first stop.</param>
/// <param name="FillBottom">Its second, and the darker or lighter of the two.</param>
/// <param name="Ink">The text drawn on top of the fill.</param>
/// <param name="Stroke">The block's outline.</param>
public sealed record BlockThemeTokens(string FillTop, string FillBottom, string Ink, string Stroke);

/// <summary>
/// The block palette's colours, for both themes.
/// </summary>
/// <remarks>
/// <para>
/// These were computed inside <c>LiquidTheme</c>, which meant the contrast of a label against its own
/// block could be seen and never measured - §28.2 asked for 4.5:1 "in a test" and there was no number
/// for a test to read. They live here now, and the App converts what it needs.
/// </para>
/// <para>
/// The two themes are not mirror images of each other. Dark mode darkens a hue for its fill and puts
/// near-white ink on it; light mode lightens the hue and puts near-black ink on it. Both keep the ink
/// on the far side of the fill, which is what makes the ratio large, and the test holds them to it.
/// </para>
/// </remarks>
public static class BlockThemePalette
{
    /// <summary>Near-white ink, for a dark fill.</summary>
    public const string DarkInk = "#F7F9FC";

    /// <summary>Near-black ink, for a light fill.</summary>
    public const string LightInk = "#14161C";

    /// <summary>
    /// Where each theme's fill starts, before the requirement has had its say.
    /// </summary>
    /// <remarks>
    /// These are starting points for <see cref="ContrastRatio.AdjustUntil"/>, not the answer. A hue with
    /// enough luminance of its own stops here; a pale one - Events' gold, which is nearly the brightest
    /// colour in the catalogue - is walked further towards black until white text on it reaches 4.5:1.
    /// </remarks>
    private const double DarkFillStart = 0.30;
    private const double LightFillStart = 0.28;

    /// <summary>How much further the gradient's lower stop is moved than its upper one.</summary>
    private const double GradientStep = 0.12;

    /// <summary>Where an outline's walk towards black begins.</summary>
    private const double OutlineStart = 0.18;

    /// <summary>The colours one category's blocks are drawn in.</summary>
    /// <param name="category">The block's category.</param>
    /// <param name="dark">Whether the dark theme is in use.</param>
    /// <remarks>
    /// <para>
    /// The fill is chosen so its <em>lighter</em> end already carries the ink at
    /// <see cref="ContrastRatio.TextMinimum"/>, and the lower stop is then a step further from white or
    /// black. Pushing the far end only ever increases text contrast, so the gradient's worst case is the
    /// one that was measured.
    /// </para>
    /// <para>
    /// The outline is then chosen against that lighter fill at <see cref="ContrastRatio.NonTextMinimum"/>,
    /// rather than being a fixed darkening of the hue. In light mode it moves towards black - an outline
    /// has to be darker than a light fill to read as an edge - which is why the old constant went the
    /// other way and measured 1.68:1 on Control.
    /// </para>
    /// </remarks>
    public static BlockThemeTokens For(BlockCategory category, bool dark)
    {
        var hue = BlockCatalog.Category(category).Hue;

        if (dark)
        {
            var fill = ContrastRatio.AdjustUntil(hue, 0, DarkInk, ContrastRatio.TextMinimum, DarkFillStart);

            return new BlockThemeTokens(
                fill,
                ContrastRatio.Darken(fill, GradientStep),
                DarkInk,
                Outline(fill, dark: true));
        }

        var lightFill = ContrastRatio.Lighten(hue, LightFillStart);

        return new BlockThemeTokens(
            ContrastRatio.Lighten(hue, 0.52),
            lightFill,
            LightInk,
            Outline(lightFill, dark: false));
    }

    /// <summary>
    /// An outline that reads against <paramref name="fill"/> as a boundary.
    /// </summary>
    /// <param name="fill">The fill it draws the edge of.</param>
    /// <param name="dark">Whether the dark theme is in use.</param>
    /// <remarks>
    /// <para>
    /// The two themes walk in opposite directions, and that is the whole design rather than an
    /// inconsistency. On a light fill the outline goes towards black, because a dark edge on a light shape
    /// is how an edge reads. On a dark fill it goes towards <em>white</em>, for the same reason a dark UI
    /// puts a light hairline around a card: a black outline on a dark fill is not a stronger edge, it is
    /// no edge at all.
    /// </para>
    /// <para>
    /// That is not a theoretical worry. Network's dark fill measures 2.45:1 against pure black - below the
    /// 3:1 it needs - while the same fill gives near-white ink a ratio over 12:1. Walking the outline the
    /// other way is the fix; the alternative, refusing to darken a fill that far, would have traded a
    /// failing label for a failing outline.
    /// </para>
    /// </remarks>
    private static string Outline(string fill, bool dark) => ContrastRatio.AdjustUntil(
        fill,
        dark ? 255 : 0,
        fill,
        ContrastRatio.NonTextMinimum,
        OutlineStart);
}
