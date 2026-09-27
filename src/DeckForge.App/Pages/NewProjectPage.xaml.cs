using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.ViewModels;
using Microsoft.Win32;

namespace DeckForge.App.Pages;

public partial class NewProjectPage : Page
{
    public NewProjectPage(NewProjectViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        ShellMessenger.NavigateTo("docs::introduction/quickstart");

    private void PluginId_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewProjectViewModel vm && !string.IsNullOrWhiteSpace(vm.PluginId))
        {
            vm.TouchPluginIdCommand.Execute(null);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewProjectViewModel vm)
        {
            return;
        }
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to create the plugin",
        };
        if (dialog.ShowDialog() == true && DataContext is NewProjectViewModel viewModel)
        {
            viewModel.ParentDirectory = dialog.FolderName;
        }
    }
}
