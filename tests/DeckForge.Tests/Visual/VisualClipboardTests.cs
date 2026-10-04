using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// §28.5's ".dfblock export and import, and clipboard copy/paste of a run", which was "not started".
/// </summary>
/// <remarks>
/// <para>
/// The parts worth testing are the ones that are easy to get quietly wrong. A payload that round-trips
/// proves very little: the dangerous outcomes are a paste that keeps the copied block's id (two blocks,
/// one id, and a duplicate in the generated code), a paste treated as a palette drag (one fresh block,
/// the rest of the stack silently gone), and a payload from a newer build imported "successfully" into a
/// document that then fails to compile.
/// </para>
/// <para>
/// Paste is a drop, so these drive <see cref="DropPlan"/> with the payload a clipboard read produces
/// rather than calling an insert directly. That is the property that matters: a pasted run lands where a
/// dragged one would and refuses where a dragged one would.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualClipboardTests
{
    private const string HatId = "hat-1";

    private static Block Wait(double seconds) => new()
    {
        Id = "w1",
        Kind = "control.wait-seconds",
        Inputs = { ["seconds"] = BlockInput.Of(seconds) },
    };

    private static VisualScript NewScript()
    {
        var script = new VisualScript
        {
            Id = "script-1",
            Name = "when action clicked",
            Hat = new Block { Id = HatId, Kind = "hat.action-runs" },
            X = 40,
            Y = 40,
        };

        script.Body.Add(Wait(1));
        return script;
    }

    private static (DocumentEditor Editor, VisualTarget Target) Editor()
    {
        var target = new VisualTarget
        {
            Id = "target-1",
            Name = "Main",
            Kind = TargetKind.Action,
            Scripts = [NewScript()],
        };

        return (new DocumentEditor(new VisualProject { DocumentId = "doc-1", Targets = [target] }), target);
    }

    private static List<Block> Body(DocumentEditor editor, VisualTarget target) =>
        DocumentLists.ListFor(editor.Project, BodyRef.ScriptBody(target.Scripts[0].Hat.Id));

    private static DropCandidate Gap(int index) =>
        new(DropTargetKind.StackGap, HatId, Index: index);

    private static DropCandidate Onto(int index) =>
        new(DropTargetKind.OntoStatement, HatId, Index: index);

    [Test]
    [Description("A copied run comes back with the same shape and the same values.")]
    public void AWriteAndReadRoundTripsTheRun()
    {
        var original = new[] { Wait(1.5), new Block { Id = "w2", Kind = "control.stop-script" } };

        var read = VisualClipboard.Read(VisualClipboard.Write(original));

        Assert.Multiple(() =>
        {
            Assert.That(read.Problem, Is.Null);
            Assert.That(read.Run, Has.Count.EqualTo(2));
            Assert.That(read.Run[0].Kind, Is.EqualTo("control.wait-seconds"));
            Assert.That(read.Run[0].InputNumber("seconds"), Is.EqualTo(1.5));
            Assert.That(read.Run[1].Kind, Is.EqualTo("control.stop-script"));
        });
    }

    [Test]
    [Description("The whole point of re-identifying: a paste must not put a second block on an id already in the document.")]
    public void PastedBlocksGetFreshIdsIncludingNestedOnes()
    {
        var source = new Block
        {
            Id = "repeat-outer",
            Kind = "control.repeat",
            Inputs = { ["times"] = BlockInput.Of(3) },
            Bodies = { ["body"] = [new Block { Id = "inner-1", Kind = "control.stop-script" }] },
        };

        var read = VisualClipboard.Read(VisualClipboard.Write([source]));

        Assert.Multiple(() =>
        {
            Assert.That(read.Problem, Is.Null);
            Assert.That(read.Run[0].Id, Is.Not.EqualTo("repeat-outer"));

            // One level down is where this hides. A re-identify that only walked the top level would leave
            // the inner block sharing an id with the document, and it would surface as a duplicate in
            // generated code rather than as an obvious paste bug.
            Assert.That(read.Run[0].Body("body")[0].Id, Is.Not.EqualTo("inner-1"));
        });
    }

    [Test]
    [Description("A nested block in a *slot* is a child too, not only the ones in a body.")]
    public void AReporterInASlotIsAlsoReidentified()
    {
        var source = new Block
        {
            Id = "set-1",
            Kind = "var.set",
            Inputs =
            {
                ["value"] = BlockInput.Of(new Block { Id = "reporter-1", Kind = "deck.client-count" }),
            },
        };

        var read = VisualClipboard.Read(VisualClipboard.Write([source]));

        Assert.That(read.Run[0].InputBlock("value")!.Id, Is.Not.EqualTo("reporter-1"));
    }

    [Test]
    [Description("Two pastes of one copy must not collide with each other.")]
    public void TwoPastesOfOneCopyDoNotCollide()
    {
        var text = VisualClipboard.Write([Wait(1)]);
        var (editor, target) = Editor();

        DropPlan.Apply(editor, target, VisualClipboard.Read(text).ToDragPayload(), Gap(1));
        DropPlan.Apply(editor, target, VisualClipboard.Read(text).ToDragPayload(), Gap(2));

        var ids = DocumentLists.IdsOf(Body(editor, target));
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), "two pastes of one copy share a block id");
    }

    [Test]
    [Description("A pasted run keeps every block. Read as a palette drag it would have become one new block.")]
    public void APastedRunInsertsWholeRatherThanBecomingOneNewBlock()
    {
        var (editor, target) = Editor();
        var read = VisualClipboard.Read(VisualClipboard.Write([Wait(1), Wait(2), Wait(3)]));

        var result = DropPlan.Apply(editor, target, read.ToDragPayload(), Gap(1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(Body(editor, target), Has.Count.EqualTo(4), "three pasted and the one already there");
            Assert.That(
                Body(editor, target).Skip(1).Select(block => block.InputNumber("seconds")),
                Is.EqualTo(new[] { 1d, 2d, 3d }));
        });
    }

    [Test]
    [Description("A paste is undoable as one gesture, like any other drop.")]
    public void APasteIsOneUndo()
    {
        var (editor, target) = Editor();
        var read = VisualClipboard.Read(VisualClipboard.Write([Wait(1), Wait(2)]));

        DropPlan.Apply(editor, target, read.ToDragPayload(), Gap(2));
        Assert.That(Body(editor, target), Has.Count.EqualTo(3));

        editor.Undo();
        Assert.That(
            Body(editor, target),
            Has.Count.EqualTo(1),
            "one undo removed part of the paste rather than all of it");
    }

    [Test]
    [Description("A pasted container dropped on a statement wraps it, and keeps its own run inside.")]
    public void APastedContainerWrapsAStatement()
    {
        var (editor, target) = Editor();
        var pasted = new Block
        {
            Id = "r",
            Kind = "control.forever",
            Bodies = { ["body"] = [new Block { Id = "inner", Kind = "control.stop-script" }] },
        };

        var read = VisualClipboard.Read(VisualClipboard.Write([pasted]));
        var result = DropPlan.Apply(editor, target, read.ToDragPayload(), Onto(0));

        var statements = Body(editor, target);
        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(statements, Has.Count.EqualTo(1), "the statement is inside the wrapper now");
            Assert.That(statements[0].Kind, Is.EqualTo("control.forever"));
            Assert.That(statements[0].Body("body").Select(block => block.Kind), Does.Contain("control.stop-script"),
                "the pasted container kept its own contents");
            Assert.That(statements[0].Body("body").Select(block => block.Kind), Does.Contain("control.wait-seconds"),
                "and swallowed what it landed on");
        });
    }

    [Test]
    [Description("A pasted non-container dropped on a statement replaces it, as a drag would.")]
    public void APastedStatementReplacesTheOneItLandsOn()
    {
        var (editor, target) = Editor();
        var read = VisualClipboard.Read(VisualClipboard.Write([new Block { Id = "s", Kind = "control.stop-script" }]));

        var result = DropPlan.Apply(editor, target, read.ToDragPayload(), Onto(0));
        var statements = Body(editor, target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(statements, Has.Count.EqualTo(1));
            Assert.That(statements[0].Kind, Is.EqualTo("control.stop-script"));
        });
    }

    [Test]
    [Description("Pasting a reporter into a hole binds it and deletes nothing, because nothing was there to move.")]
    public void APastedReporterBindsIntoASlot()
    {
        var (editor, target) = Editor();
        var script = target.Scripts[0];
        script.Body.Add(new Block
        {
            Id = "if-1",
            Kind = "control.if",
            Bodies = { ["body"] = [] },
        });

        var read = VisualClipboard.Read(VisualClipboard.Write(
            [new Block { Id = "t", Kind = "deck.client-count" }]));

        var result = DropPlan.Apply(
            editor,
            target,
            read.ToDragPayload(),
            new DropCandidate(DropTargetKind.ValueSlot, "if-1", SlotName: "condition"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(Body(editor, target), Has.Count.EqualTo(2), "the paste deleted a statement");
        });
    }

    [Test]
    [Description("A pasted hat on empty canvas brings the stack that was under it.")]
    public void APastedHatKeepsItsStack()
    {
        var (editor, target) = Editor();
        var read = VisualClipboard.Read(VisualClipboard.Write(
        [
            new Block { Id = "h", Kind = "hat.action-runs" },
            Wait(4),
        ]));

        var result = DropPlan.Apply(
            editor,
            target,
            read.ToDragPayload(),
            new DropCandidate(DropTargetKind.Canvas, HatId),
            400,
            400);

        var created = target.Scripts[^1];
        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(target.Scripts, Has.Count.EqualTo(2));
            Assert.That(created.Body, Has.Count.EqualTo(1), "the stack under the hat");
            Assert.That(created.Body[0].InputNumber("seconds"), Is.EqualTo(4));
        });
    }

    [Test]
    [Description("A payload from a newer build is refused, because importing it would produce a document that cannot compile.")]
    public void ANewerFormatIsRefusedWithItsVersionNamed()
    {
        var text = VisualClipboard.Write([Wait(1)])
            .Replace($"\"version\": {VisualClipboard.Version}", $"\"version\": {VisualClipboard.Version + 1}");

        var read = VisualClipboard.Read(text);

        Assert.Multiple(() =>
        {
            Assert.That(read.Problem, Is.Not.Null);
            Assert.That(read.Code, Is.EqualTo("clipboard-version"));
            Assert.That(read.Problem, Does.Contain((VisualClipboard.Version + 1).ToString()));
        });
    }

    [Test]
    [Description("Ordinary pasted text says so, rather than quoting a JSON error.")]
    public void ForeignTextIsRefusedWithoutAJargonReason()
    {
        foreach (var text in new[] { "hello", "{}", "[]", "{\"format\":\"something-else\",\"run\":[]}", "   " })
        {
            var read = VisualClipboard.Read(text);
            Assert.That(read.Problem, Is.Not.Null, $"\"{text}\" was read as a block");
            Assert.That(read.Run, Is.Empty);
        }
    }

    [Test]
    [Description("Truncated JSON is a refusal with a code, not an exception out of a keypress handler.")]
    public void MalformedJsonIsRefusedRatherThanThrown()
    {
        var read = VisualClipboard.Read("{\"format\":\"deckforge-block\",\"run\":[{");

        Assert.Multiple(() =>
        {
            Assert.That(read.Problem, Is.Not.Null);
            Assert.That(read.Code, Is.EqualTo("clipboard-malformed"));
        });
    }

    [Test]
    [Description("An envelope with nothing in it is refused: pasting it would otherwise add an empty script.")]
    public void AnEmptyEnvelopeIsRefused()
    {
        var read = VisualClipboard.Read(VisualClipboard.Write([]));

        Assert.Multiple(() =>
        {
            Assert.That(read.Problem, Is.Not.Null);
            Assert.That(read.Code, Is.EqualTo("clipboard-empty"));
        });
    }

    [Test]
    [Description("The cheap pre-check has to agree with the reader, or the UI offers a paste that cannot work.")]
    public void ThePreCheckAgreesWithTheReader()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisualClipboard.LooksLikeABlock(VisualClipboard.Write([Wait(1)])), Is.True);
            Assert.That(VisualClipboard.LooksLikeABlock("just some text"), Is.False);
            Assert.That(VisualClipboard.LooksLikeABlock(""), Is.False);
            Assert.That(VisualClipboard.LooksLikeABlock(null), Is.False);
            Assert.That(VisualClipboard.LooksLikeABlock("{\"format\":\"deckforge-block\"}"), Is.False,
                "no run field means there is nothing to paste");
        });
    }

    [Test]
    [Description("The payload is text a person can read, which is the only way out of a document the editor cannot open.")]
    public void ThePayloadIsReadableText()
    {
        var text = VisualClipboard.Write([Wait(1)]);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(VisualClipboard.Format));
            Assert.That(text, Does.Contain("control.wait-seconds"));
            Assert.That(text, Does.StartWith("{"));
        });
    }

    [Test]
    [Description("A refusal always carries a code and never carries a run.")]
    public void EveryRefusalCarriesACodeAndNoRun()
    {
        var refusals = new[]
        {
            VisualClipboard.Read(null),
            VisualClipboard.Read("nonsense"),
            VisualClipboard.Read("{\"format\":\"other\",\"run\":[]}"),
            VisualClipboard.Read(VisualClipboard.Write([])),
            VisualClipboard.Read("{\"format\":\"deckforge-block\",\"version\":99,\"run\":[]}"),
        };

        foreach (var refusal in refusals)
        {
            Assert.That(refusal.Problem, Is.Not.Null);
            Assert.That(refusal.Code, Is.Not.Null.And.Not.Empty, $"\"{refusal.Problem}\" had no code");
            Assert.That(refusal.HasRun, Is.False, "a refusal produced a pasteable run");
        }
    }
}
