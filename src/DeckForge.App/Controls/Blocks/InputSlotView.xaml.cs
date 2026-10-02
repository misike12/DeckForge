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

        // Preview, so the hole's click is heard before the enclosing tile's: a click on a hole is also a
        // click on its block, and Part 9.6 wants the inspector to end up showing *the slot*, not just the
        // block. Marking the event handled stops the tile from then claiming it as a plain block click.
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
    {
        if (Data is { } slot)
        {
            args.Handled = true;
            RaiseEvent(new SlotClickedEventArgs(SlotClickedEvent, this, slot));
        }
    }

    /// <summary>
    /// Raised when the user clicks the hole itself rather than dragging a block into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tile's own click already selects a block, and Part 9.6 says selecting a block on the canvas
    /// selects it in the inspector and vice versa. So a click on a hole has to reach the inspector too,
    /// and the only element that knows which slot was clicked is this one — the bubbling event's
    /// <c>Source</c> would be the inner <c>TextBlock</c> or <c>Border</c>, which knows nothing about slots.
    /// </para>
    /// <para>
    /// It carries the slot in its own argument type for the same reason <c>BlockSelectedEventArgs</c>
    /// does: the route is built from <c>Source</c>, and a view model there is not in the tree at all.
    /// </para>
    /// </remarks>
    public static readonly RoutedEvent SlotClickedEvent = EventManager.RegisterRoutedEvent(
        "SlotClicked",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(InputSlotView));

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
