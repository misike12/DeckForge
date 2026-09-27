using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class ExplorerPage : Page
{
    private readonly ExplorerViewModel _vm;

    public ExplorerPage(ExplorerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => vm.Load();
    }

    /// <summary>Shell hook: reload when workspace changes or on navigation.</summary>
    public void RefreshOnNavigate() => _vm.Load();

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileNode { IsDirectory: false } file)
        {
            try
            {
                Process.Start(new ProcessStartInfo(file.FullPath) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // no default app; ignore
            }
        }
    }
}
