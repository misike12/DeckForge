using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class ManifestPage : Page
{
    private readonly ManifestStudioViewModel _vm;

    public ManifestPage(ManifestStudioViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => vm.LoadFromWorkspace();
    }

    /// <summary>Called by the shell on navigation so the editor follows the open workspace.</summary>
    public void RefreshOnNavigate() => _vm.LoadFromWorkspace();

    private void GoHome_Click(object sender, RoutedEventArgs e) => ShellMessenger.NavigateTo("home");

    private void Permission_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PermissionRow row })
        {
            row.SetEnabled(true);
        }
    }

    private void Permission_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PermissionRow row })
        {
            row.SetEnabled(false);
        }
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::reference/manifest");
}
