using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
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

    /// <summary>
    /// The certificate the Creator Portal issued, as an absolute path. A key pair is not a
    /// certificate: <c>keygen</c> produces one and says so, and the Portal issues the other.
    /// </summary>
    [ObservableProperty]
    private string _certificatePath = "";

    /// <summary>The detached signature the Portal issued alongside the certificate.</summary>
    [ObservableProperty]
    private string _certificateSignaturePath = "";

    /// <summary>The key name <c>keygen</c> was asked for, which decides the two file names.</summary>
    [ObservableProperty]
    private string _keyName = MacroDeckCli.DefaultKeyName;

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

            StatusText = result.Succeeded
                ? $"{label} OK"
                : $"{label} FAILED ({result.ExitCode}): {MacroDeckCli.ExplainExitCode(result.ExitCode)}";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusText = $"{label} failed: macrodeck-plugin not found on PATH.";
        }
        catch (OperationCanceledException)
        {
            // A cancelled run is a user action, not a failure worth a crash-log entry.
            StatusText = $"{label} cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? FindNewestArtifactUnchanged() => FindNewestArtifact();

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    private CancellableOperation? _operation;

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    public CancellableOperation Operation =>
        _operation ??= new CancellableOperation().Track(PackageCommand, ValidateCommand, InspectCommand, VerifySignatureCommand, GenerateKeyCommand, SignCommand, PackVelopackCommand);
    
    [RelayCommand]
    private async Task PackageAsync(CancellationToken ct)
    {
        if (Workspace is null)
        {
            return;
        }

        Directory.CreateDirectory(Workspace.ArtifactsDirectory);
        await RunStepAsync("macrodeck-plugin build (publish + pack)",
            () => _cli.BuildAsync(Workspace.PluginProjectDirectory, Workspace.ArtifactsDirectory, null, true, ct));
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
        await RunStepAsync("macrodeck-plugin inspect",
            () => _cli.InspectAsync(artifact, "text", false, ct));
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

        await RunStepAsync("macrodeck-plugin verify",
            () => _cli.VerifyAsync(artifact, "text", null, ct));
    }

    private string KeysDirectory => Path.GetFullPath(
        Path.Combine(Workspace?.RootDirectory ?? ".", "creator-keys"));

    /// <summary>The private key <c>keygen</c> writes, or null when it has not been run.</summary>
    private string? PrivateKeyPath => Path.GetFullPath(MacroDeckCli.KeyPrivatePath(KeysDirectory, KeyName));

    /// <summary>The public key <c>keygen</c> writes, which is what gets submitted to the Portal.</summary>
    private string? PublicKeyPath => Path.GetFullPath(MacroDeckCli.KeyPublicPath(KeysDirectory, KeyName));

    [RelayCommand]
    private async Task GenerateKeyAsync(CancellationToken ct)
    {
        if (MacroDeckCli.ReservedKeyNames.Contains(KeyName, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = $"'{KeyName}' is a reserved key name. Pick another.";
            return;
        }

        Directory.CreateDirectory(KeysDirectory);
        await RunStepAsync("macrodeck-plugin keygen (creator key for out-of-Store distribution)",
            () => _cli.KeygenAsync(KeysDirectory, KeyName, ct));

        var publicPath = PublicKeyPath;
        if (publicPath is not null && File.Exists(publicPath))
        {
            StatusText = $"Key pair written. Submit {publicPath} to the Creator Portal; it issues the certificate.";
        }
    }

    [RelayCommand]
    private async Task SignAsync(CancellationToken ct)
    {
        var artifact = ArtifactPath is not "" ? ArtifactPath : FindNewestArtifact();
        if (artifact is null)
        {
            StatusText = "Package first - no artifact found.";
            return;
        }

        // The tool's contract: sign needs a package, an output path, a certificate, its detached
        // signature and a private key. All five. The previous version looked for a "*.key" file,
        // which keygen has never written, so this button could never succeed.
        var keyPath = PrivateKeyPath;
        if (keyPath is null || !File.Exists(keyPath))
        {
            StatusText = "No creator key yet - run Generate key first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CertificatePath) || string.IsNullOrWhiteSpace(CertificateSignaturePath))
        {
            StatusText = "Signing needs the certificate the Creator Portal issued. Set the certificate and its "
                + "signature paths above. The key pair on its own cannot sign anything.";
            return;
        }

        if (!File.Exists(CertificatePath))
        {
            StatusText = $"No certificate at '{CertificatePath}'.";
            return;
        }

        if (!File.Exists(CertificateSignaturePath))
        {
            StatusText = $"No certificate signature at '{CertificateSignaturePath}'.";
            return;
        }

        var output = Path.Combine(
            Path.GetDirectoryName(artifact) ?? KeysDirectory,
            Path.GetFileNameWithoutExtension(artifact) + ".signed" + Path.GetExtension(artifact));

        await RunStepAsync("macrodeck-plugin sign",
            () => _cli.SignAsync(artifact, output, CertificatePath, CertificateSignaturePath, keyPath, ct: ct));
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
