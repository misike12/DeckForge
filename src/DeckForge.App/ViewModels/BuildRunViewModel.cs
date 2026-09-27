using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CliAdapter.Processes;
using DeckForge.CliAdapter.Tools;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class BuildRunViewModel : ObservableObject
{
    private readonly DotNetCli _dotnet;
    private readonly MacroDeckCli _cli;
    private readonly WorkspaceManager _workspaces;

    public BuildRunViewModel(DotNetCli dotnet, MacroDeckCli cli, WorkspaceManager workspaces)
    {
        _dotnet = dotnet;
        _cli = cli;
        _workspaces = workspaces;

        Services.ShellMessenger.WorkspaceChanged += _ =>
        {
            StatusText = Workspace is null
                ? "Open or create a plugin first."
                : $"Workspace: {Workspace.Options.PluginName}";
        };

        // F5 is documented as running the stub host. The window raises this rather than reaching
        // in here, so the shell stays unaware of which page owns the command.
        Services.ShellMessenger.RunRequested += () =>
        {
            if (RunStubHostCommand.CanExecute(null))
            {
                RunStubHostCommand.Execute(null);
            }
        };
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Idle";

    public ObservableCollection<string> OutputLines { get; } = [];

    private WorkspaceContext? Workspace => _workspaces.Current;

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    private CancellableOperation? _operation;

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    public CancellableOperation Operation =>
        _operation ??= new CancellableOperation().Track(BuildCommand, TestCommand, RunStubHostCommand, PackageCommand);
    
    [RelayCommand]
    private async Task BuildAsync(CancellationToken ct)
    {
        if (Workspace is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        await RunStreamingAsync(
            "dotnet build",
            () => _dotnet.BuildAsync(Workspace.SolutionPath, "Debug", ct));
    }

    [RelayCommand]
    private async Task TestAsync(CancellationToken ct)
    {
        if (Workspace is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        await RunStreamingAsync(
            "dotnet test",
            () => _dotnet.TestAsync(Workspace.SolutionPath, ct));
    }

    [RelayCommand]
    private async Task RunStubHostAsync(CancellationToken ct)
    {
        if (Workspace is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        await RunStreamingAsync(
            "macrodeck-plugin run --stub-host",
            () => _cli.RunStubHostAsync(Workspace.PluginProjectDirectory, ct),
            longRunning: true);
    }

    [RelayCommand]
    private async Task PackageAsync(CancellationToken ct)
    {
        if (Workspace is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        Directory.CreateDirectory(Workspace.ArtifactsDirectory);
        await RunStreamingAsync(
            "macrodeck-plugin build --output artifacts",
            () => _cli.BuildAsync(Workspace.PluginProjectDirectory, Workspace.ArtifactsDirectory, null, true, ct));
    }

    /// <summary>Run-console v2: pokes a reserved plugin endpoint and prints the response.</summary>
    [RelayCommand]
    private async Task PokeEndpointAsync(string? endpoint)
    {
        var port = FindPluginPort();
        if (port is null)
        {
            StatusText = "No plugin listener detected - run the plugin first (stub host prints its port).";
            return;
        }
        var url = $"http://127.0.0.1:{port}/{endpoint}";
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            OutputLines.Add($"> GET {url}");
            OutputLines.Add($"{(int)response.StatusCode} {response.StatusCode}");
            if (!string.IsNullOrWhiteSpace(body))
            {
                OutputLines.Add(body.Length > 800 ? body[..800] + "..." : body);
            }
            StatusText = $"{endpoint}: {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            OutputLines.Add($"> GET {url}");
            OutputLines.Add($"unreachable: {ex.Message}");
            StatusText = $"{endpoint}: unreachable";
        }
    }

    /// <summary>Finds the plugin listener port from the stub-host output lines.</summary>
    private int? FindPluginPort()
    {
        foreach (var line in OutputLines)
        {
            // "Now listening on: "http://127.0.0.1:59827""
            var idx = line.IndexOf("Now listening on:", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = line.IndexOf(':', idx + 17);
                var end = line.IndexOfAny(['"', ' '], start + 1);
                if (start > 0 && end > start && int.TryParse(line[(start + 1)..end], out var port))
                {
                    return port;
                }
            }
        }
        return null;
    }

    private async Task RunStreamingAsync(string label, Func<Task<ProcessResult>> run, bool longRunning = false)
    {
        IsBusy = true;
        StatusText = $"Running {label}...";
        OutputLines.Add($"> {label}");
        try
        {
            var result = await run();
            foreach (var line in result.CombinedOutput.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    OutputLines.Add(line.TrimEnd('\r'));
                }
            }
            StatusText = longRunning
                ? $"{label} finished with exit code {result.ExitCode}."
                : result.Succeeded ? $"{label} succeeded." : $"{label} failed ({result.ExitCode}).";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{label} cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
