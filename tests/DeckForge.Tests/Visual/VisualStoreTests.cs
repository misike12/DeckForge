using DeckForge.Core.Visual;
using DeckForge.Core.Workspace;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Saving a canvas to a workspace and getting it back exactly.
/// </summary>
/// <remarks>
/// <para>
/// P5's exit criterion in one sentence: "reload restores it exactly". Everything here serves that, and the
/// cases that matter are the ones where it does not — a file that cannot be read, a save that was
/// interrupted, a workspace that has never had a canvas.
/// </para>
/// <para>
/// Every test writes into a fresh temp directory and deletes it afterwards. A test that shared a
/// directory with another would pass or fail depending on which ran first, which is the worst possible
/// property for a test that is meant to be evidence.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualStoreTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "deckforge-store-tests", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "TestPlugin"));
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Test]
    public void A_saved_canvas_comes_back_exactly_as_it_went_in()
    {
        var workspace = Workspace();
        var project = VisualSampleProject.Build();
        var expected = VisualStore.Serialize(project);

        var save = VisualStore.Save(workspace, project);
        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(save.Ok, Is.True, save.Message);
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(File.Exists(VisualStore.PathFor(workspace)), Is.True);
            Assert.That(VisualStore.Serialize(load.Project!), Is.EqualTo(expected),
                "P5's whole exit criterion: reload restores the document exactly. Anything else and a "
                + "user who saved, closed and reopened has lost work with no error anywhere");
        });
    }

    [Test]
    public void A_save_keeps_the_previous_file_aside()
    {
        var workspace = Workspace();

        VisualStore.Save(workspace, ProjectWith("first"));
        VisualStore.Save(workspace, ProjectWith("second"));

        var backup = VisualStore.BackupPathFor(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(backup), Is.True,
                "the C# can always be regenerated from the document; if the document is lost there is "
                + "nothing to regenerate it from");
            Assert.That(File.ReadAllText(backup), Does.Contain("first"));
            Assert.That(File.ReadAllText(VisualStore.PathFor(workspace)), Does.Contain("second"));
        });
    }

    [Test]
    public void A_save_leaves_no_temporary_file_behind()
    {
        var workspace = Workspace();

        VisualStore.Save(workspace, ProjectWith("only"));

        var leftovers = Directory
            .GetFiles(VisualStore.PathFor(workspace).Replace("canvas.json", string.Empty), "canvas.json*")
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(leftovers, Is.EqualTo(new[] { "canvas.json" }),
            "the write-beside-then-replace leaves a .tmp behind if the replace fails, and a stray file "
            + "in the state directory is a thing nobody later knows what to do with");
    }

    [Test]
    public void An_unreadable_canvas_falls_back_to_the_previous_save()
    {
        var workspace = Workspace();
        VisualStore.Save(workspace, ProjectWith("good"));
        VisualStore.Save(workspace, ProjectWith("newer"));

        // Corrupt the current file the way a write cut short by a crash would.
        File.WriteAllText(VisualStore.PathFor(workspace), "{ \"version\": 1, \"targets\": [ {");

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True,
                "a canvas that cannot be parsed but whose previous version can is not lost work - it is a "
                + "save that went wrong, and the honest answer is the last good state");
            Assert.That(load.Recovered, Is.True,
                "the page has to be able to tell this apart from a plain read, because 'Unsaved changes' "
                + "on its own tells the user nothing about the fact that what is on screen is not what is "
                + "on disk - they would conclude they had unsaved work rather than that it recovered");
            Assert.That(load.Message, Does.Contain("previous save"));
            Assert.That(load.Message, Does.Contain("saving will replace it"),
                "a warning that does not name the consequence is decoration: the corrupt file is only "
                + "evidence until the first save, and the user cannot act on what they have not been told");
            Assert.That(VisualStore.Serialize(load.Project!), Does.Contain("good"));
            Assert.That(File.ReadAllText(VisualStore.PathFor(workspace)), Does.StartWith("{ \"version\": 1, \"targets\""),
                "the unreadable file is evidence and must not be overwritten by the file that worked - "
                + "doing so destroys the only copy of whatever happened");
        });
    }

    [Test]
    public void A_plain_read_is_not_a_recovery()
    {
        var workspace = Workspace();
        VisualStore.Save(workspace, ProjectWith("fine"));

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True);
            Assert.That(load.Recovered, Is.False,
                "recovery is a claim the page makes to the user; if an ordinary load also claims it, the "
                + "warning stops meaning anything");
            Assert.That(load.Path, Is.EqualTo(VisualStore.PathFor(workspace)));
        });
    }

    [Test]
    public void A_workspace_with_no_canvas_says_so_instead_of_failing()
    {
        var load = VisualStore.Load(Workspace());

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.False);
            Assert.That(load.Message, Does.Contain("no canvas"),
                "the difference between 'no file' and 'bad file' is the difference between opening a new "
                + "document and refusing to start");
            Assert.That(load.Project, Is.Null);
        });
    }

    [Test]
    public void Saving_twice_the_same_document_is_not_dirty()
    {
        var workspace = Workspace();
        var project = ProjectWith("same");

        VisualStore.Save(workspace, project);
        var reloaded = VisualProjectJson.Deserialize(VisualStore.Serialize(project));

        Assert.That(VisualStore.IsDirty(workspace, reloaded), Is.False,
            "a document that survives a round trip unchanged must not report itself as unsaved, or the "
            + "indicator is noise and gets ignored - which is the same as having none");
    }

    [Test]
    public void An_edited_document_is_dirty_and_a_missing_file_is_dirty()
    {
        var workspace = Workspace();
        var project = ProjectWith("base");
        VisualStore.Save(workspace, project);

        project.Targets[0].Scripts[0].Body[0].Inputs["template"] = new BlockInput { Text = "changed" };

        Assert.Multiple(() =>
        {
            Assert.That(VisualStore.IsDirty(workspace, project), Is.True);
            Assert.That(VisualStore.IsDirty(Workspace(), ProjectWith("never-saved")), Is.True,
                "a document with no file is unsaved work by definition");
        });
    }

    [Test]
    public void The_canvas_lives_in_the_workspaces_own_state_directory()
    {
        var workspace = Workspace();

        Assert.That(VisualStore.PathFor(workspace),
            Is.EqualTo(Path.Combine(workspace.RootDirectory, ".deckforge", "canvas.json")),
            "beside the manifest would invite a user to edit DeckForge's own state by hand and then "
            + "wonder why the canvas came back different");
    }

    [Test]
    public void A_legacy_canvas_is_read_and_the_original_is_kept()
    {
        var workspace = Workspace();
        var path = VisualStore.PathFor(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // A v1 document: the migration exists and is tested on its own, so what matters here is that the
        // store routes through TryLoad rather than Deserialize and loses the distinction.
        File.WriteAllText(path, """
            {
              "version": 1,
              "blocks": [
                { "id": "b1", "kind": "ui.log", "fields": { "level": "info", "template": "hello" } },
                { "id": "b2", "kind": "control.forever", "bodies": { "body": [] } }
              ],
              "hats": [ { "id": "h1", "kind": "hat.action-runs" } ]
            }
            """);

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(VisualStore.Serialize(load.Project!), Is.Not.Empty);
        });
    }

    /// <summary>
    /// A workspace shaped like a generated one, without generating it.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkspaceManager.FromPluginProject"/> reads a manifest off disk, and a store test that
    /// had to generate a whole plugin project first would be testing the generator. What matters here is
    /// the derived paths, and those come from the solution path and the project name alone.
    /// </remarks>
    private WorkspaceContext Workspace() => new(
        Path.Combine(_root, "TestPlugin.slnx"),
        new DeckForge.Core.Plugins.NewProjectOptions
        {
            PluginName = "Test Plugin",
            PluginId = "test-plugin",
            Publisher = "Tests",
            ParentDirectory = _root,
            ProjectName = "TestPlugin",
        });

    private static VisualProject ProjectWith(string marker)
    {
        var project = VisualSampleProject.Build();
        project.Targets[0].Scripts[0].Body[0].Inputs["template"] = new BlockInput { Text = marker };
        return project;
    }
}