using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class PublishPage : Page, IRefreshOnNavigate
{
    private readonly PublishViewModel _vm;

    public PublishPage(PublishViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Shell hook: refresh the Store Gate when the workspace changes.</summary>
    public void RefreshOnNavigate() => _vm.Load();

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::guides/publishing");
}
