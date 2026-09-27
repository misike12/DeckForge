using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CliAdapter;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly EnvironmentDoctor _doctor;

    public MainViewModel(WorkspaceManager workspaceManager, EnvironmentDoctor doctor)
    {
        _workspaceManager = workspaceManager;
        _doctor = doctor;
    }

    [ObservableProperty]
    private string _title = "DeckForge";

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _workspaceName = "No plugin open";

    [ObservableProperty]
    private bool _isBusy;

    public WorkspaceContext? Workspace => _workspaceManager.Current;

    [RelayCommand]
    private async Task RefreshEnvironmentAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var checks = await _doctor.RunAllAsync(ct);
            EnvironmentChecks.Clear();
            foreach (var check in checks)
            {
                EnvironmentChecks.Add(check);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ObservableCollection<DoctorCheck> EnvironmentChecks { get; } = [];
}
