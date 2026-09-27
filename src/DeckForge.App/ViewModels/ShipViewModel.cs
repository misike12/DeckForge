using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CliAdapter.Processes;
using DeckForge.CliAdapter.Tools;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class ShipViewModel : ObservableObject
{
    private readonly MacroDeckCli _cli;
    private readonly DotNetCli _dotnet;
    private readonly WorkspaceManager _workspaces;
    private readonly Services.VelopackPackagingService _velopack;

    public ShipViewModel(MacroDeckCli cli, DotNetCli dotnet, WorkspaceManager workspaces, Services.VelopackPackagingService velopack)
    {
        _cli = cli;
        _dotnet = dotnet;
        _workspaces = workspaces;
        _velopack = velopack;
        Services.ShellMessenger.WorkspaceChanged += _ => RefreshWorkspace();
        RefreshWorkspace();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _artifactPath = "";

    public ObservableCollection<string> OutputLines { get; } = [];

    private WorkspaceContext? Workspace => _workspaces.Current;

    private void RefreshWorkspace() => HasWorkspace = Workspace is not null;

    private void Append(string line) => System.Windows.Application.Current?.Dispatcher.Invoke(() => OutputLines.Add(line));

    private async Task RunStepAsync(string label, Func<Task<ProcessResult>> run)
    {
        IsBusy = true;
        StatusText = $"{label}...";
        Append($"> {label}");
        try
        {
            var result = await run();
            foreach (var line in result.CombinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Append(line.TrimEnd('\r'));
            }
            StatusText = result.Succeeded ? $"{label} OK" : $"{label} FAILED ({result.ExitCode})";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusText = $"{label} failed: macrodeck-plugin not found on PATH.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PackageAsync(CancellationToken ct)
    {
        if (Workspace is null)
        {
            return;
        }
        Directory.CreateDirectory(Workspace.ArtifactsDirectory);
        await RunStepAsync("macrodeck-plugin build (publish + pack)",
            () => _cli.BuildAsync(Workspace.PluginProjectDirectory, Workspace.ArtifactsDirectory, null, ct));
        var artifact = FindNewestArtifact();
        if (artifact is not null)
        {
            ArtifactPath = artifact;
        }
    }

    [RelayCommand]
    private async Task ValidateAsync(CancellationToken ct)
    {
        var artifact = ArtifactPath is not "" ? ArtifactPath : FindNewestArtifact();
        if (artifact is null)
        {
            StatusText = "Package first - no artifact found.";
            return;
        }
        ArtifactPath = artifact;
        await RunStepAsync("macrodeck-plugin validate --artifact",
            () => _cli.ValidateArtifactAsync(artifact, ct));
    }

    [RelayCommand]
    private async Task InspectAsync(CancellationToken ct)
    {
        var artifact = ArtifactPath is not "" ? ArtifactPath : FindNewestArtifact();
        if (artifact is null)
        {
            StatusText = "Package first - no artifact found.";
            return;
        }
        ArtifactPath = artifact;
        await RunStepAsync("macrodeck-plugin inspect", () => _cli.InspectAsync(artifact, "text", ct));
    }

    [RelayCommand]
    private async Task VerifySignatureAsync(CancellationToken ct)
    {
        var artifact = ArtifactPath is not "" ? ArtifactPath : FindNewestArtifact();
        if (artifact is null)
        {
            StatusText = "Package first - no artifact found.";
            return;
        }
        await RunStepAsync("macrodeck-plugin verify (Store-signed or creator-signed)",
            () => _cli.VerifyAsync(artifact, ct));
    }

    [RelayCommand]
    private async Task GenerateKeyAsync(CancellationToken ct)
    {
        var keysDir = Path.Combine(Workspace?.RootDirectory ?? ".", "creator-keys");
        Directory.CreateDirectory(keysDir);
        await RunStepAsync("macrodeck-plugin keygen (creator key for out-of-Store distribution)",
            () => _cli.KeygenAsync(keysDir, ct));
    }

    [RelayCommand]
    private async Task SignAsync(CancellationToken ct)
    {
        var artifact = ArtifactPath is not "" ? ArtifactPath : FindNewestArtifact();
        var keysDir = Path.Combine(Workspace?.RootDirectory ?? ".", "creator-keys");
        var keyPath = Directory.GetFiles(keysDir).FirstOrDefault(f => f.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pem", StringComparison.OrdinalIgnoreCase));
        if (artifact is null || keyPath is null)
        {
            StatusText = "Needs an artifact and a creator key (Generate key first).";
            return;
        }
        await RunStepAsync("macrodeck-plugin sign", () => _cli.SignAsync(artifact, keyPath, ct));
    }

    /// <summary>Packs DeckForge itself with Velopack: setup, portable and delta packages.</summary>
    [RelayCommand]
    private async Task PackVelopackAsync(CancellationToken ct)
    {
        var root = Workspace?.RootDirectory;
        if (root is null)
        {
            StatusText = "Open a plugin first (the releases land next to your workspace).";
            return;
        }

        var appProject = Path.Combine(root, "..");
        StatusText = "Velopack: publishing DeckForge (this runs dotnet publish)...";
        Append("> dotnet publish src/DeckForge.App -c Release -r win-x64");
        var publishDir = Path.GetFullPath(Path.Combine(root, "velopack", "publish"));
        var outDir = Path.GetFullPath(Path.Combine(root, "velopack", "out"));
        var projectPath = FindAppProject();
        if (projectPath is null)
        {
            StatusText = "DeckForge.App.csproj not found - run vpk manually (see README).";
            return;
        }

        var publish = await _dotnet.PublishAsync(projectPath, "Release", "win-x64", publishDir, ct);
        foreach (var line in publish.CombinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Append(line.TrimEnd('\r'));
        }
        if (!publish.Succeeded)
        {
            StatusText = "Velopack: dotnet publish FAILED.";
            return;
        }

        var version = typeof(DeckForge.App.App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        StatusText = $"Velopack: vpk pack (v{version})...";
        var result = await _velopack.PackAsync(
            publishDir,
            outDir,
            "DeckForge",
            version,
            Path.Combine(Path.GetDirectoryName(projectPath)!, "Assets", "app-icon.ico"),
            ct);
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Append(line.TrimEnd('\r'));
        }
        StatusText = result.Succeeded
            ? $"Velopack: done. Setup + portable in {result.ReleasesDirectory}"
            : "Velopack: vpk pack FAILED (see output).";
    }

    private static string? FindAppProject()
    {
        // The app source tree sits next to the workspace; walk up from the executing assembly.
        var dir = Path.GetDirectoryName(typeof(ShipViewModel).Assembly.Location);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "DeckForge.App", "DeckForge.App.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private string? FindNewestArtifact() =>
        Workspace is null
            ? null
            : Directory.Exists(Workspace.ArtifactsDirectory)
                ? Directory.GetFiles(Workspace.ArtifactsDirectory, "*.macroDeckPlugin")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault()?.FullName
                : null;
}
