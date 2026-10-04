using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Runtime;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The transport's Pause, and the one target a run is allowed to touch.
/// </summary>
/// <remarks>
/// <para>
/// A new fixture rather than more cases in <c>ScriptInterpreterTests</c>, because what is being pinned here
/// is not what a run <em>computes</em> but where it is willing to <em>stop</em> — and the whole reason the
/// feature is worth a file is that the previous answer was "wherever it happens to be", which in practice
/// meant nowhere: there was no Pause, Stop was the only way out, and a Stop could not be resumed.
/// </para>
/// <para>
/// The interesting cases are the ones a stage creates rather than a person: a request that arrives while
/// nothing is running, a request that survives a reset, and a stepped pump that must ignore it. Those are
/// the three ways a pause can be real and still be useless, and none of them is visible without a driver
/// asking for a pause at a chosen moment.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ScriptInterpreterPauseTests
{
    [Test]
    public void A_pause_stops_the_run_after_the_block_that_was_running_and_continue_finishes_it()
    {
        var project = Project(Set("a"), Set("b"), Set("c"));
        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);

        // Asked for from the observer, which is the only place a *batch* run can be interrupted from: the
        // pump is inside the interpreter, on this thread, with the caller long gone. Without a seam here,
        // "pause a run to its end" is untestable and therefore unchecked.
        var afterFirst = 0;
        interpreter.Trace.Observer = step =>
        {
            if (step.Kind == ExecutionStepKind.BlockExited && ++afterFirst == 1)
            {
                interpreter.RequestPause();
            }
        };

        var paused = interpreter.Run(project.Targets[0].Scripts[0]);

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.EqualTo(RunOutcome.Paused));
            Assert.That(interpreter.PausedOnRequest, Is.True,
                "otherwise a stage cannot tell a pause it asked for from a breakpoint it did not");
            Assert.That(host.Variables.Get("a"), Is.EqualTo("a"), "the block in flight finished");
            Assert.That(host.Variables.Get("b"), Is.Null,
                "and the one after it did not start: a pause is a boundary, not a cancellation");
            Assert.That(interpreter.PausedAfterBlockId, Is.EqualTo(project.Targets[0].Scripts[0].Body[0].Id),
                "and the stage is told which block it stopped after, so it can say so rather than 'paused'");
            Assert.That(
                interpreter.Trace.Any(step => step.Kind == ExecutionStepKind.PausedAfter),
                Is.True,
                "a pause with no trace line is indistinguishable from a run that stopped by itself");
        });

        var finished = interpreter.Continue();

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.EqualTo(RunOutcome.Completed));
            Assert.That(host.Variables.Get("b"), Is.EqualTo("b"));
            Assert.That(host.Variables.Get("c"), Is.EqualTo("c"),
                "the resume carries on from the block boundary rather than starting the script again, which "
                + "is the whole difference between a pause and a second Run");
            Assert.That(interpreter.PausedOnRequest, Is.False,
                "and the marks of the pause are cleared, or the stage offers to Resume a run that is over");
        });
    }

    [Test]
    public void A_pause_asked_for_before_anything_runs_does_nothing_at_all()
    {
        var project = Project(Set("a"), Set("b"));
        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);

        // Nothing has been Reset, so nothing is running.
        interpreter.RequestPause();

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.PauseRequested, Is.False,
                "an armed request that survives into the next run is a Pause that appears to do nothing and "
                + "then stops a run the user never paused - which is worse than having no Pause at all");
            Assert.That(interpreter.Run(project.Targets[0].Scripts[0]), Is.EqualTo(RunOutcome.Completed));
            Assert.That(host.Variables.Get("b"), Is.EqualTo("b"),
                "and the run it was armed for finishes, rather than stopping after its first block");
        });
    }

    [Test]
    public void A_reset_takes_the_request_with_it()
    {
        var project = Project(Set("a"), Set("b"));
        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);
        var script = project.Targets[0].Scripts[0];

        interpreter.Run(script);
        interpreter.RequestPause();
        interpreter.Reset(script);

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.PauseRequested, Is.False,
                "Reset is exactly when a user who was about to press Pause presses Reset instead");
            Assert.That(interpreter.Run(script), Is.EqualTo(RunOutcome.Completed));
        });
    }

    [Test]
    public void A_step_runs_its_one_block_and_then_honours_the_pause_because_that_is_the_only_pump_a_paced_run_has()
    {
        var project = Project(Set("a"), Set("b"));
        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);
        var script = project.Targets[0].Scripts[0];

        interpreter.Reset(script);
        interpreter.Step();

        // Asked for between two steps, which is exactly where a stage's Pause arrives: on the UI thread,
        // after a tick has finished and before the next one is armed.
        interpreter.RequestPause();

        interpreter.Step();

        Assert.Multiple(() =>
        {
            Assert.That(host.Variables.Get("a"), Is.EqualTo("a"));
            Assert.That(host.Variables.Get("b"), Is.EqualTo("b"),
                "a pause stops the *next* block, not the one already under way: cancelling the block in "
                + "flight would leave the host half written and the trace mid statement");
            Assert.That(interpreter.PausedOnRequest, Is.True,
                "the step honoured the request, which is the only way a stage's own one-block-per-tick pump "
                + "can be paused at all - an earlier version checked the request only in the multi-block "
                + "path and the transport button armed something nothing ever read");
            Assert.That(interpreter.PausedAfterBlockId, Is.EqualTo(script.Body[1].Id));
        });
    }

    [Test]
    public void A_pause_stops_at_the_container_and_not_one_block_inside_it()
    {
        var inner = Set("inner");
        var repeat = new Block { Kind = "control.repeat", Id = "repeat-1" };
        repeat.Inputs["times"] = new BlockInput { Number = 3 };
        repeat.Body("body").Add(inner);

        var project = Project(repeat, Set("after"));
        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);

        var exits = 0;
        interpreter.Trace.Observer = step =>
        {
            if (step.Kind == ExecutionStepKind.BlockExited && ++exits == 1)
            {
                interpreter.RequestPause();
            }
        };

        interpreter.Run(project.Targets[0].Scripts[0]);

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.PausedAfterBlockId, Is.EqualTo(repeat.Id),
                "the loop's own step is written and nothing inside it has run, so this is the boundary the "
                + "user asked to stop at");
            Assert.That(host.Variables.Get("inner"), Is.Null);
            Assert.That(host.Variables.Get("after"), Is.Null);
        });
    }

    [Test]
    public void A_run_over_one_target_cannot_touch_another_targets_scripts()
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                Target("action:one", "One", Script("script-1", Set("one"))),
                Target("action:two", "Two", Script("script-2", Set("two"))),
            ],
        };

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host, targetId: "action:two");

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.TargetId, Is.EqualTo("action:two"));
            Assert.That(interpreter.Steps.Select(step => step.Id), Is.EqualTo(new[] { "script-2" }),
                "Part 9.7's header picker makes the target the user's choice, and a stage that also ran the "
                + "other actions' scripts would produce a trace full of values they never wrote");

            interpreter.Run();
            Assert.That(host.Variables.Get("one"), Is.Null);
            Assert.That(host.Variables.Get("two"), Is.EqualTo("two"));
        });
    }

    [Test]
    public void A_run_with_no_target_named_still_covers_the_whole_document()
    {
        // The batch Run of a test and the dry-run tracer both want everything, so the filter must not turn
        // into a default. That is the mistake this case exists to catch.
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                Target("action:one", "One", Script("script-1", Set("one"))),
                Target("action:two", "Two", Script("script-2", Set("two"))),
            ],
        };

        var interpreter = new ScriptInterpreter(project, new SimulatedHost());

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.TargetId, Is.Null);
            Assert.That(interpreter.Steps.Select(step => step.Id),
                Is.EqualTo(new[] { "script-1", "script-2" }));
        });
    }

    [Test]
    public void A_target_that_is_not_in_the_document_leaves_the_run_with_nothing_to_do()
    {
        // A stale id from a picker bound to a document that has since been replaced. It must not fall back
        // to "the first target", because that runs the wrong action rather than nothing.
        var project = Project(Set("a"));

        var interpreter = new ScriptInterpreter(project, new SimulatedHost(), targetId: "action:gone");

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.Steps, Is.Empty);
            Assert.That(interpreter.Run(), Is.EqualTo(RunOutcome.Completed));
        });
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>One target holding one script whose body is the given blocks.</summary>
    private static VisualProject Project(params Block[] body)
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        project.Targets.Add(Target("action:one", "One", Script("script-1", body)));
        return project;
    }

    private static VisualTarget Target(string id, string name, VisualScript script) =>
        new()
        {
            Id = id,
            Name = name,
            Kind = TargetKind.Action,
            TargetFile = $"Actions/{name}Action.cs",
            AnchorId = "ExecuteAsync",
            Scripts = [script],
        };

    private static VisualScript Script(string id, params Block[] body)
    {
        var script = new VisualScript
        {
            Id = id,
            Name = id,
            Hat = Block("hat.action-runs"),
        };

        foreach (var block in body)
        {
            script.Body.Add(block);
        }

        return script;
    }

    /// <summary>A <c>set {var} to {value}</c> that sets its own name.</summary>
    private static Block Set(string name) =>
        Block("var.set", block =>
        {
            block.Inputs["var"] = new BlockInput { Variable = name };
            block.Inputs["value"] = new BlockInput { Text = name };
        });

    /// <summary>A block with every slot and menu filled from the catalogue, then set up.</summary>
    private static Block Block(string kind, Action<Block>? setUp = null)
    {
        var descriptor = BlockCatalog.Find(kind);
        var block = new Block { Kind = kind, Id = kind + "-" + Guid.NewGuid().ToString("N")[..4] };

        foreach (var slot in descriptor?.Slots ?? [])
        {
            block.Inputs[slot.Name] = new BlockInput { Text = "1" };
        }

        foreach (var menu in descriptor?.Menus ?? [])
        {
            block.Fields[menu.Name] = menu.Default;
        }

        setUp?.Invoke(block);

        return block;
    }
}
