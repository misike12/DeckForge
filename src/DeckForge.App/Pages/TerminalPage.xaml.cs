using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class TerminalPage : Page
{
    private readonly TerminalViewModel _vm;

    public TerminalPage(TerminalViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.Lines.CollectionChanged += (_, _) => ScrollToEnd();
    }

    /// <summary>Called by the shell on navigation so the cwd label follows the workspace.</summary>
    public void RefreshOnNavigate()
    {
        // TerminalViewModel listens for workspace changes itself; nothing to re-read here.
    }

    private void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                if (_vm.HistoryUp() is { } up)
                {
                    CommandInput.Text = up;
                    CommandInput.CaretIndex = CommandInput.Text.Length;
                }
                e.Handled = true;
                break;
            case Key.Down:
                if (_vm.HistoryDown() is { } down)
                {
                    CommandInput.Text = down;
                    CommandInput.CaretIndex = CommandInput.Text.Length;
                }
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Deferred auto-scroll: CollectionChanged fires synchronously per added item while the
    /// ListBox is still measuring; scrolling here re-entered layout and crashed under burst
    /// output. Waiting for Background priority lets the items panel settle first.
    /// </summary>
    private void ScrollToEnd()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (OutputList.Items.Count > 0)
            {
                OutputList.ScrollIntoView(OutputList.Items[^1]);
            }
        });
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        Services.ShellMessenger.NavigateTo("docs::cli/");
}
