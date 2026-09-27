using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeckForge.App.Services;
using DeckForge.App.Themes;
using Wpf.Ui.Appearance;

namespace DeckForge.App.Pages;

public partial class SettingsPage : Page
{
    private readonly SettingsService _settings;
    private bool _ready;

    public SettingsPage(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;

        foreach (var accent in LiquidTheme.Accents)
        {
            accent.ColorBrush = new SolidColorBrush(accent.Color);
            accent.ColorBrush.Freeze();
        }
        AccentList.ItemsSource = LiquidTheme.Accents;

        Loaded += (_, _) =>
        {
            if (_ready)
            {
                return;
            }
            _ready = true;
            var theme = _settings.Settings.Theme;
            ThemeSystem.IsChecked = theme == AppTheme.System;
            ThemeLight.IsChecked = theme == AppTheme.Light;
            ThemeDark.IsChecked = theme == AppTheme.Dark;
            AccentName.Text = _settings.Settings.Accent;
            ResolvedTheme.Text = theme == AppTheme.System
                ? $"System (currently {(ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light ? "light" : "dark")})"
                : theme.ToString();
        };
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        // Radios raise Checked while the XAML tree is built; ignore until ready.
        if (!_ready || sender is not RadioButton { Tag: string tag })
        {
            return;
        }
        _settings.Settings.Theme = tag switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.System,
        };
        _settings.Save();
        ResolvedTheme.Text = tag == "System" ? "System" : tag;
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }
        if (sender is Button { DataContext: NamedColor accent })
        {
            _settings.Settings.Accent = accent.Name;
            _settings.Save();
            AccentName.Text = accent.Name;
        }
    }

    private void Docs_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://docs.macro-deck.app/") { UseShellExecute = true });

    private void Site_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://macro-deck.app/") { UseShellExecute = true });

    private void CrashLog_Click(object sender, RoutedEventArgs e)
    {
        var dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "DeckForge");
        System.IO.Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatus.Text = "Checking for updates...";
        try
        {
            var updateUrl = _settings.Settings.VelopackUpdateUrl;
            if (string.IsNullOrWhiteSpace(updateUrl))
            {
                UpdateStatus.Text = "No update source configured. Set VelopackUpdateUrl in %LOCALAPPDATA%/DeckForge/settings.json (e.g. a GitHub releases URL or a folder). A dev run (dotnet run) is never updated in place.";
                return;
            }

            var manager = new Velopack.UpdateManager(new Velopack.Sources.SimpleWebSource(updateUrl));
            if (!manager.IsInstalled)
            {
                UpdateStatus.Text = "This copy is not a Velopack install (dev run or unpackaged) - updates apply only to the installed build.";
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                UpdateStatus.Text = $"You are up to date (v{manager.CurrentVersion}).";
                return;
            }

            UpdateStatus.Text = $"Downloading update {update.TargetFullRelease.Version}...";
            await manager.DownloadUpdatesAsync(update);
            UpdateStatus.Text = $"Update {update.TargetFullRelease.Version} downloaded - it applies at the next restart.";
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = $"Update check failed: {ex.Message}";
        }
    }
}
