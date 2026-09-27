using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Orchestrates project generation: stock template files, then every registered
/// <see cref="IProjectContentContributor"/> in order, then persistence to disk.
/// Extensions add capability scaffolders by registering contributors in DI - the
/// generator itself never changes when a new capability is added.
/// </summary>
public sealed class PluginProjectGenerator
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginProjectGenerator> _logger;

    public PluginProjectGenerator(IServiceProvider services, ILogger<PluginProjectGenerator> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>Generated file count; surfaced in the wizard's summary page.</summary>
    public int LastGeneratedFileCount { get; private set; }

    /// <summary>Renders everything in memory. Throws <see cref="GenerationException"/> on invalid options.</summary>
    public ProjectContentBuilder BuildContent(NewProjectOptions options)
    {
        Validate(options);

        var builder = new ProjectContentBuilder(options);
        MacroDeckTemplateFactory.AddStockFiles(builder);

        var contributors = _services.GetServices<IProjectContentContributor>()
            .Where(c => c.AppliesTo(options))
            .OrderBy(c => c.Order)
            .ToList();

        foreach (var contributor in contributors)
        {
            _logger.LogDebug("Contributor {Id} running", contributor.Id);
            contributor.Contribute(builder, options);
        }

        // The stock files are written first so a contributor can patch them, which means the ones
        // derived from contributor state have to be re-rendered once the contributors are done.
        MacroDeckTemplateFactory.ApplyContributorState(builder);

        return builder;
    }

    /// <summary>Generates the full project to <see cref="NewProjectOptions.ParentDirectory"/>&lt;FolderName&gt;.</summary>
    public string Generate(NewProjectOptions options)
    {
        var builder = BuildContent(options);
        var targetRoot = Path.Combine(options.ParentDirectory, options.FolderName);
        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new GenerationException($"Target directory '{targetRoot}' already exists and is not empty.");
        }

        foreach (var (relative, content) in builder.Files)
        {
            var path = Path.Combine(targetRoot, ProjectContentBuilder.ToOsPath(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        }

        LastGeneratedFileCount = builder.Files.Count;
        _logger.LogInformation("Generated {Count} files at {Root}", builder.Files.Count, targetRoot);
        return targetRoot;
    }

    private static void Validate(NewProjectOptions o)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(o.PluginName))
        {
            problems.Add("Plugin name is required.");
        }
        if (!Core.Utils.MacroDeckRules.IsValidPluginId(o.PluginId))
        {
            problems.Add($"Plugin id '{o.PluginId}' is not a valid reverse-domain id (com.example.my-plugin).");
        }
        if (string.IsNullOrWhiteSpace(o.Publisher))
        {
            problems.Add("Publisher is required (the Store refuses builds without it).");
        }
        if (string.IsNullOrWhiteSpace(o.ParentDirectory) || !Directory.Exists(o.ParentDirectory))
        {
            problems.Add($"Parent directory '{o.ParentDirectory}' does not exist.");
        }
        if (o.Platforms.Count == 0)
        {
            problems.Add("At least one platform must be selected.");
        }
        var projectName = o.ProjectName ?? o.PluginName.Replace(" ", "");
        if (!Core.Utils.MacroDeckRules.IsValidCSharpIdentifier(projectName))
        {
            problems.Add($"Project name '{projectName}' is not a valid C# name.");
        }
        foreach (var tag in o.Languages.Where(t => !Core.Utils.MacroDeckRules.IsValidLanguageTag(t)))
        {
            problems.Add($"Language tag '{tag}' is not a valid BCP-47 tag.");
        }
        if (problems.Count > 0)
        {
            throw new GenerationException(string.Join(Environment.NewLine, problems));
        }
    }
}

/// <summary>Aggregated generation failure; the message is user-facing.</summary>
public sealed class GenerationException(string message) : Exception(message);
