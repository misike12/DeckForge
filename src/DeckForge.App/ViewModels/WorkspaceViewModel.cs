using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>Tracks the open workspace for the shell and Home page.</summary>
public partial class WorkspaceViewModel : ObservableObject
{
    private readonly WorkspaceManager _manager;
    private readonly SettingsService _settings;

    public WorkspaceViewModel(WorkspaceManager manager, SettingsService settings)
    {
        _manager = manager;
        _settings = settings;
        RefreshRecents();

        ShellMessenger.WorkspaceChanged += _ => RefreshFromManager();
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
        var context = WorkspaceManager.FromPluginProject(pluginDir);
        SetWorkspace(context);
    }

    public void OpenSolution(string solutionPath)
    {
        var root = Path.GetDirectoryName(solutionPath)!;
        var srcDir = Path.Combine(root, "src");
        if (!Directory.Exists(srcDir))
        {
            WorkspaceTitle = "Invalid workspace";
            WorkspaceDetail = $"{solutionPath} has no src folder.";
            return;
        }
        var pluginDir = Directory.GetDirectories(srcDir).FirstOrDefault();
        if (pluginDir is null)
        {
            WorkspaceTitle = "Invalid workspace";
            WorkspaceDetail = "No plugin project under src/.";
            return;
        }
        var context = WorkspaceManager.FromPluginProject(pluginDir);
        context.SolutionPath = solutionPath;
        SetWorkspace(context);
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
