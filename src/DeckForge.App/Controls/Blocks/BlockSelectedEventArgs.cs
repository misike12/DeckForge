using System.Windows;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// A tile was clicked: which tile, and which block it is drawing.
/// </summary>
/// <remarks>
/// Its own type because <see cref="RoutedEventArgs"/> has nowhere to put the block, and the two
/// alternatives both fail quietly. Putting it in <c>Source</c> breaks the event route, because WPF walks
/// the visual tree from <c>Source</c> and a view model is not in it; putting it in a property on the
/// tile means every handler has to walk back down to find the sender, which is what the sender is for.
/// </remarks>
public sealed class BlockSelectedEventArgs : RoutedEventArgs
{
    public BlockSelectedEventArgs(RoutedEvent routedEvent, object source, BlockNodeViewModel block)
        : base(routedEvent, source) => Block = block;

    /// <summary>The block that was clicked.</summary>
    public BlockNodeViewModel Block { get; }
}
