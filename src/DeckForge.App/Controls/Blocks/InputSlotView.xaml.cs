using System.Windows;
using System.Windows.Controls;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// One hole in a block's label: either a field with a value, or a nested reporter tile.
/// </summary>
/// <remarks>
/// <para>
/// The two states are one control rather than two because they occupy the same space in the label and
/// swapping between them must not move anything around them. Part 9.4 calls for exactly this — "either a
/// static literal field or a host for a nested reporter tile" — and Part 5.3's "a literal is a block
/// node like any other" is why there is one model behind both: the model says which, and the markup does
/// not second-guess it.
/// </para>
/// <para>
/// Phase 3 draws the field but does not let anything be typed into it. Editing arrives in Phase 5 with
/// undo, and an editable field that mutates the document before then loses work the moment a user
/// types and navigates away.
/// </para>
/// </remarks>
public partial class InputSlotView : UserControl
{
    public InputSlotView()
    {
        InitializeComponent();

        // The DataContext is set from markup, on the Grid inside, rather than here. Assigning it from a
        // property-changed callback works right up until the element is connected to a tree, when data
        // context inheritance runs and puts the label piece back - and a hole that has silently become a
        // piece of a label renders as an empty pill with nothing in it.
    }

    /// <summary>The slot, set from a parent that binds it as a property rather than a DataContext.</summary>
    public SlotViewModel? Data
    {
        get => (SlotViewModel?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(SlotViewModel),
            typeof(InputSlotView),
            new PropertyMetadata(null, OnDataChanged));

    private static void OnDataChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is InputSlotView view)
        {
            view.ToolTip = (args.NewValue as SlotViewModel)?.ToolTipText;
        }
    }
}