using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class EventsEditorPage : Page, IRefreshOnNavigate
{
    private readonly EventsEditorViewModel _vm;

    public EventsEditorPage(EventsEditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public void RefreshOnNavigate() => _vm.RefreshOnNavigate();

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/events");
}
