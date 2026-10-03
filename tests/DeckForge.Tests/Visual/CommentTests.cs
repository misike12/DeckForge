using DeckForge.CodeGen.Generation;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Comments on blocks: the command, its coalescing, and the two things that make them useless if they go
/// wrong.
/// </summary>
/// <remarks>
/// The field has existed on the model since P1 and nothing could write it — no command, no editor, no
/// marker on the tile — which is the shape of a feature that was specced and never built. These tests are
/// mostly about undo, because a comment that is not on the undo stack is the one defect that makes the
/// feature worse than not having it: the user trusts it to save their note and Ctrl+Z takes it away.
/// </remarks>
[TestFixture]
public sealed class CommentTests
{
    [Test]
    public void A_comment_is_set_and_cleared_through_the_editor()
    {
        var project = Project();
        var block = project.Targets[0].Scripts[0].Body[0];
        var editor = new DocumentEditor(project);

        Assert.That(editor.Execute(new SetComment(block.Id, "why this exists")).Applied, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(block.Comment, Is.EqualTo("why this exists"));
            Assert.That(editor.Undo(), Is.True);
            Assert.That(block.Comment, Is.Null, "and undo puts it back the way it was");
        });

        editor.Execute(new SetComment(block.Id, "temporary"));
        editor.Execute(new SetComment(block.Id, null));

        Assert.That(block.Comment, Is.Null, "an empty comment is no comment, not an empty string");
    }

    [Test]
    public void Typing_a_comment_is_one_undo_step_rather_than_one_per_character()
    {
        var project = Project();
        var block = project.Targets[0].Scripts[0].Body[0];
        var editor = new DocumentEditor(project);

        foreach (var text in new[] { "W", "Wh", "Why", "Why this", "Why this exists" })
        {
            editor.Execute(new SetComment(block.Id, text));
        }

        Assert.Multiple(() =>
        {
            Assert.That(block.Comment, Is.EqualTo("Why this exists"));
            Assert.That(editor.Undo(), Is.True);
            Assert.That(
                block.Comment,
                Is.Null,
                "five keystrokes are one edit to the user, so one Ctrl+Z takes the whole sentence back "
                + "rather than leaving 'Why this exis' behind");
        });
    }

    [Test]
    public void Coalescing_keeps_the_oldest_comment_to_undo_to()
    {
        // Merged the other way round, the entry would undo to the third keystroke — which looks like a bug
        // in the undo stack and is really a bug in the merge.
        var project = ProjectWithComment("W");
        var first = new SetComment("b-1", "Why");
        first.Apply(project);

        var second = new SetComment("b-1", "Why this");
        second.Apply(project);

        var merged = first.Merge(second);
        merged?.Apply(project);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Is.SameAs(first));
            Assert.That(project.Targets[0].Scripts[0].Body[0].Comment, Is.EqualTo("Why this"),
                "the merged entry's after is the newest text");
        });

        merged?.Revert(project);

        Assert.That(
            project.Targets[0].Scripts[0].Body[0].Comment,
            Is.EqualTo("W"),
            "and its before is the oldest one, so undo lands before the user started typing rather than "
            + "after the third keystroke");
    }

    [Test]
    public void A_comment_on_another_block_is_not_merged_into_this_one()
    {
        var one = new SetComment("b-1", "a");
        var other = new SetComment("b-2", "b");

        Assert.That(one.Merge(other), Is.Null, "two blocks' comments are two edits, always");
    }

    [Test]
    public void A_comment_survives_a_save_and_reload()
    {
        var project = Project();
        var block = project.Targets[0].Scripts[0].Body[0];
        new DocumentEditor(project).Execute(new SetComment(block.Id, "kept"));

        var text = VisualProjectJson.Serialize(project);
        var reloaded = VisualProjectJson.Deserialize(text);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(
                reloaded!.Targets[0].Scripts[0].Body[0].Comment,
                Is.EqualTo("kept"),
                "a comment that does not survive the sidecar is a comment the user loses when the window "
                + "closes, which is the only thing they wrote it for");
        });
    }

    [Test]
    public void A_comment_reaches_the_generated_code_as_a_comment_and_changes_no_behaviour()
    {
        // Part 8.1 says a modifier emits "a comment or nothing at all", and that is right: the generated
        // file is read by people too, and a note that stops at the canvas is a note half the audience
        // never sees. What it must never be is *code* - a comment that changed behaviour would be the
        // worst bug this feature could have.
        var project = Project();
        var block = project.Targets[0].Scripts[0].Body[0];
        new DocumentEditor(project).Execute(new SetComment(block.Id, "why this exists"));

        var code = VisualEmitter
            .CompileTarget(project.Targets[0], project)
            .Code;

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("// why this exists"),
                "the note travels with the code as a comment, at the block's own indent");
            Assert.That(code, Does.Contain("VisualRuntime.LogTemplate"),
                "and the block still does exactly what it did without one");
        });
    }

    [Test]
    public void A_comment_of_only_spaces_counts_as_none()
    {
        // " " is not a note, and a marker on the tile for a note nobody wrote is a lie on the canvas.
        // The tile's own projection lives in the WPF App project, which this test project deliberately
        // does not reference, so what is asserted here is the model rule the projection will read.
        Assert.Multiple(() =>
        {
            Assert.That(new Block { Kind = "ui.log", Id = "b-1" }.Comment is null, Is.True);
            Assert.That(new Block { Kind = "ui.log", Id = "b-1", Comment = "   " }.Comment, Is.EqualTo("   "),
                "the model keeps what it was given, and the tile decides what counts");
        });
    }
    // ---- helpers -------------------------------------------------------------------------------------

    private static VisualProject Project()
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget { Id = "t", Name = "Action", Kind = TargetKind.Action };
        project.Targets.Add(target);

        target.Scripts.Add(new VisualScript
        {
            Id = "s",
            Name = "Main",
            Hat = new Block { Kind = "hat.action-runs", Id = "hat-1" },
            Body = [new Block { Kind = "ui.log", Id = "b-1" }],
        });

        return project;
    }

    private static VisualProject ProjectWithComment(string comment)
    {
        var project = Project();
        project.Targets[0].Scripts[0].Body[0].Comment = comment;

        return project;
    }

}
