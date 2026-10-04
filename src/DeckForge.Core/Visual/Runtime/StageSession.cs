using System.ComponentModel;
using System.Globalization;

namespace DeckForge.Core.Visual.Runtime;

/// <summary>
/// A run of a document, paced: the transport, the state the host has reached, and everything a panel
/// shows while it happens.
/// </summary>
/// <remarks>
/// <para>
/// In Core rather than in a view model, for the same reason <c>KeyboardMoves</c> and <c>DropPlan</c> are.
/// Part 8's whole claim is that the stage answers "what will my blocks do", and a claim that can only be
/// checked by watching a window is a claim that quietly stops being true. Everything below is window-free:
/// the transport, the watch table, the trace, the deck, the notifications, the parameters and the honesty
/// line. The panel in the application projects this and holds no state of its own.
/// </para>
/// <para>
/// The pacing is a scheduled callback rather than a loop, which is why one tick runs one block instead of
/// the whole script. A run that cannot be interrupted between two blocks is a run that cannot be stopped,
/// and a Stop button that only works when the script happens to finish is the one button nobody needs
/// until they need it.
/// </para>
/// </remarks>
public sealed class StageSession
{
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly HashSet<string> _breakpoints;
    private readonly HashSet<string> _watched;

    private VisualProject _document;
    private SimulatedHost _host = new();
    private ScriptInterpreter _interpreter;
    private IDisposable? _timer;
    private string? _selectedScriptId;
    private string? _targetId;

    /// <summary>The steps per second a run starts at, and what the slider starts showing.</summary>
    /// <remarks>
    /// Named rather than left as a literal in two places, because the panel's slider and the session's
    /// timer were two separate numbers that did not have to agree: the slider began at 0 and was clamped
    /// to 1 while the run advanced at eight, so the stage reported a speed it was not running at.
    /// </remarks>
    public const double DefaultSpeed = 8;

    private double _speed = DefaultSpeed;

    /// <summary>Builds a session over a document.</summary>
    /// <param name="document">The document to run.</param>
    /// <param name="breakpoints">
    /// The block ids that stop a run. Held by reference rather than copied, because the tile the user
    /// clicked and the run that pauses must read the same set: a copy on each side is a breakpoint that
    /// shows as set on a tile and does nothing when the script reaches it.
    /// </param>
    /// <param name="watched">
    /// The names the watch table shows, held by reference for the same reason the breakpoints are. A
    /// watch list that vanished when a user edited a block would make the debugger look broken.
    /// </param>
    /// <param name="schedule">
    /// Runs an action after a delay and hands back the canceller. The tests pass their own so a run can
    /// be advanced one tick at a time.
    /// </param>
    /// <param name="targetId">
    /// The one target the stage may run, or null for all of them. Part 9.7's header picker makes "which
    /// action am I editing" a thing the user chooses, and a stage that listed and ran every action's
    /// scripts underneath that choice would be answering a question nobody asked.
    /// </param>
    public StageSession(
        VisualProject document,
        HashSet<string>? breakpoints = null,
        HashSet<string>? watched = null,
        Func<TimeSpan, Action, IDisposable>? schedule = null,
        string? targetId = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _breakpoints = breakpoints ?? new HashSet<string>(StringComparer.Ordinal);
        _watched = watched ?? new HashSet<string>(StringComparer.Ordinal);
        _schedule = schedule ?? DefaultSchedule;
        _targetId = targetId;
        _interpreter = NewInterpreter();

        Rebind();
    }


    /// <summary>
    /// What a panel says about itself, whether or not anything has run.
    /// </summary>
    /// <remarks>
    /// Part 10.5 asks for the honesty statement in the UI, and the reason it is not a tooltip is that a
    /// caveat nobody has to ask for is a caveat nobody reads. The stage is a preview of what the SDK does,
    /// not the thing that does it: two engines can agree for a hundred blocks and differ on the hundred
    /// and first.
    /// </remarks>
    public string HonestyStatement =>
        "This stage is a preview, not the plugin. The interpreter and the compiler are two engines, and "
        + "the SDK decides what really happens. A trace that ends cleanly is not a guarantee that the "
        + "generated plugin will behave the same way.";

    /// <summary>The host the run is writing to.</summary>
    public IVisualHost Host => _host;

    /// <summary>
    /// The one target the stage runs, or null when it runs them all.
    /// </summary>
    /// <remarks>
    /// Published because a panel that shows a script list has to be able to say where the list came from.
    /// A stage whose picker says "Log a message" and whose Run presses a script from a different action is
    /// not a small disagreement: it produces a trace full of values the user never wrote.
    /// </remarks>
    public string? TargetId => _targetId;


    /// <summary>The scripts that can be run, in document order.</summary>
    public IReadOnlyList<StageScript> Scripts { get; private set; } = [];

    /// <summary>One script the stage can run.</summary>
    /// <param name="Id">Its id.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Trigger">What starts it, in words.</param>
    public sealed record StageScript(string Id, string Name, string Trigger);

    /// <summary>The script Run and Step will run, or null when the document has none.</summary>
    public StageScript? SelectedScript =>
        Scripts.FirstOrDefault(script => script.Id == _selectedScriptId);

    /// <summary>Everything that has happened, in order.</summary>
    public IReadOnlyList<StageTraceRow> Trace { get; private set; } = [];

    /// <summary>One traced step, as a timeline shows it.</summary>
    /// <param name="Kind">What happened.</param>
    /// <param name="Label">Which block, with its holes filled.</param>
    /// <param name="Detail">The value computed, or the message an error carries.</param>
    /// <param name="Milliseconds">The simulated clock when it happened.</param>
    /// <param name="BlockId">The block it belongs to, which is what the tile's pulse follows.</param>
    public sealed record StageTraceRow(
        ExecutionStepKind Kind,
        string Label,
        string Detail,
        double Milliseconds,
        string BlockId);

    /// <summary>The watch table: the names a script asked to be shown, and any added by hand.</summary>
    public IReadOnlyList<StageWatchRow> Watched { get; private set; } = [];

    /// <summary>One watched variable and what it holds now.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Value">What it holds, or a dash when nothing has set it.</param>
    public sealed record StageWatchRow(string Name, string Value);

    /// <summary>The mocked deck: folders and buttons.</summary>
    public IReadOnlyList<StageDeckTile> Deck { get; private set; } = [];

    /// <summary>One tile on the mocked deck.</summary>
    /// <param name="Id">Its id, which is what a block's menu names.</param>
    /// <param name="Title">What it says.</param>
    /// <param name="IsFolder">Whether it sits inside another tile.</param>
    /// <param name="IsPressed">Whether a button is pressed right now.</param>
    /// <param name="IsThePressingClient">Whether it is the tile the run is pressing.</param>
    public sealed record StageDeckTile(
        string Id,
        string Title,
        bool IsFolder,
        bool IsPressed,
        bool IsThePressingClient);

    /// <summary>Everything the run has notified, oldest first.</summary>
    public IReadOnlyList<StageNotificationRow> Notifications { get; private set; } = [];

    /// <summary>One notification.</summary>
    /// <param name="Level">How loudly it asked for attention.</param>
    /// <param name="Title">Its heading.</param>
    /// <param name="Body">Its text.</param>
    public sealed record StageNotificationRow(NotificationLevel Level, string Title, string Body);

    /// <summary>The log, one line per entry.</summary>
    public IReadOnlyList<string> Log { get; private set; } = [];

    /// <summary>The values supplied for this run's parameters.</summary>
    public IReadOnlyList<StageParameterRow> Parameters { get; private set; } = [];

    /// <summary>One parameter and the value this run supplies for it.</summary>
    /// <remarks>
    /// A class with a setter that pushes into the host, not a record. The stage's Parameters table binds
    /// two-way to <see cref="Value"/>, and against an init-only record that write-back failed silently: the
    /// box accepted the caret, the binding produced a WPF error nobody sees, and the run used an empty
    /// value - so a hole with no parameter behind it printed as written, produced by a control that looks
    /// exactly like one that had. Nothing in the app called SetParameter at all, which is the same defect
    /// seen from the other side.
    /// </remarks>
    public sealed class StageParameterRow : INotifyPropertyChanged
    {
        private readonly Action<string, string?> _apply;
        private string _value = string.Empty;

        internal StageParameterRow(string name, Action<string, string?> apply)
        {
            Name = name;
            _apply = apply;
        }

        /// <summary>The parameter's name.</summary>
        public string Name { get; }

        /// <summary>What the user typed, and what the next run will use.</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (string.Equals(_value, value, StringComparison.Ordinal))
                {
                    return;
                }

                _value = value;
                _apply(Name, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Why the last run ended, in a sentence.</summary>
    public string Outcome { get; private set; } = "Nothing has run yet.";

    /// <summary>
    /// The block the stage is standing on, which is the one whose tile pulses.
    /// </summary>
    /// <remarks>
    /// Part 10.3 calls the tile pulsing the marquee part of the debugger, and a pulse nothing can find is
    /// decoration. The session publishes the id and the editor turns it into a property on the tile.
    /// </remarks>
    public string CurrentBlockId { get; private set; } = string.Empty;

    /// <summary>The breakpoints, by block id, for a caller that has to draw them.</summary>
    public HashSet<string> Breakpoints => _breakpoints;

    /// <summary>The names the watch table shows.</summary>
    public HashSet<string> WatchedNames => _watched;

    /// <summary>
    /// How many blocks one run of the stage may execute.
    /// </summary>
    /// <remarks>
    /// Lower than the interpreter's own default, and deliberately. The interpreter's budget is sized for a
    /// test that runs flat out; the stage runs at a speed a person is watching, so twenty thousand steps
    /// is forty minutes of a <c>forever</c> nobody asked to watch. A preview that stops and says the
    /// budget stopped it is more use than one that keeps going.
    /// </remarks>
    public int StepBudget { get; init; } = 2_000;

    /// <summary>Steps per second.</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            var clamped = Math.Clamp(value, 1, 120);
            if (Math.Abs(clamped - _speed) < double.Epsilon)
            {
                return;
            }

            _speed = clamped;

            // Re-armed, because a timer set to eight steps a second would keep running at eight while the
            // slider said sixty.
            if (_timer is not null)
            {
                Run();
            }
        }
    }

    /// <summary>Whether a run is in progress.</summary>
    public bool IsRunning => _timer is not null;

    /// <summary>
    /// Whether a run is suspended at a block boundary and can be carried on from there.
    /// </summary>
    /// <remarks>
    /// Not the same question as "is something paused". A run stopped at a breakpoint is paused in the
    /// ordinary English sense and <em>cannot</em> be resumed with <see cref="Resume"/> — the run finished
    /// for that purpose, and the answer to "carry on" is <see cref="Run"/>. Only a pause asked for while
    /// the transport was running leaves a run that is still going, halfway through, with its stack intact.
    /// </remarks>
    public bool IsPaused => _timer is null && _interpreter.PausedOnRequest;

    /// <summary>Whether the transport's pause control can do anything right now.</summary>
    public bool CanPause => _timer is not null || IsPaused;

    /// <summary>What the transport's pause control says, which is Pause or Resume and never both.</summary>
    public string PauseLabel => IsPaused ? "Resume" : "Pause";


    /// <summary>Whether there is a script to step.</summary>
    public bool CanStep => SelectedScript is not null;

    /// <summary>The simulated clock, in milliseconds.</summary>
    public double ClockMilliseconds => _host.Clock.ElapsedMilliseconds;

    /// <summary>The simulated clock, as a panel says it.</summary>
    public string ClockText => $"{ClockMilliseconds.ToString("N0", CultureInfo.CurrentCulture)} ms of simulated time";

    /// <summary>How many breakpoints are set.</summary>
    public string BreakpointCountText => _breakpoints.Count switch
    {
        0 => "No breakpoints",
        1 => "1 breakpoint",
        var count => $"{count} breakpoints",
    };

    /// <summary>
    /// What the transport row says about the network, because it matters.
    /// </summary>
    /// <remarks>
    /// Part 10.1 wants the HTTP layer offline behind an explicit toggle. Saying so on the panel is what
    /// stops a dry run that answered from a canned table looking like one that asked the internet — a user
    /// who builds a button on that belief has built a button that will do nothing on a real deck.
    /// </remarks>
    public string NetworkText => _host.Http.AllowRealNetwork
        ? "Network: real requests allowed"
        : "Network: offline — requests are answered from the canned table";

    /// <summary>How many trace rows the timeline keeps.</summary>
    /// <remarks>
    /// A cap rather than a growing list, because a <c>forever</c> at sixty steps a second produces twenty
    /// thousand rows in under a minute and a panel drawing twenty thousand rows takes the window with it.
    /// </remarks>
    public const int TraceLimit = 500;

    /// <summary>Which script the transport runs.</summary>
    /// <param name="scriptId">The script's id.</param>
    public void SelectScript(string? scriptId)
    {
        if (string.Equals(_selectedScriptId, scriptId, StringComparison.Ordinal))
        {
            return;
        }

        _selectedScriptId = scriptId;

        // Switching scripts mid-run would leave one script's variables on the stage under another
        // script's name, which is worse than not having run either.
        Reset();
    }

    /// <summary>
    /// Starts the run, or resumes a paused one.
    /// </summary>
    /// <remarks>
    /// Arming a scheduled tick rather than blocking, because whoever is driving this has to stay free to
    /// answer Stop, to scroll the trace and to close the window. A run that has not started yet is started
    /// here and then left to the ticks, so an empty script says so immediately rather than after a delay.
    /// </remarks>
    public void Run()
    {
        if (SelectedScript is not { } choice)
        {
            Outcome = "Pick a script first.";
            return;
        }

        var script = _interpreter.Steps.FirstOrDefault(candidate => candidate.Id == choice.Id);
        _ = script is null;
        if (script is null)
        {
            Outcome = "That script is no longer in the document.";
            return;
        }

        if (!_interpreter.IsRunning)
        {
            // Reset rather than the batch Run: a transport button that ran the whole script here would
            // leave nothing for Stop, nothing for the trace to show happening, and no way to see the
            // first block before the hundredth.
            _interpreter.Reset(script);
        }

        _timer?.Dispose();
        _timer = _schedule(TickDelay, () => Tick());
    }

    /// <summary>Executes one block and stops.</summary>
    public void Step() => Tick(interactive: true);

    /// <summary>Executes one block, stepping into a container's body when it meets one.</summary>
    public void StepInto() => Tick(interactive: true, into: true);

    /// <summary>
    /// Suspends a running script at the next block boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asks the interpreter and lets the tick that is already in flight finish, rather than cancelling the
    /// timer here. That is the difference between a pause and a stop: the interpreter is asked to yield
    /// <em>between</em> two blocks, so the block that was running when the button was pressed completes and
    /// the one after it does not start. Cancelling the timer instead would freeze the stage at whatever
    /// point the pointer happened to reach, which reads as a pause and is really a snapshot — the trace and
    /// the host would agree with each other and both be one block ahead of what the user was shown.
    /// </para>
    /// <para>
    /// It also means at most one more block runs after the press, at the transport's own speed. A user who
    /// pauses a fast script and watches one more line appear in the trace has been given a fact about where
    /// the run stopped, which the outcome line then names.
    /// </para>
    /// </remarks>
    public void Pause()
    {
        if (_timer is null)
        {
            // Said rather than silently ignored, because the two reasons a transport is not running are
            // different problems with different answers, and a button that does nothing teaches nothing.
            Outcome = _interpreter.PausedAtBreakpoint is not null
                ? $"Already stopped at a breakpoint, before: {_interpreter.PausedAtBreakpointLabel}. "
                    + "Run carries on from there."
                : IsPaused
                    ? $"Already paused, after: {_interpreter.PausedAfterBlockLabel}."
                    : "Nothing is running.";

            return;
        }

        _interpreter.RequestPause();
    }

    /// <summary>
    /// Carries on from a pause, keeping everything the run had already done.
    /// </summary>
    /// <remarks>
    /// <see cref="Run"/> and not a fresh start: the interpreter is still running — it yielded, it did not
    /// finish — so <see cref="Run"/> re-arms the tick without resetting, which is what keeps the trace,
    /// the variables and the clock. Resetting here would make Resume indistinguishable from pressing Run
    /// twice, and a pause the user has to restart from the top to leave is not a pause.
    /// </remarks>
    public void Resume()
    {
        if (!IsPaused)
        {
            Outcome = "Nothing is paused.";
            return;
        }

        _interpreter.ClearPause();
        Run();
    }


    /// <summary>
    /// Stops the run where it stands.
    /// </summary>
    /// <remarks>
    /// Cancelling the tick and nothing else. The interpreter is between blocks when this is called, so
    /// there is nothing half-executed to unwind, and telling it to cancel as well would make the trace
    /// claim a cancellation the user did not ask the interpreter for.
    /// </remarks>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        // So the transport stops offering to resume a run the user has just ended. Without this the Pause
        // control keeps reading "Resume" after Stop, and pressing it starts a run the user did not ask for.
        _interpreter.ForgetPause();

        Outcome = "Stopped by hand.";
    }


    /// <summary>
    /// Clears the trace, the host and the clock, and puts the stage back before its hat.
    /// </summary>
    /// <remarks>
    /// The host is rebuilt rather than cleared, because a reset that leaves half a run's state behind is
    /// why a second run of the same script does not do what the first did — and "it worked when I pressed
    /// it once" is the most expensive kind of bug report to chase.
    /// </remarks>
    public void Reset()
    {
        _timer?.Dispose();
        _timer = null;

        _host = new SimulatedHost();
        _interpreter = NewInterpreter();

        CurrentBlockId = string.Empty;
        Outcome = "Nothing has run yet.";

        foreach (var row in Parameters)
        {
            _host.Parameters.Set(row.Name, row.Value);
        }

        Mirror();
    }

    /// <summary>Turns the breakpoint on a block on or off.</summary>
    /// <param name="blockId">The block.</param>
    /// <returns>Whether there is one now.</returns>
    public bool ToggleBreakpoint(string blockId)
    {
        if (!_breakpoints.Remove(blockId))
        {
            _breakpoints.Add(blockId);
        }

        return _breakpoints.Contains(blockId);
    }

    /// <summary>Watches a name by hand, as Scratch's <i>show variable</i> does.</summary>
    /// <param name="name">The variable.</param>
    public void Watch(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (_watched.Add(name))
        {
            RefreshWatch();
        }
    }

    /// <summary>Stops watching a name. Doing it twice is not an error.</summary>
    /// <param name="name">The variable.</param>
    public void Unwatch(string name)
    {
        if (_watched.Remove(name))
        {
            RefreshWatch();
        }
    }

    /// <summary>Supplies a value for one of the run's parameters.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="value">The value.</param>
    public void SetParameter(string name, string? value) => _host.Parameters.Set(name, value);

    /// <summary>Whether the run may make real requests. Off until somebody says so.</summary>
    /// <param name="allow">Whether to let requests leave the machine.</param>
    public void AllowRealNetwork(bool allow) => _host.Http.AllowRealNetwork = allow;

    /// <summary>
    /// Re-reads the document, after an edit or a revert.
    /// </summary>
    /// <remarks>
    /// The watch list and the breakpoints survive deliberately. They are facts about running the script
    /// rather than about what the script says, and losing them because a user typed a character is the
    /// fastest way to make a debugger feel broken.
    /// </remarks>
    /// <param name="document">The new document, or null to re-read the one already held.</param>
    /// <param name="parameters">The parameter names the action declares.</param>
    /// <param name="targetId">The target to run, or null for all of them.</param>
    public void Rebind(
        VisualProject? document = null,
        IEnumerable<string>? parameters = null,
        string? targetId = null)
    {
        _timer?.Dispose();
        _timer = null;

        if (document is not null)
        {
            _document = document;
        }

        // Null means "no change", which is what makes the two-argument calls still work: switching the
        // stage's script must not silently widen it back to every target in the document.
        _targetId = targetId ?? _targetId;

        _interpreter = NewInterpreter();


        var scripts = _interpreter.Steps
            .Select(script => new StageScript(
                script.Id,
                script.Name,
                BlockCatalog.Find(script.Hat.Kind) is { } hat ? BlockLabel.PreviewText(hat) : script.Hat.Kind))
            .ToList();

        Scripts = scripts;
        _selectedScriptId = scripts.Any(script => script.Id == _selectedScriptId)
            ? _selectedScriptId
            : scripts.FirstOrDefault()?.Id;

        Parameters = (parameters ?? [])
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new StageParameterRow(name, SetParameter))
            .ToList();

        Reset();
    }

    /// <summary>
    /// One tick: a block, then a look at what the host now says.
    /// </summary>
    /// <remarks>
    /// Every tick ends by refreshing the deck, the watch table and the notifications, because the only
    /// reason the stage exists is that the script and its effects are visible together.
    /// </remarks>
    private void Tick(bool interactive = false, bool into = false)
    {
        // The interpreter's own Step starts the first script in the document when nothing is running, so
        // the selection is pushed in first: a stage where the picker says one script and Step runs another
        // is worse than no picker.
        var selected = SelectedScript;
        var script = selected is null
            ? null
            : _interpreter.Steps.FirstOrDefault(candidate => candidate.Id == selected.Id);

        // Nothing running yet means the interpreter is about to start *its* first script, which is not
        // necessarily the one the picker says - the picker is the user's, and a stage that steps a
        // different script from the one named above its own trace is worse than no picker at all. Reset
        // into the chosen script first, exactly as Run does.
        if (script is not null && !_interpreter.IsRunning)
        {
            _interpreter.Reset(script);
        }

        var outcome = script is null
            ? RunOutcome.Completed
            : interactive
                ? into
                    ? _interpreter.StepInto()
                    : _interpreter.Step()
                : _interpreter.Step();

        Mirror();
        PublishOutcome(outcome);

        // Keep going - for a run, and for nothing else. Stepping pauses after every block on purpose,
        // so a pause on its own is not a reason to re-arm: arm it here and one press of Step runs the
        // whole script at the transport's speed while the outcome still says "Paused", which is the
        // difference between a debugger and a very slow Run. The Run button re-arms it from Run().
        //
        // A pause somebody asked for is the third thing that must not re-arm, and it is the one this
        // condition did not know about when it was written: without it the tick that honoured the request
        // would immediately queue another one, so Pause would appear to do nothing at all - the run would
        // keep going at full speed with the transport button now reading "Resume". The three conditions
        // are now the three reasons a paced run stops on its own.
        if (!interactive
            && outcome is RunOutcome.Paused
            && !_interpreter.PausedOnRequest
            && _interpreter.PausedAtBreakpoint is null
            && _interpreter.Executed < _interpreter.StepBudget)
        {
            _timer = _schedule(TickDelay, () => Tick());
        }
    }


    /// <summary>
    /// The trace, the watch table, the deck, the notifications and the log, read off the host.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than diffed. A stage showing five things has no user who would notice a row changing
    /// colour, and the diff is where the bug that shows a stale value always is.
    /// </remarks>
    private void Mirror()
    {
        var trace = _interpreter.Trace;
        var first = Math.Max(0, trace.Count - TraceLimit);

        Trace = [.. trace.Skip(first).Select(step => new StageTraceRow(
            step.Kind,
            step.Label ?? string.Empty,
            step.Value ?? string.Empty,
            step.Millisecond,
            step.BlockId ?? string.Empty))];

        if (trace.Count > 0)
        {
            CurrentBlockId = trace[^1].BlockId ?? string.Empty;
        }

        RefreshWatch();

        Deck = [.. _host.Deck.Items.Select(item => new StageDeckTile(
            item.Id,
            item.Title,
            item.ParentId is not null,
            item.Pressed,
            string.Equals(item.Id, _host.Deck.PressingClientId, StringComparison.Ordinal)))];

        Notifications = [.. _host.Notifications.Notifications.Select(notification => new StageNotificationRow(
            notification.Level,
            notification.Title,
            notification.Body ?? string.Empty))];

        Log = [.. _host.Log.Lines];

        // The form is the authority on parameter values while nobody is typing, so they are pushed in
        // rather than read out: a run that used the value the user supplied is the whole point of the form.
        for (var index = 0; index < Parameters.Count; index++)
        {
            var row = Parameters[index];
            _host.Parameters.Set(row.Name, row.Value);
        }
    }

    /// <summary>Re-reads the watch table from the host.</summary>
    private void RefreshWatch()
    {
        Watched = [.. _watched
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new StageWatchRow(name, _host.Variables.Get(name) ?? "—"))];
    }

    /// <summary>Says why the run stopped, and stops the pacing if it has.</summary>
    private void PublishOutcome(RunOutcome outcome)
    {
        switch (outcome)
        {
            case RunOutcome.Paused:
                StopTimer();

                // Three ways a paced run ends in a pause, and they are told apart rather than lumped into
                // "Paused.": a breakpoint the user set, a pause the user just asked for, and the budget
                // running out. The last one is not a pause at all and says so below; the first two are
                // genuinely different and the difference decides what the transport should offer next.
                if (_interpreter.PausedOnRequest)
                {
                    Outcome = $"Paused by hand, after: {_interpreter.PausedAfterBlockLabel}";
                    return;
                }

                // Stepping pauses after every block, so a pause on its own says nothing. What the stage has
                // to say is *why* it stopped, and the two things that can end a stepped run are a
                // breakpoint the user set and the budget running out.
                if (_interpreter.PausedAtBreakpoint is not null)
                {
                    Outcome = $"Paused at a breakpoint, before: {_interpreter.PausedAtBreakpointLabel}";
                    return;
                }

                if (_interpreter.Executed >= _interpreter.StepBudget)
                {
                    StopTimer();
                    Outcome = "Stopped at the step budget: something in this script does not end.";
                    return;
                }

                Outcome = "Paused.";
                return;


            case RunOutcome.Completed:
                StopTimer();
                Outcome = "Finished.";
                return;

            case RunOutcome.StepBudgetExhausted:
                StopTimer();
                Outcome = "Stopped at the step budget: something in this script does not end.";
                return;

            case RunOutcome.Cancelled:
                StopTimer();
                Outcome = "Stopped.";
                return;

            case RunOutcome.Failed:
                StopTimer();
                Outcome = "Stopped on an error. The last trace line names the block.";
                return;

            default:
                return;
        }
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>A fresh interpreter over the document, sharing this session's breakpoint set.</summary>
    private ScriptInterpreter NewInterpreter()
    {
        var interpreter = new ScriptInterpreter(_document, _host, targetId: _targetId) { StepBudget = StepBudget };


        // Handed the same set rather than a copy of it.
        interpreter.Breakpoints = _breakpoints;

        // The parameter names the action declares, so a template's {holes} know what they may name.
        interpreter.DeclaredParameters = [.. Parameters.Select(row => row.Name)];

        return interpreter;
    }

    /// <summary>How long one tick waits.</summary>
    private TimeSpan TickDelay => TimeSpan.FromMilliseconds(Math.Max(1, 1000d / Math.Max(1, Speed)));

    /// <summary>
    /// The default pacing, for a caller that has nothing better: run it now.
    /// </summary>
    /// <remarks>
    /// A windowed caller passes a dispatcher timer instead. This default exists so that a headless caller
    /// — a test, or a future batch dry run — gets a run that finishes rather than one that never starts,
    /// and it is why the panel is the thing that knows about threads.
    /// </remarks>
    private static IDisposable DefaultSchedule(TimeSpan delay, Action action)
    {
        action();

        return new NothingToCancel();
    }

    private sealed class NothingToCancel : IDisposable
    {
        public void Dispose()
        {
        }
    }
}