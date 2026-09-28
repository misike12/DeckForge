using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.App.Themes;
using DeckForge.Core.Plugins;

namespace DeckForge.App.ViewModels;

/// <summary>
/// Backs the Settings page.
/// </summary>
/// <remarks>
/// The page's own code-behind used to hold the field values in named XAML elements and write the
/// settings from each event handler, so there was no one place that knew what a setting was, and
/// the page could not be reasoned about or tested. Two of the nine settings had no UI at all and
/// so could never be changed by a user - they existed only in the JSON.
/// </remarks>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly UpdateCheckService _updates;

    /// <summary>True while the form is being filled from disk, so change handlers stay quiet.</summary>
    private bool _loading;

    public SettingsViewModel(SettingsService settings, UpdateCheckService updates)
    {
        _settings = settings;
        _updates = updates;

        Services.ShellMessenger.UpdateAvailable += result =>
        {
            UpdateStatus = result.Message;
            UpdateAvailable = result.Available;
        };

        Load();
    }

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private string _accent = "";

    [ObservableProperty]
    private string _defaultProjectsDirectory = "";

    [ObservableProperty]
    private bool _checkForUpdatesOnStart = true;

    // Named so the generated property is GitHubAccount: the toolkit uppercases only the first
    // character, so _githubAccount would have produced GithubAccount and every binding to it
    // would have failed silently.
    [ObservableProperty]
    private string _gitHubAccount = "";

    [ObservableProperty]
    private string _macroDeckCliVersion = "";

    [ObservableProperty]
    private string _velopackUpdateUrl = "";

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    private bool _updateAvailable;

    /// <summary>What the theme setting resolves to right now.</summary>
    public string ResolvedTheme => Theme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        _ => $"System (currently {(IsSystemLight() ? "light" : "dark")})",
    };

    private static bool IsSystemLight()
    {
        try
        {
            return Wpf.Ui.Appearance.ApplicationThemeManager.GetSystemTheme()
                == Wpf.Ui.Appearance.SystemTheme.Light;
        }
        catch (Exception)
        {
            // Reading the system theme can fail in a session with no shell. Dark is the documented
            // default, so a failure is not worth surfacing.
            return false;
        }
    }

    /// <summary>Accent swatches, with a frozen brush so the gallery does not mutate global state.</summary>
    public IReadOnlyList<NamedColor> Accents { get; } = LiquidTheme.Accents;

    public ObservableCollection<string> RecentWorkspaces { get; } = [];

    public void Load()
    {
        _loading = true;
        try
        {
            var settings = _settings.Settings;
            Theme = settings.Theme;
            Accent = settings.Accent;
            DefaultProjectsDirectory = settings.DefaultProjectsDirectory ?? "";
            CheckForUpdatesOnStart = settings.CheckForUpdatesOnStart;
            GitHubAccount = settings.GitHubAccount ?? "";
            MacroDeckCliVersion = settings.MacroDeckCliVersion ?? "";
            VelopackUpdateUrl = settings.VelopackUpdateUrl ?? "";

            RecentWorkspaces.Clear();
            foreach (var recent in settings.RecentWorkspaces.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                RecentWorkspaces.Add(recent);
            }
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(Accents));
        OnPropertyChanged(nameof(ResolvedTheme));
    }

    partial void OnThemeChanged(AppTheme value) => Persist(s => s.Theme = value, nameof(ResolvedTheme));

    partial void OnAccentChanged(string value) => Persist(s => s.Accent = value);

    partial void OnDefaultProjectsDirectoryChanged(string value) =>
        Persist(s => s.DefaultProjectsDirectory = value);

    partial void OnCheckForUpdatesOnStartChanged(bool value) =>
        Persist(s => s.CheckForUpdatesOnStart = value);

    partial void OnGitHubAccountChanged(string value) => Persist(s => s.GitHubAccount = value);

    partial void OnMacroDeckCliVersionChanged(string value) =>
        Persist(s => s.MacroDeckCliVersion = value);

    partial void OnVelopackUpdateUrlChanged(string value) => Persist(s => s.VelopackUpdateUrl = value);

    /// <summary>Writes one setting and saves, unless the form is still being filled from disk.</summary>
    private void Persist(Action<AppSettings> change, params string[] alsoChanged)
    {
        if (_loading)
        {
            return;
        }

        _settings.Update(change);
        foreach (var property in alsoChanged)
        {
            OnPropertyChanged(property);
        }
    }

    [RelayCommand]
    private void ChooseDefaultDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where new plugin projects are created",
            InitialDirectory = Directory.Exists(DefaultProjectsDirectory)
                ? DefaultProjectsDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog() == true)
        {
            DefaultProjectsDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void ClearDefaultDirectory() => DefaultProjectsDirectory = "";

    [RelayCommand]
    private void ClearGitHubAccount() => GitHubAccount = "";

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        UpdateStatus = "Checking for updates...";
        UpdateAvailable = false;

        var result = await _updates.CheckAsync();
        UpdateStatus = result.Message;
        UpdateAvailable = result.Available;
    }

    [RelayCommand]
    private void ClearRecent(string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            _settings.RemoveRecentWorkspace(solutionPath);
            Load();
        }
    }

    [RelayCommand]
    private void ClearAllRecent()
    {
        _settings.Settings.RecentWorkspaces.Clear();
        _settings.Save();
        Load();
    }

    /// <summary>The CLI version DeckForge targets, for the Environment page's text.</summary>
    public string DefaultCliVersion => MacroDeckSdkInfo.DefaultCliVersion;

    /// <summary>
    /// The shortcuts this window actually binds.
    /// </summary>
    /// <remarks>
    /// The page used to carry a hand-maintained table in XAML. It had already drifted from the
    /// bindings - it listed seven entries while the window handled nine pages' worth of Ctrl+digit -
    /// so a shortcut that stopped working left its description in place. This list is declared
    /// beside the handler, and a test checks the two agree.
    /// </remarks>
    public IReadOnlyList<ShortcutDescription> Shortcuts { get; } =
    [
        new("Ctrl+N", "New plugin"),
        new("Ctrl+B", "Build workspace"),
        new("F5", "Run against the stub host"),
        new("Ctrl+T", "Focus terminal"),
        new("Ctrl+1..9, Ctrl+0", "Jump between pages, in sidebar order"),
        new("Ctrl+,", "Open settings"),
        new("F1", "Open docs"),
    ];
}

/// <param name="Keys">The key combination, as shown.</param>
/// <param name="Description">What it does.</param>
public sealed record ShortcutDescription(string Keys, string Description);
