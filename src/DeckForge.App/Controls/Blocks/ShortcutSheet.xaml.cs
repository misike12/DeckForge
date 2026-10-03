using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The shortcut sheet as a control: Core's table, grouped and read-only, with one way out.
/// </summary>
/// <remarks>
/// A user control rather than a page, for the reason the palette is one: the sheet is documentation
/// *about* the Visual page and belongs over it, not somewhere else in the sidebar where a user has to
/// navigate away from the thing they are trying to learn the keys of.
/// </remarks>
public partial class ShortcutSheet : UserControl
{
    /// <summary>Raised when the sheet closes itself.</summary>
    public static readonly RoutedEvent ClosedEvent = EventManager.RegisterRoutedEvent(
        "Closed",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(ShortcutSheet));

    /// <summary>Raised when the sheet closes itself.</summary>
    public event RoutedEventHandler? Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    public ShortcutSheet()
    {
        InitializeComponent();
        DataContext = new ShortcutSheetViewModel();
    }

    /// <summary>
    /// Escape as well as the button.
    /// </summary>
    /// <remarks>
    /// Because a sheet with no keyboard exit is a sheet a keyboard user has to hunt for a mouse to
    /// dismiss — which is the one thing a keyboard user cannot do.
    /// </remarks>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key != Key.Escape)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(ClosedEvent, this));
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(ClosedEvent, this));
}