using System.Globalization;

namespace DeckForge.Core.Visual.Runtime;

/// <summary>What the interpreter should do next.</summary>
public enum RunOutcome
{
    /// <summary>It finished.</summary>
    Completed,

    /// <summary>It is stopped at a breakpoint.</summary>
    Paused,

    /// <summary>It was asked to stop.</summary>
    Cancelled,

    /// <summary>It ran out of steps, which is how a `forever` ends.</summary>
    StepBudgetExhausted,

    /// <summary>It stopped on an error.</summary>
    Failed,
}

/// <summary>
/// One run of the interpreter over a document.
/// </summary>
/// <remarks>
/// <para>
/// A stateful object rather than a static walk, because a run is a conversation: it is stepped, paused,
/// resumed and reset, and every one of those needs the host, the breakpoints and the clock to still be
/// there. <see cref="Run"/> is the batch form for tests and for the dry-run tracer; <see cref="Step"/> is
/// what the stage's Step button drives.
/// </para>
/// <para>
/// Deterministic by construction. There is no wall clock, no real network and no unseeded randomness, so
/// the same document produces the same trace every time — which is what makes a test asserting on a step
/// sequence a statement about the document rather than about the machine.
/// </para>
/// </remarks>
public sealed class ScriptInterpreter
{
    private Random _random;
    private VisualScript? _script;

    /// <summary>
    /// The body stack, on the instance rather than inside a call.
    /// </summary>
    /// <remarks>
    /// Step has to resume where the last step stopped, and a stack rebuilt at the top of every call
    /// starts the run again from the first statement - so Step would execute the same first block as many
    /// times as it was pressed, which is a Step button that never gets anywhere.
    /// </remarks>
    private List<Frame> _bodies = [];
    private bool _running;
    private bool _paused;

    public ScriptInterpreter(VisualProject project, IVisualHost host, int seed = 20240601, string? targetId = null)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _random = new Random(seed);

        Seed = seed;
        TargetId = targetId;

        // Scoped to one target when the caller names one, and to the whole document when it does not.
        //
        // Null rather than "the first target", because the second is what this was before: a stage whose
        // script picker was set to an action's second script listed and ran that action's *and* every other
        // action's, and the header had no way to say which target was being edited at all. A caller that
        // genuinely wants everything - the batch Run of a test, the dry-run tracer over a whole document -
        // still gets everything, which is why this is a filter and not a default.
        var targets = targetId is null
            ? project.Targets
            : [.. project.Targets.Where(candidate =>
                string.Equals(candidate.Id, targetId, StringComparison.Ordinal))];

        Steps = [.. targets.SelectMany(target => target.Scripts)];
        // First one wins, rather than throwing on the second. Two procedures with the same name should be
        // impossible - the editor refuses the rename that would make it so - but a document can arrive from
        // a file that predates the rule or from a hand edit, and a duplicate key here threw out of a WPF
        // event handler and took the canvas with it: the page could not be opened, edited or saved until
        // someone hand-fixed the json. A canvas that reports the duplicate is a bug report; one that cannot
        // be opened is a brick.
        Procedures = project.Procedures
            .GroupBy(procedure => procedure.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var script in Steps)
        {
            Host.Scripts.Register(script.Id, script.Name);
        }
    }

    /// <summary>The document being interpreted.</summary>
    public VisualProject Project { get; }

    /// <summary>Where every runtime call goes.</summary>
    public IVisualHost Host { get; }

    /// <summary>The seed, so a trace can be reproduced exactly.</summary>
    public int Seed { get; }

    /// <summary>
    /// The one target this run is allowed to touch, or null when it may touch them all.
    /// </summary>
    /// <remarks>
    /// Published rather than kept private because a stage that lists scripts from three actions and says
    /// it is showing one target's trace is lying about where the numbers came from, and the only thing that
    /// can check is whatever is asking the session what it is running.
    /// </remarks>
    public string? TargetId { get; }

    /// <summary>The scripts, in document order.</summary>
    public IReadOnlyList<VisualScript> Steps { get; }

    /// <summary>The procedures, by name.</summary>
    public IReadOnlyDictionary<string, ProcedureDeclaration> Procedures { get; }

    /// <summary>
    /// Block ids that stop the run.
    /// </summary>
    /// <remarks>
    /// Settable so a UI can hand its own set straight to the interpreter rather than keeping two copies in
    /// step; two sets and a subscription between them is a breakpoint that works in the panel and not in the
    /// run.
    /// </remarks>
    public HashSet<string> Breakpoints { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The names the watch table shows.</summary>
    public HashSet<string> Watched { get; } = new(StringComparer.Ordinal);

    /// <summary>The parameter names the action declared, which is what a template's holes may name.</summary>
    public IReadOnlyList<string> DeclaredParameters { get; set; } = [];

    /// <summary>How many blocks one run may execute before it is cut off.</summary>
    /// <remarks>
    /// A bound rather than a timeout, because a simulated clock that only moves when a block says so cannot
    /// time anything out. The default is generous enough for the sample and small enough that a
    /// <c>forever</c> with a wait in it does not appear to hang.
    /// </remarks>
    public int StepBudget { get; set; } = 20_000;

    /// <summary>Everything that has happened, in order.</summary>
    /// <remarks>
    /// A <see cref="List{T}"/> because that is what every reader wants from it, extended so the stage can
    /// watch a run happen instead of only reading it afterwards. The observer is called on the thread doing
    /// the run, before the next block, which is the only seam that lets a UI pace a run - Core never sleeps,
    /// and the stage does the waiting.
    /// </remarks>
    public PacedTrace Trace { get; } = new();

    /// <summary>Why the last run ended.</summary>
    public RunOutcome LastOutcome { get; private set; } = RunOutcome.Completed;

    /// <summary>Whether a run is in progress.</summary>
    public bool IsRunning => _running;

    /// <summary>Whether a run is stopped at a breakpoint.</summary>
    public bool IsPaused => _paused;

    /// <summary>
    /// The block the run paused in front of, when it paused at a breakpoint.
    /// </summary>
    /// <remarks>
    /// Asked rather than re-derived. A caller that wanted to know "was that a breakpoint" by looking the
    /// id up in <see cref="Breakpoints"/> would be guessing: the id in the trace's last line is whatever
    /// the run last touched, which is not the same thing once the run has been stepped past one.
    /// </remarks>
    public string? PausedAtBreakpoint { get; private set; }

    /// <summary>The block's own label, so the stage can say what it stopped in front of.</summary>
    public string PausedAtBreakpointLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Whether somebody has asked the run to stop at the next block boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A flag rather than a cancellation token, and the difference matters. A
    /// <see cref="CancellationToken"/> means "abandon this run": the interpreter returns
    /// <see cref="RunOutcome.Cancelled"/>, the stage says the run was stopped, and nothing about the
    /// stack is kept. That is the wrong verb for Pause, which has to leave the run able to carry on from
    /// exactly where it stopped — which is why <see cref="Continue"/> exists at all and why the body stack
    /// survives a pause.
    /// </para>
    /// <para>
    /// Also not a thread-blocking wait. Core has no threads and no dispatcher, and a "pause" implemented by
    /// parking the calling thread would freeze whichever thread called <see cref="Run"/> — in this
    /// application, the UI thread, which is the one thing that has to stay free to answer the button.
    /// </para>
    /// </remarks>
    public bool PauseRequested { get; private set; }

    /// <summary>
    /// Whether the run stopped because somebody asked it to, rather than because of a breakpoint.
    /// </summary>
    /// <remarks>
    /// Asked separately from <see cref="PausedAtBreakpoint"/> because both end in
    /// <see cref="RunOutcome.Paused"/> and a caller that only looked at the outcome could not tell a
    /// deliberate pause from a breakpoint it did not know about — which is the difference between resuming
    /// where the user left off and stopping again on the next line.
    /// </remarks>
    public bool PausedOnRequest { get; private set; }

    /// <summary>The block the run had just finished when a requested pause stopped it.</summary>
    public string? PausedAfterBlockId { get; private set; }

    /// <summary>That block's label, so the stage can say where the run is standing.</summary>
    public string PausedAfterBlockLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Asks the run to stop once the block it is executing has finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Honoured at a boundary and never inside one, which is the whole contract. Core cannot unwind half an
    /// executed block from the outside — by the time this returns the block has already written to the
    /// host's variables, moved the clock and appended to the trace — so the earliest honest moment to stop
    /// is between two blocks, and the earliest is also the only one that leaves the document and the host in
    /// a state a Resume can continue from.
    /// </para>
    /// <para>
    /// A no-op when nothing is running, rather than a stored request that fires on the next Run. That
    /// version reads fine while you are testing it and is maddening in the application: press Pause before
    /// Run, then press Run, and the very first block runs and the run stops again — a Pause that appears to
    /// do nothing and then stops a run the user never paused.
    /// </para>
    /// </remarks>
    public void RequestPause()
    {
        PauseRequested = _running;
    }

    /// <summary>
    /// Takes the request back without stopping anything, for a caller that changed its mind.
    /// </summary>
    public void ClearPause() => PauseRequested = false;

    /// <summary>
    /// Drops every mark a pause left, without touching the run itself.
    /// </summary>
    /// <remarks>
    /// For the caller that ends a run some other way — Stop, a script selection, a panel closing. Without
    /// it the stage's <c>IsPaused</c> keeps answering "yes, resume it" after the user pressed Stop, because
    /// nothing between here and there had any reason to clear a flag nobody was reading any more.
    /// <see cref="Reset"/> and <see cref="Step"/> already do this for themselves; this is the version for
    /// the paths that are not starting, continuing or resetting.
    /// </remarks>
    public void ForgetPause()
    {
        PauseRequested = false;
        PausedOnRequest = false;
        PausedAfterBlockId = null;
        PausedAfterBlockLabel = string.Empty;
    }

    /// <summary>Blocks executed by the current run.</summary>
    public int Executed { get; private set; }

    /// <summary>
    /// Runs one script from the beginning to its end, or to the first thing that stops it.
    /// </summary>
    /// <param name="script">The script to run, or null for the first one in the document.</param>
    /// <param name="cancellation">Stops the run when signalled.</param>
    /// <returns>Why it ended.</returns>
    public RunOutcome Run(VisualScript? script = null, CancellationToken cancellation = default)
    {
        var target = script ?? Steps.FirstOrDefault();
        if (target is null)
        {
            LastOutcome = RunOutcome.Completed;
            return LastOutcome;
        }

        Reset(target);

        return Pump(target, cancellation, stopAtBreakpoints: true);
    }

    /// <summary>
    /// Executes exactly one block and stops.
    /// </summary>
    /// <remarks>
    /// The granularity is a block, which is what makes "step into" a loop body possible: the loop's own
    /// step is one block, and each block inside it is another. It walks to the next block rather than
    /// running to the next breakpoint, because a Step button that ran until a breakpoint would jump a
    /// hundred blocks when there were none.
    /// </remarks>
    public RunOutcome Step(CancellationToken cancellation = default) =>
        Step(cancellation, intoContainer: false);

    /// <summary>
    /// Executes the next block and, if it is a container, stops inside it.
    /// </summary>
    /// <remarks>
    /// The difference from <see cref="Step"/> is what a loop body costs: stepping *over* a hundred-pass
    /// repeat runs a hundred passes, and stepping *into* it stops on the first block inside so the user can
    /// watch one pass and then step through the rest.
    /// </remarks>
    public RunOutcome StepInto(CancellationToken cancellation = default) =>
        Step(cancellation, intoContainer: true);

    private RunOutcome Step(CancellationToken cancellation, bool intoContainer)
    {
        if (!_running || _script is null)
        {
            var target = Steps.FirstOrDefault();
            if (target is null)
            {
                return LastOutcome = RunOutcome.Completed;
            }

            Reset(target);
        }

        _paused = false;

        // Same reason as Continue: a step is a deliberate move on from wherever the run is, so the marks of
        // the stop it is leaving behind are history rather than state.
        PausedOnRequest = false;
        PausedAfterBlockId = null;
        PausedAfterBlockLabel = string.Empty;

        return Pump(_script!, cancellation, stopAtBreakpoints: true, singleBlock: true, stepInto: intoContainer);
    }

    /// <summary>Runs to the end from wherever a paused run stopped.</summary>
    public RunOutcome Continue(CancellationToken cancellation = default)
    {
        if (!_running || _script is null)
        {
            return RunOutcome.Completed;
        }

        _paused = false;

        // The markers from the pause being left, or the next stop would be reported as the previous one.
        // A stage that reads <see cref="PausedOnRequest"/> to decide whether its transport shows Pause or
        // Resume would otherwise offer Resume forever, because nothing ever put the flag back.
        PausedOnRequest = false;
        PausedAfterBlockId = null;
        PausedAfterBlockLabel = string.Empty;

        return Pump(_script!, cancellation, stopAtBreakpoints: false);
    }

    /// <summary>Stops a run where it stands.</summary>
    public RunOutcome Stop()
    {
        _running = false;
        return LastOutcome = RunOutcome.Cancelled;
    }

    /// <summary>Clears the trace, the breakpoints and the host's state for a fresh run.</summary>
    public void Reset(VisualScript? script = null)
    {
        Trace.Clear();
        Executed = 0;
        _script = script ?? Steps.FirstOrDefault();
        // Reseeded, because the class promises that the same document produces the same trace every time
        // and a Random carried across runs produces a different one: pick random {from} to {to} gave 3 and
        // then 7. The seed is the run's, so a run is reproducible and two runs are not.
        _random = new Random(Seed);
        _bodies = _script is null ? [] : [new Frame(_script.Body)];
        _running = _script is not null;
        _paused = false;
        PausedAtBreakpoint = null;
        PausedAtBreakpointLabel = string.Empty;

        // The pause state goes too. A request that survived a Reset would stop the first block of the next
        // run, which is the same trap as leaving a pause armed while nothing is running - and Reset is
        // exactly when a user who was about to press Pause presses Reset instead.
        ForgetPause();

        LastOutcome = RunOutcome.Completed;
    }

    /// <summary>Starts a named script, for a script block or an event.</summary>
    public RunOutcome StartScript(string scriptId, CancellationToken cancellation = default)
    {
        var script = Steps.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, scriptId, StringComparison.Ordinal));

        return script is null ? RunOutcome.Completed : Run(script, cancellation);
    }

    /// <summary>
    /// Runs until the work runs out, the caller asks to stop, or a breakpoint is hit.
    /// </summary>
    private RunOutcome Pump(
        VisualScript script,
        CancellationToken cancellation,
        bool stopAtBreakpoints,
        bool singleBlock = false,
        bool stepInto = false)
    {
        // The caller's script argument only decides *which* run this is; where it is comes from the
        // instance's own stack, so a paused run resumes instead of starting again.
        var bodies = _bodies;

        while (true)
        {
            if (cancellation.IsCancellationRequested)
            {
                return LastOutcome = RunOutcome.Cancelled;
            }

            if (Executed >= StepBudget)
            {
                // Said in the trace, because a run that stops without a word is indistinguishable from one
                // that finished.
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.Error,
                    Label: $"Stopped after {StepBudget:N0} blocks. Raise the step budget, or look for a loop "
                        + "that never ends.",
                    Millisecond: Host.Clock.ElapsedMilliseconds));

                return LastOutcome = RunOutcome.StepBudgetExhausted;
            }

            if (!_running)
            {
                // Something inside a block said to stop, and the trace has already said why. Returning
                // here rather than looping is what keeps an errored run from looking like a long one.
                return LastOutcome = RunOutcome.Failed;
            }

            var frame = bodies[^1];

            if (frame.Index >= frame.Body.Count)
            {
                if (frame.Loop is { } loop && Again(frame, loop))
                {
                    // Another pass. The loop's own entered and exited steps are the frame's business, so
                    // the trace reads "enter repeat, change, change, exit repeat" rather than repeating
                    // the loop's own header once per pass - which is what the generated C# looks like too.
                    //
                    // Counted against the budget, and this is the reason: a loop whose body is empty
                    // executes no blocks at all, so nothing else here would ever bring it to an end and
                    // an empty `forever` would spin for ever instead of stopping at the budget and saying
                    // so.
                    Executed++;

                    frame.Index = 0;
                    continue;
                }

                bodies.RemoveAt(bodies.Count - 1);

                if (bodies.Count == 0)
                {
                    // The root body is done, so the run is.
                    _running = false;
                    return LastOutcome = RunOutcome.Completed;
                }

                if (frame.Owner is { } owner)
                {
                    Trace.Add(new ExecutionStep(
                        ExecutionStepKind.BlockExited, owner.Id, Label(owner), Millisecond: Host.Clock.ElapsedMilliseconds));
                }

                continue;
            }

            var block = frame.Body[frame.Index];

            if (stopAtBreakpoints && Breakpoints.Contains(block.Id))
            {
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.BreakpointHit, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));
                _paused = true;
                PausedAtBreakpoint = block.Id;
                PausedAtBreakpointLabel = Label(block);
                return LastOutcome = RunOutcome.Paused;
            }

            var advanced = Execute(block, bodies, cancellation);
            if (advanced)
            {
                // A container pushed a body, so the next block is inside it. "Step into" spends the step
                // here; a plain Step carries on into the body, which is what stepping *over* a loop means.
                if (singleBlock && stepInto)
                {
                    return LastOutcome = RunOutcome.Paused;
                }

                // Still a boundary, even though a body was pushed: the container's own step is written and
                // nothing inside it has run. Stopping here rather than one block deeper is the difference
                // between pausing "after repeat 10" and pausing "after repeat 10, then after its first
                // statement", and only the first is what the user asked for.
                if (HonourPause(block))
                {
                    return LastOutcome = RunOutcome.Paused;
                }

                continue;
            }

            frame.Index++;

            if (singleBlock)
            {
                if (_running && bodies.Count == 0)
                {
                    // The run is over, so there is nothing a pause could be holding up. Honoured anyway it
                    // would leave the stage offering to resume a script that has already finished.
                    return LastOutcome = RunOutcome.Completed;
                }

                // Checked here as well, and this is the line that makes the stage's Pause work at all.
                //
                // A paced run is *not* one long pump: StageSession asks for one block at a time, so the
                // only pump there is to break out of is this one. An earlier version checked the request
                // only in the multi-block path — the right instinct, since a stepped pump has already
                // suspended — and the consequence was that the transport's Pause button armed a request
                // nothing ever read, and the run carried on at full speed with the button now reading
                // "Resume".
                HonourPause(block);

                return LastOutcome = RunOutcome.Paused;
            }

            // Checked after the block, never before it: a pause pressed between two of a stage's ticks
            // has to let the tick in flight finish, or the button looks like it did nothing and the run
            // stops a moment later for no stated reason.
            if (HonourPause(block))
            {
                return LastOutcome = RunOutcome.Paused;
            }
        }
    }

    /// <summary>
    /// Stops the run if a pause was asked for, and says where it stopped.
    /// </summary>
    /// <param name="block">The block that has just finished.</param>
    /// <returns>Whether the pump should return.</returns>
    /// <remarks>
    /// The request is consumed here rather than left set, because it has now been acted on: leaving it
    /// would make the next <see cref="Continue"/> stop again one block later, and a Resume that only advances
    /// one block is indistinguishable from a second pause.
    /// </remarks>
    private bool HonourPause(Block block)
    {
        if (!PauseRequested)
        {
            return false;
        }

        PauseRequested = false;
        _paused = true;
        PausedOnRequest = true;
        PausedAfterBlockId = block.Id;
        PausedAfterBlockLabel = Label(block);

        // In the trace, in the same place a breakpoint writes its line. A pause with no trace line looks
        // exactly like a run that stopped on its own, and the trace is where a user goes to find out why.
        Trace.Add(new ExecutionStep(
            ExecutionStepKind.PausedAfter, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));

        return true;
    }

    /// <summary>
    /// Runs one block, and says whether the interpreter should come back to it.
    /// </summary>
    /// <param name="block">The block to run.</param>
    /// <param name="bodies">The body stack, which a container pushes onto.</param>
    /// <param name="cancellation">Stops the run when signalled.</param>
    /// <returns>True when the block moved the stack itself and no advance is wanted.</returns>
    private bool Execute(Block block, List<Frame> bodies, CancellationToken cancellation)
    {
        Executed++;

        if (block.Disabled)
        {
            // Entered and left with nothing between them: the trace has to show that a disabled block was
            // skipped rather than that it ran and did nothing, which is the difference between a diagnosis
            // and a guess.
            Trace.Add(new ExecutionStep(ExecutionStepKind.BlockEntered, block.Id, Label(block) + " (disabled)"));
            Trace.Add(new ExecutionStep(ExecutionStepKind.BlockExited, block.Id, Label(block)));
            return false;
        }

        if (!BlockSemantics.Knows(block.Kind))
        {
            Trace.Add(new ExecutionStep(
                ExecutionStepKind.Error,
                block.Id,
                BlockSemantics.NotInterpreted(block.Kind, Label(block)),
                Millisecond: Host.Clock.ElapsedMilliseconds));

            _running = false;
            return true;
        }

        Trace.Add(new ExecutionStep(
            ExecutionStepKind.BlockEntered, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));

        if (EnterNested(block) is { } nested)
        {
            if (nested.Loop is null && nested.Body is null)
            {
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.BlockExited, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));
                return false;
            }

            bodies.Add(new Frame(nested.Body ?? [], block, nested.Loop));
            return true;
        }

        RunBody(block, cancellation);
        Trace.Add(new ExecutionStep(
            ExecutionStepKind.BlockExited, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));
        return false;
    }

    /// <summary>
    /// Whether the block is a container whose body the interpreter should descend into now.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>
    /// Null when it is not a container. Otherwise the body to descend into, which may be none at all, and
    /// the loop that body belongs to, which is null for anything that runs its body once.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A loop's condition is evaluated here and its body pushed, because the push has to happen between
    /// the loop's own entered and exited steps — otherwise the trace reads "enter repeat, exit repeat,
    /// enter wait" and the nesting is not visible anywhere.
    /// </para>
    /// <para>
    /// Scratch's order is kept deliberately: <c>repeat until</c> runs the body <i>before</i> it tests, and
    /// <c>while</c> tests before it runs. A simulator that quietly swapped the two would show a user a
    /// script that works, for a body that would never run in the plugin they then built.
    /// </para>
    /// </remarks>
    private Nested? EnterNested(Block block)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is not { IsContainer: true })
        {
            return null;
        }

        switch (block.Kind)
        {
            case "control.if":
                return Descend(block, "then", Values.Boolean(Reporters(block, "condition")));

            case "control.if-else":
            {
                var taken = Values.Boolean(Reporters(block, "condition"));
                return Descend(block, taken ? "then" : "else", true);
            }

            case "control.if-else-if":
            {
                // In catalogue order: then, else if, else. The first true condition wins and nothing after
                // it is tested, which is what makes the chain a chain.
                if (Values.Boolean(Reporters(block, "condition")))
                {
                    return Descend(block, "then", true);
                }

                if (block.Inputs.ContainsKey("condition2") && Values.Boolean(Reporters(block, "condition2")))
                {
                    return Descend(block, "elseIf", true);
                }

                return Descend(block, "else", true);
            }

            case "control.repeat":
            {
                var times = (int)Values.Number(Reporters(block, "count"));
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.ValueComputed,
                    block.Id,
                    "count",
                    times.ToString(CultureInfo.InvariantCulture),
                    Millisecond: Host.Clock.ElapsedMilliseconds));

                // Counted once, at the start, as the generated C# does: a `repeat` whose count is a
                // variable must not change halfway through because an earlier block wrote to it.
                return times > 0
                    ? new Nested(ContainerBody(block, "body"), new Loop(block, LoopKind.Counted, times))
                    : Nested.Nothing;
            }

            case "control.forever":
                return new Nested(ContainerBody(block, "body"), new Loop(block, LoopKind.Forever, 0));

            case "control.while":
            {
                // Tested before the body, so an empty body is not an infinite loop.
                return Values.Boolean(Reporters(block, "condition"))
                    ? new Nested(ContainerBody(block, "body"), new Loop(block, LoopKind.While, 0))
                    : Nested.Nothing;
            }

            case "control.repeat-until":
            {
                // The body always runs once, and the condition is tested afterwards. Entering with the
                // condition already true is Scratch's behaviour and is the case a simulator most often
                // gets wrong in the direction of running the body twice.
                if (Values.Boolean(Reporters(block, "condition")))
                {
                    return Nested.Nothing;
                }

                return new Nested(ContainerBody(block, "body"), new Loop(block, LoopKind.Until, 0));
            }

            default:
                // A container the table has no rule beyond "enter the body" is still a container, and
                // entering it is the least surprising thing to do.
                return new Nested(ContainerBody(block), null);
        }
    }

    /// <summary>One of a container's bodies, when the condition chose it.</summary>
    private Nested Descend(Block block, string bodyName, bool taken)
    {
        var body = ContainerBody(block, bodyName);

        // A chosen branch that is empty still descends: an `if` with nothing in it is a fact about the
        // document, and the trace saying the branch was taken and did nothing is more use than silence.
        return taken ? new Nested(body, null) : Nested.Nothing;
    }

    /// <summary>Whether a loop whose body has just finished runs another pass.</summary>
    private bool Again(Frame frame, Loop loop)
    {
        switch (loop.Kind)
        {
            case LoopKind.Counted:
                return --loop.Remaining > 0;

            case LoopKind.Forever:
                return true;

            case LoopKind.While:
                return Values.Boolean(Reporters(loop.Owner, "condition"));

            case LoopKind.Until:
                // Tested after the pass, which is what "repeat until" means.
                return !Values.Boolean(Reporters(loop.Owner, "condition"));

            default:
                return false;
        }
    }

    /// <summary>Runs what a block does that is not descending into it.</summary>
    private void RunBody(Block block, CancellationToken cancellation)
    {
        switch (block.Kind)
        {
            case "control.wait-seconds":
                Wait(block, Values.Number(Reporters(block, "seconds")) * 1000);
                break;

            case "control.wait-ms":
                Wait(block, Values.Number(Reporters(block, "ms")));
                break;

            case "control.stop-script":
                _running = false;
                break;

            case "var.set":
            {
                var name = Values.Text(Reporters(block, "var"));
                var value = Reporters(block, "value");
                Host.Variables.Set(name, Values.Text(value));
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, $"{name} =", Values.Text(value),
                    "variables", "set", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "var.change":
            {
                var name = Values.Text(Reporters(block, "var"));
                var amount = Values.Number(Reporters(block, "amount"));
                Host.Variables.Change(name, amount);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, Values.Text(Host.Variables.Get(name)),
                    "variables", "change", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "var.watch":
            {
                var name = Values.Text(Reporters(block, "var"));
                Watched.Add(name);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, Watched.Contains(name) ? "watched" : string.Empty,
                    "variables", "watch", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "var.unwatch":
            {
                var name = Values.Text(Reporters(block, "var"));
                var was = Watched.Remove(name);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, was ? "unwatched" : "was not watched",
                    "variables", "unwatch", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "list.define":
            {
                var name = Values.Text(Reporters(block, "name"));
                Host.Lists.Set(name, Host.Lists.Get(name));
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, "declared", "lists", "set",
                    Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "list.add":
            {
                var name = Values.Text(Reporters(block, "list"));
                var item = Values.Text(Reporters(block, "item"));
                Host.Lists.Add(name, item);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, item, "lists", "add",
                    Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "list.clear":
            {
                var name = Values.Text(Reporters(block, "list"));
                Host.Lists.Set(name, []);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, "0 items", "lists", "set",
                    Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "events.publish":
            case "events.publish-with-payload":
            {
                var name = Values.Text(Reporters(block, "event"));
                var payload = block.Inputs.ContainsKey("payload") ? Values.Text(Reporters(block, "payload")) : null;
                Host.Events.Publish(name, payload);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, name, payload ?? string.Empty,
                    "events", "publish", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "events.send-message":
            {
                var topic = Values.Text(Reporters(block, "topic"));
                var body = block.Inputs.ContainsKey("body") ? Values.Text(Reporters(block, "body")) : null;
                Host.Messages.Show(topic, body);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, topic, body ?? string.Empty,
                    "messages", "show", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "ui.notify":
            {
                var title = Values.Text(Reporters(block, "title"));
                var message = block.Inputs.ContainsKey("message") ? Values.Text(Reporters(block, "message")) : null;
                Host.Notifications.Show(NotificationLevel.Information, title, message);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, title, message ?? string.Empty,
                    "notifications", "show", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "ui.notify-level":
            {
                var level = Values.Text(block.Fields.TryGetValue("level", out var key) ? key : "Info");
                var title = Values.Text(Reporters(block, "title"));
                var message = block.Inputs.ContainsKey("message") ? Values.Text(Reporters(block, "message")) : null;
                Host.Notifications.Show(LevelOf(level), title, message);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, title, level,
                    "notifications", "show", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "ui.notify-key":
            {
                var key = Values.Text(Reporters(block, "key"));
                var title = Values.Text(Reporters(block, "title"));
                var message = block.Inputs.ContainsKey("message") ? Values.Text(Reporters(block, "message")) : null;
                Host.Notifications.Show(NotificationLevel.Information, title, message, key);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, title, key,
                    "notifications", "show", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "ui.clear-notification":
            {
                var key = Values.Text(Reporters(block, "key"));
                Host.Notifications.Show(NotificationLevel.Debug, string.Empty, null, key);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, key, "cleared",
                    "notifications", "clear", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "deck.open-folder":
            case "deck.open-folder-on-client":
            {
                var folder = Values.Text(Reporters(block, "folder"));
                var client = block.Inputs.ContainsKey("client") ? Values.Text(Reporters(block, "client")) : null;
                Host.Deck.OpenFolder(folder, client);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, folder, client ?? "the pressing client",
                    "deck", "openFolder", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "deck.go-to-parent":
                Host.Deck.GoToParent();
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, "parent folder", string.Empty,
                    "deck", "goToParent", Host.Clock.ElapsedMilliseconds));
                break;

            case "deck.go-back":
                Host.Deck.GoBack();
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, "back", string.Empty,
                    "deck", "goBack", Host.Clock.ElapsedMilliseconds));
                break;

            case "deck.set-button-state":
            {
                var widget = Values.Text(Reporters(block, "widget"));
                var state = Values.Text(Reporters(block, "state"));
                Host.Deck.SetButtonState(widget, state);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.HostCall, block.Id, widget, state,
                    "deck", "setButtonState", Host.Clock.ElapsedMilliseconds));
                break;
            }

            case "ui.log":
            {
                var level = Values.Text(block.Fields.TryGetValue("level", out var key) ? key : "info");
                var template = Values.Text(Reporters(block, "template"));
                var line = Values.FillTemplate(template, Host.Parameters, DeclaredParameters);

                Host.Log.Write(LevelOf(level), line);
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.LogEmitted, block.Id, line, level, "log", level,
                    Host.Clock.ElapsedMilliseconds));
                break;
            }

            default:
                // Every remaining interpreted block only reads its own values, and the ValueComputed steps
                // for those were recorded while collecting them.
                break;
        }

        _ = cancellation;
    }

    /// <summary>The notification level a menu key names.</summary>
    private static NotificationLevel LevelOf(string key) => key.ToLowerInvariant() switch
    {
        "debug" => NotificationLevel.Debug,
        "warning" => NotificationLevel.Warning,
        "error" => NotificationLevel.Error,
        "success" => NotificationLevel.Success,
        _ => NotificationLevel.Information,
    };

    /// <summary>Moves the clock and says so.</summary>
    private void Wait(Block block, double milliseconds)
    {
        var elapsed = Math.Max(0, milliseconds);

        if (Host.Clock is SimulatedHost.ManualClock clock)
        {
            clock.Advance(elapsed);
        }

        Trace.Add(new ExecutionStep(
            ExecutionStepKind.WaitElapsed,
            block.Id,
            Label(block),
            elapsed.ToString(CultureInfo.InvariantCulture),
            Millisecond: Host.Clock.ElapsedMilliseconds));
    }

    /// <summary>
    /// The value of a slot: a nested reporter's value, or the literal in the slot.
    /// </summary>
    /// <remarks>
    /// Every read is recorded as a <see cref="ExecutionStepKind.ValueComputed"/> step. That is the whole
    /// value of a dry run: not what happened, but what each expression *was* while it happened, which is
    /// the question a user actually has when a program misbehaves.
    /// </remarks>
    private object? Reporters(Block block, string slot)
    {
        if (!block.Inputs.TryGetValue(slot, out var input))
        {
            return null;
        }

        var names = BlockSemantics.For(block.Kind)?.NameSlot == slot;

        object? value = input.Block is { } nested
            ? Evaluate(nested)
            : names
                // The name, read literally. `set {var}` and `get {var}` hold the same kind of input and
                // mean opposite things by it, and the table is where that difference is written down.
                ? input.Variable ?? input.Text
                : input.Kind switch
                {
                    BlockInputKind.Number => input.Number ?? 0d,
                    BlockInputKind.Boolean => input.Boolean ?? false,
                    BlockInputKind.Variable => Host.Variables.Get(input.Variable!) ?? input.Variable,
                    _ => input.Text,
                };

        Trace.Add(new ExecutionStep(
            ExecutionStepKind.ValueComputed,
            block.Id,
            slot,
            Values.Text(value),
            Millisecond: Host.Clock.ElapsedMilliseconds));

        return value;
    }

    /// <summary>Evaluates a reporter that sits in a slot.</summary>
    private object? Evaluate(Block block)
    {
        if (block.Disabled)
        {
            return string.Empty;
        }

        if (!BlockSemantics.Knows(block.Kind))
        {
            Trace.Add(new ExecutionStep(
                ExecutionStepKind.Error,
                block.Id,
                BlockSemantics.NotInterpreted(block.Kind, Label(block)),
                Millisecond: Host.Clock.ElapsedMilliseconds));

            return string.Empty;
        }

        return block.Kind switch
        {
            "ops.random" => Random(Values.Number(Slot(block, "from")), Values.Number(Slot(block, "to"))),
            "ops.random-item" => RandomItem(block),
            _ => Slot(block, "value"),
        };
    }

    private object? Slot(Block block, string name) =>
        block.Inputs.TryGetValue(name, out var input)
            ? input.Kind switch
            {
                BlockInputKind.Number => input.Number ?? 0d,
                BlockInputKind.Boolean => input.Boolean ?? false,
                BlockInputKind.Variable => Host.Variables.Get(input.Variable!) ?? input.Variable,
                _ => input.Text,
            }
            : null;

    private double Random(double from, double to)
    {
        var low = Math.Min(from, to);
        var high = Math.Max(from, to);

        // Seeded, and inclusive of the high end the way Scratch's is, so a trace is reproducible.
        return low + Math.Floor(_random.NextDouble() * ((high - low) + 1));
    }

    private object? RandomItem(Block block)
    {
        var name = Values.Text(Reporters(block, "list"));
        var items = Host.Lists.Get(name);

        return items.Count == 0 ? string.Empty : items[_random.Next(items.Count)];
    }

    /// <summary>Which of a container's bodies to run, and whether it loops.</summary>
    /// <param name="Body">The statements, or null when no branch was chosen.</param>
    /// <param name="Loop">The loop the body belongs to, or null for a branch that runs once.</param>
    private sealed record Nested(List<Block>? Body, Loop? Loop)
    {
        /// <summary>No branch was taken, or the loop has nothing to do this time.</summary>
        public static readonly Nested Nothing = new(null, null);
    }

    /// <summary>How a loop decides whether to run again.</summary>
    private enum LoopKind
    {
        /// <summary>A number of passes, counted once at the start.</summary>
        Counted,

        /// <summary>Until something stops the run.</summary>
        Forever,

        /// <summary>While the condition holds, tested before each pass.</summary>
        While,

        /// <summary>Until the condition holds, tested after each pass.</summary>
        Until,
    }

    /// <summary>A loop that is part-way through its passes.</summary>
    /// <param name="Owner">The container block.</param>
    /// <param name="Kind">How it decides whether to go again.</param>
    /// <param name="Remaining">How many passes are left, for a counted loop.</param>
    /// <remarks>
    /// A class rather than a record because a counted loop mutates its own remaining passes as it goes,
    /// and a record's value equality would quietly make every pass compare equal to the first.
    /// </remarks>
    private sealed class Loop(Block owner, LoopKind kind, int remaining)
    {
        /// <summary>The container block.</summary>
        public Block Owner { get; } = owner;

        /// <summary>How it decides whether to go again.</summary>
        public LoopKind Kind { get; } = kind;

        /// <summary>How many passes are left, for a counted loop.</summary>
        public int Remaining { get; set; } = remaining;
    }

    /// <summary>One body on the stack, and the loop it belongs to.</summary>
    private sealed class Frame(List<Block> body, Block? owner = null, Loop? loop = null)
    {
        /// <summary>The statements to run, in order.</summary>
        public List<Block> Body { get; } = body;

        /// <summary>The container this frame is the body of, or null for the script's own body.</summary>
        public Block? Owner { get; } = owner;

        /// <summary>The loop this frame belongs to, or null when it runs once.</summary>
        public Loop? Loop { get; } = loop;

        /// <summary>How far through the body the run has got.</summary>
        public int Index { get; set; }
    }

    /// <summary>One named body of a container, which is where its statements live.</summary>
    private static List<Block> ContainerBody(Block block, string? name = null)
    {
        var wanted = name ?? BlockCatalog.Find(block.Kind)?.Bodies?.FirstOrDefault()?.Name;
        return wanted is null ? [] : block.Body(wanted);
    }

    /// <summary>The block's label with its holes filled.</summary>
    /// <remarks>
    /// Delegated to <see cref="BlockAnnouncement.Label"/>, which is the same reading. It used to be a
    /// second implementation living here: two copies of "what does this block say" in one assembly is how
    /// the trace and the screen reader end up describing different blocks, and neither of them is the one
    /// the tile draws. The nested-block case moved with it, which is also why the null-forgiving call that
    /// threw out of a timer callback when a document named a kind this build does not know is gone - the
    /// shared version falls back to the kind, as the three other readers of a label already did.
    /// </remarks>
    private string Label(Block block) => BlockAnnouncement.Label(block, NoTranslations.Instance);

    /// <summary>
    /// The trace, which tells an observer as it grows.
    /// </summary>
    /// <remarks>
    /// Every path that adds a step notifies, not just <c>Add</c>: <c>AddRange</c> and <c>Insert</c> are list
    /// members any caller may use, and an observer that only hears about some of the steps is the stage
    /// panel showing a run that is not the run happening. The remark this replaces claimed every step goes
    /// through a list add, which was true and was also why the compiler warned (CS0108) that the hiding was
    /// unintentional.
    /// </remarks>
    public sealed class PacedTrace : List<ExecutionStep>
    {
        /// <summary>Called after each step is recorded, on the thread doing the run.</summary>
        public Action<ExecutionStep>? Observer { get; set; }

        /// <summary>Records a step and tells the observer.</summary>
        public new void Add(ExecutionStep step)
        {
            base.Add(step);
            Observer?.Invoke(step);
        }

        /// <summary>Records several steps and tells the observer about each, in order.</summary>
        public new void AddRange(IEnumerable<ExecutionStep> steps)
        {
            ArgumentNullException.ThrowIfNull(steps);

            foreach (var step in steps)
            {
                base.Add(step);
                Observer?.Invoke(step);
            }
        }

        /// <summary>Records a step at a position and tells the observer.</summary>
        public new void Insert(int index, ExecutionStep step)
        {
            base.Insert(index, step);
            Observer?.Invoke(step);
        }

        /// <summary>Records several steps at a position and tells the observer about each, in order.</summary>
        public new void InsertRange(int index, IEnumerable<ExecutionStep> steps)
        {
            ArgumentNullException.ThrowIfNull(steps);

            foreach (var step in steps)
            {
                base.Insert(index++, step);
                Observer?.Invoke(step);
            }
        }
    }
}