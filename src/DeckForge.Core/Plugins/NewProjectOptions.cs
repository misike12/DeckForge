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

    /// <summary>Create a git repository with an initial commit.</summary>
    public bool InitGit { get; init; } = true;

    /// <summary>Project folder name; defaults to ProjectName.</summary>
    public string FolderName => ProjectName ?? PluginName.Replace(" ", "");

    /// <summary>Namespace root = project name.</summary>
    public string RootNamespace => ProjectName ?? PluginName.Replace(" ", "");
}
