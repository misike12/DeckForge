using System.Text.Json;
using DeckForge.Core.Workspace;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for opening a solution or a plugin project.
/// </summary>
/// <remarks>
/// The solution opener took the first directory it found under <c>src/</c>, which is alphabetical
/// and therefore arbitrary - a solution with a shared library beside the plugin opened the wrong
/// project, and <c>FromPluginProject</c> then threw a FileNotFoundException that three callers let
/// escape, so choosing a folder without a manifest closed the app.
/// </remarks>
[TestFixture]
public sealed class WorkspaceOpeningTests
{
    private TempDirectory _temp = null!;

    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-ws");
        _root = _temp.Root;
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private string WriteSolution(string name, params string[] pluginFolders)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        File.WriteAllText(Path.Combine(directory, name + ".slnx"), "<Solution />");

        foreach (var folder in pluginFolders)
        {
            var pluginDirectory = Path.Combine(directory, "src", folder);
            Directory.CreateDirectory(pluginDirectory);
            File.WriteAllText(
                Path.Combine(pluginDirectory, "manifest.json"),
                $$"""
                {
                  "manifestVersion": 1,
                  "id": "com.example.{{folder.ToLowerInvariant()}}",
                  "name": "{{folder}}",
                  "version": "1.0.0",
                  "icon": "Assets/icon.svg",
                  "publisher": { "name": "Example" }
                }
                """);
        }

        return Path.Combine(directory, name + ".slnx");
    }

    [Test]
    public void A_solution_with_one_plugin_opens_it()
    {
        var solution = WriteSolution("Solo", "MyPlugin");
        var manager = new WorkspaceManager();

        Assert.That(manager.TryOpenSolution(solution, out var context, out var problem), Is.True, problem);
        Assert.That(context, Is.Not.Null);
        Assert.That(context!.ProjectName, Is.EqualTo("MyPlugin"));
        Assert.That(context.SolutionPath, Is.EqualTo(solution));
    }

    [Test]
    public void A_solution_with_two_plugins_is_reported_rather_than_guessed_at()
    {
        // Alphabetically the first is whichever name sorts first, which is not the one the user
        // meant. Opening the wrong project means editing the wrong files.
        var solution = WriteSolution("Two", "AlphaPlugin", "BetaPlugin");
        var manager = new WorkspaceManager();

        Assert.Multiple(() =>
        {
            Assert.That(manager.TryOpenSolution(solution, out _, out var problem), Is.False);
            Assert.That(problem, Does.Contain("more than one"));
            Assert.That(problem, Does.Contain("AlphaPlugin"));
            Assert.That(problem, Does.Contain("BetaPlugin"));
        });
    }

    [Test]
    public void A_project_without_a_manifest_is_not_chosen()
    {
        // src/Shared is a class library, not a plugin. It sorts before the plugin in many names, so
        // the old "first directory" rule picked it and then threw.
        var solution = WriteSolution("Mixed", "ZebraPlugin");
        Directory.CreateDirectory(Path.Combine(_root, "Mixed", "src", "AaaSharedLibrary"));
        var manager = new WorkspaceManager();

        Assert.That(manager.TryOpenSolution(solution, out var context, out var problem), Is.True, problem);
        Assert.That(context!.ProjectName, Is.EqualTo("ZebraPlugin"));
    }

    [Test]
    public void A_solution_with_no_plugin_is_reported()
    {
        var solution = WriteSolution("Empty");
        Directory.CreateDirectory(Path.Combine(_root, "Empty", "src", "NotAPlugin"));
        var manager = new WorkspaceManager();

        Assert.Multiple(() =>
        {
            Assert.That(manager.TryOpenSolution(solution, out _, out var problem), Is.False);
            Assert.That(problem, Does.Contain("manifest.json"));
        });
    }

    [Test]
    public void A_solution_with_no_src_folder_is_reported()
    {
        var directory = Path.Combine(_root, "NoSrc");
        Directory.CreateDirectory(directory);
        var solution = Path.Combine(directory, "NoSrc.slnx");
        File.WriteAllText(solution, "<Solution />");

        var manager = new WorkspaceManager();

        Assert.Multiple(() =>
        {
            Assert.That(manager.TryOpenSolution(solution, out _, out var problem), Is.False);
            Assert.That(problem, Does.Contain("src"));
        });
    }

    [Test]
    public void A_solution_that_does_not_exist_is_reported_not_thrown()
    {
        var manager = new WorkspaceManager();

        Assert.Multiple(() =>
        {
            Assert.That(
                manager.TryOpenSolution(Path.Combine(_root, "nope.slnx"), out _, out var problem),
                Is.False);
            Assert.That(problem, Does.Contain("does not exist"));
        });
    }

    [Test]
    public void Opening_a_plugin_project_without_a_manifest_throws_a_named_error()
    {
        // The throw is fine - the caller's job is to catch it, and the three that did not were the
        // bug. What matters is that the message names the file that was looked for.
        var directory = Path.Combine(_root, "NoManifest");
        Directory.CreateDirectory(directory);

        var failure = Assert.Throws<FileNotFoundException>(() => WorkspaceManager.FromPluginProject(directory));
        Assert.That(failure!.Message, Does.Contain("manifest.json"));
    }

    [Test]
    public void A_manifest_that_will_not_parse_still_yields_a_usable_workspace()
    {
        // The manifest editor is where a parse error gets fixed, so a broken manifest must still
        // open as a workspace rather than blocking the user out of the one page that can help.
        var solution = WriteSolution("Broken", "BrokenPlugin");
        File.WriteAllText(
            Path.Combine(_root, "Broken", "src", "BrokenPlugin", "manifest.json"),
            "{ this is not json");

        var manager = new WorkspaceManager();

        Assert.That(manager.TryOpenSolution(solution, out var context, out var problem), Is.True, problem);
        Assert.Multiple(() =>
        {
            Assert.That(context!.ProjectName, Is.EqualTo("BrokenPlugin"));
            Assert.That(context.Options.PluginId, Is.EqualTo("unknown"),
                "A fallback identity is better than refusing to open the project at all.");
        });
    }
}
