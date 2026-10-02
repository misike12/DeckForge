using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Runtime;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The interpreter: what it does, and what it refuses to pretend about.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is about a trace — the ordered steps a run produced — rather than about private
/// state. That is deliberate: the trace is what the stage panel shows, so a test that asserted anything
/// else would be testing a thing no user ever sees.
/// </para>
/// <para>
/// The coercion cases are the parity surface from Part 10.5. The generated plugin has its own
/// <c>ToText</c>, <c>ToNumber</c> and <c>ToBool</c>, and these are the same rules written down twice on
/// purpose; a change to one side without the other is the failure this file exists to make visible.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ScriptInterpreterTests
{
    [Test]
    public void A_run_walks_the_body_and_says_what_it_did()
    {
        // Both slots every time: `var` names the variable to write and `value` is what to write into it.
        // Setting only `var` writes the variable's own current value back into itself, which is a real
        // block but not what this test is about.
        var (project, script) = Script(
            ("var.set", b =>
            {
                b.Inputs["var"] = Input.OfVariable("count");
                b.Inputs["value"] = Input.Of("5");
            }),
            ("var.change", b =>
            {
                b.Inputs["var"] = Input.OfVariable("count");
                b.Inputs["amount"] = Input.Of("2");
            }));

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);

        var outcome = interpreter.Run(script);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(RunOutcome.Completed));
            Assert.That(host.Variables.Get("count"), Is.EqualTo("7"),
                "set count to 5, then change count by 2: the change adds to what is there rather than "
                + "replacing it, which is the whole difference between change and set");
            Assert.That(interpreter.Trace.Where(step => step.Kind == ExecutionStepKind.BlockEntered).Count(),
                Is.EqualTo(2), "every block says it started");
            Assert.That(interpreter.Trace.Last().Kind, Is.EqualTo(ExecutionStepKind.BlockExited));
        });
    }

    [Test]
    public void A_disabled_block_does_not_run()
    {
        var (project, script) = Script(("var.set", b =>
        {
            b.Disabled = true;
            b.Inputs["var"] = Input.OfVariable("skipped");
        }));

        var host = new SimulatedHost();
        new ScriptInterpreter(project, host).Run(script);

        Assert.That(host.Variables.Get("skipped"), Is.Null, "a disabled block does not run");
    }

    [Test]
    public void The_trace_names_a_disabled_block_so_its_absence_is_visible()
    {
        var (project, script) = Script(("var.set", b =>
        {
            b.Disabled = true;
            b.Inputs["var"] = Input.OfVariable("skipped");
        }));

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);
        interpreter.Run(script);

        Assert.That(
            interpreter.Trace.Any(step =>
                step.Kind == ExecutionStepKind.BlockEntered && step.Label!.Contains("(disabled)", StringComparison.Ordinal)),
            Is.True,
            "a trace that simply omits a disabled block cannot be told apart from one where the block was "
            + "deleted, and those are very different documents");
    }

    [Test]
    public void Waiting_moves_the_simulated_clock_and_not_real_time()
    {
        var (project, script) = Script(
            ("control.wait-ms", b => b.Inputs["ms"] = Input.Of("250")),
            ("control.wait-seconds", b => b.Inputs["seconds"] = Input.Of("2")));

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);

        var before = DateTimeOffset.UtcNow;
        interpreter.Run(script);
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.Multiple(() =>
        {
            Assert.That(host.Clock.ElapsedMilliseconds, Is.EqualTo(2250),
                "250 ms plus two seconds, and the seconds block multiplies by a thousand the way the "
                + "emitted Task.Delay does");
            Assert.That(elapsed.TotalMilliseconds, Is.LessThan(1000),
                "the interpreter must not wait for real: a dry run that takes as long as the program would "
                + "defeat the point of having one");
            Assert.That(
                interpreter.Trace.Count(step => step.Kind == ExecutionStepKind.WaitElapsed),
                Is.EqualTo(2));
        });
    }

    [Test]
    public void A_clock_cannot_be_asked_to_run_backwards()
    {
        var clock = new SimulatedHost.ManualClock();

        Assert.Multiple(() =>
        {
            Assert.That(() => clock.Advance(-1), Throws.TypeOf<ArgumentOutOfRangeException>(),
                "a negative wait is a block that never finished, not a wait of minus one");
            Assert.That(() => clock.Advance(10), Throws.Nothing);
            Assert.That(clock.ElapsedMilliseconds, Is.EqualTo(10));
        });
    }

    [Test]
    public void A_breakpoint_stops_the_run_and_the_next_step_continues_it()
    {
        var (project, script) = Script(
            Set("first"),
            Set("second"),
            Set("third"));

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);
        var second = script.Body[1].Id;
        interpreter.Breakpoints.Add(second);

        var paused = interpreter.Run(script);

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.EqualTo(RunOutcome.Paused),
                "a run stops at a breakpoint rather than running past it, which is the whole feature");
            Assert.That(host.Variables.Get("first"), Is.EqualTo("first"), "the block before it ran");
            Assert.That(host.Variables.Get("second"), Is.Null, "the block it stopped on did not");
            Assert.That(interpreter.IsPaused, Is.True);
            Assert.That(
                interpreter.Trace.Last().Kind,
                Is.EqualTo(ExecutionStepKind.BreakpointHit));
        });

        var finished = interpreter.Continue();

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.EqualTo(RunOutcome.Completed));
            Assert.That(host.Variables.Get("second"), Is.EqualTo("second"));
            Assert.That(host.Variables.Get("third"), Is.EqualTo("third"));
        });
    }

    [Test]
    public void Stepping_executes_one_block_at_a_time()
    {
        var (project, script) = Script(
            Set("one"),
            Set("two"));

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host);

        interpreter.Step();
        var afterFirst = host.Variables.Get("one");
        var secondAfterFirst = host.Variables.Get("two");

        interpreter.Step();
        interpreter.Step();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo("one"));
            Assert.That(secondAfterFirst, Is.Null,
                "one step is one block: a Step button that ran the whole script would make the debugger "
                + "a Run button with a slower name");
            Assert.That(host.Variables.Get("two"), Is.EqualTo("two"));
        });
    }

    [Test]
    public void A_run_that_is_asked_to_stop_says_so()
    {
        var (project, script) = Script(("control.wait-ms", b => b.Inputs["ms"] = Input.Of("100")));
        var interpreter = new ScriptInterpreter(project, new SimulatedHost());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.Run(script, cancellation.Token), Is.EqualTo(RunOutcome.Cancelled));
            Assert.That(interpreter.Stop(), Is.EqualTo(RunOutcome.Cancelled));
        });
    }

    [Test]
    public void A_forever_ends_on_the_step_budget_and_says_that_is_why()
    {
        var (project, script) = Script(("control.forever", _ => { }));

        var host = new SimulatedHost();
        var interpreter = new ScriptInterpreter(project, host) { StepBudget = 50 };

        var outcome = interpreter.Run(script);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(RunOutcome.StepBudgetExhausted),
                "an unbounded loop is legal and the budget is how a preview stops pretending to run it "
                + "forever");
            Assert.That(
                interpreter.Trace.Last(),
                Is.EqualTo(new ExecutionStep(
                    ExecutionStepKind.Error,
                    Label: interpreter.Trace.Last().Label)));
            Assert.That(interpreter.Trace.Last().Label, Does.Contain("50"), "and the message says the budget");
        });
    }

    [Test]
    public void A_block_the_interpreter_does_not_know_stops_the_run_and_says_so()
    {
        var (project, script) = Script(("ui.log", b => b.Inputs["template"] = Input.Of("hello")));

        // A row with no semantics entry: the honest state for the preview, and the one the UI must not hide.
        var unknown = new Block { Kind = "ui.something-new", Id = "b-unknown" };
        script.Body.Add(unknown);

        var interpreter = new ScriptInterpreter(project, new SimulatedHost());

        var outcome = interpreter.Run(script);

        Assert.Multiple(() =>
        {
Assert.That(outcome, Is.EqualTo(RunOutcome.Failed),
                "an unknown block ends the run and says so; Completed would claim a clean run the "
                + "trace knows did not happen");
            Assert.That(
                interpreter.Trace.Any(step =>
                    step.Kind == ExecutionStepKind.Error && step.Label!.Contains("not interpreted", StringComparison.Ordinal)),
                Is.True,
                "a preview that quietly skips the blocks it does not know produces a trace that looks "
                + "complete and is not");
        });
    }

    [Test]
    public void A_log_template_is_filled_from_the_runs_parameters()
    {
        var (project, script) = Script(("ui.log", b => b.Inputs["template"] = Input.Of("Hello {who}, you have {count}")));

        var host = new SimulatedHost();
        host.Parameters.Set("who", "there");
        host.Parameters.Set("count", "3");

        var interpreter = new ScriptInterpreter(project, host) { DeclaredParameters = ["who", "count"] };
        interpreter.Run(script);

        Assert.Multiple(() =>
        {
            Assert.That(host.Log.Lines, Has.Some.Contains("Hello there, you have 3"));
            Assert.That(
                interpreter.Trace.Any(step => step.Kind == ExecutionStepKind.LogEmitted),
                Is.True);
        });
    }

    [Test]
    public void A_hole_with_no_parameter_behind_it_is_left_written()
    {
        var (project, script) = Script(("ui.log", b => b.Inputs["template"] = Input.Of("Hello {who}")));

        var host = new SimulatedHost();
        new ScriptInterpreter(project, host) { DeclaredParameters = ["who"] }.Run(script);

        Assert.That(host.Log.Lines, Has.Some.Contains("Hello {who}"),
            "blanking it produces a line that says nothing about which parameter is missing, while the "
            + "written hole names it");
    }

    [Test]
    public void Watching_adds_and_removes_a_name_without_complaining_twice()
    {
        var (project, script) = Script(
            ("var.watch", b => b.Inputs["var"] = Input.OfVariable("a")),
            ("var.unwatch", b => b.Inputs["var"] = Input.OfVariable("a")),
            ("var.unwatch", b => b.Inputs["var"] = Input.OfVariable("b")));

        var interpreter = new ScriptInterpreter(project, new SimulatedHost());
        interpreter.Run(script);

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.Watched, Is.Empty);
            Assert.That(
                interpreter.Trace.Any(step =>
                    step.Kind == ExecutionStepKind.HostCall && step.Member == "unwatch" && step.Value == "was not watched"),
                Is.True,
                "unwatching something that was never watched is a thing users do; the trace should say so "
                + "rather than failing or staying silent");
        });
    }

    [Test]
    public void Random_is_reproducible_from_the_seed_and_includes_both_ends()
    {
        var results = new List<string>();

for (var run = 0; run < 3; run++)
        {
            // The reporter sits in a slot, because that is where a reporter goes. A reporter used as a
            // top-level statement computes nothing, so asserting on it would be asserting on a shape a
            // user cannot build.
            var (project, script) = Script(("var.set", block =>
            {
                var random = Block("ops.random");
                random.Inputs["from"] = Input.Of("1");
                random.Inputs["to"] = Input.Of("3");

                block.Inputs["var"] = Input.OfVariable("pick");
                block.Inputs["value"] = new BlockInput { Block = random };
            }));

            var host = new SimulatedHost();
            var interpreter = new ScriptInterpreter(project, host, seed: 1234);
            interpreter.Run(script);

            results.Add(host.Variables.Get("pick") ?? string.Empty);
        }

        Assert.Multiple(() =>
        {
            Assert.That(results.Distinct().Count(), Is.EqualTo(1),
                "the same seed and the same document must produce the same trace, or a test asserting on a "
                + "step sequence is a statement about the machine");
            Assert.That(double.Parse(results[0], System.Globalization.CultureInfo.InvariantCulture),
                Is.InRange(1, 3),
                "inclusive of both ends, the way Scratch's random is");
        });
    }

    // ---- coercion: the parity surface ----------------------------------------------------------------

    [TestCase(3d, "3")]
    [TestCase(3.5, "3.5")]
    [TestCase(true, "true")]
    [TestCase(false, "false")]
    [TestCase(null, "")]
    [TestCase("already text", "already text")]
    public void Text_renders_a_value_the_way_the_generated_runtime_does(object? value, string expected) =>
        Assert.That(Values.Text(value), Is.EqualTo(expected));

    [TestCase("3", 3d)]
    [TestCase("3.5", 3.5d)]
    [TestCase("  7  ", 7d)]
[TestCase("true", 0d)]
    [TestCase("false", 0d)]
    [TestCase("not a number", 0d)]
    [TestCase("", 0d)]
    [TestCase(null, 0d)]
    public void Number_reads_a_value_the_way_the_generated_runtime_does(string? value, double expected) =>
        Assert.That(Values.Number(value), Is.EqualTo(expected));

    [Test]
    public void A_boolean_is_a_number_but_the_text_of_one_is_not()
    {
        // The asymmetry is the runtime's, and this is the test that pins it. `ToNumber` handles a real
        // boolean before it ever reaches the text, so `true` becomes 1; but "true" *as a string* is parsed
        // as a number, fails, and becomes 0. A slot holding one and a slot holding the word are not the
        // same slot, and the trace has to agree with the plugin about which is which.
        Assert.Multiple(() =>
        {
            Assert.That(Values.Number(true), Is.EqualTo(1d));
            Assert.That(Values.Number("true"), Is.EqualTo(0d));
            Assert.That(Values.Text(Values.Number(true)), Is.EqualTo("1"));
        });
    }

[TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("2", true)]
    [TestCase("0", false)]
    [TestCase("anything", true)]
    [TestCase("", false)]
    [TestCase(null, false)]
public void Boolean_reads_a_value_the_way_the_generated_runtime_does(string? value, bool expected) =>
        Assert.That(Values.Boolean(value), Is.EqualTo(expected));

    [Test]
    public void Boolean_keeps_a_boolean_a_boolean()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Values.Boolean(true), Is.True);
            Assert.That(Values.Boolean(false), Is.False);
            Assert.That(Values.Boolean(0d), Is.False, "and a zero is a false, not a \"there is text here\"");
            Assert.That(Values.Boolean(2d), Is.True);
            Assert.That(Values.Boolean((object?)null), Is.False);
        });
    }

    [Test]
    public void Number_and_Boolean_agree_with_their_string_forms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Values.Number(Values.Text(3.5d)), Is.EqualTo(3.5d),
                "a value has to survive the round trip through its own text, or the trace and the plugin "
                + "disagree about what a slot held");
            Assert.That(Values.Boolean(Values.Text(Values.Boolean("anything"))), Is.True);
            Assert.That(Values.IsNumber("3.5"), Is.True);
            Assert.That(Values.IsNumber("three"), Is.False);
        });
    }

    // ---- the semantics table -----------------------------------------------------------------------

    [Test]
    public void The_semantics_table_names_a_shape_for_every_entry()
    {
        Assert.That(BlockSemantics.All, Is.Not.Empty);
        Assert.That(
            BlockSemantics.All.Values.Select(semantic => semantic.Shape),
            Has.All.Matches<string>(shape => shape is "stack" or "reporter" or "hat" or "container"));
    }

    [Test]
    public void Every_interpreted_block_has_an_entry_and_an_entry_agrees_with_the_catalogue()
    {
        // The two directions matter: an entry for a block the catalogue does not have is a rule about
        // nothing, and an interpreted block with no entry is a rule that is never checked.
        var known = BlockSemantics.All.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(
                known.Where(kind => BlockCatalog.Find(kind) is null),
                Is.Empty,
                "these have semantics but no catalogue row, so nothing can ever be interpreted as them");

            Assert.That(
                BlockCatalog.All.Where(descriptor => descriptor.IsContainer)
                    .Select(descriptor => descriptor.Kind)
                    .Where(kind => !known.Contains(kind)),
                Is.Empty,
                "every container has to be interpreted, because descending into one is what a loop is");
        });
    }

    [Test]
    public void A_wait_block_is_marked_as_waiting_and_the_others_are_not()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlockSemantics.For("control.wait-ms")!.Waits, Is.True);
            Assert.That(BlockSemantics.For("control.wait-seconds")!.Waits, Is.True);
            Assert.That(BlockSemantics.For("var.set")!.Waits, Is.False,
                "a block that does not wait must not move the clock, or the simulated elapsed time stops "
                + "meaning anything");
        });
    }

    [Test]
    public void Every_host_calling_entry_names_the_surface_and_member_it_uses()
    {
        foreach (var semantic in BlockSemantics.All.Values.Where(semantic => semantic.HostSurface is not null))
        {
            Assert.Multiple(() =>
            {
                Assert.That(semantic.HostMember, Is.Not.Null.And.Not.Empty,
                    $"{semantic.Kind} names a surface but no member");
                Assert.That(semantic.Notes, Is.Not.Null.And.Not.Empty,
                    $"{semantic.Kind} has no note saying what is easy to get wrong about it");
            });
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------



    private static (VisualProject Project, VisualScript Script) Script(
        params (string Kind, Action<Block> SetUp)[] blocks)
    {
        var script = new VisualScript
        {
            Id = "script-1",
            Name = "script",
            Hat = Block("hat.action-runs"),
        };

        foreach (var (kind, setUp) in blocks)
        {
            var block = Block(kind);
            setUp(block);
            script.Body.Add(block);
        }

        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:one",
                    Name = "One",
                    Kind = TargetKind.Action,
                    TargetFile = "Actions/OneAction.cs",
                    AnchorId = "ExecuteAsync",
                    Scripts = [script],
                },
            ],
        };

        return (project, script);
    }

    /// <summary>A `set {var} to {value}` naming <paramref name="name"/>.</summary>
    private static (string Kind, Action<Block> SetUp) Set(string name) =>
        ("var.set", block =>
        {
            block.Inputs["var"] = Input.OfVariable(name);
            block.Inputs["value"] = Input.Of(name);
        });

    private static Block Block(string kind)
    {
        var descriptor = BlockCatalog.Find(kind);
        var block = new Block { Kind = kind, Id = kind + "-" + Guid.NewGuid().ToString("N")[..4] };

        foreach (var slot in descriptor?.Slots ?? [])
        {
            block.Inputs[slot.Name] = Input.Of("1");
        }

        foreach (var menu in descriptor?.Menus ?? [])
        {
            block.Fields[menu.Name] = menu.Default;
        }

        return block;
    }

    private sealed class Input
    {
        public static BlockInput Of(string text) => new() { Text = text };

        public static BlockInput OfVariable(string name) => new() { Variable = name };
    }
}