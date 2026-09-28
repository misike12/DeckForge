using System.Windows.Controls;
using DeckForge.App.ViewModels;

namespace DeckForge.App.Pages;

public partial class ExtensionsPage : Page, IRefreshOnNavigate
{
    private readonly ExtensionsViewModel _vm;

    public ExtensionsPage(ExtensionsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Called by the shell on navigation, so the list follows a rescan or a toggle.</summary>
    public void RefreshOnNavigate() => _vm.RefreshOnNavigate();
}
