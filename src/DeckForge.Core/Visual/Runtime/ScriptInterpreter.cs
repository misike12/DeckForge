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
    private readonly Random _random;
    private VisualScript? _script;

    /// <summary>
    /// The body stack, on the instance rather than inside a call.
    /// </summary>
    /// <remarks>
    /// Step has to resume where the last step stopped, and a stack rebuilt at the top of every call
    /// starts the run again from the first statement - so Step would execute the same first block as many
    /// times as it was pressed, which is a Step button that never gets anywhere.
    /// </remarks>
    private List<(List<Block> Body, int Index)> _bodies = [];
    private bool _running;
    private bool _paused;

    public ScriptInterpreter(VisualProject project, IVisualHost host, int seed = 20240601)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _random = new Random(seed);

        Seed = seed;
        Steps = [.. project.Targets.SelectMany(target => target.Scripts)];
        Procedures = project.Procedures.ToDictionary(procedure => procedure.Name, StringComparer.Ordinal);

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

    /// <summary>The scripts, in document order.</summary>
    public IReadOnlyList<VisualScript> Steps { get; }

    /// <summary>The procedures, by name.</summary>
    public IReadOnlyDictionary<string, ProcedureDeclaration> Procedures { get; }

    /// <summary>Block ids that stop the run.</summary>
    public HashSet<string> Breakpoints { get; } = new(StringComparer.Ordinal);

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
    public List<ExecutionStep> Trace { get; } = [];

    /// <summary>Why the last run ended.</summary>
    public RunOutcome LastOutcome { get; private set; } = RunOutcome.Completed;

    /// <summary>Whether a run is in progress.</summary>
    public bool IsRunning => _running;

    /// <summary>Whether a run is stopped at a breakpoint.</summary>
    public bool IsPaused => _paused;

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
    public RunOutcome Step(CancellationToken cancellation = default)
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
        return Pump(_script!, cancellation, stopAtBreakpoints: true, singleBlock: true);
    }

    /// <summary>Runs to the end from wherever a paused run stopped.</summary>
    public RunOutcome Continue(CancellationToken cancellation = default)
    {
        if (!_running || _script is null)
        {
            return RunOutcome.Completed;
        }

        _paused = false;
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
        _bodies = _script is null ? [] : [(_script.Body, 0)];
        _running = _script is not null;
        _paused = false;
        LastOutcome = RunOutcome.Completed;
    }

    /// <summary>Starts a named script, for a script block or an event.</summary>
    public RunOutcome StartScript(string scriptId, CancellationToken cancellation = default)
    {
        var script = Steps.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, scriptId, StringComparison.Ordinal));

        return script is null ? RunOutcome.Completed : Run(script, cancellation);
    }

    /// <summary>Runs until the work runs out, the caller asks to stop, or a breakpoint is hit.</summary>
    private RunOutcome Pump(
        VisualScript script,
        CancellationToken cancellation,
        bool stopAtBreakpoints,
        bool singleBlock = false)
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

            if (bodies.Count == 1 && bodies[0].Index >= bodies[0].Body.Count)
            {
                // The root body is done, so the run is. Popping it and carrying on would index an empty
                // stack on the next pass, which is what a run that used to end quietly does now.
                _running = false;
                return LastOutcome = RunOutcome.Completed;
            }

            var frame = bodies[^1];
            if (frame.Index >= frame.Body.Count)
            {
                bodies.RemoveAt(bodies.Count - 1);
                continue;
            }

            var block = frame.Body[frame.Index];

            if (stopAtBreakpoints && Breakpoints.Contains(block.Id))
            {
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.BreakpointHit, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));
                _paused = true;
                return LastOutcome = RunOutcome.Paused;
            }

            var advanced = Execute(block, bodies, cancellation);
            if (advanced)
            {
                // A container pushed a body, so the next block is inside it and the step is not spent yet.
                continue;
            }

            bodies[^1] = (frame.Body, frame.Index + 1);

            if (singleBlock)
            {
                return LastOutcome = _running && bodies.Count == 1 && bodies[0].Index >= bodies[0].Body.Count
                    ? RunOutcome.Completed
                    : RunOutcome.Paused;
            }
        }
    }

    /// <summary>
    /// Runs one block, and says whether the interpreter should come back to it.
    /// </summary>
    /// <param name="block">The block to run.</param>
    /// <param name="bodies">The body stack, which a container pushes onto.</param>
    /// <param name="cancellation">Stops the run when signalled.</param>
    /// <returns>True when the block moved the stack itself and no advance is wanted.</returns>
    private bool Execute(Block block, List<(List<Block> Body, int Index)> bodies, CancellationToken cancellation)
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
            if (nested)
            {
                bodies.Add((ContainerBody(block), 0));
                return true;
            }

            Trace.Add(new ExecutionStep(
                ExecutionStepKind.BlockExited, block.Id, Label(block), Millisecond: Host.Clock.ElapsedMilliseconds));
            return false;
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
    /// <returns>Null when it is not a container; otherwise whether to descend.</returns>
    /// <remarks>
    /// A loop's condition is evaluated here and its body pushed, because the push has to happen between
    /// the loop's own entered and exited steps — otherwise the trace reads "enter repeat, exit repeat,
    /// enter wait" and the nesting is not visible anywhere.
    /// </remarks>
    private bool? EnterNested(Block block)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is not { IsContainer: true })
        {
            return null;
        }

        var body = ContainerBody(block);

        switch (block.Kind)
        {
            case "control.if":
                return Values.Boolean(Reporters(block, "condition"));

            case "control.if-else":
                return Values.Boolean(Reporters(block, "condition"));

            case "control.repeat":
            {
                var times = (int)Values.Number(Reporters(block, "count"));
                Trace.Add(new ExecutionStep(
                    ExecutionStepKind.ValueComputed,
                    block.Id,
                    "times",
                    Values.Number(times).ToString(CultureInfo.InvariantCulture),
                    Millisecond: Host.Clock.ElapsedMilliseconds));

                return times > 0;
            }

            default:
                // forever, while, repeat-until, if-else-if and every other loop the table has no rule for
                // beyond "enter the body": the step budget is what ends an unbounded one, and pretending
                // otherwise is a hang wearing a progress bar.
                return true;
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

            case "ui.log":
            {
                var level = Values.Text(block.Fields.TryGetValue("level", out var key) ? key : "info");
                var template = Values.Text(Reporters(block, "template"));
                var line = Values.FillTemplate(template, Host.Parameters, DeclaredParameters);

                Host.Log.Write(NotificationLevel.Information, line);
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

    /// <summary>A container's first body, which is where its statements live.</summary>
    private static List<Block> ContainerBody(Block block)
    {
        var name = BlockCatalog.Find(block.Kind)?.Bodies?.FirstOrDefault()?.Name;
        return name is null ? [] : block.Body(name);
    }

    /// <summary>The block's label with its holes filled.</summary>
    private string Label(Block block)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is null)
        {
            return block.Kind;
        }

        var text = descriptor.Label;

        foreach (var (name, input) in block.Inputs)
        {
            var shown = input.Block is { } nested
                ? BlockLabel.PreviewText(BlockCatalog.Find(nested.Kind)!) + "…"
                : Values.Text(input.Text ?? input.Variable ?? input.Number?.ToString(CultureInfo.InvariantCulture));

            text = text.Replace("{" + name + "}", shown);
        }

        return text;
    }
}