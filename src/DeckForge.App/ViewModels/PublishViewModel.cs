using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Plugins;
using DeckForge.Core.Workspace;
using DeckForge.Core.Settings;

namespace DeckForge.App.ViewModels;

public partial class PublishViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly SettingsService _settings;
    private readonly CliAdapter.Processes.ProcessRunner _runner;
    private readonly CliAdapter.Tools.MacroDeckCli _cli;

    public PublishViewModel(
        WorkspaceManager workspaces,
        SettingsService settings,
        CliAdapter.Processes.ProcessRunner runner,
        CliAdapter.Tools.MacroDeckCli cli)
    {
        _workspaces = workspaces;
        _settings = settings;

        // Injected rather than looked up. The service-locator version null-forgot the result, so a
        // registration that was missing or resolved late surfaced as a NullReferenceException
        // halfway through publishing a release.
        _runner = runner;
        _cli = cli;

        Services.ShellMessenger.WorkspaceChanged += _ => Load();
        Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _releaseTag = "v1.0.0";

    [ObservableProperty]
    private string _releaseNotes = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _pluginId = "";

    [ObservableProperty]
    private string _repository = "";

    public ObservableCollection<StoreGateCheck> StoreGate { get; } = [];

    public void Load()
    {
        var ws = _workspaces.Current;
        HasWorkspace = ws is not null;
        if (ws is null)
        {
            return;
        }
        try
        {
            var doc = ManifestDocument.Load(ws.ManifestPath);
            PluginId = doc.Id;
            Repository = doc.RawDocument["repository"]?.GetValue<string>() ?? "";
        }
        catch
        {
            // parse errors are surfaced in Manifest Studio
        }
        RefreshStoreGate(ws);
    }

    private void RefreshStoreGate(WorkspaceContext ws)
    {
        StoreGate.Clear();
        try
        {
            var doc = ManifestDocument.Load(ws.ManifestPath);
            StoreGate.Add(new("Publisher set", !string.IsNullOrWhiteSpace(doc.PublisherName)
                && doc.PublisherName is not "Example Publisher"));
            StoreGate.Add(new("License set", !string.IsNullOrWhiteSpace(doc.RawDocument["license"]?.GetValue<string>())));
            var repo = doc.RawDocument["repository"]?.GetValue<string>() ?? "";
            StoreGate.Add(new("Repository is a real GitHub repo",
                repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                && !repo.StartsWith("https://github.com/example/", StringComparison.OrdinalIgnoreCase)));
            StoreGate.Add(new("Description set", !string.IsNullOrWhiteSpace(doc.RawDocument["description"]?.GetValue<string>())));
            StoreGate.Add(new("Icon declared", !string.IsNullOrWhiteSpace(doc.RawDocument["icon"]?.GetValue<string>())));
            StoreGate.Add(new("Compatibility declared", doc.RawDocument["compatibility"] is not null));
        }
        catch
        {
            StoreGate.Add(new("manifest.json parses", false));
        }
    }

    /// <summary>Writes the exact release workflow the Creator Portal checks for.</summary>

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    private CancellableOperation? _operation;

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    public CancellableOperation Operation =>
        _operation ??= new CancellableOperation().Track(CreateReleaseCommand);
        [RelayCommand]
    private void WriteReleaseWorkflow()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            return;
        }
        var project = ws.ProjectName;
        var workflow = """
            name: Release

            on:
              release:
                types: [published]

            jobs:
              publish:
                uses: Macro-Deck-App/GitHub-Actions/.github/workflows/publish-plugin.yml@v1
                permissions:
                  contents: read
                  id-token: write
                with:
                  version: ${{ github.event.release.tag_name }}
                  source: src/$Project
                  changelog: ${{ github.event.release.body }}
            """.Replace("$Project", project);

        var path = Path.Combine(ws.RootDirectory, ".github", "workflows", "release.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, workflow, new UTF8Encoding(false));
        StatusText = $"Wrote {path} - commit and push it, then publish a GitHub release.";
    }

    [RelayCommand]
    private async Task CreateReleaseAsync(CancellationToken ct)
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(ReleaseTag) || !ReleaseTag.StartsWith('v'))
        {
            StatusText = "Tag must look like v1.0.0 (the version comes from the tag).";
            return;
        }

        var args = new List<string> { "release", "create", ReleaseTag, "--title", ReleaseTag.TrimStart('v') };

        // The account setting is the fallback owner. gh infers the repo from the git remote, which
        // a plugin folder often does not have, and then creates the release somewhere the user did
        // not mean - so an explicit owner wins.
        var owner = ReleaseOwner();
        if (owner is not null)
        {
            args.AddRange(["--repo", $"{owner}/{RepositorySlug()}"]);
        }

        if (!string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            args.AddRange(["--notes", ReleaseNotes]);
        }
        else
        {
            args.Add("--generate-notes");
        }

        StatusText = "Creating GitHub release via gh...";
        try
        {
            var result = await _runner.RunAsync("gh", args, ws.RootDirectory, null, ct);
            StatusText = result.Succeeded
                ? $"Release {ReleaseTag} published - the workflow uploads the build to the Creator Portal."
                : $"gh failed ({result.ExitCode}): {result.StandardError.Split('\n').FirstOrDefault()?.Trim()}";
        }
        catch (CliAdapter.Processes.ProcessStartFailedException)
        {
            StatusText = "gh CLI not found - install GitHub CLI and run 'gh auth login'.";
        }
    }

    /// <summary>
    /// The account to create the release under: the setting, else the owner of the manifest's
    /// repository URL.
    /// </summary>
    private string? ReleaseOwner()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Settings.GitHubAccount))
        {
            return _settings.Settings.GitHubAccount.Trim();
        }

        if (!Uri.TryCreate(Repository, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[0] : null;
    }

    /// <summary>The repository name from the manifest's repository URL.</summary>
    private string RepositorySlug() =>
        Uri.TryCreate(Repository, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ""
            : "";

    [RelayCommand]
    private void OpenPortal() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://studio.macro-deck.app/") { UseShellExecute = true });
}

public sealed record StoreGateCheck(string Title, bool Ok);
