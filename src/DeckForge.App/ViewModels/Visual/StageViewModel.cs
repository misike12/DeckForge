using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Runtime;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// The stage panel's data: a <see cref="StageSession"/> and the collections a panel binds to.
/// </summary>
/// <remarks>
/// <para>
/// A projection and nothing else. Every decision — what a run does, when it stops, what the watch table
/// says, what the honesty line is — lives in <see cref="StageSession"/> in Core, where a test can reach it
/// without a window. This class exists because XAML cannot bind to a property that raises no notification
/// and cannot bind to an <see cref="IReadOnlyList{T}"/> at all.
/// </para>
/// <para>
/// The transport buttons are the view model's own commands rather than the session's methods bound through
/// a converter, because a command that projects before and after it runs is the only way the collections
/// are guaranteed to match the session afterwards.
/// </para>
/// </remarks>
public sealed partial class StageViewModel : ObservableObject
{
    private readonly VisualEditorViewModel _editor;
    private StageSession _session;
    private StageSession.StageScript? _selectedScript;
    private string _outcome = string.Empty;
    private string _currentBlockId = string.Empty;
    private double _speed;

    /// <summary>Builds the stage for a document.</summary>
    /// <param name="editor">The editor whose document is being run.</param>
    /// <param name="schedule">
    /// Runs an action after a delay. Defaults to a dispatcher timer so the UI thread stays free; a test
    /// passes its own and drives the ticks by hand.
    /// </param>
    public StageViewModel(VisualEditorViewModel editor, Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _editor = editor;
        _session = new StageSession(
            editor.Document,
            editor.Breakpoints,
            editor.Watched,
            // The session drives its own tick; this wraps it so the panel is re-projected once the tick has
            // happened. A transport that ran the session but never looked at the result would leave the
            // trace and the watch table describing the previous run.
            (delay, action) => (schedule ?? DispatcherSchedule)(delay, () =>
            {
                action();
                Project();
            }));
    }

    /// <summary>The session, for a caller that needs something this class does not project.</summary>
    public StageSession Session => _session;

    /// <summary>What the panel says about itself, whether or not anything has run.</summary>
    public string HonestyStatement => _session.HonestyStatement;

    /// <summary>The scripts that can be run, in document order.</summary>
    public ObservableCollection<StageSession.StageScript> Scripts { get; } = [];

    /// <summary>The script Run and Step will run.</summary>
    public StageSession.StageScript? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (SetProperty(ref _selectedScript, value))
            {
                _session.SelectScript(value?.Id);
                Project();
            }
        }
    }

    /// <summary>What each step of the trace says.</summary>
    public ObservableCollection<StageSession.StageTraceRow> Trace { get; } = [];

    /// <summary>The watch table: the names a script asked to be shown, and any added by hand.</summary>
    public ObservableCollection<StageSession.StageWatchRow> WatchRows { get; } = [];

    /// <summary>The mocked deck: folders and buttons.</summary>
    public ObservableCollection<StageSession.StageDeckTile> Deck { get; } = [];

    /// <summary>Everything the run has notified, oldest first.</summary>
    public ObservableCollection<StageSession.StageNotificationRow> Notifications { get; } = [];

    /// <summary>The log, one line per entry.</summary>
    public ObservableCollection<string> Log { get; } = [];

    /// <summary>The values supplied for this run's parameters.</summary>
    public ObservableCollection<StageSession.StageParameterRow> Parameters { get; } = [];

    /// <summary>Why the last run ended, in a sentence.</summary>
    public string Outcome
    {
        get => _outcome;
        private set => SetProperty(ref _outcome, value);
    }

    /// <summary>The block the stage is standing on, which is the one whose tile pulses.</summary>
    [ObservableProperty]
    private string _currentBlockIdValue = string.Empty;

    /// <summary>The block the stage is standing on, for the page's breakpoint stamp.</summary>
    public string CurrentBlockId => CurrentBlockIdValue;

    /// <summary>Steps per second, from the speed slider.</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, value))
            {
                _session.Speed = value;
            }
        }
    }

    /// <summary>Whether a run is in progress.</summary>
    public bool IsRunning => _session.IsRunning;

    /// <summary>Whether there is a script to step.</summary>
    public bool CanStep => _session.CanStep;

    /// <summary>Whether the trace has anything in it.</summary>
    public bool HasTrace => Trace.Count > 0;

    /// <summary>Whether the run put anything on the watch table.</summary>
    public bool HasWatch => WatchRows.Count > 0;

    /// <summary>Whether anything was notified.</summary>
    public bool HasNotifications => Notifications.Count > 0;

    /// <summary>Whether the mocked deck has any items.</summary>
    public bool HasDeck => Deck.Count > 0;

    /// <summary>Whether the action declares any parameters.</summary>
    public bool HasParameters => Parameters.Count > 0;

    /// <summary>The simulated clock, in milliseconds.</summary>
    public double ClockMilliseconds => _session.ClockMilliseconds;

    /// <summary>The simulated clock, as the panel says it.</summary>
    public string ClockText => _session.ClockText;

    /// <summary>How many breakpoints are set, for the transport row.</summary>
    public string BreakpointCountText => _session.BreakpointCountText;

    /// <summary>What the transport row says about the network, because it matters.</summary>
    public string NetworkText => _session.NetworkText;

    /// <summary>Starts the run, or resumes a paused one.</summary>
    [RelayCommand]
    private void Run()
    {
        _session.Run();
        Project();
    }

    /// <summary>Executes one block and stops.</summary>
    [RelayCommand]
    private void Step()
    {
        _session.Step();
        Project();
    }

    /// <summary>Executes one block, stepping into a container's body when it meets one.</summary>
    [RelayCommand]
    private void StepInto()
    {
        _session.StepInto();
        Project();
    }

    /// <summary>Stops the run where it stands.</summary>
    [RelayCommand]
    private void Stop()
    {
        _session.Stop();
        Project();
    }

    /// <summary>Clears the trace, the host and the clock.</summary>
    [RelayCommand]
    private void Reset()
    {
        _session.Reset();
        Project();
    }

    /// <summary>Turns the breakpoint on a block on or off.</summary>
    /// <param name="blockId">The block.</param>
    /// <returns>Whether there is one now.</returns>
    public bool ToggleBreakpoint(string blockId)
    {
        var set = _session.ToggleBreakpoint(blockId);

        _editor.StampBreakpoints();
        OnPropertyChanged(nameof(BreakpointCountText));

        return set;
    }

    /// <summary>Watches a name by hand, as Scratch's <i>show variable</i> does.</summary>
    /// <param name="name">The variable.</param>
    public void Watch(string name)
    {
        _session.Watch(name);
        _editor.Watched.Add(name);
        Project();
    }

    /// <summary>Stops watching a name.</summary>
    /// <param name="name">The variable.</param>
    public void Unwatch(string name)
    {
        _session.Unwatch(name);
        _editor.Watched.Remove(name);
        Project();
    }

    /// <summary>Re-reads the document, after an edit or a revert.</summary>
    public void Rebind()
    {
        _session.Rebind(_editor.Document, _editor.Validation.Parameters);
        Project();
    }

    /// <summary>
    /// Copies the session's state into the bound collections, and says what changed.
    /// </summary>
    /// <remarks>
    /// One method, called after every command. A panel that refreshed half its lists after a step and the
    /// rest after a run is a panel whose watch table and whose trace disagree, and the user has no way to
    /// tell which one is lying.
    /// </remarks>
    public void Project()
    {
        Swap(Scripts, _session.Scripts);
        Swap(Trace, _session.Trace);
        Swap(WatchRows, _session.Watched);
        Swap(Deck, _session.Deck);
        Swap(Notifications, _session.Notifications);
        Swap(Log, _session.Log);
        Swap(Parameters, _session.Parameters);

        if (!ReferenceEquals(_selectedScript, _session.SelectedScript))
        {
            _selectedScript = _session.SelectedScript;
            OnPropertyChanged(nameof(SelectedScript));
        }

        Outcome = _session.Outcome;
        CurrentBlockIdValue = _session.CurrentBlockId;

        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanStep));
        OnPropertyChanged(nameof(HasTrace));
        OnPropertyChanged(nameof(HasWatch));
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(HasDeck));
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(ClockMilliseconds));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(BreakpointCountText));
    }

    /// <summary>
    /// Puts one session list into one bound collection, and refreshes only when it differs.
    /// </summary>
    /// <remarks>
    /// The comparison is by content rather than by reference, so an unchanged list does not rebuild its
    /// rows. A stage that redrew four hundred trace rows on every tick because the collection instance
    /// changed while its contents did not would spend more time painting than interpreting.
    /// </remarks>
    private static void Swap<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        if (target.Count == source.Count && target.SequenceEqual(source))
        {
            return;
        }

        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    /// <summary>
    /// The default pacing: a dispatcher timer, so the wait happens with the UI thread free.
    /// </summary>
    /// <remarks>
    /// DispatcherTimer rather than Task.Delay on a background thread, because the tick touches
    /// ObservableCollections the UI is bound to. The alternative is a lock on every row and a class of bug
    /// that only appears on a fast machine.
    /// </remarks>
    private static IDisposable DispatcherSchedule(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };

        EventHandler? onTick = null;
        onTick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= onTick;
            action();
        };

        timer.Tick += onTick;
        timer.Start();

        return new TimerStopper(timer);
    }

    private sealed class TimerStopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}