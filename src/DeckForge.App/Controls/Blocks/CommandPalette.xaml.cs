using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual.Commands;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The palette's window-less overlay: a search box, a ranked list, and the keys that dismiss it.
/// </summary>
/// <remarks>
/// <para>
/// It handles its own keys and raises one event for "this command was chosen", because the commands it
/// offers belong to the page and a control cannot reach the page's view model from inside its own
/// template — the same reason the tile's selection is a routed event.
/// </para>
/// <para>
/// Escape closes and Enter runs, and both are marked handled so they never reach the canvas underneath:
/// Part 18.3's third rule is that a modal swallows canvas keys entirely, and the cheapest way to keep
/// that true is for the modal to be the last thing to see them.
/// </para>
/// </remarks>
public partial class CommandPalette : UserControl
{
    /// <summary>Raised when the user chooses a command.</summary>
    public static readonly RoutedEvent CommandChosenEvent = EventManager.RegisterRoutedEvent(
        "CommandChosen",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(CommandPalette));

    /// <summary>Raised when the user dismisses the palette without choosing.</summary>
    public static readonly RoutedEvent DismissedEvent = EventManager.RegisterRoutedEvent(
        "Dismissed",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(CommandPalette));

    /// <summary>Raised when the user chooses a command.</summary>
    public event RoutedEventHandler? CommandChosen
    {
        add => AddHandler(CommandChosenEvent, value);
        remove => RemoveHandler(CommandChosenEvent, value);
    }

    /// <summary>Raised when the user dismisses the palette.</summary>
    public event RoutedEventHandler? Dismissed
    {
        add => AddHandler(DismissedEvent, value);
        remove => RemoveHandler(DismissedEvent, value);
    }

    public CommandPalette()
    {
        InitializeComponent();
        DataContext = new CommandPaletteViewModel();
    }

    /// <summary>Empties the box and takes the keyboard, which is what "opened" has to mean.</summary>
    public void Open()
    {
        if (DataContext is CommandPaletteViewModel model)
        {
            model.Query = string.Empty;
        }

        Search.Text = string.Empty;
        Search.Focus();
        Search.CaretIndex = 0;
    }

    /// <summary>
    /// Escape and Enter, and the arrows between them.
    /// </summary>
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                RaiseEvent(new RoutedEventArgs(DismissedEvent, this));
                e.Handled = true;
                return;

            case Key.Enter:
                if (model.Selected is { } chosen)
                {
                    RaiseEvent(new CommandChosenEventArgs(CommandChosenEvent, this, chosen));
                }

                e.Handled = true;
                return;

            case Key.Down:
                Move(model, 1);
                e.Handled = true;
                return;

            case Key.Up:
                Move(model, -1);
                e.Handled = true;
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Moves the selection, wrapping.
    /// </summary>
    /// <remarks>
    /// Wrapping because the list is short enough to see all of: a palette that stops at the ends turns a
    /// two-key search into a hunt, and the user can see there is nothing below.
    /// </remarks>
    private static void Move(CommandPaletteViewModel model, int by)
    {
        if (model.Results.Count == 0)
        {
            return;
        }

        var at = model.Selected is null ? -1 : model.Results.IndexOf(model.Selected);
        var next = (at + by + model.Results.Count) % model.Results.Count;
        model.Selected = model.Results[next];
    }
}

/// <summary>The command the user chose.</summary>
public sealed class CommandChosenEventArgs(RoutedEvent routedEvent, object source, VisualCommand command)
    : RoutedEventArgs(routedEvent, source)
{
    /// <summary>The command.</summary>
    public VisualCommand Command { get; } = command;
}