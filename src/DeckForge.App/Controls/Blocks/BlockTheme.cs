using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The theme resource keys one block category draws with.
/// </summary>
/// <remarks>
/// <para>
/// The categories' colours live in <c>LiquidTheme</c>, generated from the catalogue's hues, and a tile
/// needs three of them: a fill, a border tint and the ink on top. A binding cannot name a resource by a
/// computed key, so the view model carries the key <em>strings</em> and
/// <c>ResourceKeyToBrushConverter</c> resolves them — which is what keeps Part 9.3's "no hard-coded
/// colours" true for a block whose colour comes from data.
/// </para>
/// <para>
/// The suffix is the enum member's name rather than the display name, because the display name is
/// localized (Part 21) and a key that changed with the language would make every saved theme
/// meaningless.
/// </para>
/// </remarks>
public static class BlockTheme
{
    /// <summary>The fill key for a category.</summary>
    public static string FillKey(BlockCategory category) => $"Liquid.BlockFill.{category}";

    /// <summary>The border key for a category.</summary>
    public static string StrokeKey(BlockCategory category) => $"Liquid.BlockStroke.{category}";

    /// <summary>The ink key for a category.</summary>
    public static string InkKey(BlockCategory category) => $"Liquid.BlockInk.{category}";

    /// <summary>Whether the theme has a set of brushes for this category.</summary>
    /// <remarks>
    /// <c>BlockCategory.Media</c> has no blocks and no descriptor, so it has no tokens either. The check
    /// exists so a document containing a Media block from a build that had some draws in the warning
    /// colour instead of nothing at all.
    /// </remarks>
    public static bool IsKnown(BlockCategory category) =>
        BlockCatalog.Categories.Any(descriptor => descriptor.Category == category);

    /// <summary>
    /// The contrast the application is drawing at.
    /// </summary>
    /// <remarks>
    /// Asked of the *system* rather than of a settings page, because §9.8 says "when the system asks" and a
    /// user who has set the OS to high contrast has already answered the question. It is read once and
    /// cached, because it changes only when the user restarts the application in practice, and a theme
    /// property that raises a notification per tile would re-render the whole canvas to say nothing new.
    /// </remarks>
    public static ContrastLevel Contrast { get; private set; } = ReadSystemContrast();

    /// <summary>
    /// Re-reads the system's contrast preference.
    /// </summary>
    /// <remarks>
    /// Called once at startup and available for a settings control to call. <see cref="SystemParameters.HighContrast"/>
    /// is the WPF answer to "the system asks"; the extra monochrome step is ours, because a user who needs
    /// more than a stronger outline needs the fills gone, and no system setting offers exactly that.
    /// </remarks>
    public static ContrastLevel ReadSystemContrast()
    {
        if (MonochromeRequested())
        {
            return ContrastLevel.Monochrome;
        }

        return System.Windows.SystemParameters.HighContrast ? ContrastLevel.Strong : ContrastLevel.Standard;
    }

    /// <summary>
    /// Whether the user has asked for no fills at all.
    /// </summary>
    /// <remarks>
    /// Not a system setting, because none exists. It is an environment variable so that it can be set for a
    /// screenshot run, a bug report and an automated check without a user having to find a hidden switch —
    /// and so that "what does the monochrome canvas look like" has an answer that is not "change the OS".
    /// </remarks>
    private static bool MonochromeRequested() =>
        Environment.GetEnvironmentVariable("DECKFORGE_BLOCK_MONOCHROME") is "1" or "true";

    /// <summary>How thick a block's outline is drawn at this contrast.</summary>
    public static double StrokeThicknessFor(BlockCategory category) =>
        BlockContrast.StrokeWidthFor(Contrast);
}