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
    /// <summary>
    /// Opens a solution, choosing the plugin project under <c>src/</c>.
    /// </summary>
    /// <remarks>
    /// This used to take the first directory it found under <c>src/</c>, which is alphabetical and
    /// therefore arbitrary: a solution with a shared library or a second project beside the plugin
    /// opened the wrong one, or nothing at all if the first entry had no manifest. Now every
    /// candidate is checked for a manifest, and a solution with more than one is reported rather
    /// than guessed at - picking the wrong project means editing the wrong files.
    /// </remarks>
    public bool TryOpenSolution(string solutionPath, out WorkspaceContext? context, out string problem)
    {
        context = null;
        problem = "";

        if (!File.Exists(solutionPath))
        {
            problem = $"{solutionPath} does not exist.";
            return false;
        }

        var root = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var srcDir = Path.Combine(root, "src");
        if (!Directory.Exists(srcDir))
        {
            problem = $"{solutionPath} has no src folder.";
            return false;
        }

        var candidates = Directory
            .GetDirectories(srcDir)
            .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        switch (candidates.Count)
        {
            case 0:
                problem = "No plugin project with a manifest.json under src/.";
                return false;
            case 1:
                break;
            default:
                problem = "This solution has more than one plugin under src/ ("
                    + string.Join(", ", candidates.Select(Path.GetFileName))
                    + "). Open the plugin's manifest.json instead.";
                return false;
        }

        context = WorkspaceManager.FromPluginProject(candidates[0]);
        context.SolutionPath = solutionPath;
        return true;
    }

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
