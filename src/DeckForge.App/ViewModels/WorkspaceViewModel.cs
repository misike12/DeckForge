using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Workspace;
using DeckForge.Core.Settings;

namespace DeckForge.App.ViewModels;

/// <summary>Tracks the open workspace for the shell and Home page.</summary>
/// <remarks>
/// The Home page binds its whole DataContext to this view model, so the environment checklist
/// used to live on a different one - <c>MainViewModel</c> - and every binding to it resolved to
/// nothing. The section rendered empty and nothing said why. The checks live here now, where the
/// page can actually see them, and the old view model is gone.
/// </remarks>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly WorkspaceManager _manager;
    private readonly SettingsService _settings;
    private readonly CliAdapter.EnvironmentDoctor _doctor;
    private readonly CliAdapter.Tools.PrerequisiteInstaller _installer;

    public WorkspaceViewModel(
        WorkspaceManager manager,
        SettingsService settings,
        CliAdapter.EnvironmentDoctor doctor,
        CliAdapter.Tools.PrerequisiteInstaller installer)
    {
        _manager = manager;
        _settings = settings;
        _doctor = doctor;
        _installer = installer;
        RefreshRecents();

        ShellMessenger.WorkspaceChanged += _ => RefreshFromManager();

        // The doctor only ran when the collection happened to be empty, so a machine that gained
        // the .NET SDK while DeckForge was open kept reporting it missing until a restart. It runs
        // once on load and again on demand.
        _ = RefreshEnvironmentAsync(CancellationToken.None);
    }

    /// <summary>The environment checklist shown on the Home page.</summary>
    public ObservableCollection<CliAdapter.DoctorCheck> EnvironmentChecks { get; } = [];

    [ObservableProperty]
    private bool _isCheckingEnvironment;

    /// <summary>True while a prerequisite is being installed, so the buttons can be disabled.</summary>
    [ObservableProperty]
    private bool _isInstallingPrerequisite;

    /// <summary>
    /// True when at least one missing prerequisite DeckForge can act on.
    /// </summary>
    public bool HasInstallablePrerequisites =>
        !IsInstallingPrerequisite && EnvironmentChecks.Any(_installer.CanInstall);

    /// <summary>How many missing prerequisites there are, for the button's caption.</summary>
    public int MissingPrerequisiteCount => _installer.Pending(EnvironmentChecks).Count;

    /// <summary>
    /// Installs every missing prerequisite, in dependency order, and re-checks afterwards.
    /// </summary>
    /// <remarks>
    /// Installing the CLI before the SDK cannot work - it is a dotnet global tool - so the order is
    /// the installer's, not the order the checks happen to be discovered in. And the sequence stops
    /// at the first failure, because continuing would produce a second error that is really just a
    /// consequence of the first.
    /// </remarks>
    [RelayCommand]
    private async Task InstallMissingPrerequisitesAsync()
    {
        if (IsInstallingPrerequisite)
        {
            return;
        }

        var pending = _installer.Pending(EnvironmentChecks);
        if (pending.Count == 0)
        {
            return;
        }

        IsInstallingPrerequisite = true;
        EnvironmentStatus = $"Installing {pending.Count} prerequisite(s)...";
        try
        {
            var results = await _installer.InstallAllAsync(
                pending,
                line => EnvironmentStatus = line);

            EnvironmentStatus = string.Join(
                Environment.NewLine,
                results.Select(r => (r.Ok ? "OK   " : "FAIL ") + r.Message));

            await RefreshEnvironmentAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            EnvironmentStatus = "Cancelled.";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            EnvironmentStatus = $"Could not install: {ex.Message}";
        }
        finally
        {
            IsInstallingPrerequisite = false;
            OnPropertyChanged(nameof(HasInstallablePrerequisites));
            OnPropertyChanged(nameof(MissingPrerequisiteCount));
        }
    }

    /// <summary>Installs one prerequisite, then re-checks.</summary>
    [RelayCommand]
    private async Task InstallPrerequisiteAsync(CliAdapter.DoctorCheck? check)
    {
        if (check is null || IsInstallingPrerequisite)
        {
            return;
        }

        IsInstallingPrerequisite = true;
        EnvironmentStatus = $"Installing {check.Title}...";
        try
        {
            var result = await _installer.InstallAsync(check, line => EnvironmentStatus = line);
            EnvironmentStatus = result.Message;
            await RefreshEnvironmentAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            EnvironmentStatus = "Cancelled.";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            EnvironmentStatus = $"Could not install: {ex.Message}";
        }
        finally
        {
            IsInstallingPrerequisite = false;
            OnPropertyChanged(nameof(HasInstallablePrerequisites));
            OnPropertyChanged(nameof(MissingPrerequisiteCount));
        }
    }

    /// <summary>The install action's caption for one check, or null when there is nothing to do.</summary>
    public string? InstallActionLabel(CliAdapter.DoctorCheck check) =>
        _installer.CanInstall(check)
            ? check.Id == "macrodeck-host" ? "Get Macro Deck" : "Install"
            : null;

    /// <summary>Progress or result of the last install, shown under the checklist.</summary>
    [ObservableProperty]
    private string _environmentStatus = "";

    /// <summary>Re-runs the environment checks. Safe to call at any time.</summary>
    [RelayCommand]
    public async Task RefreshEnvironmentAsync(CancellationToken ct)
    {
        if (IsCheckingEnvironment)
        {
            return;
        }

        IsCheckingEnvironment = true;
        try
        {
            var checks = await _doctor.RunAllAsync(ct);
            EnvironmentChecks.Clear();
            foreach (var check in checks)
            {
                EnvironmentChecks.Add(check);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled check leaves the previous results in place, which is more useful than an
            // empty list.
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            EnvironmentChecks.Clear();
            EnvironmentChecks.Add(new CliAdapter.DoctorCheck(
                "environment",
                "Environment checks",
                false,
                "The checks could not run: " + ex.Message,
                "DeckForge needs dotnet and the macrodeck-plugin CLI on PATH to check them.",
                CliAdapter.DoctorSeverity.Required));
        }
        finally
        {
            IsCheckingEnvironment = false;
            OnPropertyChanged(nameof(HasInstallablePrerequisites));
            OnPropertyChanged(nameof(MissingPrerequisiteCount));
        }
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _workspaceTitle = "No plugin open";

    [ObservableProperty]
    private string _workspaceDetail = "";

    public ObservableCollection<string> RecentWorkspaces { get; } = [];

    public WorkspaceContext? Current => _manager.Current;

    [RelayCommand]
    public void OpenRecent(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            _settings.RemoveRecentWorkspace(solutionPath);
            RefreshRecents();
            return;
        }
        OpenSolution(solutionPath);
    }

    [RelayCommand]
    private void OpenSolutionDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a plugin solution",
            Filter = "Solution (*.slnx)|*.slnx|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        OpenSolution(dialog.FileName);
    }

    /// <summary>Opens the workspace containing a manifest.json chosen by the user.</summary>
    [RelayCommand]
    private void OpenManifestDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a plugin's manifest.json",
            Filter = "Manifest (manifest.json)|manifest.json",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var pluginDir = Path.GetDirectoryName(dialog.FileName)!;
        try
        {
            SetWorkspace(WorkspaceManager.FromPluginProject(pluginDir));
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            // FromPluginProject throws when the manifest is missing or unreadable. Three callers
            // let that escape, so choosing a folder without a manifest closed the app.
            WorkspaceTitle = "Could not open that manifest";
            WorkspaceDetail = ex.Message;
        }
    }

    public void OpenSolution(string solutionPath)
    {
        if (!_manager.TryOpenSolution(solutionPath, out var context, out var problem))
        {
            // Reported, not thrown. This is called from a command the user just triggered, and an
            // unhandled FileNotFoundException from opening the wrong folder is not an answer.
            WorkspaceTitle = "Could not open that solution";
            WorkspaceDetail = problem;
            return;
        }

        SetWorkspace(context!);
    }

    private void SetWorkspace(WorkspaceContext context)
    {
        _manager.Open(context);
        _settings.AddRecentWorkspace(context.SolutionPath);
        RefreshFromManager();
        RefreshRecents();
        ShellMessenger.NotifyWorkspaceChanged(context.SolutionPath);
    }

    [RelayCommand]
    private void NewPlugin() => ShellMessenger.NavigateTo("new-project");

    [RelayCommand]
    private void OpenDocs() => ShellMessenger.NavigateTo("docs");

    [RelayCommand]
    private void CloseWorkspace()
    {
        _manager.Close();
        RefreshFromManager();
        ShellMessenger.NotifyWorkspaceChanged(null);
    }

    private void RefreshFromManager()
    {
        var ws = _manager.Current;
        HasWorkspace = ws is not null;
        WorkspaceTitle = ws?.Options.PluginName ?? "No plugin open";
        WorkspaceDetail = ws is null ? "" : ws.PluginProjectDirectory;
        OnPropertyChanged(nameof(Current));
    }

    [ObservableProperty]
    private bool _hasRecentWorkspaces;

    private void RefreshRecents()
    {
        RecentWorkspaces.Clear();
        foreach (var recent in _settings.Settings.RecentWorkspaces)
        {
            RecentWorkspaces.Add(recent);
        }
        HasRecentWorkspaces = RecentWorkspaces.Count > 0;
    }
}
