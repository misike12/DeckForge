using DeckForge.CodeGen.Capabilities;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Template parity harness.
//
// The Macro Deck plugin template is the ground truth for what a generated project should look
// like, and "looks right" is not something a compiler or a unit test can tell you. This generates a
// project from the factory and compares it, file by file, against a copy of the official template
// scaffolded by the CLI - so a drift in whitespace, indentation or a comment shows up as a diff
// instead of as a surprise months later.
//
// Files DeckForge intentionally differs on are listed with the reason. Anything else that differs
// is a defect.
//
//   dotnet run --project tools/TemplateParity -- <official-template-dir> [output-dir]

var officialRoot = args.Length > 0 ? args[0] : null;
if (officialRoot is null || !Directory.Exists(officialRoot))
{
    Console.Error.WriteLine("usage: TemplateParity <official-template-dir> [output-dir]");
    Console.Error.WriteLine("Scaffold the reference with:");
    Console.Error.WriteLine("  macrodeck-plugin new --name Ref --id com.example.ref --output <dir>");
    return 1;
}

var outputRoot = args.Length > 1
    ? args[1]
    : Path.Combine(Path.GetTempPath(), "deckforge-parity-" + Guid.NewGuid().ToString("N")[..8]);

var services = new ServiceCollection();
services.AddSingleton<CapabilityPresetContributor>();
services.AddSingleton<IProjectContentContributor>(sp => sp.GetRequiredService<CapabilityPresetContributor>());

var generator = new PluginProjectGenerator(
    services.BuildServiceProvider(), NullLogger<PluginProjectGenerator>.Instance);

// The same identity the official reference uses, so the only variable left is the template.
var projectName = "Ref";
Directory.CreateDirectory(outputRoot);
var generated = generator.Generate(new NewProjectOptions
{
    PluginName = projectName,
    PluginId = "com.example.ref",
    Publisher = "Ex",
    Description = "A reference plugin.",
    ParentDirectory = outputRoot,
    ProjectName = projectName,
    InitGit = false,
    Repository = "https://github.com/example/ref",
    Homepage = "https://example.com/ref",
});

var Skip = (string relativePath, string reason) => (relativePath, reason);

var intentional = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    // DeckForge is a different project: its own licence and its own copyright holder, which the
    // generator parameterises from the plugin's publisher.
    ["LICENSE"] = "parameterised licence text and copyright holder",

    // DeckForge's developer documentation. The official README describes the official template's
    // own workflow; this one describes a DeckForge-generated project.
    ["README.md"] = "DeckForge's documentation for a generated project",
    ["AGENTS.md"] = "DeckForge's agent guidance",
    ["CLAUDE.md"] = "DeckForge's agent guidance",

    // DeckForge pins the SDK to a confirmed version instead of floating, so a plugin built today
    // does not silently change behaviour when a new prerelease is published.
    ["Directory.Packages.props"] = "pins 3.0.0-beta.14 instead of floating 3.0.0-*",

    // The stock example action and the integration carry the host hand-off the SDK does not
    // provide, so an action can reach the host at all. See ActionContextPatcher.
    ["src/Ref/LogMessageAction.cs"] = "wires IIntegrationContextAware",
    ["src/Ref/PluginIntegration.cs"] = "hands the host context to aware actions",

    // The contract the SDK does not have.
    ["src/Ref/IIntegrationContextAware.cs"] = "only in DeckForge, by design",

    // The official plugin declares no repository, homepage or description; DeckForge writes what
    // the wizard collected, so the reference run sets them.
    ["src/Ref/manifest.json"] = "carries the description, repository and homepage the wizard collected",

    // Formatting and identity that do not affect the build.
    [".gitignore"] = "generated from DeckForge's own template set",
    ["Ref.slnx"] = "solution layout and platform entries",
    ["src/Ref/Ref.csproj"] = "pins AssemblyName and RootNamespace",
    ["src/Ref/macrodeck-build.json"] = "targets every selected platform",
};

var ignored = new[] { ".git", "obj", "bin", "artifacts" };

var officialFiles = Directory
    .EnumerateFiles(officialRoot, "*", SearchOption.AllDirectories)
    .Select(p => Path.GetRelativePath(officialRoot, p).Replace('\\', '/'))
    .Where(p => !ignored.Any(seg => p.Split('/').Contains(seg, StringComparer.OrdinalIgnoreCase)))
    .OrderBy(p => p, StringComparer.Ordinal)
    .ToList();

var generatedFiles = Directory
    .EnumerateFiles(generated, "*", SearchOption.AllDirectories)
    .Select(p => Path.GetRelativePath(generated, p).Replace('\\', '/'))
    .Where(p => !ignored.Any(seg => p.Split('/').Contains(seg, StringComparer.OrdinalIgnoreCase)))
    .OrderBy(p => p, StringComparer.Ordinal)
    .ToList();

var identical = new List<string>();
var intentionalDiffs = new List<string>();
var unexpected = new List<string>();

foreach (var relative in officialFiles.Union(generatedFiles).Distinct(StringComparer.Ordinal))
{
    if (intentional.ContainsKey(relative))
    {
        intentionalDiffs.Add(relative);
        continue;
    }

    var officialPath = Path.Combine(officialRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    var generatedPath = Path.Combine(generated, relative.Replace('/', Path.DirectorySeparatorChar));

    if (!File.Exists(officialPath) || !File.Exists(generatedPath))
    {
        unexpected.Add($"{relative}: present in only one project");
        continue;
    }

    // Line endings are a checkout setting, not template content, so they are not a drift.
    static string Normalise(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    if (Normalise(officialPath) == Normalise(generatedPath))
    {
        identical.Add(relative);
    }
    else
    {
        unexpected.Add($"{relative}: content differs");
    }
}

Console.WriteLine($"official: {officialRoot}");
Console.WriteLine($"generated: {generated}");
Console.WriteLine();
Console.WriteLine($"identical  ({identical.Count}): {string.Join(", ", identical)}");
Console.WriteLine();
Console.WriteLine($"intentional ({intentionalDiffs.Count}):");
foreach (var relative in intentionalDiffs.OrderBy(p => p, StringComparer.Ordinal))
{
    Console.WriteLine($"  {relative} - {intentional[relative]}");
}

Console.WriteLine();

var declaredButAbsent = intentional.Keys
    .Where(k => !generatedFiles.Contains(k, StringComparer.OrdinalIgnoreCase)
             && !officialFiles.Contains(k, StringComparer.OrdinalIgnoreCase))
    .ToList();

foreach (var note in declaredButAbsent)
{
    Console.WriteLine($"note: {note} is on the intentional list but was in neither project");
}

if (unexpected.Count == 0)
{
    Console.WriteLine("no unexpected differences.");
    return 0;
}

Console.WriteLine($"UNEXPECTED ({unexpected.Count}):");
foreach (var note in unexpected)
{
    Console.WriteLine($"  {note}");
}

return 1;
