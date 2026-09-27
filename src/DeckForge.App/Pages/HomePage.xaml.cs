using System.Windows;
using System.Windows.Controls;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class HomePage : Page
{
    private readonly MainViewModel _main;
    private readonly WorkspaceViewModel _workspace;

    public HomePage(MainViewModel main, WorkspaceViewModel workspace)
    {
        InitializeComponent();
        _main = main;
        _workspace = workspace;
        DataContext = workspace;

        Loaded += async (_, _) =>
        {
            if (_main.EnvironmentChecks.Count == 0)
            {
                await _main.RefreshEnvironmentCommand.ExecuteAsync(null);
            }
        };
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: string path })
        {
            _workspace.OpenRecent(path);
        }
    }
}
