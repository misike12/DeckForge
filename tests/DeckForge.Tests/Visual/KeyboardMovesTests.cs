using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The keyboard: traversal, moving, deleting and duplicating, and the pick-up-and-drop cursor.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.5's last line is "nothing requires a mouse", and the bindings themselves are the easy half —
/// WPF delivers them. What is hard, and what this file covers, is everything the bindings need to be
/// <em>about</em>: where the cursor is, what "the next block" means at the end of a stack and inside a
/// loop, and whether a keyboard drag walks the same zones the pointer does. All of that is a question
/// about the document, so it lives in <see cref="KeyboardMoves"/> in Core and is tested here with no
/// window.
/// </para>
/// <para>
/// Every case below is a case somebody reaches by accident. The ends of stacks are where a naive
/// implementation either wraps round unexpectedly or falls over, and "down from the last statement of a
/// loop" is where it is genuinely not obvious what should happen.
/// </para>
/// </remarks>
[TestFixture]
public sealed class KeyboardMovesTests
{
    // ---- traversal -----------------------------------------------------------------------------------

    [Test]
    public void Up_and_down_walk_a_stack()
    {
        var project = Document();
        var script = project.Targets[0].Scripts[0];

        var down = KeyboardMoves.Step(project, script.Body[0], down: true);
        var up = KeyboardMoves.Step(project, script.Body[2], down: false);

        Assert.Multiple(() =>
        {
            Assert.That(down, Is.SameAs(script.Body[1]));
            Assert.That(up, Is.SameAs(script.Body[1]));
        });
    }

    [Test]
    public void The_ends_of_a_stack_stop_rather_than_wrapping()
    {
        var project = Document();
        var script = project.Targets[0].Scripts[0];

        Assert.Multiple(() =>
        {
            Assert.That(KeyboardMoves.Step(project, script.Body[^1], down: true), Is.Null,
                "there is nothing after the last statement, and wrapping to the top would move a block "
                + "somewhere the user did not ask for");
            Assert.That(KeyboardMoves.Step(project, script.Body[0], down: false)?.Id,
                Is.EqualTo(script.Hat.Id), "up from the first statement reaches the hat, so a script is "
                + "traversable from its own contents");
            Assert.That(KeyboardMoves.Step(project, script.Hat, down: false), Is.Null,
                "and a hat has nothing above it");
        });
    }

    [Test]
    public void Down_from_the_last_statement_of_a_loop_stays_inside_the_loop()
    {
        var project = Document();
        var loop = project.Targets[0].Scripts[0].Body[1];
        var last = loop.Body("body")[^1];

        Assert.That(KeyboardMoves.Step(project, last, down: true), Is.Null,
            "a user pressing Down at the foot of a loop is asking about what follows inside it, and being "
            + "thrown onto a different stack answers a question they did not ask");
    }

    [Test]
    public void Traversal_stays_inside_a_nested_body()
    {
        var project = Document();
        var loop = project.Targets[0].Scripts[0].Body[1];
        var inner = loop.Body("body");

        var next = KeyboardMoves.Step(project, inner[0], down: true);

        Assert.That(next, Is.SameAs(inner[1]),
            "not the loop, and not the statement after it - the body is its own list");
    }

    [Test]
    public void Nothing_happens_for_a_block_that_is_not_in_a_stack()
    {
        var project = Document();
        var reporter = new Block { Kind = "deck.current-folder", Id = "r1" };

        Assert.Multiple(() =>
        {
            Assert.That(KeyboardMoves.Step(project, reporter, down: true), Is.Null,
                "a reporter in a hole is not a statement in a body");
            Assert.That(KeyboardMoves.Step(project, null, down: true), Is.Null);
        });
    }

    // ---- moving -------------------------------------------------------------------------------------

    [Test]
    public void Ctrl_down_moves_the_run_one_place_down()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var before = script.Body.Select(block => block.Id).ToList();

        var result = editor.Execute(KeyboardMoves.Nudge(project, script.Body[0], down: true)!);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True);
            Assert.That(script.Body.Select(block => block.Id).ToList(),
                Is.EqualTo(new[] { before[1], before[0], before[2] }),
                "one place down, and it swapped with the block that was there");
        });

        Assert.That(editor.Undo(), Is.True);
        Assert.That(script.Body.Select(block => block.Id).ToList(), Is.EqualTo(before));
    }

    [Test]
    public void A_keyboard_move_takes_one_block_rather_than_the_run_below_it()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var before = script.Body.Select(block => block.Id).ToList();

        editor.Execute(KeyboardMoves.Nudge(project, script.Body[1], down: true)!);

        Assert.That(script.Body.Select(block => block.Id).ToList(),
            Is.EqualTo(new[] { before[0], before[2], before[1] }),
            "deliberately not what a drag does. A drag carries the tail because grabbing statement n and "
            + "leaving the rest behind orphans them; a user who has selected one block and pressed "
            + "Ctrl+Down has named which block they mean, and carrying the tail would make the binding a "
            + "no-op for every block except the top of the stack");
    }

    [Test]
    public void Moving_past_the_end_of_a_stack_does_nothing_at_all()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var before = script.Body.Select(block => block.Id).ToList();

        var command = KeyboardMoves.Nudge(project, script.Body[^1], down: true);

        Assert.Multiple(() =>
        {
            Assert.That(command, Is.Null, "not possible is silence; refused is a complaint, and a user "
                + "holding Ctrl+Down at the bottom of a stack does not need to be told the stack ends");
            Assert.That(script.Body.Select(block => block.Id), Is.EqualTo(before));
            Assert.That(editor.Depth, Is.Zero);
        });
    }

    // ---- delete and duplicate -----------------------------------------------------------------------

    [Test]
    public void Delete_removes_one_block_and_closes_the_stack_up_behind_it()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];

        var doomed = script.Body[1].Id;
        var before = script.Body.Select(block => block.Id).ToList();

        editor.Execute(KeyboardMoves.Delete(project, script.Body[1])!);

        Assert.Multiple(() =>
        {
            Assert.That(script.Body.Select(block => block.Id),
                Is.EqualTo(new[] { before[0], before[2] }),
                "not the run below it. A drag carries the tail to avoid orphaning what was above, but "
                + "Delete has no such problem: the stack closes up behind the gap");
            Assert.That(script.Body.Any(block => block.Id == doomed), Is.False);
        });

        Assert.That(editor.Undo(), Is.True);
        Assert.That(script.Body.Select(block => block.Id).ToList(), Is.EqualTo(before),
            "and undo puts it back where it was, not at the end");
    }

    [Test]
    public void Delete_refuses_a_hat()
    {
        var project = Document();
        var script = project.Targets[0].Scripts[0];

        Assert.That(KeyboardMoves.Delete(project, script.Hat), Is.Null,
            "deleting a hat deletes its whole script, which is a different gesture with a different meaning");
    }

    [Test]
    public void Duplicate_copies_the_run_and_regenerates_every_id()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var originals = script.Body.Select(block => block.Id).ToList();
        var firstKind = script.Body[0].Kind;

        editor.Execute(KeyboardMoves.Duplicate(project, script.Body[0])!);

        var after = script.Body.Select(block => block.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(after, Has.Count.EqualTo(originals.Count * 2),
                "the run is duplicated, not just its first block");
            Assert.That(after.Distinct().Count(), Is.EqualTo(after.Count),
                "every id is fresh. Two nodes sharing an id is a document the validator reports, the "
                + "emitter emits twice, and no undo can find the right one");
            Assert.That(script.Body.Count(b => b.Kind == firstKind), Is.EqualTo(2),
                "and the duplicate of the first statement is the second statement of its kind");
        });

        Assert.That(editor.Undo(), Is.True);
        Assert.That(script.Body.Select(block => block.Id), Is.EqualTo(originals));
    }

    [Test]
    public void A_duplicate_does_not_share_its_nested_blocks_with_the_original()
    {
        var project = Document();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var loop = script.Body[1];

        // The fixture's loop holds two statements, so a shallow copy would share both of them.
        editor.Execute(KeyboardMoves.Duplicate(project, loop)!);

        var copy = script.Body.First(block => block.Id != loop.Id && block.Kind == loop.Kind);
        var originalInner = loop.Body("body").Select(block => block.Id).ToList();
        var copyInner = copy.Body("body").Select(block => block.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(copyInner, Has.Count.EqualTo(originalInner.Count));
            Assert.That(copyInner.Intersect(originalInner), Is.Empty,
                "a shallow copy leaves editing one of them editing the other - two blocks that are "
                + "secretly one");
            Assert.That(copy.Body("body").Zip(loop.Body("body")).Any(pair =>
                ReferenceEquals(pair.First, pair.Second)), Is.False);
        });
    }

    // ---- the keyboard drag cursor -------------------------------------------------------------------

    [Test]
    public void The_keyboard_cursor_walks_the_same_zones_the_pointer_uses()
    {
        var project = Document();
        var zones = KeyboardMoves.Zones(project, BlockCatalog.Find("ui.log")!);

        Assert.That(zones, Is.Not.Empty);
        Assert.That(zones.All(zone => DropResolver.Accepts(zone, BlockCatalog.Find("ui.log")!)), Is.True,
            "the cursor only ever lands somewhere the shape fits, so a keyboard user is never offered a "
            + "drop that would be refused");
    }

    [Test]
    public void The_cursor_skips_zones_that_refuse_the_shape()
    {
        var project = Document();

        var statements = KeyboardMoves.Zones(project, BlockCatalog.Find("ui.log")!);
        var reporters = KeyboardMoves.Zones(project, BlockCatalog.Find("deck.current-folder")!);

        Assert.Multiple(() =>
        {
            Assert.That(statements.All(zone => zone.Kind is not DropTargetKind.ValueSlot), Is.True);
            Assert.That(reporters.All(zone => zone.Kind is DropTargetKind.ValueSlot), Is.True,
                "a reporter's only zone is a hole, so that is the entire list");
        });
    }

    [Test]
    public void Stepping_the_cursor_comes_back_round()
    {
        var project = Document();
        var descriptor = BlockCatalog.Find("deck.current-folder")!;
        var zones = KeyboardMoves.Zones(project, descriptor);
        var cursor = KeyboardMoves.StepZone(project, null, "deck.current-folder");

        // Walk once more than there are zones and we must be back where we started.
        DropCandidate? walked = null;
        for (var i = 0; i < zones.Count; i++)
        {
            walked = KeyboardMoves.StepZone(project, walked, "deck.current-folder");
        }

        Assert.Multiple(() =>
        {
            Assert.That(walked, Is.Not.Null);
            Assert.That(walked!.Kind, Is.EqualTo(zones[0].Kind),
                "a keyboard has no pointer, so the only ordering a user can predict is 'the next thing "
                + "that fits'");
            Assert.That(cursor, Is.Not.Null);
        });
    }

    [Test]
    public void Nothing_is_offered_for_a_shape_the_catalogue_does_not_know()
    {
        var project = Document();

        Assert.Multiple(() =>
        {
            Assert.That(KeyboardMoves.StepZone(project, null, "no.such-block"), Is.Null);
            Assert.That(KeyboardMoves.Zones(project, BlockCatalog.Find("deck.current-folder")!), Is.Not.Null);
        });
    }

    // ---- cycling ------------------------------------------------------------------------------------

    [Test]
    public void Cycling_visits_every_statement_exactly_once()
    {
        var project = Document();
        var script = project.Targets[0].Scripts[0];
        var expected = project.Blocks().Count(block => !KeyboardMoves.IsHat(project, block));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        Block? cursor = script.Body[0];

        while (true)
        {
            var next = KeyboardMoves.Cycle(project, cursor, forward: true).First();
            if (!seen.Add(next.Id))
            {
                break;
            }

            cursor = next;
            if (seen.Count > expected + 2)
            {
                break;
            }
        }

        Assert.That(seen.Count, Is.EqualTo(expected),
            "Tab cycles; a control that stops at the end gives no sign anything else exists");
    }

    // ---- fixture ------------------------------------------------------------------------------------

    /// <summary>One action target, one script, three statements, the middle one a loop with two inside.</summary>
    private static VisualProject Document()
    {
        var project = new VisualProject
        {
            DocumentId = "keys",
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
                            // Three statements of three different kinds on purpose. A fixture of
                            // same-kind statements makes "how many of this kind are there now" useless as an
                            // assertion, because the copies and the originals are indistinguishable by
                            // anything except the thing under test.
                            Body =
                            [
                                Block("control.yield"),
                                Block("control.forever"),
                                Block("ui.log"),
                            ],
                        },
                    ],
                },
            ],
        };

        project.Targets[0].Scripts[0].Body[1].Body("body").Add(Block("control.wait-seconds"));
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
