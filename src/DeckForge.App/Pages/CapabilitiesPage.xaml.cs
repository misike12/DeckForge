using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
        if (sender is System.Windows.Controls.Button { DataContext: CapabilityCard card })
        {
            Process.Start(new ProcessStartInfo(card.DocsUrl) { UseShellExecute = true });
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
