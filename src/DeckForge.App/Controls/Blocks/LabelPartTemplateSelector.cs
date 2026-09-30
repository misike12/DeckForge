using System.Windows;
using System.Windows.Controls;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// Picks the template for one piece of a block's label.
/// </summary>
/// <remarks>
/// Three cases — words, a hole, a dropdown — and one selector, because Part 9.4 asks for the label's
/// pieces to sit inline in reading order rather than in three panels, and three <c>ItemsControl</c>s
/// would need the tile's width to be the sum of three measured rows. A selector also means a fourth
/// case added later cannot silently render as blank text.
/// </remarks>
public sealed class LabelPartTemplateSelector : DataTemplateSelector
{
    /// <summary>The template a run of words is drawn with.</summary>
    public DataTemplate? Words { get; set; }

    /// <summary>The template a slot is drawn with.</summary>
    public DataTemplate? Slot { get; set; }

    /// <summary>The template a menu is drawn with.</summary>
    public DataTemplate? Menu { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item is not LabelPartViewModel part
            ? Words
            : part.Kind switch
            {
                Core.Visual.BlockLabelRunKind.Slot => Slot,
                Core.Visual.BlockLabelRunKind.Menu => Menu,
                _ => Words,
            };
    }
}