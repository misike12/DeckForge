using DeckForge.Core.Plugins;
using DeckForge.Core.Workspace;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Where a workspace's palette memory is written and read back.
/// </summary>
/// <remarks>
/// The point of the file is that a habit survives closing the window. The point of the *separate* file is
/// that saving a habit must never dirty a script — so the tests here are mostly about the two not being
/// able to hurt each other, and about a broken file not being able to stop the palette opening.
/// </remarks>
[TestFixture]
public sealed class PaletteStoreTests
{
    private static string Root => _root;

    private static string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "deckforge-palette-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green suite over.
        }
    }

    [Test]
    public void A_workspace_with_no_file_starts_with_an_empty_palette_rather_than_an_error()
    {
        var (memory, restored) = PaletteStore.Load(Workspace(), Known("a", "b"));

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False);
            Assert.That(memory.Recents, Is.Empty);
            Assert.That(memory.Favourites, Is.Empty);
        });
    }

    [Test]
    public void What_was_written_comes_back_in_the_same_order()
    {
        var workspace = Workspace();
        var memory = new PaletteMemory();
        memory.Note("b");
        memory.Note("a");
        memory.ToggleFavourite("a");

        var (path, saved) = PaletteStore.Save(workspace, memory);
        var (again, restored) = PaletteStore.Load(workspace, Known("a", "b"));

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.True);
            Assert.That(path, Does.EndWith("palette.json"));
            Assert.That(restored, Is.True);
            Assert.That(again.Recents, Is.EqualTo(new[] { "a", "b" }),
                "the newest first, because a round trip that reverses the row is worse than no row");
            Assert.That(again.Favourites, Is.EqualTo(new[] { "a" }));
        });
    }

    [Test]
    public void The_palette_lives_beside_the_canvas_and_not_inside_it()
    {
        var workspace = Workspace();

        var memory = new PaletteMemory();
        memory.Note("a");

        PaletteStore.Save(workspace, memory);

        Assert.Multiple(() =>
        {
            Assert.That(
                PaletteStore.PathFor(workspace),
                Does.StartWith(workspace.DeckForgeStateDirectory),
                "same directory as the canvas");

            Assert.That(
                PaletteStore.FileName,
                Is.Not.EqualTo(VisualStore.FileName),
                "and a different file, so pinning a block never makes the user's script look modified");
        });
    }

    [Test]
    public void A_file_that_cannot_be_parsed_gives_an_empty_palette_and_leaves_the_file_alone()
    {
        var workspace = Workspace();
        Directory.CreateDirectory(workspace.DeckForgeStateDirectory);
        File.WriteAllText(PaletteStore.PathFor(workspace), "{ not json");

        var (memory, restored) = PaletteStore.Load(workspace, Known("a"));

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.False, "a broken habit file is an empty habit, not a failed load");
            Assert.That(memory.Recents, Is.Empty);
            Assert.That(File.ReadAllText(PaletteStore.PathFor(workspace)), Is.EqualTo("{ not json"),
                "and the file is left as it was, so the user can look at it or send it to whoever");
        });
    }

    [Test]
    public void A_block_the_catalogue_has_since_dropped_does_not_come_back_from_the_file()
    {
        var workspace = Workspace();
        var memory = new PaletteMemory();
        memory.Note("still.here");
        memory.Note("gone.away");
        memory.ToggleFavourite("gone.away");
        PaletteStore.Save(workspace, memory);

        // The catalogue moved on since the file was written, which is the whole reason loading prunes.
        var (again, _) = PaletteStore.Load(workspace, Known("still.here"));

        Assert.Multiple(() =>
        {
            Assert.That(again.Recents, Is.EqualTo(new[] { "still.here" }));
            Assert.That(again.Favourites, Is.Empty);
        });
    }

    [Test]
    public void Two_workspaces_do_not_share_a_palette()
    {
        // Per workspace, not per application: two plugins in one folder are two projects with two authors,
        // and a recency list is a fact about a person.
        var one = Workspace("one");
        var two = Workspace("two");

        var memory = new PaletteMemory();
        memory.Note("only.mine");
        PaletteStore.Save(one, memory);

        var (theirs, _) = PaletteStore.Load(two, Known("only.mine"));

        Assert.That(theirs.Recents, Is.Empty);
    }

    [Test]
    public void A_save_that_cannot_be_written_says_so_instead_of_throwing_out_of_a_star_click()
    {
        // The write-failure arm had no test, because provoking one means making the directory unwritable.
        // A file in the way of the directory does it without changing any permissions, and it is the
        // closest thing to the real case: the state path exists as a file because something else made it one.
        var workspace = Workspace();
        var state = Path.Combine(Root, "probe", ".deckforge");
        Directory.Delete(state, recursive: true);
        File.WriteAllText(state, "not a directory");

        var memory = new PaletteMemory();
        memory.Note("control.wait");

        var result = default((string Path, bool Saved));

        Assert.That(() => result = PaletteStore.Save(workspace, memory), Throws.Nothing,
            "a habit is not worth an exception out of a click");
        Assert.That(result.Saved, Is.False, "and the caller is told it did not happen");
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static WorkspaceContext Workspace(string name = "probe")
    {
        var parent = Path.Combine(Root, name);
        var state = Path.Combine(parent, ".deckforge");
        Directory.CreateDirectory(state);

        return new WorkspaceContext(
            Path.Combine(parent, name + ".slnx"),
            new NewProjectOptions
            {
                PluginName = name,
                PluginId = "com.example." + name,
                Publisher = "Example",
                ParentDirectory = parent,
                ProjectName = name,
            });
    }

    private static HashSet<string> Known(params string[] kinds) => new(kinds, StringComparer.Ordinal);
}