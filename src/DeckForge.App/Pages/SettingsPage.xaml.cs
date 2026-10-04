using System.Windows;
using System.Windows.Controls;
using DeckForge.App.Services;
using DeckForge.App.Themes;
using DeckForge.App.ViewModels;
using DeckForge.Core.Settings;

namespace DeckForge.App.Pages;

public partial class SettingsPage : Page, IRefreshOnNavigate
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Re-reads from disk, so a change made elsewhere is not overwritten by this form.</summary>
    public void RefreshOnNavigate() => _vm.Load();

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NamedColor accent })
        {
            _vm.Accent = accent.Name;
        }
    }

    private void Docs_Click(object sender, RoutedEventArgs e) =>
        Open("https://docs.macro-deck.app/");

    private void Site_Click(object sender, RoutedEventArgs e) =>
        Open("https://macro-deck.app/");

    private void SettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = System.IO.Path.GetDirectoryName(AppSettings.StorePath)!;
        System.IO.Directory.CreateDirectory(directory);
        Open(directory);
    }

    private static void Open(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            // No browser, or a locked-down shell. A silent no-op here would look like the button is
            // broken, so it is at least worth not throwing.
            System.Diagnostics.Debug.WriteLine($"Could not open {target}: {ex.Message}");
        }
    }
}
