using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class CapabilitiesPage : Page
{
    public CapabilitiesPage(CapabilityGalleryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Docs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CapabilityCard card })
        {
            // The view model already navigates the embedded browser; this used to start the
            // system browser instead, which bypassed the offline snapshot and the in-app
            // navigation every other page's docs button uses.
            var path = card.Descriptor.DocsPath;
            ShellMessenger.NavigateTo(string.IsNullOrWhiteSpace(path) ? "docs" : $"docs::{path}");
        }
    }

    private void Scaffold_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: CapabilityCard card }
            && DataContext is CapabilityGalleryViewModel vm)
        {
            vm.ScaffoldCommand.Execute(card);
        }
    }
}
