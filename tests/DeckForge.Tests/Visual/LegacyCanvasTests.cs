using DeckForge.Core.Plugins;
using DeckForge.Core.Visual;
using DeckForge.Core.Workspace;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Finding a retired <c>.blocks.json</c> in a workspace, migrating it, and keeping the user's original.
/// </summary>
/// <remarks>
/// <para>
/// §28.5 row 10 said "half", and the half that was missing is the half a user has: the migration existed and
/// nothing ever called it, because the only workspace it worked for was one somebody had already copied the
/// file for by hand. These tests are about the finding and about what is left behind afterwards — the second
/// is the half that is easy to get wrong, because a migration that overwrites its own safety copy is worse
/// than one that does nothing at all.
/// </para>
/// <para>
/// Every test writes into its own temp directory for the reason <c>VisualStoreTests</c> gives: a shared
/// directory makes a test pass or fail depending on which ran first, which is the worst property a test
/// meant to be evidence can have.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LegacyCanvasTests
{
    private TempDirectory _temp = null!;

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-legacy-tests");
        _root = _temp.Root;
        Directory.CreateDirectory(PluginDirectory());
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    [Test]
    [Description("28.5 row 10 - a workspace holding only a legacy sidecar migrates on load.")]
    public void A_workspace_with_only_a_legacy_canvas_migrates_it()
    {
        var workspace = Workspace();
        WriteLegacy("log-message.blocks.json", LegacyProgram());

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(load.Project, Is.Not.Null);
            Assert.That(load.Recovered, Is.True,
                "the page has to be able to tell a migration apart from an ordinary read, or the user is "
                + "told they have unsaved work on a canvas that is only just theirs");
            Assert.That(load.Project!.Targets.Single().Scripts.Single().Body, Is.Not.Empty,
                "a migration that produces an empty canvas has lost the work it was supposed to keep");
            Assert.That(File.Exists(VisualStore.PathFor(workspace)), Is.True,
                "the migrated canvas is written out, so the migration happens once rather than on every open");
        });
    }

    [Test]
    [Description("28.5 row 10 - the untouched original is kept beside the canvas.")]
    public void The_untouched_original_is_kept_beside_the_file_it_came_from()
    {
        var workspace = Workspace();
        var original = WriteLegacy("log-message.blocks.json", LegacyProgram());
        var before = File.ReadAllText(original);

        var load = VisualStore.Load(workspace);
        var kept = Path.Combine(PluginDirectory(), "log-message.blocks.original.json");

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(load.KeptOriginalPath, Is.EqualTo(kept),
                "the result carries the path so the page can name a file the user can go and open, rather "
                + "than only describing it");
            Assert.That(load.Message, Does.Contain("log-message.blocks.original.json"),
                "the message has to name it: 'your original is still there' is not actionable, and this "
                + "is the one sentence the user reads about the copy");
            Assert.That(File.Exists(kept), Is.True);
            Assert.That(File.ReadAllText(kept), Is.EqualTo(before),
                "a safety copy that is not byte-identical is not a safety copy");
            Assert.That(File.ReadAllText(original), Is.EqualTo(before),
                "the file itself is never moved, rewritten or deleted — a migration that consumes its input "
                + "has one failure left instead of two");
        });
    }

    [Test]
    [Description("28.5 row 10 - loading twice must not claim a second migration or refresh the copy.")]
    public void Loading_twice_does_not_migrate_twice_or_touch_the_kept_original()
    {
        var workspace = Workspace();
        WriteLegacy("log-message.blocks.json", LegacyProgram());

        var first = VisualStore.Load(workspace);
        var migrated = VisualStore.Serialize(first.Project!);

        var kept = first.KeptOriginalPath!;
        var keptText = File.ReadAllText(kept);

        // Something a user would do, and the thing a refresh would destroy: they open the preserved copy,
        // read it, and close it again. The timestamp is the evidence — a rewritten file is a new file.
        var backdated = File.GetLastWriteTimeUtc(kept).AddDays(-3);
        File.SetLastWriteTimeUtc(kept, backdated);

        var second = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(second.Ok, Is.True, second.Message);
            Assert.That(second.Recovered, Is.False,
                "the second load is an ordinary read of canvas.json, and saying otherwise would tell a user "
                + "their work was recovered when it was simply opened");
            Assert.That(second.KeptOriginalPath, Is.Null);
            Assert.That(VisualStore.Serialize(second.Project!), Is.EqualTo(migrated),
                "the migration happened once; a second pass would re-derive fresh block ids and every diff "
                + "after it would show a rewrite");
            Assert.That(File.ReadAllText(kept), Is.EqualTo(keptText));
            Assert.That(File.GetLastWriteTimeUtc(kept), Is.EqualTo(backdated),
                "a preserved original that a second open rewrites is no longer the record of what the "
                + "workspace looked like before DeckForge touched it");
        });
    }

    [Test]
    public void A_kept_original_is_never_overwritten_even_when_the_canvas_is_deleted_by_hand()
    {
        var workspace = Workspace();
        WriteLegacy("log-message.blocks.json", LegacyProgram());

        var first = VisualStore.Load(workspace);
        var kept = first.KeptOriginalPath!;

        // The user deleted canvas.json to start again. The legacy file is still there, so the discovery
        // finds it again — and must treat the copy it already made as the record, not as something to refresh.
        File.Delete(VisualStore.PathFor(workspace));
        File.WriteAllText(kept, "{\"edited\": true}");

        var again = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(again.Ok, Is.True, again.Message);
            Assert.That(again.Recovered, Is.True,
                "a canvas that had to be deleted by hand is not there to be read, so this is a migration "
                + "again and the page must say so");
            Assert.That(File.ReadAllText(kept), Is.EqualTo("{\"edited\": true}"),
                "an existing copy is the record. Overwriting it would silently discard the only surviving "
                + "evidence of what the user's workspace looked like before");
            Assert.That(again.KeptOriginalPath, Is.EqualTo(kept));
        });
    }

    [Test]
    public void A_preserved_original_is_never_itself_a_migration_candidate()
    {
        var workspace = Workspace();
        WriteLegacy("log-message.blocks.json", LegacyProgram());
        var first = VisualStore.Load(workspace);

        // Naming is load-bearing: `log-message.blocks.original.json` must not end in `.blocks.json`, or a
        // preserved copy is a second candidate for the same migration.
        Assert.That(LegacyCanvas.Candidates(workspace).Select(Path.GetFileName).ToList(),
            Is.EqualTo(new[] { "log-message.blocks.json" }));
        Assert.That(LegacyCanvas.Candidates(workspace), Does.Not.Contain(first.KeptOriginalPath),
            "a preserved original that also looks like a legacy sidecar would be migrated over itself on a "
            + "later open");
    }

    [Test]
    public void Several_legacy_sidecars_are_read_in_the_same_order_every_time()
    {
        var workspace = Workspace();
        WriteLegacy("zebra.blocks.json", LegacyProgram("Zebra"));
        WriteLegacy("alpha.blocks.json", LegacyProgram("Alpha"));

        var candidates = LegacyCanvas.Candidates(workspace);

        Assert.That(candidates.Select(Path.GetFileName).ToList(), Is.EqualTo(new[] { "alpha.blocks.json", "zebra.blocks.json" }),
            "A workspace holding two actions' canvases must migrate the same one every time, and a "
            + "migration that picked at random would leave the user with a document that is half of one "
            + "action and half of another. Sorted by path, because nothing else in a file system promises "
            + "an order.");
    }

    [Test]
    public void A_file_named_like_a_sidecar_that_is_not_one_is_named_and_left_alone()
    {
        var workspace = Workspace();
        var stranger = WriteLegacy("notes.blocks.json", "{ \"hello\": \"world\" }");

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.False);
            Assert.That(load.Message, Does.Contain("notes.blocks.json"),
                "the message has to name the file, or the user has to go looking for which one of their "
                + "files DeckForge decided was not one");
            Assert.That(load.Message, Does.Contain("left alone"));
            Assert.That(File.ReadAllText(stranger), Is.EqualTo("{ \"hello\": \"world\" }"));
            Assert.That(File.Exists(VisualStore.PathFor(workspace)), Is.False,
                "an unreadable sidecar must not produce an empty canvas file, which would then be an "
                + "ordinary read on the next open and the message would never be seen again");
        });
    }

    [Test]
    public void A_build_output_copy_of_a_sidecar_is_not_a_legacy_canvas()
    {
        var workspace = Workspace();
        var build = Path.Combine(PluginDirectory(), "obj", "Debug");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, "log-message.blocks.json"), LegacyProgram());

        Assert.That(LegacyCanvas.Candidates(workspace), Is.Empty,
            "a build output directory is not where a user put anything, and migrating from it would "
            + "recover a canvas nobody ever edited");
    }

    [Test]
    public void A_workspace_with_no_legacy_canvas_still_says_it_has_no_canvas()
    {
        var load = VisualStore.Load(Workspace());

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.False);
            Assert.That(load.Message, Does.Contain("no canvas"),
                "the difference between 'no file' and 'bad file' is the difference between opening a new "
                + "document and refusing to start");
            Assert.That(load.KeptOriginalPath, Is.Null);
        });
    }

    [Test]
    public void The_kept_copy_sits_beside_the_original_and_keeps_the_action_id()
    {
        var kept = LegacyCanvas.KeptPathFor(Path.Combine(PluginDirectory(), "log-message.blocks.json"));

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(kept), Is.EqualTo("log-message.blocks.original.json"),
                "beside rather than under a `legacy/` directory: a directory that only ever holds "
                + "migrations is one the user has to know about before they can find it, and the action id "
                + "keeps two actions' originals in one plugin directory distinguishable");
            Assert.That(kept, Does.Not.EndWith(".blocks.json"),
                "so a preserved copy is never itself a candidate for migration");
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private string PluginDirectory() => Path.Combine(_root, "src", "TestPlugin");

    private string WriteLegacy(string name, string text)
    {
        var path = Path.Combine(PluginDirectory(), name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>A legacy document of the shape the retired Blocks page wrote.</summary>
    /// <remarks>
    /// The statements carry a <c>kind</c> discriminator, because <c>BlockStatement</c> is polymorphic and
    /// the serializer says so on the type. Written out by hand rather than serialised, so a change to the
    /// legacy format shows up here as a failing migration rather than as a test that follows whatever the
    /// serializer now does.
    /// </remarks>
    private static string LegacyProgram(string template = "Hello from the old canvas") => $$"""
        {
          "version": 1,
          "targetActionId": "log-message",
          "targetFile": "LogMessageAction.cs",
          "statements": [
            { "kind": "log", "template": "{{template}}", "level": "Information" }
          ]
        }
        """;

    /// <summary>
    /// A workspace shaped like a generated one, without generating it.
    /// </summary>
    /// <remarks>
    /// The derived paths come from the solution path and the project name alone, which is what this test
    /// needs to control: a generated plugin would also bring a manifest nobody here cares about.
    /// </remarks>
    private WorkspaceContext Workspace() => new(
        Path.Combine(_root, "TestPlugin.slnx"),
        new NewProjectOptions
        {
            PluginName = "Test Plugin",
            PluginId = "test-plugin",
            Publisher = "Tests",
            ParentDirectory = _root,
            ProjectName = "TestPlugin",
        });
}