using DeckForge.Core.Plugins;

namespace DeckForge.Core.Workspace;

/// <summary>
/// An open DeckForge workspace: a generated Macro Deck plugin project. Exposes the derived
/// layout the UI and services rely on; the files on disk stay 100% stock Macro Deck template,
/// so any template-based plugin opens here and every DeckForge project opens in any IDE.
/// </summary>
public sealed class WorkspaceContext
{
    public WorkspaceContext(string solutionPath, NewProjectOptions options)
    {
        SolutionPath = solutionPath;
        Options = options;
    }

    /// <summary>Path to the .slnx (workspace root).</summary>
    public string SolutionPath { get; set; }

    /// <summary>The options the project was generated with (persisted in .deckforge/settings.json).</summary>
    public NewProjectOptions Options { get; }

    public string RootDirectory => Path.GetDirectoryName(SolutionPath)!;
    public string ProjectName => Options.ProjectName ?? Options.PluginName.Replace(" ", "");
    /// <summary>src/&lt;ProjectName&gt; - the directory holding manifest.json.</summary>
    public string PluginProjectDirectory => Path.Combine(RootDirectory, "src", ProjectName);
    public string ManifestPath => Path.Combine(PluginProjectDirectory, "manifest.json");
    public string BuildConfigPath => Path.Combine(PluginProjectDirectory, "macrodeck-build.json");
    public string LocalizationDirectory => Path.Combine(PluginProjectDirectory, "Localization");
    public string AssetsDirectory => Path.Combine(PluginProjectDirectory, "Assets");
    public string TestsDirectory => Path.Combine(RootDirectory, "tests", ProjectName + ".Tests");
    public string ArtifactsDirectory => Path.Combine(RootDirectory, "artifacts");
    /// <summary>DeckForge-private per-workspace state (block files cache, window layout).</summary>
    public string DeckForgeStateDirectory => Path.Combine(RootDirectory, ".deckforge");
    public string EntryExecutableName =>
        OperatingSystem.IsWindows() ? ProjectName + ".exe" : ProjectName;

    public string? ManifestEntrypointFor(string rid) =>
        Options.Platforms.Contains(rid) ? rid : null;
}
