using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;
using DeckForge.Core.Plugins;

namespace DeckForge.App.Pages;

public partial class ManifestPage : Page
{
    private readonly ManifestStudioViewModel _vm;

    public ManifestPage(ManifestStudioViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _vm.LoadFromWorkspace();
    }

    /// <summary>Called by the shell on navigation so the editor follows the open workspace.</summary>
    public void RefreshOnNavigate() => _vm.RefreshOnNavigate();

    private void GoHome_Click(object sender, RoutedEventArgs e) => ShellMessenger.NavigateTo("home");

    /// <summary>
    /// A permission checkbox was clicked by the user.
    /// </summary>
    /// <remarks>
    /// This used to be Checked plus Unchecked handlers on a one-way IsChecked binding. Checked and
    /// Unchecked also fire when the *program* sets IsChecked, so every load toggled every
    /// permission once - and because the toggle writes back to the manifest and re-renders, the
    /// per-group counter drifted upwards by one on every visit. Click fires only on real user
    /// interaction, which is exactly the distinction that was missing.
    /// </remarks>
    private void Permission_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PermissionRow row, IsChecked: var isChecked })
        {
            row.SetEnabled(isChecked == true);
        }
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::reference/manifest");
}
