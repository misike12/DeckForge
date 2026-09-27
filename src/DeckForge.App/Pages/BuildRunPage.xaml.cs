using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class BuildRunPage : Page
{
    public BuildRunPage(BuildRunViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::features/testing");
}
