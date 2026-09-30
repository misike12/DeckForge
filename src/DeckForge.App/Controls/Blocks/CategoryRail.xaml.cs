using System.Windows.Controls;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The palette rail: one row per category, drawn from the catalogue's own descriptors.
/// </summary>
/// <remarks>
/// Nothing here knows what a category is. The rows come from <c>BlockCatalog.Categories</c>, each with the
/// glyph, the hue and the one-line summary the catalogue already holds, so adding a category to the
/// catalogue adds a row here without this file changing — which is what keeps the rail from becoming the
/// twelfth place a block list has to be edited.
/// </remarks>
public partial class CategoryRail : UserControl
{
    public CategoryRail() => InitializeComponent();
}