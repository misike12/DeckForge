using DeckForge.CodeGen.Generation;
using DeckForge.Core.Code;
using DeckForge.Core.Workspace;
using DeckForge.Validators;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// The defects a second review pass verified but left open, each pinned here.
/// </summary>
/// <remarks>
/// Every one of these was a crash or a false report on a path a user reaches by opening a project
/// or editing a manifest - not a code path a passing test happened to cover.
/// </remarks>
[TestFixture]
public sealed class OpenFindingsTests
{
    private static TempDirectory _temp = null!;

    private static string _root = "";

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("openfindings");
        _root = _temp.Root;
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    [Test]
    public void A_plugin_outside_src_still_gets_paths_that_exist()
    {
        // FromPluginProject assumed <root>/src/<ProjectName> and took the root as two levels up, so a
        // plugin laid out any other way produced a workspace whose every path pointed somewhere that
        // does not exist - and nothing complained until a page tried to write to one.
        var plugin = Path.Combine(_root, "beside", "Widget");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), """{"name":"Widget","id":"com.example.widget"}""");

        var context = WorkspaceManager.FromPluginProject(plugin);

        Assert.Multiple(() =>
        {
            Assert.That(context.PluginProjectDirectory, Is.EqualTo(plugin));
            Assert.That(File.Exists(context.ManifestPath), Is.True);
            Assert.That(context.AssetsDirectory, Is.EqualTo(Path.Combine(plugin, "Assets")),
                "The assets directory has to sit under the plugin that was actually opened.");
        });
    }

    [Test]
    public void A_plugin_under_src_finds_the_solution_above_it()
    {
        var plugin = Path.Combine(_root, "src", "Widget");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), """{"name":"Widget","id":"com.example.widget"}""");
        var solution = Path.Combine(_root, "Widget.slnx");
        File.WriteAllText(solution, "<Solution />");

        var context = WorkspaceManager.FromPluginProject(plugin);

        Assert.Multiple(() =>
        {
            Assert.That(context.SolutionPath, Is.EqualTo(solution));
            Assert.That(context.PluginProjectDirectory, Is.EqualTo(plugin));
        });
    }

    [Test]
    public void A_trailing_slash_does_not_end_up_in_the_project_name()
    {
        // The path was trimmed of '\' only, so a caller passing a forward-slash path - which the
        // docs, the CLI and .NET's own conventions all use - kept its slash, and that slash became
        // part of every derived path.
        var plugin = Path.Combine(_root, "src", "Widget");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), """{"name":"Widget","id":"com.example.widget"}""");

        var context = WorkspaceManager.FromPluginProject(plugin.Replace('\\', '/') + "/");

        Assert.That(context.Options.ProjectName, Is.EqualTo("Widget"));
    }

    [Test]
    public void A_workspace_that_cannot_be_opened_is_not_published_as_current()
    {
        // Current was assigned before CreateDirectory, so a read-only parent left Current and every
        // page's HasWorkspace claiming a workspace was open when none was.
        var manager = new WorkspaceManager();

        // Under src/, so with no solution above it the root falls back to two levels up and the state
        // directory is _root/.deckforge. A file there means CreateDirectory cannot succeed, so Open
        // throws instead of publishing a workspace that does not exist.
        File.WriteAllText(Path.Combine(_root, ".deckforge"), "not a directory");
        var plugin = Path.Combine(_root, "src", "Plugin");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), """{"name":"P","id":"com.example.p"}""");

        Assert.Throws<IOException>(() => manager.Open(WorkspaceManager.FromPluginProject(plugin)));

        Assert.That(manager.Current, Is.Null, "A workspace that failed to open was reported as current.");
    }

    [Test]
    public void A_duplicate_resx_key_does_not_throw()
    {
        // ToDictionary threw on the second entry. Nothing between the merger and the shell caught it,
        // so a hand-edited resx took the app down when the Localization page was opened.
        var path = Path.Combine(_root, "Strings.resx");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
              <data name="A"><value>first</value></data>
              <data name="A"><value>second</value></data>
              <data name="B"><value>other</value></data>
            </root>
            """);

        var keys = ResxMerger.ReadKeys(path);

        Assert.Multiple(() =>
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys["A"], Is.EqualTo("first"), "A duplicate key should keep the first value.");
            Assert.That(keys["B"], Is.EqualTo("other"));
        });
    }

    [Test]
    public void A_malformed_resx_is_reported_rather_than_crashing_the_page()
    {
        var path = Path.Combine(_root, "Strings.resx");
        File.WriteAllText(path, "<root><data name=\"A\"><value>x</value>");

        Assert.Throws<System.Xml.XmlException>(() => ResxMerger.ReadKeys(path));
    }

    [TestCase("3.0.0")]
    [TestCase("3.0.0-beta.14")]
    [TestCase("3.0.0-beta.14+088befbf36036b18c52aaaf5ba9597a413efd0db")]
    [TestCase(">=3.0.0")]
    [TestCase(">=3.0.0,<4.0.0")]
    [TestCase("=3.0.0")]
    [TestCase("*")]
    [TestCase(">1.0.0, <2.0.0")]
    public void A_range_the_real_cli_accepts_is_not_reported_as_invalid(string range) =>
        Assert.That(
            ManifestValidator.IsVersionRangeSyntaxForTest(range),
            Is.True,
            $"'{range}' is accepted by the CLI at every level, so DeckForge must not call it invalid.");

    [TestCase("^3.0.0")]
    [TestCase("~3.0.0")]
    [TestCase("3.x")]
    [TestCase("not-a-version")]
    [TestCase("")]
    public void A_range_the_cli_rejects_is_still_reported_as_invalid(string range) =>
        Assert.That(ManifestValidator.IsVersionRangeSyntaxForTest(range), Is.False);

    [Test]
    public void A_huge_signature_value_is_refused_rather_than_allocated()
    {
        // The buffer was stackalloc'd from the manifest's own length. A signature value of a few
        // hundred megabytes asked for that much stack, and a stack overrun kills the process with no
        // catch and no message.
        var huge = new string('A', 400_000_000);

        Assert.That(ManifestValidator.IsBase64ForTest(huge), Is.False);
    }

    [Test]
    public void An_ordinary_signature_value_still_validates()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        Assert.Multiple(() =>
        {
            Assert.That(ManifestValidator.IsBase64ForTest(key), Is.True);
            Assert.That(ManifestValidator.IsBase64ForTest("not base64!!"), Is.False);
        });
    }

    [Test]
    public void A_url_is_escaped_once()
    {
        // Xml escaped the ampersand, then the extra pass escaped it again, so "?a=1&b=2" was written
        // as "?a=1&amp;amp;b=2" and a reader decoded the separator as literal text.
        var escaped = CSharpCode.XmlUrl("https://example.com/x?a=1&b=2");

        Assert.Multiple(() =>
        {
            Assert.That(escaped, Is.EqualTo("https://example.com/x?a=1&amp;b=2"));
            Assert.That(escaped, Does.Not.Contain("&amp;amp;"));
        });
    }

    [Test]
    public void A_newline_in_generated_markup_does_not_close_a_comment()
    {
        // A generated /// comment holds its text on one line, so a newline closed the comment early
        // and the rest of the value was compiled as code.
        var escaped = CSharpCode.Xml("line one\nline two");

        Assert.Multiple(() =>
        {
            Assert.That(escaped, Does.Not.Contain("\n"));
            Assert.That(escaped, Does.Not.Contain("\r"));
            Assert.That(escaped, Is.EqualTo("line one line two"));
        });
    }

    private static string WriteMinimalPlugin(string plugin)
    {
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), """{"name":"P","id":"com.example.p"}""");
        return plugin;
    }
}
