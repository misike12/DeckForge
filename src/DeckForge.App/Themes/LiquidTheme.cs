using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using DeckForge.App.Services;

namespace DeckForge.App.Themes;

/// <summary>
/// Design tokens of the DeckForge liquid design language: a minimal Fluent 2 base with
/// translucent surfaces, soft depth and one drifting accent. All values flow through the
/// theme service into WPF resources; pages never hard-code colors.
/// </summary>
public static class LiquidTheme
{
    // Accent presets (user-selectable in Settings).
    public static readonly IReadOnlyList<NamedColor> Accents =
    [
        new("Deck Blue", Color.FromRgb(0x4F, 0x8C, 0xFF)),
        new("Aqua", Color.FromRgb(0x2D, 0xD4, 0xBF)),
        new("Violet", Color.FromRgb(0x8B, 0x7C, 0xF6)),
        new("Sunset", Color.FromRgb(0xFF, 0x7A, 0x59)),
        new("Magenta", Color.FromRgb(0xE9, 0x4F, 0xA8)),
        new("Lime", Color.FromRgb(0x7A, 0xC7, 0x43)),
    ];

    public static readonly IReadOnlyList<NamedColor> DarkSurfaces = new List<NamedColor>
    {
        // Deliberately deep base so cards separate clearly (the "liquid elevation" look).
        new("Base", Color.FromRgb(0x12, 0x13, 0x18)),
        new("Layer", Color.FromRgb(0x1E, 0x21, 0x29)),
        new("Card", Color.FromRgb(0x27, 0x2B, 0x35)),
        new("Border", Color.FromRgb(0x48, 0x4F, 0x5E)),
    }.AsReadOnly();

    public static readonly IReadOnlyList<NamedColor> LightSurfaces = new List<NamedColor>
    {
        // Layer is a shade off the base and Card sits on it, so light mode has the same
        // three-step elevation dark mode does. They were both pure white, so a card and the
        // panel behind it were the same colour and the only separation was a hairline border.
        new("Base", Color.FromRgb(0xE7, 0xEA, 0xF0)),
        new("Layer", Color.FromRgb(0xF4, 0xF6, 0xF9)),
        new("Card", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        new("Border", Color.FromRgb(0xC9, 0xCF, 0xDA)),
    }.AsReadOnly();

    public static void Apply(ResourceDictionary resources, AppTheme mode, string accentName)
    {
        var accent = Accents.FirstOrDefault(a => a.Name == accentName) ?? Accents[0];
        var surfaces = mode == AppTheme.Dark ? DarkSurfaces : LightSurfaces;
        var isDark = mode == AppTheme.Dark;

        void Set(string key, object value) => resources[key] = value;

        Color Base() => surfaces[0].Color;
        Color Layer() => surfaces[1].Color;
        Color Card() => surfaces[2].Color;
        Color Border() => surfaces[3].Color;

        Color Text() => isDark ? Color.FromRgb(0xF2, 0xF4, 0xF8) : Color.FromRgb(0x18, 0x1A, 0x20);
        Color TextSecondary() => isDark ? Color.FromRgb(0xC4, 0xCB, 0xD8) : Color.FromRgb(0x51, 0x59, 0x66);
        Color TextTertiary() => isDark ? Color.FromRgb(0x8E, 0x96, 0xA4) : Color.FromRgb(0x7E, 0x86, 0x94);

        // WPF-UI control accent: makes ui:Button Primary, toggles, sliders and highlights
        // follow the chosen liquid accent everywhere.
        Set("SystemAccentColor", accent.Color);
        Set("SystemAccentColorPrimary", accent.Color);
        Set("SystemAccentColorSecondary", Color.FromArgb(0xDD, accent.Color.R, accent.Color.G, accent.Color.B));

        // WPF-UI's own text brushes: this is what its controls (and default text) use, so
        // overriding them here fixes every page at once - including placeholder text,
        // which reads as secondary (gray) instead of white.
        Set("TextFillColorPrimaryBrush", new SolidColorBrush(Text()));
        Set("TextFillColorSecondaryBrush", new SolidColorBrush(TextSecondary()));
        Set("TextFillColorTertiaryBrush", new SolidColorBrush(TextTertiary()));
        Set("TextFillColorDisabledBrush", new SolidColorBrush(TextTertiary()));
        Set("TextFillColorInverseBrush", new SolidColorBrush(isDark ? Color.FromRgb(0x18, 0x1A, 0x20) : Color.FromRgb(0xF2, 0xF4, 0xF8)));

        // Accent (Primary) buttons read best with near-black text on the bright liquid
        // accent fill. WPF-UI's own accent manager only switches to dark text when the
        // accent is extremely bright, so force the outcome we want for every accent here.
        var onAccent = Color.FromRgb(0x10, 0x12, 0x16);
        Set("AccentButtonForeground", new SolidColorBrush(onAccent));
        Set("AccentButtonForegroundPointerOver", new SolidColorBrush(onAccent));
        Set("AccentButtonForegroundPressed", new SolidColorBrush(onAccent));
        Set("TextOnAccentFillColorPrimaryBrush", new SolidColorBrush(onAccent));
        Set("TextOnAccentFillColorSecondaryBrush", new SolidColorBrush(Color.FromArgb(0x80, onAccent.R, onAccent.G, onAccent.B)));
        Set("AccentTextFillColorPrimaryBrush", new SolidColorBrush(onAccent));
        Set("AccentTextFillColorSecondaryBrush", new SolidColorBrush(onAccent));
        Set("AccentTextFillColorTertiaryBrush", new SolidColorBrush(onAccent));

        // Core surfaces.
        Set("Liquid.BaseBrush", new SolidColorBrush(Base()));
        Set("Liquid.LayerBrush", new SolidColorBrush(Layer()));
        Set("Liquid.CardBrush", new SolidColorBrush(Card()));
        Set("Liquid.BorderBrush", new SolidColorBrush(Border()));
        Set("Liquid.TextBrush", new SolidColorBrush(Text()));
        Set("Liquid.TextSecondaryBrush", new SolidColorBrush(TextSecondary()));
        Set("Liquid.TextTertiaryBrush", new SolidColorBrush(TextTertiary()));
        Set("Liquid.AccentBrush", new SolidColorBrush(accent.Color));
        Set("Liquid.AccentColor", accent.Color);

        // Liquid translucency: acrylic-like card fill over the Mica backdrop. High alpha so
        // cards read clearly against the base - translucency comes from the backdrop itself.
        var cardAlpha = isDark ? (byte)0xF2 : (byte)0xFA;
        Set("Liquid.AcrylicBrush", new SolidColorBrush(Color.FromArgb(cardAlpha, Card().R, Card().G, Card().B)));
        Set("Liquid.AcrylicStrongBrush", new SolidColorBrush(Color.FromArgb(isDark ? (byte)0xFA : (byte)0xFF, Card().R, Card().G, Card().B)));

        // Accent-derived washes (hover, selection, hero tint).
        Set("Liquid.AccentHoverBrush", new SolidColorBrush(Color.FromArgb(0x38, accent.Color.R, accent.Color.G, accent.Color.B)));
        Set("Liquid.AccentWashBrush", new SolidColorBrush(Color.FromArgb(0x24, accent.Color.R, accent.Color.G, accent.Color.B)));
        Set("Liquid.AccentStrongBrush", new SolidColorBrush(Color.FromArgb(0xFF, accent.Color.R, accent.Color.G, accent.Color.B)));

        // Semantic. In light mode these are darkened so they keep enough contrast against a white
        // card; the dark-mode values are tuned for a near-black base and are too pale on white.
        var success = isDark ? Color.FromRgb(0x3F, 0xB8, 0x63) : Color.FromRgb(0x1E, 0x7A, 0x3C);
        var warning = isDark ? Color.FromRgb(0xE2, 0x9E, 0x2D) : Color.FromRgb(0x9A, 0x64, 0x00);
        var danger = isDark ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0xC0, 0x2A, 0x2F);
        Set("Liquid.SuccessBrush", new SolidColorBrush(success));
        Set("Liquid.WarningBrush", new SolidColorBrush(warning));
        Set("Liquid.DangerBrush", new SolidColorBrush(danger));

        // The liquid accent gradient. The end caps used to be hardcoded blue and indigo, so
        // choosing the Lime or Magenta accent still produced a blue hero - the accent was only the
        // middle stop. Both ends are now derived from the accent: a lightened version at the top
        // and a darkened one at the bottom, which keeps the depth the gradient was for.
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1.1, 1.2),
        };
        gradient.GradientStops.Add(new GradientStop(Lighten(accent.Color, 0.35f), 0));
        gradient.GradientStops.Add(new GradientStop(accent.Color, 0.55));
        gradient.GradientStops.Add(new GradientStop(Darken(accent.Color, 0.55f), 1));
        gradient.Freeze();
        Set("Liquid.AccentGradientBrush", gradient);

        // Corner radii of the liquid language.
        Set("Liquid.RadiusSmall", new CornerRadius(6));
        Set("Liquid.RadiusMedium", new CornerRadius(10));
        Set("Liquid.RadiusLarge", new CornerRadius(16));

        // Typography scale (minimal, tight).
        Set("Liquid.FontFamily", new FontFamily("Segoe UI Variable Display, Segoe UI"));
        Set("Liquid.FontSizeCaption", 12.0);
        Set("Liquid.FontSizeBody", 14.0);
        Set("Liquid.FontSizeSubtitle", 18.0);
        Set("Liquid.FontSizeTitle", 26.0);
        Set("Liquid.FontSizeDisplay", 34.0);

        Set("Liquid.IsDark", isDark);

        ApplyBlockTokens(resources, isDark);
    }

    /// <summary>
    /// The tokens the Visual block editor draws with: the canvas it sits on, and one set of brushes per
    /// palette category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generated from <see cref="BlockCatalog.Categories"/> rather than written out, for the same reason
    /// the catalogue itself is data: eleven categories times four brushes is forty-four lines of colour
    /// that a new category would silently not have, and a block whose category has no token draws with
    /// no fill and reads as an empty row. Here a category cannot exist without its colours.
    /// </para>
    /// <para>
    /// The fills are the category hue from Part 7.2, pulled down in dark mode and lightened in light
    /// mode, and the ink on top is chosen to clear contrast against whichever of those is in use. That
    /// is the one number here that is not the design's: a hue that reads white-on-blue in light mode
    /// reads blue-on-white in dark mode if the fill is not also inverted, and Part 9.3's "a light-mode
    /// variant" is exactly this pair.
    /// </para>
    /// </remarks>
    private static void ApplyBlockTokens(ResourceDictionary resources, bool isDark)
    {
        void Set(string key, object value) => resources[key] = value;

        var canvas = isDark ? Color.FromRgb(0x0E, 0x0F, 0x14) : Color.FromRgb(0xDD, 0xE1, 0xEA);
        var grid = isDark ? Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0x1A, 0x1D, 0x26);

        Set("Liquid.BlockCanvasBrush", new SolidColorBrush(canvas));

        var dot = new GeometryDrawing
        {
            Geometry = new EllipseGeometry(new System.Windows.Point(1, 1), 1, 1),
            Brush = new SolidColorBrush(grid),
        };
        dot.Freeze();
        var gridTile = new DrawingBrush(dot)
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, 24, 24),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        gridTile.Freeze();
        Set("Liquid.BlockGridBrush", gridTile);

        Set("Liquid.BlockSheenBrush", new SolidColorBrush(
            Color.FromArgb(isDark ? (byte)0x26 : (byte)0x66, 0xFF, 0xFF, 0xFF)));

        Set("Liquid.BlockSlotBrush", new SolidColorBrush(
            isDark ? Color.FromRgb(0x12, 0x14, 0x1A) : Color.FromRgb(0xFF, 0xFF, 0xFF)));
        Set("Liquid.BlockSlotStrokeBrush", new SolidColorBrush(
            isDark ? Color.FromRgb(0x3C, 0x42, 0x50) : Color.FromRgb(0xA9, 0xB2, 0xC2)));
        Set("Liquid.BlockMenuBrush", new SolidColorBrush(
            isDark ? Color.FromArgb(0x99, 0x0A, 0x0B, 0x10) : Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)));
        Set("Liquid.BlockRailBrush", new SolidColorBrush(
            isDark ? Color.FromRgb(0x16, 0x18, 0x1E) : Color.FromRgb(0xF4, 0xF6, 0xF9)));

        foreach (var category in Core.Visual.BlockCatalog.Categories)
        {
            var hue = ParseHue(category.Hue);
            var top = isDark ? Darken(hue, 0.30f) : Lighten(hue, 0.52f);
            var bottom = isDark ? Darken(hue, 0.48f) : Lighten(hue, 0.28f);

            var fill = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0.15, 1),
            };
            fill.GradientStops.Add(new GradientStop(top, 0));
            fill.GradientStops.Add(new GradientStop(bottom, 1));
            fill.Freeze();

            var suffix = category.Category.ToString();
            Set($"Liquid.BlockFill.{suffix}", fill);
            Set($"Liquid.BlockStroke.{suffix}", new SolidColorBrush(Darken(hue, isDark ? 0.58f : 0.06f)));
            Set($"Liquid.BlockInk.{suffix}", new SolidColorBrush(
                isDark ? Color.FromRgb(0xF7, 0xF9, 0xFC) : Color.FromRgb(0x14, 0x16, 0x1C)));
        }
    }

    /// <summary>Reads a catalogue hue, which is written as <c>#RRGGBB</c>.</summary>
    private static Color ParseHue(string hue) =>
        ColorConverter.ConvertFromString(hue) is Color color ? color : Colors.Gray;

    /// <summary>Moves a colour towards white by <paramref name="amount"/>.</summary>
    internal static Color Lighten(Color color, float amount) => Color.FromRgb(
        Blend(color.R, 255, amount),
        Blend(color.G, 255, amount),
        Blend(color.B, 255, amount));

    /// <summary>Moves a colour towards black by <paramref name="amount"/>.</summary>
    internal static Color Darken(Color color, float amount) => Color.FromRgb(
        Blend(color.R, 0, amount),
        Blend(color.G, 0, amount),
        Blend(color.B, 0, amount));

    private static byte Blend(byte from, byte to, float amount) =>
        (byte)Math.Clamp(from + ((to - from) * amount), 0, 255);
}

/// <summary>A named colour with a frozen brush, ready to bind.</summary>
/// <remarks>
/// The brush used to be a public settable property that every page filled in for itself, so the
/// same <see cref="NamedColor"/> instance was mutated by whichever page loaded last and a frozen
/// brush could be assigned twice. It is built once here, frozen, and read-only - there is nothing
/// for a page to change.
/// </remarks>
public sealed class NamedColor
{
    public NamedColor(string name, Color color)
    {
        Name = name;
        Color = color;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        ColorBrush = brush;
    }

    public string Name { get; }
    public Color Color { get; }

    /// <summary>A frozen brush, ready to bind. Never null and never replaced.</summary>
    public SolidColorBrush ColorBrush { get; }
}
