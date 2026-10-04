using DeckForge.Core.Plugins;
using DeckForge.Core.Visual;
using DeckForge.Core.Workspace;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// A canvas written by a newer DeckForge: opened, shown, and never written back.
/// </summary>
/// <remarks>
/// <para>
/// Part 22.1's table says a version above this build's is <em>read-only</em>, and §28.5 said "half":
/// the reader refused the file and the page refused to save over it. Both halves were refusals, which means
/// the one thing a user with a newer file wants — to see what is in it — did not happen. The page answered
/// a refusal by drawing the sample document, so somebody with real work in that file was looking at a
/// demonstration of a blank canvas and a sentence saying theirs had not been read.
/// </para>
/// <para>
/// So the shape changed from "refuse and return nothing" to "read it, hand it over, and mark it". What did
/// not change, and what these tests are mostly about: the file is never written back, the refusal message is
/// still the same sentence, a corrupt canvas still falls back to its backup, and nothing about the document
/// is downgraded or partially loaded.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ReadOnlyCanvasTests
{
    private TempDirectory _temp = null!;

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-readonly-tests");
        _root = _temp.Root;
        Directory.CreateDirectory(Path.Combine(_root, "src", "TestPlugin"));
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    [Test]
    [Description("28.5 row 10 - the newer file is opened, not refused.")]
    public void A_newer_canvas_is_loaded_and_flagged_read_only()
    {
        var workspace = Workspace();
        WriteNewerCanvas(workspace, out _);

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True,
                "Part 22.1's table says read-only, not unreadable: a refusal made the page draw the sample "
                + "document, so a user with real work in this file was looking at a blank canvas");
            Assert.That(load.ReadOnly, Is.True);
            Assert.That(load.Project, Is.Not.Null);
            Assert.That(load.Recovered, Is.True,
                "the document on screen is not what an ordinary read produced, and the page has to be able "
                + "to say so rather than showing the sample with Save enabled");
            Assert.That(load.Project!.ReadOnly, Is.True,
                "the marker is on the document as well as the result: the store can then refuse a save on "
                + "its own, instead of relying on one caller remembering to check");
        });

        Assert.That(load.Project!.Version, Is.EqualTo(99),
            "the document is the newer document, not a translation of it into this build's vocabulary");
        Assert.That(VisualStore.Serialize(load.Project!), Does.Contain("\"the future\""),
            "the user's own content comes back, which is the whole reason for opening it at all");
    }

    [Test]
    public void The_reason_names_the_version_mismatch_and_says_read_only()
    {
        var workspace = Workspace();
        WriteNewerCanvas(workspace, out _);

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Message, Does.Contain("newer DeckForge"));
            Assert.That(load.Message, Does.Contain("99"));
            Assert.That(load.Message, Does.Contain("read-only"));
            Assert.That(load.Message, Does.Contain("will not be overwritten"),
                "a warning that does not name the consequence is decoration, and the consequence here is "
                + "that pressing Save would replace the user's newer file with an older reading of it");
        });
    }

    [Test]
    public void A_read_only_canvas_cannot_be_saved_over_the_newer_file()
    {
        var workspace = Workspace();
        WriteNewerCanvas(workspace, out var onDisk);
        var before = File.ReadAllText(onDisk);

        var loaded = VisualStore.Load(workspace);
        loaded.Project!.Targets[0].Scripts[0].Body.Clear();

        var save = VisualStore.Save(workspace, loaded.Project);

        Assert.Multiple(() =>
        {
            Assert.That(save.Ok, Is.False);
            Assert.That(save.Message, Does.Contain("read-only"));
            Assert.That(save.Message, Does.Contain("Nothing was written"));
            Assert.That(File.ReadAllText(onDisk), Is.EqualTo(before),
                "the guard used to live only in the page's CanSave, which is a guard in one caller: this is "
                + "the check that makes it impossible rather than unlikely");
            Assert.That(File.Exists(VisualStore.BackupPathFor(workspace)), Is.False,
                "a refused save must not leave a backup of a file this build never legitimately owned, "
                + "because the next real save would copy the newer file over the only good copy");
        });
    }

    [Test]
    public void The_strict_reader_still_refuses_what_it_cannot_own()
    {
        // The writer's question — "may this build edit this file?" — is unchanged, and so is the sentence it
        // gets. A reader whose happy path handed over a newer document would be a reader every downstream
        // consumer of `TryLoad` had to second-guess.
        var json = NewerCanvasJson();

        var outcome = VisualProjectJson.TryLoad(json, out var project, out var message);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(VisualLoadOutcome.UnsupportedSchema));
            Assert.That(project.Blocks(), Is.Empty, "a newer document must not be loaded partially");
            Assert.That(message, Does.Contain("read-only"));
            Assert.That(message, Does.Contain("99"));
        });
    }

    [Test]
    public void Both_readers_say_the_same_thing_about_a_newer_file()
    {
        var json = NewerCanvasJson();

        VisualProjectJson.TryLoad(json, out _, out var refused);
        VisualProjectJson.TryLoadReadOnly(json, out _, out var readOnly);

        Assert.That(readOnly, Is.EqualTo(refused),
            "the refusal and the read-only open are one fact told to two callers, and a caller choosing "
            + "between two sentences is choosing what the user reads about their own file");
    }

    [Test]
    public void An_ordinary_canvas_is_not_read_only_and_still_round_trips()
    {
        var workspace = Workspace();
        var project = VisualSampleProject.Build();
        VisualStore.Save(workspace, project);
        var text = File.ReadAllText(VisualStore.PathFor(workspace));

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(load.ReadOnly, Is.False);
            Assert.That(load.Recovered, Is.False,
                "a read-only flag that fires on an ordinary load would train everybody to ignore it");
            Assert.That(load.Project!.ReadOnly, Is.False);
            Assert.That(VisualStore.Serialize(load.Project!), Is.EqualTo(text),
                "the marker must not be written to the sidecar: it describes this session's relationship "
                + "with a file, not the document, and serialising it would break text-stability");
            Assert.That(text, Does.Not.Contain("readOnly"),
                "a saved canvas that told the next build it was read-only would be a document that cannot "
                + "be reopened for editing anywhere");
        });
    }

    [Test]
    public void An_unreadable_canvas_still_falls_back_to_its_previous_save()
    {
        // The backup fallback is the other half of the load and must not have been traded away for the
        // read-only case: a corrupt file whose previous save is good is a save that went wrong, not a
        // document to refuse.
        var workspace = Workspace();
        VisualStore.Save(workspace, ProjectWith("good"));
        VisualStore.Save(workspace, ProjectWith("newer and unreadable"));

        File.WriteAllText(VisualStore.PathFor(workspace), "{ \"version\": 1, \"targets\": [ {");

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(load.Recovered, Is.True);
            Assert.That(load.ReadOnly, Is.False, "a corrupt file is not a newer one");
            Assert.That(VisualStore.Serialize(load.Project!), Does.Contain("good"));
        });
    }

    [Test]
    public void A_newer_canvas_is_not_replaced_by_the_backup()
    {
        // The one behaviour the read-only case must not inherit from the fallback: an older canvas.json
        // standing in for a newer one is the "silently drew the previous save" defect, and offering the
        // backup as a substitute for a file this build cannot own is exactly that.
        var workspace = Workspace();
        VisualStore.Save(workspace, ProjectWith("older"));
        var newer = NewerCanvasJson();
        File.WriteAllText(VisualStore.BackupPathFor(workspace), VisualStore.Serialize(ProjectWith("backup")));
        File.WriteAllText(VisualStore.PathFor(workspace), newer);

        var load = VisualStore.Load(workspace);

        Assert.Multiple(() =>
        {
            Assert.That(load.Ok, Is.True, load.Message);
            Assert.That(load.Path, Is.EqualTo(VisualStore.PathFor(workspace)),
                "the file the user opened is the file they were shown; a two-saves-old reading of the same "
                + "workspace would be a silent downgrade");
            Assert.That(load.ReadOnly, Is.True);
            Assert.That(File.ReadAllText(VisualStore.PathFor(workspace)), Is.EqualTo(newer));
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static string NewerCanvasJson() => """
        {
          "version": 99,
          "documentId": "future1",
          "targets": [
            {
              "id": "action:log-message",
              "name": "log-message",
              "kind": "action",
              "targetFile": "LogMessageAction.cs",
              "scripts": [
                {
                  "id": "s1",
                  "name": "main",
                  "hat": { "kind": "hat.action-runs", "id": "b1" },
                  "body": [
                    { "kind": "ui.log", "id": "b2", "fields": { "level": "Information" },
                      "inputs": { "template": { "text": "the future" } } }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private void WriteNewerCanvas(WorkspaceContext workspace, out string path)
    {
        path = VisualStore.PathFor(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, NewerCanvasJson());
    }

    private static VisualProject ProjectWith(string marker)
    {
        var project = VisualSampleProject.Build();
        project.Targets[0].Scripts[0].Body[0].Inputs["template"] = new BlockInput { Text = marker };
        return project;
    }

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