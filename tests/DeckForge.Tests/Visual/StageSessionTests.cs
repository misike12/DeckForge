using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Runtime;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The stage: the transport, the watch table, the breakpoints and the trace, with no window in the process.
/// </summary>
/// <remarks>
/// Every one of these is a statement about something a user would otherwise have to check by watching a
/// window: that the transport runs, that a breakpoint the tile shows is the same set the interpreter
/// reads, that the watch table reads the host rather than the trace, that a loop that never ends is cut
/// off *and says so*, and that the honesty line is a property rather than a sentence in a design document.
/// </remarks>
[TestFixture]
public sealed class StageSessionTests
{
    private HashSet<string> _breakpoints = null!;
    private HashSet<string> _watched = null!;
    private Ticker _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _breakpoints = new HashSet<string>(StringComparer.Ordinal);
        _watched = new HashSet<string>(StringComparer.Ordinal);
        _clock = new Ticker();
    }

    [Test]
    public void The_panel_says_out_loud_that_it_is_a_preview_and_not_the_plugin()
    {
        var session = Session(Project());

        Assert.Multiple(() =>
        {
            Assert.That(session.HonestyStatement, Does.Contain("preview"));
            Assert.That(
                session.HonestyStatement,
                Does.Contain("not a guarantee"),
                "the whole point of the line is that it says what it cannot promise, not merely that it "
                + "is a preview");
            Assert.That(
                session.HonestyStatement,
                Does.Contain("SDK"),
                "and it names what does decide, so the user is left knowing who to believe");
        });
    }

    [Test]
    public void The_scripts_on_screen_are_the_document_s_scripts()
    {
        var project = Project(Log("one"));

        // A second script, because a stage that can only run the first script is not the transport
        // Part 10.3 asks for.
        var second = new VisualScript
        {
            Id = "script-2",
            Name = "Second",
            Hat = Block("hat.action-runs", _ => { }),
        };
        second.Body.Add(Log("two"));
        project.Targets[0].Scripts.Add(second);

        var session = Session(project);

        Assert.Multiple(() =>
        {
            Assert.That(session.Scripts.Select(entry => entry.Id), Is.EqualTo(new[] { "script-1", "script-2" }));
            Assert.That(session.SelectedScript?.Id, Is.EqualTo("script-1"),
                "the first is preselected, because a stage with nothing chosen gives the user nothing to "
                + "press Run on");
            Assert.That(session.Scripts[0].Trigger, Is.Not.Empty,
                "each row says what starts it, which is the part of a hat a name does not tell you");
        });
    }

    [Test]
    public void Step_runs_one_block_and_leaves_the_rest_for_the_next_press()
    {
        var session = Session(Project(Set("a", "1"), Set("b", "2")));

        session.Step();

        Assert.Multiple(() =>
        {
            Assert.That(session.Host.Variables.Get("a"), Is.EqualTo("1"), "the first block ran");
            Assert.That(
                session.Host.Variables.Get("b"),
                Is.Null,
                "and only the first one did, because Step that keeps going is Run wearing a Step button's "
                + "label");
            Assert.That(session.Trace, Is.Not.Empty, "and the trace says what it did");
        });
    }

    [Test]
    public void Step_into_stops_inside_a_loop_rather_than_running_every_pass()
    {
        var change = Block("var.change", block =>
        {
            block.Inputs["var"] = Input.OfVariable("count");
            block.Inputs["amount"] = Input.Of("2");
        });

        var repeat = Block("control.repeat", block =>
        {
            block.Inputs["times"] = new BlockInput { Number = 5 };
            block.Body("body").Add(change);
        });

        var session = Session(Project(Set("count", "0"), repeat));

        session.Step();
        session.StepInto();

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Host.Variables.Get("count"),
                Is.EqualTo("0"),
                "stepping into a loop stops *inside* it, on the way to its first statement - the body has "
                + "not run yet, and a simulator that ran it here would have skipped a whole pass without "
                + "saying so");
            Assert.That(
                session.Trace.Any(row => row.BlockId == repeat.Id),
                Is.True,
                "the loop's own step is traced, or the user cannot tell what they stepped into");
        });

        session.Step();

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Host.Variables.Get("count"),
                Is.EqualTo("2"),
                "and the next press runs one pass, not all five: stepping over the repeat would have run "
                + "the whole thing");
            Assert.That(
                session.Trace.Any(row => row.BlockId == change.Id),
                Is.True,
                "the trace has to show the block inside the loop, or the user cannot see where it stopped");
        });
    }

    [Test]
    public void A_breakpoint_the_tile_shows_is_the_one_the_interpreter_stops_on()
    {
        var project = Project(Set("a", "1"), Set("b", "2"));
        var second = project.Targets[0].Scripts[0].Body[1];

        // The set the editor draws from, handed to the session by reference - which is the arrangement
        // that makes a tile and a run agree.
        Assert.Multiple(() =>
        {
            Assert.That(_clock, Is.Not.Null, "the clock exists before the assertion that needs it");
        });

        var session = Session(project);

        Assert.Multiple(() =>
        {
            Assert.That(session.ToggleBreakpoint(second.Id), Is.True, "clicking an empty gutter sets one");
            Assert.That(
                _breakpoints,
                Does.Contain(second.Id),
                "and it lands in the set the editor reads, or the tile shows a dot and the run ignores it");
            Assert.That(session.ToggleBreakpoint(second.Id), Is.False, "and clicking again clears it");
            Assert.That(_breakpoints, Does.Not.Contain(second.Id));
        });
    }

    [Test]
    public void A_breakpoint_stops_the_run_and_the_outcome_says_that_is_why()
    {
        var project = Project(Set("a", "1"), Set("b", "2"));
        var session = Session(project);

        session.ToggleBreakpoint(project.Targets[0].Scripts[0].Body[1].Id);
        session.Run();
        _clock.FireAll();

        Assert.Multiple(() =>
        {
            Assert.That(session.Host.Variables.Get("a"), Is.EqualTo("1"));
            Assert.That(
                session.Host.Variables.Get("b"),
                Is.Null,
                "the breakpoint is on the second block, so the run stops before it");
            Assert.That(
                session.Outcome,
                Does.Contain("breakpoint").IgnoreCase,
                "a run that stops without saying why looks exactly like one that finished");
            Assert.That(session.IsRunning, Is.False);
        });
    }

    [Test]
    public void The_watch_table_reads_the_host_and_survives_an_edit_that_rebuilds_the_stage()
    {
        var project = Project(Set("count", "7"));
        var session = Session(project);

        session.Watch("count");
        session.Step();

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Watched.Single().Value,
                Is.EqualTo("7"),
                "the value comes from the host, which is the only thing that knows what the block did");

            // An edit rebuilds the stage; a watch list that did not survive it would be the first thing a
            // user reported.
            session.Rebind(project);

            Assert.That(
                session.Watched.Select(row => row.Name),
                Does.Contain("count"),
                "a rebuild must not forget what was watched, and it did not re-read the host, so the value "
                + "is a dash rather than a lie");
        });
    }

    [Test]
    public void Watching_a_name_by_hand_and_unwatching_it_are_both_quiet_the_second_time()
    {
        var session = Session(Project());

        session.Watch("anything");
        session.Unwatch("anything");
        session.Unwatch("anything");

        Assert.Multiple(() =>
        {
            Assert.That(session.Watched, Is.Empty);
            Assert.That(_watched, Is.Empty, "and the editor's own set agrees, because there is only one");
        });
    }

    [Test]
    public void A_notification_the_script_raised_shows_on_the_stage_and_not_only_in_the_trace()
    {
        var notify = Block("ui.notify", block =>
        {
            block.Inputs["title"] = Input.Of("done");
            block.Inputs["body"] = Input.Of("nothing to declare");
        });

        var session = Session(Project(notify));

        session.Step();

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Notifications,
                Is.Not.Empty,
                "a notification the script raised has to be on the stage, which is the whole reason there "
                + "is a stage rather than only a trace");
            Assert.That(session.Notifications[^1].Title, Is.EqualTo("done"));
            Assert.That(
                session.ClockMilliseconds,
                Is.EqualTo(0),
                "nothing waited, so the simulated clock has not moved");
        });
    }

    [Test]
    public void Reset_puts_the_host_back_so_a_second_run_is_the_first_run()
    {
        // Three statements, because the third press has to be the one that differs: with two, the third
        // press would find the script finished and change nothing, which is the same as a reset that did
        // not work.
        var session = Session(Project(Set("count", "1"), Change("count", "2"), Change("count", "4")));

        session.Step();
        session.Step();
        var afterTwo = session.Host.Variables.Get("count");

        session.Step();
        Assert.That(session.Host.Variables.Get("count"), Is.Not.EqualTo(afterTwo), "the change added again");

        session.Reset();
        session.Step();
        session.Step();

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Host.Variables.Get("count"),
                Is.EqualTo(afterTwo),
                "a reset that leaves half the last run behind is why a script works the first time only");
            Assert.That(session.Outcome, Does.Contain("Paused"), "and the outcome line describes the state it is in");
        });
    }

    [Test]
    public void Stop_ends_the_run_without_claiming_the_interpreter_was_cancelled()
    {
        var session = Session(Project(Forever()));
        _watched.Add("count");

        session.Run();
        _clock.FireOne();

        session.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(session.IsRunning, Is.False);
            Assert.That(
                session.Outcome,
                Does.Contain("hand"),
                "pressing Stop by hand says that, because the trace has no line saying so");
        });
    }

    [Test]
    public void A_forever_that_never_ends_is_cut_off_and_the_stage_says_the_budget_did_it()
    {
        var session = Session(Project(Forever()));

        session.Run();

        // Every tick the timer owes. A forever has no other ending, so the budget is the ending - and the
        // only question worth asking is whether the stage admits it.
        for (var tick = 0; tick < 4_000 && session.IsRunning; tick++)
        {
            _clock.FireOne();
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Outcome,
                Does.Contain("budget"),
                "a loop that never ends and a stage that quietly stops look the same to a user unless the "
                + "stage says which one happened");
            Assert.That(session.IsRunning, Is.False);
        });
    }

    [Test]
    public void The_stage_starts_offline_and_says_so()
    {
        var session = Session(Project());

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Host.Http.AllowRealNetwork,
                Is.False,
                "Part 10.1 wants it behind an explicit toggle, which means off before the toggle is touched");
            Assert.That(session.NetworkText, Does.Contain("offline"));

            session.AllowRealNetwork(true);

            Assert.That(session.NetworkText, Does.Contain("real"), "and the line follows the toggle");
        });
    }

    [Test]
    public void The_speed_slider_clamps_itself_because_zero_steps_a_second_is_not_a_speed()
    {
        var session = Session(Project());

        session.Speed = 0;
        Assert.That(session.Speed, Is.EqualTo(1));

        session.Speed = 5_000;
        Assert.That(session.Speed, Is.EqualTo(120));
    }

    [Test]
    public void Switching_scripts_clears_the_stage_rather_than_relabelling_the_last_run()
    {
        var project = Project(Log("hello"));
        var second = new VisualScript
        {
            Id = "script-2",
            Name = "Second",
            Hat = new Block { Kind = "hat.action", Id = "hat-2" },
        };
        second.Body.Add(Log("other"));
        project.Targets[0].Scripts.Add(second);

        var session = Session(project);

        session.Step();
        Assert.That(session.Trace, Is.Not.Empty);

        session.SelectScript("script-2");

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Trace,
                Is.Empty,
                "one script's variables on the stage under another script's name is worse than not having "
                + "run either");
            Assert.That(session.SelectedScript?.Id, Is.EqualTo("script-2"));
            Assert.That(session.Outcome, Does.Contain("Nothing has run"));
        });
    }

    [Test]
    public void Parameters_the_action_declared_are_offered_with_an_empty_value_rather_than_a_guess()
    {
        var session = Session(Project());

        session.Rebind(parameters: ["name", "count"]);

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Parameters.Select(row => row.Name),
                Is.EqualTo(new[] { "count", "name" }),
                "sorted, so the form does not reorder itself between runs");
            Assert.That(
                session.Parameters.All(row => row.Value.Length == 0),
                Is.True,
                "a parameter the user has not filled in gets an empty value, not a plausible-looking one");
        });
    }

    [Test]
    public void A_parameter_the_form_supplied_is_the_value_the_run_sees()
    {
        var log = Block("ui.log", block =>
        {
            block.Inputs["template"] = Input.Of("hi {who}");
            block.Inputs["context"] = Input.OfVariable("who");
        });

        var session = Session(Project(log));
        session.Rebind(parameters: ["who"]);
        session.SetParameter("who", "world");

        session.Step();

        Assert.That(
            session.Log.Single(),
            Does.Contain("world"),
            "otherwise the form is a text box in the corner and the run supplies its own answer");
    }

    [Test]
    public void The_trace_keeps_the_last_five_hundred_steps_so_a_fast_loop_cannot_take_the_window_with_it()
    {
        var session = Session(Project(Forever()));

        session.Run();
        for (var tick = 0; tick < 700 && session.IsRunning; tick++)
        {
            _clock.FireOne();
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                session.Trace.Count,
                Is.LessThanOrEqualTo(StageSession.TraceLimit),
                "twenty thousand rows in an ItemsControl is the stage taking the window down with it");
            Assert.That(
                session.Trace,
                Is.Not.Empty,
                "and it still shows something, so the cap is a window rather than a silent no-op");
        });
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>A session over a document, sharing the test's breakpoint and watch sets.</summary>
    private StageSession Session(VisualProject project) =>
        new(project, _breakpoints, _watched, _clock.Schedule);

    /// <summary>One script, with a hat, whose body is the given blocks.</summary>
    private static VisualProject Project(params Block[] body)
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget { Id = "t", Name = "Action", Kind = TargetKind.Action };
        project.Targets.Add(target);

        var script = new VisualScript
        {
            Id = "script-1",
            Name = "Main",
            Hat = Block("hat.action-runs", _ => { }),
        };
        target.Scripts.Add(script);

        foreach (var block in body)
        {
            script.Body.Add(block);
        }

        return project;
    }

    /// <summary>A <c>set {var} to {value}</c>.</summary>
    private static Block Set(string name, string value) =>
        Block("var.set", block =>
        {
            block.Inputs["var"] = Input.OfVariable(name);
            block.Inputs["value"] = Input.Of(value);
        });

    /// <summary>A <c>change {var} by {amount}</c>.</summary>
    private static Block Change(string name, string amount) =>
        Block("var.change", block =>
        {
            block.Inputs["var"] = Input.OfVariable(name);
            block.Inputs["amount"] = Input.Of(amount);
        });

    /// <summary>A <c>log</c> block.</summary>
    private static Block Log(string template) =>
        Block("ui.log", block => block.Inputs["template"] = Input.Of(template));

    /// <summary>A notification.</summary>
    private static Block Notify(string title) =>
        Block("ui.notify", block => block.Inputs["title"] = Input.Of(title));

    /// <summary>A <c>forever</c> with a change in it, which is the block that does not end.</summary>
    private static Block Forever() =>
        Block("control.forever", block => block.Body("body").Add(Change("count", "1")));

    /// <summary>A block with every slot and menu filled from the catalogue, then set up.</summary>
    private static Block Block(string kind, Action<Block> setUp)
    {
        var descriptor = BlockCatalog.Find(kind);
        var block = new Block { Kind = kind, Id = $"{kind}-{Guid.NewGuid():N}"[..14] };

        foreach (var slot in descriptor?.Slots ?? [])
        {
            block.Inputs[slot.Name] = Input.Of("1");
        }

        foreach (var menu in descriptor?.Menus ?? [])
        {
            block.Fields[menu.Name] = menu.Default;
        }

        setUp(block);

        return block;
    }

    /// <summary>Slot values, in the shape the document holds them.</summary>
    private static class Input
    {
        public static BlockInput Of(string text) => new() { Text = text };

        public static BlockInput OfVariable(string name) => new() { Variable = name };

        public static BlockInput OfBlock(Block block) => new() { Block = block };
    }

    /// <summary>
    /// The stage's clock, driven by hand.
    /// </summary>
    /// <remarks>
    /// A dispatcher timer in a test is a test that waits, and a test that waits is a test that fails on a
    /// loaded machine and passes on an idle one. So the schedule function is the test's: it records what
    /// was asked for and fires it when the test says so.
    /// </remarks>
    private sealed class Ticker
    {
        private readonly List<Action> _owed = [];

        /// <summary>The stage's view of "run this after a delay".</summary>
        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            _owed.Add(action);

            return new Cancel(() => _owed.Remove(action));
        }

        /// <summary>
        /// Fires the oldest tick, and any it owed in turn, which is how a run makes progress: each tick
        /// arms the next one.
        /// </summary>
        /// <param name="limit">How many to fire, so a run that cannot end cannot hang a test.</param>
        public void FireAll(int limit = 20_000)
        {
            for (var fired = 0; fired < limit && _owed.Count > 0; fired++)
            {
                FireOne();
            }
        }

        /// <summary>Fires one tick.</summary>
        public void FireOne()
        {
            if (_owed.Count == 0)
            {
                return;
            }

            var action = _owed[0];
            _owed.RemoveAt(0);
            action();
        }

        private sealed class Cancel(Action cancel) : IDisposable
        {
            public void Dispose() => cancel();
        }
    }
}