using System.Windows;
using System.Windows.Controls;
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

/// <summary>Which way a non-click gesture wants the selection changed.</summary>
public enum BlockSelectionMode
{
    /// <summary>These blocks are now the selection, and nothing else is.</summary>
    Only,

    /// <summary>Add this block to what is already selected.</summary>
    Add,

    /// <summary>Take this block out of what is selected.</summary>
    Remove,
}

/// <summary>
/// Something that is not a click wants the selection changed: which tile, which block, and which way.
/// </summary>
/// <remarks>
/// Its own type for the same reason <see cref="BlockSelectedEventArgs"/> has one — WPF builds a routed
/// event's route from <c>Source</c>, and the block has to travel in the arguments because the object that
/// holds it is not in the visual tree. The mode rides along rather than being three events because the three
/// differ by one flag and the flag is the whole message.
/// </remarks>
public sealed class BlockSelectionModeEventArgs(
    RoutedEvent routedEvent,
    object source,
    BlockNodeViewModel block,
    BlockSelectionMode mode)
    : RoutedEventArgs(routedEvent, source)
{
    /// <summary>The block the gesture was about.</summary>
    public BlockNodeViewModel Block { get; } = block;

    /// <summary>Which way the selection should change.</summary>
    public BlockSelectionMode Mode { get; } = mode;
}

/// <summary>A tile's context menu is opening: which tile, which block, and the menu to fill in.</summary>
/// <remarks>
/// The menu travels in the arguments because it is a fresh object per tile and the only thing that knows
/// which one is opening is the tile. A handler that had to go and find it would be guessing.
/// </remarks>
public sealed class BlockMenuRequestedEventArgs(
    RoutedEvent routedEvent,
    object source,
    BlockNodeViewModel block,
    ContextMenu menu)
    : RoutedEventArgs(routedEvent, source)
{
    /// <summary>The block whose menu is opening.</summary>
    public BlockNodeViewModel Block { get; } = block;

    /// <summary>The menu to fill in. Replacing its items is enough; nothing else has to be done to it.</summary>
    public ContextMenu Menu { get; } = menu;
}
