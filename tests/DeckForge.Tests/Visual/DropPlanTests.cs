using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// A drop, and what it means for the document.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.5's gesture rules, tested with no window. The rule that decided where this code lives is
/// "the scorer is pure Core code so every rule is unit-testable with no window": the pointer's position is
/// <see cref="DropResolver"/>'s business and is tested in <c>StackLayoutTests</c>, while what a landing
/// zone <em>does</em> to the document is this file's business, and it is the half with the interesting
/// failures.
/// </para>
/// <para>
/// Most of the cases are things a user does by accident rather than on purpose — a stack dropped onto
/// itself, a reporter pulled out of a hole, a C-block dragged onto empty canvas. Each of them is a rule
/// nobody would write down from reading the code and every one of them is invisible until it is tested.
/// </para>
/// </remarks>
[TestFixture]
public sealed class DropPlanTests
{
    // ---- moving a run --------------------------------------------------------------------------------

    [Test]
    public void A_run_dropped_into_a_gap_moves_there()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var loop = script.Body[1];
        var carried = script.Body.Skip(2).Select(block => block.Id).ToList();
        var already = loop.Body("body").Single().Id;

        var payload = DragPayload.FromDocument(editor, BodyRef.ScriptBody(script.Hat.Id), script.Body[2]);
        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            payload,
            new DropCandidate(DropTargetKind.Mouth, loop.Id, BodyName: "body", Index: 0));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(script.Body, Has.Count.EqualTo(2), "the two grabbed statements left the stack");
            Assert.That(loop.Body("body").Select(block => block.Id).ToList(),
                Is.EqualTo(carried.Append(already)),
                "and went into the loop in order, at the index the candidate named - which was zero, so in "
                + "front of what was already there");
        });
    }

    [Test]
    public void Alt_narrows_the_drag_to_one_block_and_the_rest_stay_put()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var where = BodyRef.ScriptBody(script.Hat.Id);
        var carried = script.Body.Select(block => block.Id).ToList();
        var middle = carried[1];

        var payload = DragPayload.FromDocument(editor, where, script.Body[1], wholeStack: false);
        DropPlan.Apply(editor, project.Targets[0], payload, Gap(script.Hat.Id, index: 0));

        Assert.Multiple(() =>
        {
            Assert.That(payload.Run, Has.Count.EqualTo(1), "Alt means this block, not the tail of the stack");
            Assert.That(script.Body.Select(block => block.Id).ToList(),
                Is.EqualTo(new[] { middle }.Concat(carried.Where(id => id != middle))));
        });
    }

    [Test]
    public void A_gap_inside_the_run_being_carried_does_nothing_at_all()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var where = BodyRef.ScriptBody(script.Hat.Id);
        var before = script.Body.Select(block => block.Id).ToList();

        var payload = DragPayload.FromDocument(editor, where, script.Body[0]);
        var result = DropPlan.Apply(editor, project.Targets[0], payload, Gap(script.Hat.Id, index: 2));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True,
                "the editor refuses move-into-self with a message about nesting, which is technically "
                + "right and useless to a user who has merely dragged their own stack onto itself");
            Assert.That(script.Body.Select(block => block.Id), Is.EqualTo(before));
            Assert.That(editor.Depth, Is.Zero, "and it should not even reach the history");
        });
    }

    // ---- the palette ----------------------------------------------------------------------------------

    [Test]
    public void A_block_from_the_palette_is_inserted_not_moved()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var before = script.Body.Count;

        var result = DropPlan.Apply(
            editor, project.Targets[0], DragPayload.FromPalette("control.yield"), Gap(script.Hat.Id, index: 0));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(script.Body, Has.Count.EqualTo(before + 1));
            Assert.That(script.Body[0].Kind, Is.EqualTo("control.yield"));
            Assert.That(editor.UndoLabel, Does.StartWith("Add"), "the history says what happened in words");
        });
    }

    [Test]
    public void A_container_from_the_palette_dropped_on_a_statement_wraps_it()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var wrappedId = script.Body[0].Id;
        var afterId = script.Body[1].Id;
        var tail = script.Body.Skip(2).Select(block => block.Id).ToList();

        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            DragPayload.FromPalette("control.forever"),
            new DropCandidate(DropTargetKind.OntoStatement, script.Hat.Id, Index: 0));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(script.Body[0].Kind, Is.EqualTo("control.forever"), "the wrapper took the statement's place");
            Assert.That(script.Body[0].Body("body").Single().Id, Is.EqualTo(wrappedId), "and swallowed it");
            Assert.That(script.Body[1].Id, Is.EqualTo(afterId), "while everything below stayed where it was");
        });

        Assert.That(editor.Undo(), Is.True);
        Assert.That(script.Body.Select(block => block.Id).ToList(),
            Is.EqualTo(new[] { wrappedId, afterId }.Concat(tail)));
    }

    [Test]
    public void A_plain_stack_dropped_on_a_statement_replaces_it()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var replaced = script.Body[1].Id;

        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            DragPayload.FromPalette("control.yield"),
            new DropCandidate(DropTargetKind.OntoStatement, script.Hat.Id, Index: 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(script.Body.Select(block => block.Id).Contains(replaced), Is.False,
                "the statement it landed on is the one that was thrown away");
            Assert.That(script.Body[1].Kind, Is.EqualTo("control.yield"));
        });

        Assert.That(editor.Undo(), Is.True);
        Assert.That(script.Body.Select(block => block.Id).Contains(replaced), Is.True,
            "and undo brings it back, because replace-on-top was one gesture and is one entry");
    }

    // ---- value slots ----------------------------------------------------------------------------------

    [Test]
    public void A_reporter_pulled_out_of_a_hole_takes_its_place_and_leaves_the_old_value_for_undo()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var loop = script.Body[1];
        var slot = BlockCatalog.Find(loop.Kind)!.Slots!.First(descriptor => descriptor.Type != SlotType.Text);
        loop.Inputs[slot.Name] = BlockInput.Of("hello");
        var reporter = new Block { Kind = "deck.current-folder", Id = "reporter-1" };
        var where = BodyRef.ScriptBody(script.Hat.Id);

        editor.Insert(where, 0, [reporter]);

        // Alt, because a reporter in the middle of a stack drags the whole tail with it, and taking the
        // loop along with it would delete the very block the reporter is being dropped into.
        var payload = DragPayload.FromDocument(editor, where, reporter, wholeStack: false);

        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            payload,
            new DropCandidate(DropTargetKind.ValueSlot, loop.Id, SlotName: slot.Name));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(script.Body.Select(block => block.Id).Contains(reporter.Id), Is.False,
                "a block in two places is a document the emitter will happily emit twice");
            Assert.That(loop.Inputs[slot.Name].Block, Is.SameAs(reporter));
        });

        Assert.That(editor.Undo(), Is.True);
        Assert.That(loop.Inputs[slot.Name]?.Text, Is.EqualTo("hello"),
            "undo puts the old value back rather than clearing the hole");
        Assert.That(script.Body.Any(block => block.Id == reporter.Id), Is.True);
    }

    [Test]
    public void A_stack_is_refused_by_a_value_slot_even_if_the_caller_forgets_to_ask_the_resolver()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var where = BodyRef.ScriptBody(script.Hat.Id);
        var before = script.Body.Count;

        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            DragPayload.FromDocument(editor, where, script.Body[0]),
            new DropCandidate(DropTargetKind.ValueSlot, script.Body[1].Id, SlotName: "anything"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.False, "a statement cannot go in a hole");
            Assert.That(result.Code, Is.EqualTo("drop-shape"));
            Assert.That(script.Body, Has.Count.EqualTo(before));
        });
    }

    // ---- free canvas ----------------------------------------------------------------------------------

    [Test]
    public void A_hat_on_free_canvas_starts_a_script_where_it_was_dropped()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var target = project.Targets[0];

        var result = DropPlan.Apply(
            editor,
            target,
            DragPayload.FromPalette("hat.action-runs"),
            new DropCandidate(DropTargetKind.Canvas, string.Empty),
            pointerX: 220,
            pointerY: 90);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(target.Scripts, Has.Count.EqualTo(2));
            Assert.That(target.Scripts[1].X, Is.EqualTo(220), "and it landed where it was dropped, not in a corner");
            Assert.That(target.Scripts[1].Y, Is.EqualTo(90));
        });
    }

    [Test]
    public void A_stack_on_free_canvas_is_refused_rather_than_given_an_invented_hat()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var target = project.Targets[0];

        var result = DropPlan.Apply(
            editor, target, DragPayload.FromPalette("ui.log"), new DropCandidate(DropTargetKind.Canvas, string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.False);
            Assert.That(result.Code, Is.EqualTo("canvas-needs-hat"));
            Assert.That(result.Problem, Does.Contain("hat"), "and it says what the rule is, not just that it failed");
            Assert.That(target.Scripts, Has.Count.EqualTo(1), "no half-made script left behind");
        });
    }

    [Test]
    public void A_hat_dragged_off_its_own_script_moves_that_script()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var target = project.Targets[0];
        var script = target.Scripts[0];
        var where = BodyRef.ScriptBody(script.Hat.Id);

        // The hat is not a statement, so the drag carries it as a palette-shaped payload with a real
        // first block - which is what the canvas produces when a hat column is dragged.
        var payload = new DragPayload(null, script.Hat, null, script.Hat.Kind, IsPalette: true, IsSingle: false);
        _ = where;

        var result = DropPlan.Apply(
            editor, target, payload, new DropCandidate(DropTargetKind.Canvas, string.Empty), 400, 300);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(target.Scripts, Has.Count.EqualTo(1), "dragging a hat about does not clone its script");
            Assert.That(target.Scripts[0], Is.SameAs(script), "and it is the same script object, not a copy");
            Assert.That(script.X, Is.EqualTo(400));
        });
    }

    [Test]
    public void A_hat_into_a_gap_is_refused_rather_than_dropped_into_a_stack()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];

        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            DragPayload.FromPalette("hat.action-runs"),
            Gap(script.Hat.Id, index: 0));

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.False);
            Assert.That(result.Code, Is.EqualTo("hat-needs-canvas"));
            Assert.That(script.Body, Has.Count.EqualTo(4));
        });
    }

    // ---- round trip -----------------------------------------------------------------------------------

    [Test]
    public void A_thousand_random_drops_all_undo_cleanly()
    {
        // The property test from P4a, run through the drag entry point instead of the editor's methods.
        // Drag is where the gestures compound: a drop is a move or a transaction or a replace, and each
        // of those has to undo as one entry or the round-trip assertion is the thing that notices.
        var project = Document();
        var editor = new DocumentEditor(project);
        var target = project.Targets[0];
        var start = VisualProjectJson.Serialize(project);
        var random = new Random(20261001);

        for (var i = 0; i < 1000; i++)
        {
            var payload = RandomPayload(editor, project, target, random);
            if (payload is null)
            {
                continue;
            }

            DropPlan.Apply(editor, target, payload, RandomLanding(random), random.Next(400), random.Next(400));
        }

        while (editor.Undo())
        {
        }

        Assert.That(VisualProjectJson.Serialize(project), Is.EqualTo(start),
            "every drop undone should leave no trace. Anything else means a drop applied something its own "
            + "inverse cannot describe - the classic 'undo restored a deleted block twice'.");
    }

    private static DragPayload? RandomPayload(
        DocumentEditor editor,
        VisualProject project,
        VisualTarget target,
        Random random)
    {
        var script = target.Scripts[random.Next(target.Scripts.Count)];
        var where = BodyRef.ScriptBody(script.Hat.Id);

        if (script.Body.Count == 0 || random.Next(4) == 0)
        {
            return DragPayload.FromPalette(Pick(random));
        }

        return DragPayload.FromDocument(
            editor, where, script.Body[random.Next(script.Body.Count)], wholeStack: random.Next(3) > 0);
    }

    private static DropCandidate RandomLanding(Random random) => random.Next(6) switch
    {
        0 => new DropCandidate(DropTargetKind.Canvas, string.Empty),
        1 => new DropCandidate(DropTargetKind.HatSlot, "hat-1"),
        2 => new DropCandidate(DropTargetKind.ValueSlot, "slot-owner", SlotName: "value"),
        3 => new DropCandidate(DropTargetKind.OntoStatement, "hat-1", BodyName: "", Index: random.Next(3)),
        4 => new DropCandidate(DropTargetKind.Mouth, "hat-1", BodyName: "body", Index: random.Next(2)),
        _ => Gap("hat-1", random.Next(4)),
    };

    private static DropCandidate Gap(string ownerId, int index) =>
        new(DropTargetKind.StackGap, ownerId, Index: index);

    /// <summary>A spread of real kinds, so the shape filter and the refusals are both exercised.</summary>
    private static string Pick(Random random) => random.Next(9) switch
    {
        0 => "control.yield",
        1 => "ui.log",
        2 => "control.repeat",
        3 => "control.forever",
        4 => "deck.current-folder",
        5 => "hat.action-runs",
        6 => "op.lt",
        7 => "var.set-from-host",
        _ => "control.stop",
    };

    /// <summary>One action target with one script of four statements, one of them a loop with a body.</summary>
    private static VisualProject Document()
    {
        var project = new VisualProject
        {
            DocumentId = "droptest",
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:log",
                    Name = "Log",
                    Kind = TargetKind.Action,
                    TargetFile = "Actions/LogAction.cs",
                    Scripts =
                    [
                        new VisualScript
                        {
                            Id = "script-1",
                            Hat = new Block { Kind = "hat.action-runs", Id = "hat-1" },
                            Body =
                            [
                                Block("ui.log"),
                                Block("control.repeat"),
                                Block("ui.log"),
                                Block("control.yield"),
                            ],
                        },
                    ],
                },
            ],
        };

        project.Targets[0].Scripts[0].Body[1].Body("body").Add(Block("ui.log"));
        return project;
    }

    private static Block Block(string kind)
    {
        var descriptor = BlockCatalog.Find(kind);
        var block = new Block { Kind = kind, Id = kind + "-" + Guid.NewGuid().ToString("N")[..4] };

        foreach (var slot in descriptor?.Slots ?? [])
        {
            block.Inputs[slot.Name] = BlockFactory.DefaultInput(descriptor!, slot);
        }

        foreach (var menu in descriptor?.Menus ?? [])
        {
            block.Fields[menu.Name] = menu.Default;
        }

        return block;
    }
}
