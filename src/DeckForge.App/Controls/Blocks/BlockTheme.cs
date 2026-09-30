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
}