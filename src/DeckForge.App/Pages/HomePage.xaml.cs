using System.Windows;
using System.Windows.Controls;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class HomePage : Page, IRefreshOnNavigate
{
    private readonly WorkspaceViewModel _workspace;

    public HomePage(WorkspaceViewModel workspace)
    {
        InitializeComponent();
        _workspace = workspace;

        // One DataContext. This page used to hold two view models and bind its whole tree to the
        // second, so every binding to MainViewModel - including the environment checklist -
        // resolved to nothing and the section rendered empty.
        DataContext = workspace;
    }

    /// <summary>Re-runs the environment checks, so a machine fixed while DeckForge was open recovers.</summary>
    public void RefreshOnNavigate() =>
        _ = _workspace.RefreshEnvironmentAsync(CancellationToken.None);

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string path })
        {
            _workspace.OpenRecent(path);
        }
    }
}
