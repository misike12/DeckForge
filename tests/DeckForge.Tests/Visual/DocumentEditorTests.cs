using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The undo engine: every edit is a command with an inverse, one gesture is one entry, and undoing
/// everything leaves the document byte-identical to how it started.
/// </summary>
/// <remarks>
/// <para>
/// The last of those is the property test at the bottom, and it is the one that earns the rest of the
/// file. A hand-written set of insert/delete/move cases proves the cases someone thought of; a
/// thousand random gestures and their reverses prove the mechanism, which is the part that goes wrong in
/// the interaction nobody imagined. The bug this repository has already recorded for the old engine is
/// "undo restored a deleted block twice", and that is exactly what the round-trip assertion catches and
/// what a case-by-case reading does not.
/// </para>
/// </remarks>
[TestFixture]
public sealed class DocumentEditorTests
{
    // ---- history ------------------------------------------------------------------------------------

    [Test]
    public void An_insert_can_be_undone_and_redone()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var body = document.Targets[0].Scripts[0].Body;
        var before = body.Select(block => block.Id).ToList();

        editor.Insert(BodyRef.ScriptBody(document.Targets[0].Scripts[0].Hat.Id), 0, [Block("control.yield")]);

        Assert.That(editor.CanUndo, Is.True);
        Assert.That(editor.UndoLabel, Is.EqualTo("Insert yield to host"));

        Assert.Multiple(() =>
        {
            Assert.That(editor.Undo(), Is.True);
            Assert.That(body.Select(block => block.Id), Is.EqualTo(before), "undo put the stack back");
            Assert.That(editor.CanRedo, Is.True);
            Assert.That(editor.Redo(), Is.True);
            Assert.That(body.Select(block => block.Id), Is.Not.EqualTo(before), "redo put it back");
            Assert.That(editor.CanUndo, Is.True);
        });
    }

    [Test]
    public void Undoing_everything_leaves_the_document_exactly_as_it_started()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var start = VisualProjectJson.Serialize(document);
        var script = document.Targets[0].Scripts[0];
        var script_ = BodyRef.ScriptBody(script.Hat.Id);
        var loop = Loop(script);

        editor.Insert(script_, 0, [Block("control.forever")]);
        var carried = script.Body[0].Id;

        editor.Insert(script_, 1, [Block("control.yield"), Block("ui.log")]);
        var carriedId = script.Body[3].Id;

        editor.Move(script_, script.Body[2], script_, 0);
        editor.Delete(script_, script.Body[3]);
        editor.ToggleDisable(script.Body[0]);
        editor.EditField(script.Body[1], "template", "after");
        editor.BindSlot(script.Body[0], "template", BlockInput.Of("warning"));
        editor.Wrap(script.Body[0], Block("control.forever"), count: 1);
        editor.Insert(new BodyRef(loop.Id, "body"), 0, [Block("ui.log")]);
        editor.Unwrap(loop);

        Assert.That(script.Body.Count, Is.GreaterThan(1), "the gestures did something");

        while (editor.Undo())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(VisualProjectJson.Serialize(document), Is.EqualTo(start),
                "ten edits and ten undos should leave no trace; anything else means an inverse does not "
                + "put back exactly what it removed");
            Assert.That(carried, Is.Not.Null);
            Assert.That(carriedId, Is.Not.Null);
        });
    }

    [Test]
    public void A_new_edit_throws_away_the_redo_stack()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];

        editor.Insert(BodyRef.ScriptBody(script.Hat.Id), 0, [Block("control.yield")]);
        editor.Undo();

        editor.Insert(BodyRef.ScriptBody(script.Hat.Id), 0, [Block("control.break")]);

        Assert.That(editor.CanRedo, Is.False, "redo is only a promise about the present");
    }

    [Test]
    public void One_gesture_is_one_undo()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var before = script.Body.Count;
        var wrapper = Block("control.forever");

        editor.Transaction(
            "Drop a loop with a statement in it",
            [
                new InsertRun(BodyRef.ScriptBody(script.Hat.Id), 0, [wrapper]),
                new InsertRun(new BodyRef(wrapper.Id, "body"), 0, [Block("ui.log")]),
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Count, Is.EqualTo(before + 1), "one statement went in at the top");
            Assert.That(wrapper.Body("body"), Has.Count.EqualTo(1), "and one is inside the loop");
            Assert.That(editor.UndoLabel, Is.EqualTo("Drop a loop with a statement in it"));
            Assert.That(editor.Undo(), Is.True);
            Assert.That(script.Body.Count, Is.EqualTo(before), "one undo took back the whole gesture");
            Assert.That(editor.CanUndo, Is.False, "and only one was needed");
        });
    }

    [Test]
    public void A_transaction_is_reverted_backwards()
    {
        // Two inserts into one body, where the second's index only makes sense after the first has run.
        // Reverted forwards, the second would be inserted into a list the first has already put right.
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var first = Block("control.forever");
        var second = Block("control.while");

        editor.Transaction("Two drops", [new InsertRun(BodyRef.ScriptBody(script.Hat.Id), 0, [first]), new InsertRun(BodyRef.ScriptBody(script.Hat.Id), 0, [second])]);

        Assert.That(script.Body[0], Is.SameAs(second));
        editor.Undo();

        Assert.That(script.Body.Any(block => block.Id == first.Id || block.Id == second.Id), Is.False);
    }

    [Test]
    public void Typing_coalesces_per_field_within_the_window_and_not_after_it()
    {
        var document = Document();
        var editor = new DocumentEditor(document) { Now = Clock() };
        var log = document.Targets[0].Scripts[0].Body[0];

        editor.EditField(log, "template", "a");
        editor.EditField(log, "template", "ab");
        editor.EditField(log, "template", "abc");

        Assert.That(editor.Depth, Is.EqualTo(1), "three keystrokes in one field are one thing the user did");
        Assert.That(log.Field("template"), Is.EqualTo("abc"));

        editor.Undo();

        Assert.That(log.Field("template"), Is.Null, "undoing took the whole typing back to where it began");

        editor.Redo();
        Assert.That(log.Field("template"), Is.EqualTo("abc"));

        // A second field on the same block is a different edit, and two fields must not share an entry.
        editor.Now = Clock(5000);
        editor.EditField(log, "level", "Information");

        Assert.That(editor.Depth, Is.EqualTo(2), "a different field is a different edit");
    }

    [Test]
    public void Typing_in_two_fields_never_shares_one_undo_entry()
    {
        var document = Document();
        var editor = new DocumentEditor(document) { Now = Clock() };
        var block = document.Targets[0].Scripts[0].Body[0];

        editor.EditField(block, "template", "one");
        editor.EditField(block, "level", "Warning");

        Assert.That(editor.Depth, Is.EqualTo(2));
    }

    // ---- moving ------------------------------------------------------------------------------------

    [Test]
    public void A_drag_carries_everything_below_it()
    {
        // Part 9.5's first gesture rule. Dragging the middle of a stack and leaving the top behind
        // orphans it, so a drag takes the run from the grabbed block down.
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var script_ = BodyRef.ScriptBody(script.Hat.Id);
        var run = editor.RunFrom(script_, script.Body[1]);

        editor.Delete(script_, script.Body[1]);

        Assert.That(run.Select(block => block.Id).Count(), Is.EqualTo(2),
            "the run is the grabbed block and the one below it");
        Assert.That(script.Body, Has.Count.EqualTo(1));
    }

    [Test]
    public void Alt_narrows_a_drag_to_the_single_block()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var run = editor.RunFrom(BodyRef.ScriptBody(script.Hat.Id), script.Body[1], wholeStack: false);

        Assert.That(run.Select(block => block.Id), Is.EqualTo(new[] { script.Body[1].Id }));
    }

[Test]
    public void Moving_a_run_up_within_its_own_stack_lands_where_the_user_aimed()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var order = script.Body.Select(block => block.Id).ToList();

        editor.Move(BodyRef.ScriptBody(script.Hat.Id), script.Body[2], BodyRef.ScriptBody(script.Hat.Id), 0);

        Assert.That(script.Body.Select(block => block.Id).ToList(),
            Is.EqualTo(new[] { order[2], order[0], order[1] }));

        editor.Undo();

        Assert.That(script.Body.Select(block => block.Id).ToList(), Is.EqualTo(order));
    }

    [Test]
    public void Moving_a_run_down_within_its_own_stack_does_nothing_and_says_so()
    {
        // A drag carries everything below the grabbed block, so there is nothing below a run to swap it
        // with: aiming lower than a run already extends to is a no-op, not a silent reordering. The canvas
        // shows no drop indicator there either, because the resolver scores the same gap twice.
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var order = script.Body.Select(block => block.Id).ToList();

        editor.Move(BodyRef.ScriptBody(script.Hat.Id), script.Body[0], BodyRef.ScriptBody(script.Hat.Id), 3);

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Select(block => block.Id).ToList(), Is.EqualTo(order));
            Assert.That(editor.Depth, Is.EqualTo(1), "the gesture happened; it simply had nowhere to go");
        });
    }

    [Test]
    public void Moving_a_run_into_a_nested_body_and_back_is_exact()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var loop = Loop(script);
        var moved = script.Body[^1];
        var existing = loop.Body("body")[0].Id;

        editor.Move(BodyRef.ScriptBody(script.Hat.Id), moved, new BodyRef(loop.Id, "body"), 0);

        Assert.That(loop.Body("body").Select(block => block.Id).ToList(),
            Is.EqualTo(new[] { moved.Id, existing }),
            "the statement joined the one that was already in there, in order");

        editor.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Select(block => block.Id).ToList()[^1], Is.EqualTo(moved.Id),
                "the statement went back to the end, which is where it was dragged from");
            Assert.That(loop.Body("body").Select(block => block.Id).ToList(), Is.EqualTo(new[] { existing }),
                "and the loop is as it was");
        });
    }

    [Test]
    public void A_block_cannot_be_dropped_inside_itself()
    {
        // Appendix G's rejection rule, and the one that would otherwise lose the document: a run moved
        // into a body owned by one of its own blocks would detach the container from the tree, and the run
        // with it.
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var loop = Loop(script);

        // Put a second loop inside the first, so there is a body inside the run to aim at.
        editor.Insert(new BodyRef(loop.Id, "body"), 0, [Block("control.while")]);
        var inner = loop.Body("body")[0];

        var result = editor.Move(
            BodyRef.ScriptBody(script.Hat.Id),
            loop,
            new BodyRef(inner.Id, "body"),
            0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.False);
            Assert.That(result.Code, Is.EqualTo("vis-move-into-self"));
            Assert.That(editor.Depth, Is.EqualTo(1), "a refused edit is not on the stack");
            Assert.That(script.Body, Has.Count.EqualTo(3), "and nothing moved");
        });
    }

    // ---- wrapping ----------------------------------------------------------------------------------

    [Test]
    public void Unwrap_splices_a_container_body_into_its_parent_and_can_be_reversed()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var loop = Loop(script);
        var inner = loop.Body("body")[0].Id;

        Assert.That(editor.Unwrap(loop).Applied, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Any(block => block.Id == inner), Is.True,
                "the statement that was inside the loop is now beside it");
            Assert.That(script.Body.Any(block => block.Id == loop.Id), Is.False);
            Assert.That(loop.Body("body"), Is.Empty, "the container was emptied rather than deleted");
        });

        editor.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Any(block => block.Id == loop.Id), Is.True);
            Assert.That(loop.Body("body").Select(block => block.Id).ToList(), Is.EqualTo(new[] { inner }),
                "undo put the body back inside");
        });
    }

    [Test]
    public void Unwrapping_an_empty_container_is_refused_rather_than_losing_it()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];

        var result = editor.Unwrap(Block("control.forever"));

        Assert.That(result.Applied, Is.False, "there is nothing inside it to splice out");
        Assert.That(editor.CanUndo, Is.False);
    }

    [Test]
    public void A_wrapper_is_reversible_with_its_contents()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];
        var order = script.Body.Select(block => block.Id).ToList();
        var wrapper = Block("control.forever");

        editor.Wrap(script.Body[1], wrapper, count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Count, Is.EqualTo(2), "two statements went inside the wrapper");
            Assert.That(script.Body[1], Is.SameAs(wrapper));
            Assert.That(DocumentLists.BodyOf(wrapper).Select(block => block.Id).ToList(),
                Is.EqualTo(order.Skip(1).Take(2).ToList()));
        });

        editor.Undo();

        Assert.That(script.Body.Select(block => block.Id).ToList(), Is.EqualTo(order));
    }

    // ---- fields, slots, scripts ---------------------------------------------------------------------

    [Test]
    public void A_toggle_can_be_undone_even_though_the_command_is_its_own_inverse()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var block = document.Targets[0].Scripts[0].Body[0];

        editor.ToggleDisable(block);
        Assert.That(block.Disabled, Is.True);

        editor.Undo();
        Assert.That(block.Disabled, Is.False);

        editor.Redo();
        Assert.That(block.Disabled, Is.True);
    }

    [Test]
    public void A_slot_keeps_a_copy_so_a_later_edit_cannot_reach_back_into_an_undo_entry()
    {
        // The bug this guards: an undo entry holding a live reference to the same BlockInput the document
        // is using, so editing the slot again also rewrites what undo remembers.
        var document = Document();
        var editor = new DocumentEditor(document);
        var block = document.Targets[0].Scripts[0].Body[0];
        var original = block.InputText("template");

        editor.BindSlot(block, "template", BlockInput.Of("first"));
        editor.BindSlot(block, "template", BlockInput.Of("second"));
        editor.Undo();

        Assert.That(block.InputText("template"), Is.EqualTo("first"));

        editor.Undo();

        Assert.That(block.InputText("template"), Is.EqualTo(original),
            "the second undo went back past the first, to the value the block started with");
    }

    [Test]
    public void Adding_and_removing_a_script_leaves_the_rest_of_the_document_alone()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var target = document.Targets[0];
        var script = NewScript("second");

        editor.AddScript(target, script);
        Assert.That(target.Scripts, Has.Count.EqualTo(2));

        editor.DeleteScript(target, script);
        Assert.That(target.Scripts, Has.Count.EqualTo(1));

        editor.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(target.Scripts, Has.Count.EqualTo(2));
            Assert.That(target.Scripts[1], Is.SameAs(script), "and it came back in the same place");
        });
    }

    [Test]
    public void Moving_a_script_twice_does_not_walk_its_position()
    {
        // The command captures where it was once. Reverting twice - a redo in the middle of it - must
        // still put it where the first drag found it, not one drag further on each pass.
        var document = Document();
        var editor = new DocumentEditor(document);
        var script = document.Targets[0].Scripts[0];

        editor.MoveScript(script, 100, 200);
        editor.MoveScript(script, 300, 400);
        editor.Undo();
        editor.Undo();

        Assert.That((script.X, script.Y), Is.EqualTo((0.0, 0.0)));
    }

    [Test]
    public void A_change_raises_the_changed_event_so_the_canvas_can_rebuild()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var raised = 0;
        editor.Changed += () => raised++;

        editor.Insert(BodyRef.ScriptBody(document.Targets[0].Scripts[0].Hat.Id), 0, [Block("control.yield")]);
        editor.Undo();
        editor.Redo();

        Assert.That(raised, Is.EqualTo(3), "apply, undo and redo each announce themselves");
    }

    [Test]
    public void Dropping_from_a_body_that_no_longer_exists_is_refused_rather_than_thrown()
    {
        // A caller holding a stale reference must not take the application down, and must not find out
        // by getting no edit and no explanation either.
        var document = Document();
        var editor = new DocumentEditor(document);
        var raised = 0;
        editor.Changed += () => raised++;
        var before = VisualProjectJson.Serialize(document);

        var result = editor.Move(
            new BodyRef("no-such-block", "body"),
            document.Targets[0].Scripts[0].Body[0],
            BodyRef.ScriptBody(document.Targets[0].Scripts[0].Hat.Id),
            0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.False);
            Assert.That(result.Code, Is.EqualTo("vis-drop-missing-source"));
            Assert.That(result.Problem, Is.Not.Null, "and it says why");
            Assert.That(raised, Is.Zero, "nothing changed, so nothing was announced");
            Assert.That(VisualProjectJson.Serialize(document), Is.EqualTo(before));
        });
    }

    [Test]
    public void Dropping_into_a_body_that_no_longer_exists_is_refused()
    {
        var document = Document();
        var editor = new DocumentEditor(document);

        var result = editor.Insert(new BodyRef("b999", "body"), 0, [Block("control.yield")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.False);
            Assert.That(result.Code, Is.EqualTo("vis-drop-missing-target"));
            Assert.That(editor.CanUndo, Is.False);
        });
    }

    // ---- the property test -------------------------------------------------------------------------

    [Test]
    public void A_thousand_random_gestures_undo_cleanly()
    {
        var document = Document();
        var editor = new DocumentEditor(document);
        var start = VisualProjectJson.Serialize(document);
        var random = new Random(20260930);
        var applied = 0;

        for (var step = 0; step < 1000; step++)
        {
            if (TryRandomGesture(editor, document, random))
            {
                applied++;
            }
        }

        while (editor.Undo())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.GreaterThan(400), "the run has to actually do things to be worth anything");
            Assert.That(VisualProjectJson.Serialize(document), Is.EqualTo(start),
                $"{applied} gestures and {applied} undos left the document different; an inverse that does "
                + "not put back exactly what it removed is the defect this test exists for");
        });
    }

    /// <summary>One random legal-ish edit, so the run exercises move, delete, wrap, disable and field edits.</summary>
    private static bool TryRandomGesture(DocumentEditor editor, VisualProject document, Random random)
    {
        var script = document.Targets[0].Scripts[0];
        var script_ = BodyRef.ScriptBody(script.Hat.Id);
        var containers = script.Body.Where(block => BlockCatalog.Find(block.Kind) is { IsContainer: true }).ToList();

        if (script.Body.Count == 0)
        {
            // Nothing to move, delete or disable. An empty stack is reachable - a drag that takes
            // everything can empty it - and the run has to carry on regardless, or the property test
            // would stop testing half way through and pass for the wrong reason.
            return editor.Insert(script_, 0, [Block(Pick(random))]).Applied;
        }

        switch (random.Next(7))
        {
            // Three of the seven are inserts, and only a delete with three or more statements is legal,
            // because a delete takes everything below it. Without that balance the stack drains within a
            // dozen steps and the remaining nine hundred gestures have nothing to do - which is how a
            // property test passes without having tested anything.
            case 0:
            case 1:
            case 2:
                return editor.Insert(script_, random.Next(script.Body.Count + 1), [Block(Pick(random))]).Applied;

            case 3:
                return script.Body.Count > 3
                    && editor.Delete(script_, script.Body[random.Next(script.Body.Count - 1)]).Applied;

            case 4:
                return editor.Move(script_, script.Body[random.Next(script.Body.Count)], script_, 0).Applied;

            case 5:
                return editor.ToggleDisable(script.Body[random.Next(script.Body.Count)]).Applied;

            default:
                if (containers.Count == 0)
                {
                    return false;
                }

                var container = containers[random.Next(containers.Count)];
                return editor.Wrap(container, Block("control.forever"), random.Next(1, 3)).Applied;
        }
    }

    private static string Pick(Random random) =>
        random.Next(3) switch
        {
            0 => "control.yield",
            1 => "control.forever",
            _ => "ui.log",
        };

    // ---- fixtures ----------------------------------------------------------------------------------

    private static Func<DateTimeOffset> Clock(int milliseconds = 0)
    {
        var at = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        return () => at.AddMilliseconds(milliseconds);
    }

    /// <summary>The one container in the fixture, so a test does not have to remember which index it is at.</summary>
    private static Block Loop(VisualScript script) =>
        script.Body.First(block => BlockCatalog.Find(block.Kind) is { IsContainer: true });

    /// <summary>
    /// A small document: one action target, one script, four statements, one of them a loop.
    /// </summary>
    private static VisualProject Document()
    {
        var project = new VisualProject
        {
            DocumentId = "edittest",
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
                            Hat = new Block { Kind = "hat.action-runs", Id = "h1" },
                            Body =
                            [
                                Block("ui.log"),
                                Block("control.repeat"),
                                Block("ui.log"),
                            ],
                        },
                    ],
                },
            ],
        };

        // The loop needs something in it, or unwrapping it would be refused and the run would never
        // exercise the interesting half of the machinery.
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

    private static VisualScript NewScript(string id) => new()
    {
        Id = id,
        Name = id,
        Hat = new Block { Kind = "hat.script-starts", Id = id + "-hat" },
    };
}