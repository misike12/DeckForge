using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class IconPackEntry : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _filePath = "";

    public string FileName => Path.GetFileName(FilePath);
}

public partial class IconPackDesignerViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly CliAdapter.Tools.MacroDeckCli _cli;

    public IconPackDesignerViewModel(WorkspaceManager workspaces, CliAdapter.Tools.MacroDeckCli cli)
    {
        _workspaces = workspaces;
        _cli = cli;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _packId = "my-icons";

    [ObservableProperty]
    private string _packName = "My icons";

    [ObservableProperty]
    private string _author = "";

    /// <summary>ai.createdIcons declaration shown on the Store page.</summary>
    [ObservableProperty]
    private bool _aiCreatedIcons;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<IconPackEntry> Icons { get; } = [];

    /// <summary>Documented pack limits (IconPackArchiveLimits).</summary>
    public const int MaxFiles = 29996;

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    public void RefreshOnNavigate() => Load();

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    private CancellableOperation? _operation;

    /// <summary>Lets one Cancel button stop whichever of this page's commands is running.</summary>
    public CancellableOperation Operation =>
        _operation ??= new CancellableOperation().Track(BundleIntoPluginCommand);
    
    [RelayCommand]
    private void AddIcons()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add icon images to the pack",
            Filter = "Images|*.svg;*.png;*.jpg;*.jpeg;*.gif",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        foreach (var file in dialog.FileNames)
        {
            if (Icons.Count >= MaxFiles)
            {
                StatusText = $"Pack limit reached ({MaxFiles} files).";
                break;
            }
            Icons.Add(new IconPackEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
            });
        }
        StatusText = $"{Icons.Count} icon(s) in the pack.";
    }

    [RelayCommand]
    private void RemoveIcon(IconPackEntry? entry)
    {
        if (entry is not null)
        {
            Icons.Remove(entry);
        }
    }

    /// <summary>Exports a .macroDeckIconPack: pack.json plus one file per icon master.</summary>
    [RelayCommand]
    private void ExportPack()
    {
        if (Icons.Count == 0)
        {
            StatusText = "Add icons first.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Macro Deck icon pack|*.macroDeckIconPack",
            FileName = PackId + ".macroDeckIconPack",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var stream = File.Create(dialog.FileName);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            var pack = new Dictionary<string, object>
            {
                ["id"] = PackId,
                ["name"] = PackName,
                ["author"] = Author,
                ["version"] = "1.0.0",
                ["ai"] = new Dictionary<string, object>
                {
                    // Declared = shown; not declared is never treated as free of AI.
                    ["createdIcons"] = AiCreatedIcons,
                },
                ["icons"] = Icons.Select(i => new Dictionary<string, object>
                {
                    ["name"] = i.Name,
                    ["file"] = i.FileName,
                }).ToList(),
            };
            var packEntry = archive.CreateEntry("pack.json");
            using (var writer = new StreamWriter(packEntry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
            }

            foreach (var icon in Icons)
            {
                var entry = archive.CreateEntry(icon.FileName);
                using var target = entry.Open();
                using var source = File.OpenRead(icon.FilePath);
                source.CopyTo(target);
            }

            StatusText = $"Exported {dialog.FileName} ({Icons.Count + 1} files incl. pack.json).";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>Bundles the pack into the plugin project via the CLI (icon-pack add).</summary>
    [RelayCommand]
    private async Task BundleIntoPluginAsync(CancellationToken ct)
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }

        if (Icons.Count == 0)
        {
            StatusText = "Add at least one icon to the pack first.";
            return;
        }

        // Written under the workspace rather than the temp directory, so a failed bundle leaves a
        // file the user can inspect and a successful one is cleaned up. The previous version
        // wrote to TEMP and never removed it.
        var packDirectory = Path.Combine(ws.ArtifactsDirectory, "icon-packs");
        Directory.CreateDirectory(packDirectory);
        var packFile = Path.Combine(packDirectory, PackId + ".macroDeckIconPack");
        ExportTo(packFile);

        StatusText = "Bundling via macrodeck-plugin icon-pack add...";
        try
        {
            // The pack path is positional and the project is --source; --project/--pack were both
            // rejected as unrecognized arguments.
            var result = await _cli.IconPackAddAsync(packFile, ws.PluginProjectDirectory, key: null, copy: true, force: true, ct);
            StatusText = result.Succeeded
                ? "Bundled into the plugin - macrodeck-plugin build will carry it."
                : $"icon-pack add failed ({result.ExitCode}): {CliAdapter.Tools.MacroDeckCli.ExplainExitCode(result.ExitCode)}";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusText = "macrodeck-plugin CLI not found on PATH.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Bundling cancelled.";
        }
    }

    private void ExportTo(string path)
    {
        // Reuse ExportPack's archive writer without dialogs.
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var pack = new Dictionary<string, object>
        {
            ["id"] = PackId,
            ["name"] = PackName,
            ["author"] = Author,
            ["version"] = "1.0.0",
            ["ai"] = new Dictionary<string, object> { ["createdIcons"] = AiCreatedIcons },
            ["icons"] = Icons.Select(i => new Dictionary<string, object> { ["name"] = i.Name, ["file"] = i.FileName }).ToList(),
        };
        var packEntry = archive.CreateEntry("pack.json");
        using (var writer = new StreamWriter(packEntry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(JsonSerializer.Serialize(pack, new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var icon in Icons)
        {
            var entry = archive.CreateEntry(icon.FileName);
            using var target = entry.Open();
            using var source = File.OpenRead(icon.FilePath);
            source.CopyTo(target);
        }
    }
}
