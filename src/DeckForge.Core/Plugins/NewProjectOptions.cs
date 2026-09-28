namespace DeckForge.Core.Plugins;

/// <summary>
/// Platform/feature options collected by the New Project wizard before generation runs.
/// Mirrors the parameters of `dotnet new macrodeck-plugin` plus DeckForge extras.
/// </summary>
public sealed record NewProjectOptions
{
    /// <summary>Display name, e.g. "Acme Light Control".</summary>
    public required string PluginName { get; init; }

    /// <summary>Reverse-domain id, e.g. "com.acme.light-control".</summary>
    public required string PluginId { get; init; }

    /// <summary>Publisher shown in the Store, e.g. "Acme".</summary>
    public required string Publisher { get; init; }

    /// <summary>Target directory; the project folder is created inside it.</summary>
    public required string ParentDirectory { get; init; }

    /// <summary>C# project name; defaults to the stripped plugin name.</summary>
    public string? ProjectName { get; init; }

    /// <summary>SPDX license identifier, e.g. "MIT".</summary>
    public string License { get; init; } = "MIT";

    /// <summary>Description shown in the Store listing.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// The plugin's version, which the manifest declares.
    /// </summary>
    /// <remarks>
    /// It was hardcoded to 1.0.0 in the template, so a project generated for a 2.0 plugin shipped a
    /// manifest claiming 1.0.0 - and the version is part of the artifact's identity, so
    /// <c>macrodeck-plugin</c> and the Creator Portal both see the wrong one.
    /// </remarks>
    public string Version { get; init; } = "1.0.0";

    /// <summary>Absolute https URL of the plugin repository (publication-required).</summary>
    public string? Repository { get; init; }

    /// <summary>Absolute https homepage URL (recommended).</summary>
    public string? Homepage { get; init; }

    /// <summary>Platform entrypoints to declare. Default: Windows only.</summary>
    public IReadOnlyList<string> Platforms { get; init; } = ["win-x64"];

    /// <summary>Self-contained publishes (larger); default is framework-dependent on .NET 10.</summary>
    public bool SelfContained { get; init; }

    /// <summary>Additional resx language tags to scaffold, e.g. ["de"].</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Capability presets to scaffold beyond the example action.</summary>
    public IReadOnlyList<string> CapabilityPresets { get; init; } = [];

    /// <summary>
    /// Host permissions to declare, beyond the ones the chosen capabilities imply.
    /// </summary>
    /// <remarks>
    /// The manifest template emitted no permissions array at all, so a plugin that called the host
    /// for config or devices shipped without declaring it - and the host grants a plugin only what
    /// its manifest asks for, so the failure shows up at run time rather than at build time.
    /// </remarks>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>Create a git repository with an initial commit.</summary>
    public bool InitGit { get; init; } = true;

    /// <summary>
    /// The project name when one was given, otherwise the plugin name with its spaces removed.
    /// </summary>
    /// <remarks>
    /// This expression was written out in four places - here twice, once on the content builder
    /// and once in the template factory - so a fifth caller could easily have picked a different
    /// derivation and produced a folder name that did not match its namespace or its assembly.
    /// </remarks>
    public string EffectiveProjectName => ProjectName ?? PluginName.Replace(" ", "");

    /// <summary>Project folder name; defaults to the project name.</summary>
    public string FolderName => EffectiveProjectName;

    /// <summary>Namespace root = project name.</summary>
    public string RootNamespace => EffectiveProjectName;
}
