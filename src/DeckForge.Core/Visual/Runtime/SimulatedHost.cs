using System.Globalization;

using DeckForge.Core.Visual.Runtime;

namespace DeckForge.Core.Visual.Runtime;

/// <summary>
/// The interpreter's host: everything in memory, everything inspectable, nothing that touches the machine.
/// </summary>
/// <remarks>
/// <para>
/// Every surface is a plain list or dictionary so the stage panel can bind straight to it, and so a test can
/// assert on what a program did without a window.
/// </para>
/// <para>
/// The three surfaces that could do real harm are the ones with defaults that refuse:
/// <see cref="HttpSurface.AllowRealNetwork"/> is false, <see cref="Clock"/> only moves when something
/// advances it, and <see cref="DialogSurface"/> answers from a table rather than by asking. A simulator that
/// silently reached the network, or waited on real time, or popped a modal dialog in the middle of a step
/// trace, would be a debugger that cannot be reasoned about — and the user would have no way to tell which
/// of the two happened.
/// </para>
/// </remarks>
public sealed class SimulatedHost : IVisualHost
{
    public SimulatedHost()
    {
        Deck = new DeckSurface();
        Notifications = new NotificationSurface();
        Variables = new VariableSurface();
        Lists = new ListSurface();
        Scripts = new ScriptSurface();
        Events = new EventSurface();
        Messages = new MessageSurface();
        Ui = new DialogSurface();
        Http = new HttpSurface();
        Clock = new ManualClock();
        Log = new LogSurface();
        Parameters = new ParameterSurface();
    }

    public IDeckSurface Deck { get; }

    public INotificationSurface Notifications { get; }

    public IVariableSurface Variables { get; }

    public IListSurface Lists { get; }

    public IScriptSurface Scripts { get; }

    public IEventSurface Events { get; }

    public IMessageSurface Messages { get; }

    public IDialogSurface Ui { get; }

    public IHttpSurface Http { get; }

    public IClock Clock { get; }

    public ILogSurface Log { get; }

    public IParameterSurface Parameters { get; }

    /// <summary>
    /// Everything that was shown or asked, in the order it happened.
    /// </summary>
    /// <remarks>
    /// One list rather than five, because the useful question about a simulated run is "what did the user
    /// see", and answering it from five surfaces in five places is how one of them gets left out.
    /// </remarks>
    public IList<string> Transcript { get; } = [];

    /// <summary>Adds one line to <see cref="Transcript"/>.</summary>
    internal void Note(string line) => Transcript.Add(line);

    /// <summary>The deck: a flat list, with the pressing client set separately.</summary>
    public sealed class DeckSurface : IDeckSurface
    {
        private readonly List<DeckItem> _items = [];

        public IReadOnlyList<DeckItem> Items => _items;

        public string? PressingClientId { get; private set; }

        /// <summary>Sets who is pressing things, which is what the deck's blocks report.</summary>
        public void SetPressing(string? clientId) => PressingClientId = clientId;

        /// <summary>Adds a folder or a button.</summary>
        public DeckItem Add(string id, string title, string? parentId = null, bool pressed = false, string? icon = null)
        {
            var item = new DeckItem(id, title, parentId, pressed, icon);
            _items.Add(item);
            return item;
        }

        /// <summary>Changes a button's state.</summary>
        public void SetPressed(string id, bool pressed)
        {
            var at = _items.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (at < 0)
            {
                return;
            }

            _items[at] = _items[at] with { Pressed = pressed };
        }

        public DeckItem? Find(string id) =>
            _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

        /// <summary>Where each client is, so a back button has something to go back to.</summary>
        private readonly Dictionary<string, List<string>> _history = new(StringComparer.Ordinal);

        /// <summary>Where each client is now.</summary>
        private readonly Dictionary<string, string> _current = new(StringComparer.Ordinal);

        public void OpenFolder(string folderId, string? clientId = null)
        {
            var client = clientId ?? PressingClientId ?? SimulatedClientId;

            // An unknown folder is not an error: a deck graph a simulator did not set up is exactly the
            // case where a dry run should carry on and show the call, rather than stop and say nothing.
            _history.TryAdd(client, []);
            _history[client].Add(_current.GetValueOrDefault(client, string.Empty));
            _current[client] = folderId;

            SetPressing(client);
        }

        public void GoToParent()
        {
            var client = PressingClientId ?? SimulatedClientId;

            if (!_history.TryGetValue(client, out var path) || path.Count == 0)
            {
                return;
            }

            _current[client] = path[^1];
            path.RemoveAt(path.Count - 1);
        }

        public void GoBack() => GoToParent();

        public void SetButtonState(string widgetId, string state)
        {
            // The state name is stored on the title's own line, because the simulator has no widget to
            // re-render: a stage showing "Game: on" is a stage that answered.
            var at = _items.FindIndex(item => string.Equals(item.Id, widgetId, StringComparison.Ordinal));
            if (at < 0)
            {
                _items.Add(new DeckItem(widgetId, state));
                return;
            }

            _items[at] = _items[at] with { Title = state };
        }

        /// <summary>The client the simulator pretends is pressing things.</summary>
        public const string SimulatedClientId = "client-1";
    }

    /// <summary>The notification area, with replace-by-key.</summary>
    public sealed class NotificationSurface : INotificationSurface
    {
        private readonly List<SimulatedNotification> _notifications = [];

        public IReadOnlyList<SimulatedNotification> Notifications => _notifications;

        public void Show(NotificationLevel level, string title, string? body = null, string? replaceKey = null)
        {
            var notification = new SimulatedNotification(level, title, body, replaceKey);

            // Replace in place rather than remove-and-append: the area is a stack of cards and a
            // progress notification that has moved is a card that jumps while the user is reading it.
            var existing = replaceKey is null
                ? -1
                : _notifications.FindIndex(item => string.Equals(item.ReplaceKey, replaceKey, StringComparison.Ordinal));

            if (existing >= 0)
            {
                _notifications[existing] = notification;
                return;
            }

            _notifications.Add(notification);
        }
    }

    /// <summary>Variables, with the host's own marked as read-only to the plugin.</summary>
    public sealed class VariableSurface : IVariableSurface
    {
        private readonly SortedDictionary<string, VariableEntry> _variables =
            new(StringComparer.Ordinal);

        public IReadOnlyList<VariableEntry> Variables => [.. _variables.Values];

        /// <summary>Declares a variable the plugin owns.</summary>
        public void Declare(string name, string value = "")
        {
            _variables[name] = new VariableEntry(name, value);
        }

        /// <summary>Declares a variable the host owns, which the plugin may read but not write.</summary>
        public void DeclareHost(string name, string value = "") =>
            _variables[name] = new VariableEntry(name, value, HostShared: true);

        public string? Get(string name) => _variables.TryGetValue(name, out var entry) ? entry.Value : null;

        public void Set(string name, string value) =>
            _variables[name] = new VariableEntry(name, value, _variables.TryGetValue(name, out var was) && was.HostShared);

public void Change(string name, double amount)
        {
            // An unset or non-numeric variable changes from zero rather than refusing, which is what
            // VisualRuntime.Change does in the generated plugin.
            var current = double.TryParse(
                Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var read) ? read : 0;

            Set(name, (current + amount).ToString("R", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Lists.</summary>
    public sealed class ListSurface : IListSurface
    {
        private readonly SortedDictionary<string, ListEntry> _lists = new(StringComparer.Ordinal);

        public IReadOnlyList<ListEntry> Lists => [.. _lists.Values];

        /// <summary>Declares a list and what it holds.</summary>
        public void Declare(string name, string itemType = "Text", IReadOnlyList<string>? initial = null)
        {
            _lists[name] = new ListEntry(name, itemType, [.. initial ?? []]);
        }

        public IReadOnlyList<string> Get(string name) =>
            _lists.TryGetValue(name, out var entry) ? entry.Items : [];

        public void Set(string name, IReadOnlyList<string> items) =>
            _lists[name] = new ListEntry(
                name,
                _lists.TryGetValue(name, out var was) ? was.ItemType : "Text",
                [.. items]);

        public void Add(string name, string item) => Set(name, [.. Get(name), item]);

        public bool Remove(string name, string item)
        {
            var items = Get(name).ToList();
            if (!items.Remove(item))
            {
                return false;
            }

            Set(name, items);
            return true;
        }
    }

    /// <summary>The script registry, and the queue of scripts asking to start.</summary>
    public sealed class ScriptSurface : IScriptSurface
    {
        private readonly List<(string Id, string Name)> _scripts = [];

        public IReadOnlyList<string> ScriptIds => [.. _scripts.Select(script => script.Id)];

        /// <summary>The scripts that have asked to start, in order.</summary>
        public IReadOnlyList<string> PendingStarts => [.. _pending];

        private readonly List<string> _pending = [];

        public void Register(string scriptId, string name) => _scripts.Add((scriptId, name));

        public void RequestStart(string scriptId) => _pending.Add(scriptId);

        /// <summary>Takes the next script to start, or null when nothing is waiting.</summary>
        public string? TakePendingStart() => _pending.Count == 0 ? null : _pending[0];

        /// <summary>Discards everything waiting.</summary>
        public void ClearPending() => _pending.Clear();
    }

    /// <summary>The event bus, with what was published and who is listening.</summary>
    public sealed class EventSurface : IEventSurface
    {
        private readonly List<(string Event, string? Payload)> _published = [];
        private readonly List<(string ScriptId, string Event)> _subscriptions = [];

        /// <summary>Everything published, in order.</summary>
        public IReadOnlyList<(string Event, string? Payload)> Published => [.. _published];

        /// <summary>Everything subscribed, in order.</summary>
        public IReadOnlyList<(string ScriptId, string Event)> Subscriptions => [.. _subscriptions];

        public void Publish(string eventId, string? payload = null) => _published.Add((eventId, payload));

        public void Subscribe(string scriptId, string eventId) => _subscriptions.Add((scriptId, eventId));
    }

    /// <summary>Messages, recorded rather than shown.</summary>
    public sealed class MessageSurface : IMessageSurface
    {
        private readonly List<(string Title, string? Body)> _messages = [];

        /// <summary>Everything shown, in order.</summary>
        public IReadOnlyList<(string Title, string? Body)> Messages => [.. _messages];

        public void Show(string title, string? body = null) => _messages.Add((title, body));
    }

    /// <summary>Dialogs, answered from a table.</summary>
    public sealed class DialogSurface : IDialogSurface
    {
        private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
        private readonly List<string> _asked = [];

        /// <summary>What was asked, in order.</summary>
        public IReadOnlyList<string> Asked => [.. _asked];

        /// <summary>What a given dialog answers.</summary>
        public void Answer(string title, string answer) => _answers[title] = answer;

        public string Ask(string title, string? message = null)
        {
            _asked.Add(title);

            return _answers.TryGetValue(title, out var answer)
                ? answer
                : string.Empty;
        }
    }

    /// <summary>HTTP, answered from a table and refusing to leave the machine by default.</summary>
    public sealed class HttpSurface : IHttpSurface
    {
        private readonly List<CannedResponse> _canned = [];
        private readonly List<HttpExchange> _exchanges = [];

        public IReadOnlyList<HttpExchange> Exchanges => _exchanges;

        /// <summary>
        /// Whether a request may reach the network. False from the start, every time.
        /// </summary>
        /// <remarks>
        /// The default is the whole point of the surface. A simulator that fetched real URLs while
        /// stepping would send the user's data, hang on their latency, and produce a trace that differs
        /// between two runs of the same program.
        /// </remarks>
        public bool AllowRealNetwork { get; set; }

        public IReadOnlyList<CannedResponse> CannedResponses => _canned;

        public void SetCannedResponse(CannedResponse response)
        {
            _canned.RemoveAll(canned => string.Equals(canned.Url, response.Url, StringComparison.Ordinal));
            _canned.Add(response);
        }

        public string Get(string url) => Request("GET", url);

        /// <summary>Answers a request, and records what it did either way.</summary>
        public string Request(string method, string url)
        {
            if (!AllowRealNetwork)
            {
                var refused = new HttpExchange(
                    method, url, 0, string.Empty, Failed: true,
                    Reason: "The simulator is offline. Turn on real network access to allow this.");

                _exchanges.Add(refused);
                return string.Empty;
            }

            var canned = _canned.FirstOrDefault(response => string.Equals(response.Url, url, StringComparison.Ordinal));
            var exchange = canned is null
                ? new HttpExchange(method, url, 404, string.Empty, Failed: true, Reason: "No canned answer for this URL.")
                : new HttpExchange(method, url, canned.Status, canned.Body, canned.IsError);

            _exchanges.Add(exchange);
            return exchange.Body;
        }
    }

    /// <summary>A clock that only moves when something moves it.</summary>
    public sealed class ManualClock : IClock
    {
        public double ElapsedMilliseconds { get; private set; }

        public void Advance(double milliseconds)
        {
            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(milliseconds),
                    milliseconds,
                    "A clock cannot run backwards. A wait of a negative number is a block that never "
                    + "finished, not a wait of minus one.");
            }

            ElapsedMilliseconds += milliseconds;
        }
    }

    /// <summary>The log.</summary>
    public sealed class LogSurface : ILogSurface
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => _lines;

        public void Write(NotificationLevel level, string message) =>
            _lines.Add($"[{level.ToString().ToLowerInvariant()}] {message}");
    }

    /// <summary>The values supplied for this run.</summary>
    public sealed class ParameterSurface : IParameterSurface
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

        public void Set(string name, string? value) => _values[name] = value;
    }
}