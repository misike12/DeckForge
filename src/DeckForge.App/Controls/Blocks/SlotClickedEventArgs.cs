using System.Windows;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// A click on one value slot, carrying the slot rather than the block.
/// </summary>
/// <remarks>
/// Separate from <see cref="BlockSelectedEventArgs"/> because the two mean different things and the
/// inspector does different work for each: selecting a block shows what it is, and selecting a slot shows
/// what can go in it. A single argument type would have to pick one, and the slot would lose the thing
/// that makes a slot clickable in the first place.
/// </remarks>
public sealed class SlotClickedEventArgs : RoutedEventArgs
{
    public SlotClickedEventArgs(RoutedEvent routedEvent, object source, SlotViewModel slot)
        : base(routedEvent, source)
    {
        Slot = slot;
    }

    /// <summary>The hole that was clicked.</summary>
    public SlotViewModel Slot { get; }

    /// <summary>The block that owns it.</summary>
    public BlockNodeViewModel Block => Slot.Owner;
}
