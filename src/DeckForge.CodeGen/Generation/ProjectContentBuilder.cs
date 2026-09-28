using DeckForge.Core.Plugins;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Collects everything a generation run produces. Stock template files are added first;
/// contributors then add files (relative, '/'-separated), resx keys, extra packages or
/// capability registrations. <see cref="PluginProjectGenerator"/> persists the result.
/// </summary>
public sealed class ProjectContentBuilder
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stringsKeys = new(StringComparer.Ordinal);

    public NewProjectOptions Options { get; }
    public string ProjectName { get; }
    public string RootNamespace { get; }

    public ProjectContentBuilder(NewProjectOptions options)
    {
        Options = options;
        ProjectName = options.EffectiveProjectName;
        RootNamespace = options.RootNamespace;
    }

    /// <summary>All files relative to the solution root ('/' separators, no leading slash).</summary>
    public IReadOnlyDictionary<string, string> Files => _files;

    /// <summary>Keys added to Localization/Strings.resx (key -> default-culture value).</summary>
    public IReadOnlyDictionary<string, string> StringsKeys => _stringsKeys;

    /// <summary>Extra PackageReference ids added by contributors (version resolved via CPM).</summary>
    public List<string> ExtraMacroDeckPackages { get; } = [];

    /// <summary>
    /// Host permissions the generated manifest declares, contributed alongside the code that
    /// needs them. A capability whose code calls the host but whose permission is missing fails at
    /// run time, not at build time, so the two travel together.
    /// </summary>
    public List<string> ExtraPermissions { get; } = [];

    /// <summary>Integration interfaces contributors add, e.g. "IVariableProvider".</summary>
    public List<string> ExtraIntegrationInterfaces { get; } = [];

    /// <summary>using lines added to PluginIntegration.cs.</summary>
    public List<string> ExtraIntegrationUsings { get; } = [];

    /// <summary>Field declarations + constructor assignments injected into PluginIntegration.cs.</summary>
    public List<string> ExtraIntegrationMembers { get; } = [];

    public void AddFile(string relativePath, string content) => _files[Normalize(relativePath)] = content;

    public bool HasFile(string relativePath) => _files.ContainsKey(Normalize(relativePath));

    public string? GetFile(string relativePath) => _files.TryGetValue(Normalize(relativePath), out var content) ? content : null;

    /// <summary>Adds a resx key if not already present. Returns false when it existed.</summary>
    public bool AddStringKey(string key, string defaultValue) => _stringsKeys.TryAdd(key, defaultValue);

    public static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    public static string ToOsPath(string relativePath) =>
        Normalize(relativePath).Replace('/', Path.DirectorySeparatorChar);
}
