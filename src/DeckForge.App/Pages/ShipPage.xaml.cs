using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class ShipPage : Page
{
    public ShipPage(ShipViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::cli/pack");

    private void OpenSigningDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::cli/signing");
}
