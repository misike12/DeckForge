using DeckForge.Core.Code;
using DeckForge.Core.Plugins;

namespace DeckForge.Core.Workspace;

/// <summary>
/// An open DeckForge workspace: a generated Macro Deck plugin project. Exposes the derived
/// layout the UI and services rely on; the files on disk stay 100% stock Macro Deck template,
/// so any template-based plugin opens here and every DeckForge project opens in any IDE.
/// </summary>
public sealed class WorkspaceContext
{
    public WorkspaceContext(string solutionPath, NewProjectOptions options, string? pluginProjectDirectory = null)
    {
        SolutionPath = solutionPath;
        Options = options;
        PluginProjectDirectoryOverride = pluginProjectDirectory;
    }

    /// <summary>
    /// The plugin project's real location, when it is not the derived one.
    /// </summary>
    /// <remarks>
    /// <see cref="PluginProjectDirectory"/> is derived from the solution path and the project name,
    /// which is right for a generated DeckForge project and wrong for a plugin laid out any other
    /// way - a sibling directory, a nested folder, a plugin opened on its own. Every path on this
    /// class then pointed at directories that did not exist, and nothing complained until a page
    /// tried to write to one. Set when the caller knows where the manifest actually is.
    /// </remarks>
    public string? PluginProjectDirectoryOverride { get; }

    /// <summary>Path to the .slnx (workspace root).</summary>
    public string SolutionPath { get; set; }

    /// <summary>The options the project was generated with (persisted in .deckforge/settings.json).</summary>
    public NewProjectOptions Options { get; }

    public string RootDirectory => Path.GetDirectoryName(SolutionPath)!;
    public string ProjectName => Options.ProjectName ?? Options.PluginName.Replace(" ", "");

    /// <summary>
    /// The C# namespace the plugin's own code lives in.
    /// </summary>
    /// <remarks>
    /// The project name, because the generated project file pins
    /// <c>RootNamespace</c> to it - and because the localization source generator puts the
    /// <c>Strings</c> class there, along with the template's own <c>IIntegrationContextAware</c>.
    /// Generated code has to land in the same namespace as both or it cannot see either, which is
    /// how an action written into a namespace derived from its own id failed to build.
    /// </remarks>
    public string RootNamespace => CSharpCode.Identifier(ProjectName);
    /// <summary>src/&lt;ProjectName&gt; - the directory holding manifest.json.</summary>
    public string PluginProjectDirectory =>
        PluginProjectDirectoryOverride ?? Path.Combine(RootDirectory, "src", ProjectName);
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
