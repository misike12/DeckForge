using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class BlockActionPage : Page, IRefreshOnNavigate
{
    private readonly BlockActionViewModel _vm;

    public BlockActionPage(BlockActionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => _vm.RefreshOnNavigate();

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/actions");

    private void OpenNotifyDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/logging");
}
