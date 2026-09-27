using DeckForge.Core.Plugins;

namespace DeckForge.Core.Workspace;

/// <summary>
/// Reads/creates DeckForge's private per-workspace state (.deckforge/settings.json) and
/// tracks the currently open workspace. Also serializes to/from JSON for persistence.
/// </summary>
public sealed class WorkspaceManager
{
    public WorkspaceContext? Current { get; private set; }

    public event Action<WorkspaceContext?>? CurrentChanged;

    public void Open(WorkspaceContext context)
    {
        Current = context;
        Directory.CreateDirectory(context.DeckForgeStateDirectory);
        CurrentChanged?.Invoke(context);
    }

    public void Close()
    {
        Current = null;
        CurrentChanged?.Invoke(null);
    }

    /// <summary>Reconstructs a workspace from a plugin project directory (dir of manifest.json).
    /// Reads real identity from manifest.json when present.</summary>
    public static WorkspaceContext FromPluginProject(string pluginProjectDirectory)
    {
        var manifestPath = Path.Combine(pluginProjectDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("manifest.json not found", manifestPath);
        }

        var root = Path.GetFullPath(Path.Combine(pluginProjectDirectory, "..", ".."));
        var projectName = Path.GetFileName(pluginProjectDirectory.TrimEnd(Path.DirectorySeparatorChar));

        string pluginName = projectName;
        string pluginId = "unknown";
        try
        {
            var doc = Plugins.ManifestDocument.Load(manifestPath);
            pluginName = doc.Name;
            pluginId = doc.Id;
        }
        catch (Exception)
        {
            // Fall back to folder-derived identity; the manifest editor will surface parse errors.
        }

        var options = new NewProjectOptions
        {
            PluginName = pluginName,
            PluginId = pluginId,
            Publisher = "",
            ParentDirectory = root,
            ProjectName = projectName,
        };
        return new WorkspaceContext(Path.Combine(root, projectName + ".slnx"), options);
    }
}
