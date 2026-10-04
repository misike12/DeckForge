using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.App.Themes;
using DeckForge.Core.Plugins;
using DeckForge.Core.Settings;
using Microsoft.Extensions.Logging;

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

    // ---- Part 20's sixteen keys ------------------------------------------------------------------
    //
    // One property per key, named exactly as the catalogue names it, and one line each that writes it
    // back. The alternative - a generic row bound to a dictionary of values - would have been shorter and
    // would have taken away the thing that makes a mistyped name a compile error. These are values a user
    // types, and a setting that silently writes to the wrong property is not a setting.

    /// <summary>Whether a near drop snaps to a block.</summary>
    [ObservableProperty]
    private bool _visualSnapEnabled;

    /// <summary>How near a drop has to be before it snaps, in pixels at 100% zoom.</summary>
    [ObservableProperty]
    private double _visualSnapRadius;

    /// <summary>Whether free placement lines up with a grid.</summary>
    [ObservableProperty]
    private bool _visualSnapToGrid;

    /// <summary>The spacing a grid would use.</summary>
    [ObservableProperty]
    private double _visualGridSize;

    /// <summary>The zoom a canvas opens at, as a percentage.</summary>
    [ObservableProperty]
    private double _visualDefaultZoom;

    /// <summary>Whether the canvas minimap is drawn.</summary>
    [ObservableProperty]
    private bool _visualMinimapVisible;

    /// <summary>Whether the palette lists deprecated blocks.</summary>
    [ObservableProperty]
    private bool _visualPaletteShowDeprecated;

    /// <summary>How often the sidecar canvas is written without being asked; 0 is off.</summary>
    [ObservableProperty]
    private int _visualAutosaveSeconds;

    /// <summary>Whether DeckForge animates.</summary>
    [ObservableProperty]
    private MotionPreference _visualReduceMotion;

    /// <summary>The font size of a block's label.</summary>
    [ObservableProperty]
    private double _visualBlockTextSize;

    /// <summary>Whether the simulator's HTTP blocks may reach the network.</summary>
    [ObservableProperty]
    private bool _visualStageAllowNetwork;

    /// <summary>How long the simulator waits between interpreted steps.</summary>
    [ObservableProperty]
    private int _visualStageStepDelayMs;

    /// <summary>Whether the inspector shows the C# one block compiles to.</summary>
    [ObservableProperty]
    private bool _visualShowBlockCode;

    /// <summary>Whether the empty-state walkthrough has been dismissed.</summary>
    [ObservableProperty]
    private bool _visualOnboardingSeen;

    /// <summary>What the theme setting resolves to right now.</summary>
    public string ResolvedTheme => Theme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        _ => $"System (currently {(IsSystemLight() ? "light" : "dark")})",
    };

    /// <summary>
    /// What the reduced-motion setting resolves to right now, in a sentence.
    /// </summary>
    /// <remarks>
    /// Under the three radio buttons rather than beside the label, and read fresh whenever the setting
    /// changes. The enum member alone is not an answer to "is motion on": "System" reads the same whether
    /// Windows is currently asking for reduced motion or not, and a user who picked "Always" from
    /// "System" while Windows still allowed animations deserves to see that their choice took effect.
    /// </remarks>
    public string ResolvedMotion => MotionPolicy.Describe(
        VisualReduceMotion,
        MotionController.SystemAsksToReduce());

    /// <summary>
    /// What reduced motion actually governs here, including what it does not.
    /// </summary>
    /// <remarks>
    /// On the page, in full, rather than only in a code comment. Part 19 promises a duration table and
    /// Part 20.1 records that the table's durations do not exist; a user who reads "reduced motion" and sees
    /// nothing happen needs to be told that the canvas does not animate, and that a feature which does
    /// nothing is the honest outcome rather than a bug waiting to be reported.
    /// </remarks>
    public string MotionScope => MotionReport.Scope;

    /// <summary>
    /// The keys that are stored but not offered here, named.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="SettingCatalog.StoredOnlyKeys"/> rather than written out, so a key that moves
    /// out of the internal section appears in this sentence without anyone editing it. They are named
    /// rather than counted because "two more settings are stored" raises a question the sentence does not
    /// answer.
    /// </remarks>
    public string StoredOnlyNote =>
        "Also stored, and not offered here: "
        + string.Join(
            " and ",
            SettingCatalog.StoredOnlyKeys.Select(descriptor => $"{descriptor.Name} ({descriptor.Id})"))
        + ". Both belong to the document in front of you rather than to you, and there is no "
        + "stage.json to keep them in yet (Part 11.1).";

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

            VisualSnapEnabled = settings.VisualSnapEnabled;
            VisualSnapRadius = settings.VisualSnapRadius;
            VisualSnapToGrid = settings.VisualSnapToGrid;
            VisualGridSize = settings.VisualGridSize;
            VisualDefaultZoom = settings.VisualDefaultZoom;
            VisualMinimapVisible = settings.VisualMinimapVisible;
            VisualPaletteShowDeprecated = settings.VisualPaletteShowDeprecated;
            VisualAutosaveSeconds = settings.VisualAutosaveSeconds;
            VisualReduceMotion = settings.VisualReduceMotion;
            VisualBlockTextSize = settings.VisualBlockTextSize;
            VisualStageAllowNetwork = settings.VisualStageAllowNetwork;
            VisualStageStepDelayMs = settings.VisualStageStepDelayMs;
            VisualShowBlockCode = settings.VisualShowBlockCode;
            VisualOnboardingSeen = settings.VisualOnboardingSeen;

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
        OnPropertyChanged(nameof(ResolvedMotion));
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

    partial void OnVisualSnapEnabledChanged(bool value) =>
        Persist(s => s.VisualSnapEnabled = value);

    partial void OnVisualSnapRadiusChanged(double value) =>
        Persist(s => s.VisualSnapRadius = value);

    partial void OnVisualSnapToGridChanged(bool value) =>
        Persist(s => s.VisualSnapToGrid = value);

    partial void OnVisualGridSizeChanged(double value) => Persist(s => s.VisualGridSize = value);

    partial void OnVisualDefaultZoomChanged(double value) => Persist(s => s.VisualDefaultZoom = value);

    partial void OnVisualMinimapVisibleChanged(bool value) =>
        Persist(s => s.VisualMinimapVisible = value);

    partial void OnVisualPaletteShowDeprecatedChanged(bool value) =>
        Persist(s => s.VisualPaletteShowDeprecated = value);

    partial void OnVisualAutosaveSecondsChanged(int value) =>
        Persist(s => s.VisualAutosaveSeconds = value);

    // The sentence under the radios has to change with the choice, and Persist is the only place that
    // knows the difference between "the user picked this" and "this was loaded from disk".
    partial void OnVisualReduceMotionChanged(MotionPreference value) =>
        Persist(s => s.VisualReduceMotion = value, nameof(ResolvedMotion));

    partial void OnVisualBlockTextSizeChanged(double value) =>
        Persist(s => s.VisualBlockTextSize = value);

    partial void OnVisualStageAllowNetworkChanged(bool value) =>
        Persist(s => s.VisualStageAllowNetwork = value);

    partial void OnVisualStageStepDelayMsChanged(int value) =>
        Persist(s => s.VisualStageStepDelayMs = value);

    partial void OnVisualShowBlockCodeChanged(bool value) => Persist(s => s.VisualShowBlockCode = value);

    partial void OnVisualOnboardingSeenChanged(bool value) => Persist(s => s.VisualOnboardingSeen = value);

    /// <summary>Everything the page needs to draw the snapping row, read from the catalogue.</summary>
    public SettingInfo VisualSnapEnabledInfo => Info(nameof(VisualSnapEnabled));

    /// <summary>Everything the page needs to draw the snap-radius row.</summary>
    public SettingInfo VisualSnapRadiusInfo => Info(nameof(VisualSnapRadius));

    /// <summary>Everything the page needs to draw the snap-to-grid row.</summary>
    public SettingInfo VisualSnapToGridInfo => Info(nameof(VisualSnapToGrid));

    /// <summary>Everything the page needs to draw the grid-size row.</summary>
    public SettingInfo VisualGridSizeInfo => Info(nameof(VisualGridSize));

    /// <summary>Everything the page needs to draw the default-zoom row.</summary>
    public SettingInfo VisualDefaultZoomInfo => Info(nameof(VisualDefaultZoom));

    /// <summary>Everything the page needs to draw the minimap row.</summary>
    public SettingInfo VisualMinimapVisibleInfo => Info(nameof(VisualMinimapVisible));

    /// <summary>Everything the page needs to draw the deprecated-blocks row.</summary>
    public SettingInfo VisualPaletteShowDeprecatedInfo => Info(nameof(VisualPaletteShowDeprecated));

    /// <summary>Everything the page needs to draw the autosave row.</summary>
    public SettingInfo VisualAutosaveSecondsInfo => Info(nameof(VisualAutosaveSeconds));

    /// <summary>Everything the page needs to draw the reduced-motion row.</summary>
    public SettingInfo VisualReduceMotionInfo => Info(nameof(VisualReduceMotion));

    /// <summary>Everything the page needs to draw the tile-text row.</summary>
    public SettingInfo VisualBlockTextSizeInfo => Info(nameof(VisualBlockTextSize));

    /// <summary>Everything the page needs to draw the block-code row.</summary>
    public SettingInfo VisualShowBlockCodeInfo => Info(nameof(VisualShowBlockCode));

    /// <summary>Everything the page needs to draw the walkthrough row.</summary>
    public SettingInfo VisualOnboardingSeenInfo => Info(nameof(VisualOnboardingSeen));

    /// <summary>Everything the page needs to draw the simulator's network row.</summary>
    public SettingInfo VisualStageAllowNetworkInfo => Info(nameof(VisualStageAllowNetwork));

    /// <summary>Everything the page needs to draw the simulator's step-delay row.</summary>
    public SettingInfo VisualStageStepDelayMsInfo => Info(nameof(VisualStageStepDelayMs));

    /// <summary>
    /// One row's text, straight from the catalogue rather than from the markup.
    /// </summary>
    /// <param name="id">The key's stable name, which is also this class's property name for it.</param>
    /// <returns>The row's text.</returns>
    /// <remarks>
    /// The page and the catalogue would otherwise hold two copies of the same prose within a release, and
    /// the copy in markup is the copy nobody re-reads - which is how a settings page ends up promising a
    /// feature a later commit removed, or describing one that never existed. The <c>nameof</c> at each call
    /// site is what ties the two together: rename a property and the binding breaks at compile time rather
    /// than quietly rendering an empty row.
    /// </remarks>
    private static SettingInfo Info(string id) => SettingCatalog.InfoFor(id);

    /// <summary>Writes one setting and saves, unless the form is still being filled from disk.</summary>
    /// <returns>True when the change reached the disk.</returns>
    /// <remarks>
    /// The result is only acted on for the log. A settings file that cannot be written - locked by a
    /// backup tool, on a full disk, on a read-only profile - is not worth interrupting the user over
    /// mid-form, and it is not worth pretending worked either: <c>App.Logger</c> is the record, which is
    /// what that logger is public for.
    /// </remarks>
    private bool Persist(Action<AppSettings> change, params string[] alsoChanged)
    {
        if (_loading)
        {
            return false;
        }

        var saved = _settings.Update(change);

        if (!saved)
        {
            App.Logger.LogError(
                "A settings change could not be written to {Path}. It is in force for this session and "
                + "will be lost when DeckForge closes.",
                _settings.Path);
        }

        foreach (var property in alsoChanged)
        {
            OnPropertyChanged(property);
        }

        return saved;
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
