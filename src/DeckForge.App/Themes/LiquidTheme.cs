using System.Windows;
using System.Windows.Media;
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
        new("Base", Color.FromRgb(0xEE, 0xF0, 0xF4)),
        new("Layer", Color.FromRgb(0xFF, 0xFF, 0xFF)),
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

        // Semantic.
        Set("Liquid.SuccessBrush", new SolidColorBrush(Color.FromRgb(0x3F, 0xB8, 0x63)));
        Set("Liquid.WarningBrush", new SolidColorBrush(Color.FromRgb(0xE2, 0x9E, 0x2D)));
        Set("Liquid.DangerBrush", new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));

        // The liquid accent gradient: accent -> deep indigo for a richer hero than a flat fill.
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1.1, 1.2),
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x36, 0x9E, 0xEA), 0));
        gradient.GradientStops.Add(new GradientStop(accent.Color, 0.55));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x2E, 0x8F), 1));
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
    }
}

/// <summary>A named color with a lazily created WPF brush for direct binding.</summary>
public sealed class NamedColor
{
    public NamedColor(string name, Color color)
    {
        Name = name;
        Color = color;
    }

    public string Name { get; }
    public Color Color { get; }

    /// <summary>Created on demand by UI code; not frozen so pages may freeze it themselves.</summary>
    public SolidColorBrush? ColorBrush { get; set; }
}
