using DeckForge.Core.Visual;

namespace DeckForge.Core.Visual.Runtime;

/// <summary>What one runtime call asks the host to do.</summary>
public interface IVisualHost
{
    /// <summary>The deck: its folders, buttons and the client that is pressing them.</summary>
    IDeckSurface Deck { get; }

    /// <summary>The notification area.</summary>
    INotificationSurface Notifications { get; }

    /// <summary>Variables, including the host's own.</summary>
    IVariableSurface Variables { get; }

    /// <summary>Lists.</summary>
    IListSurface Lists { get; }

    /// <summary>The script registry: what exists, and how to start it.</summary>
    IScriptSurface Scripts { get; }

    /// <summary>The event bus.</summary>
    IEventSurface Events { get; }

    /// <summary>Messages to other plugins and to the host's users.</summary>
    IMessageSurface Messages { get; }

    /// <summary>Modal dialogs and other prompts.</summary>
    IDialogSurface Ui { get; }

    /// <summary>HTTP, offline unless the user says otherwise.</summary>
    IHttpSurface Http { get; }

    /// <summary>Time, which the simulator advances by hand.</summary>
    IClock Clock { get; }

    /// <summary>The log, which is also what the trace pane shows.</summary>
    ILogSurface Log { get; }

    /// <summary>The action's declared parameters and the values supplied for this run.</summary>
    IParameterSurface Parameters { get; }
}

/// <summary>One folder or button on the deck.</summary>
/// <param name="Id">Its identifier, which is what the blocks refer to.</param>
/// <param name="Title">What it says.</param>
/// <param name="ParentId">The folder it sits in, or null on the deck itself.</param>
/// <param name="State">A button's pressed/released state.</param>
/// <param name="Icon">An icon path, for a button that carries one.</param>
public sealed record DeckItem(
    string Id,
    string Title,
    string? ParentId = null,
    bool Pressed = false,
    string? Icon = null);

/// <summary>The deck: a graph of folders and buttons, and the client currently pressing one.</summary>
public interface IDeckSurface
{
    /// <summary>Everything on the deck, in the order it was set up.</summary>
    IReadOnlyList<DeckItem> Items { get; }

    /// <summary>The client that is pressing something, or null when nothing is.</summary>
    string? PressingClientId { get; }

    /// <summary>The item with that id, or null.</summary>
    DeckItem? Find(string id);
}

/// <summary>How loudly something wants the user's attention.</summary>
public enum NotificationLevel
{
    /// <summary>A log line and nothing on screen.</summary>
    Debug,

    /// <summary>Nothing on screen.</summary>
    Information,

    /// <summary>A toast.</summary>
    Success,

    /// <summary>A toast that says something went wrong.</summary>
    Warning,

    /// <summary>A toast that says something failed.</summary>
    Error,
}

/// <summary>The notification area.</summary>
public interface INotificationSurface
{
    /// <summary>What is on it now, oldest first.</summary>
    IReadOnlyList<SimulatedNotification> Notifications { get; }

    /// <summary>Shows a notification.</summary>
    void Show(NotificationLevel level, string title, string? body = null, string? replaceKey = null);
}

/// <summary>One thing on the notification area.</summary>
/// <param name="Level">How loudly it asked for attention.</param>
/// <param name="Title">Its heading.</param>
/// <param name="Body">Its text, if any.</param>
/// <param name="ReplaceKey">The key it replaces, so a later notification overwrites it in place.</param>
public sealed record SimulatedNotification(
    NotificationLevel Level,
    string Title,
    string? Body,
    string? ReplaceKey);

/// <summary>Variables, including the host's own.</summary>
public interface IVariableSurface
{
    /// <summary>Every variable and what it holds, sorted by name.</summary>
    IReadOnlyList<VariableEntry> Variables { get; }

    /// <summary>Reads a variable, or null when nothing has declared it.</summary>
    string? Get(string name);

    /// <summary>Writes a variable.</summary>
    void Set(string name, string value);

    /// <summary>Adds to a variable as a number, treating what is there as zero when it is not a number.</summary>
    void Change(string name, double amount);
}

/// <summary>One variable, and whether the plugin owns it or merely reads the host's.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Value">What it holds, as the interpreter's canonical text.</param>
/// <param name="HostShared">Whether the host owns it and the plugin only reads it.</param>
public sealed record VariableEntry(string Name, string Value, bool HostShared = false);

/// <summary>Lists.</summary>
public interface IListSurface
{
    /// <summary>Every list and its items, sorted by name.</summary>
    IReadOnlyList<ListEntry> Lists { get; }

    /// <summary>Reads a list, or an empty one when nothing has declared it.</summary>
    IReadOnlyList<string> Get(string name);

    /// <summary>Replaces a list's contents.</summary>
    void Set(string name, IReadOnlyList<string> items);

    /// <summary>Adds one item.</summary>
    void Add(string name, string item);

    /// <summary>Removes one item, and says whether it was there.</summary>
    bool Remove(string name, string item);
}

/// <summary>One list and its items.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ItemType">The type its items are declared as.</param>
/// <param name="Items">What it holds, in order.</param>
public sealed record ListEntry(string Name, string ItemType, IReadOnlyList<string> Items);

/// <summary>The script registry.</summary>
public interface IScriptSurface
{
    /// <summary>The scripts this document has, by id.</summary>
    IReadOnlyList<string> ScriptIds { get; }

    /// <summary>Registers a script.</summary>
    void Register(string scriptId, string name);

    /// <summary>Asks for a script to be started.</summary>
    /// <remarks>
    /// Queued rather than run: the interpreter is single-threaded and a script that starts another one has
    /// to wait for the current statement to finish, or the trace is two overlapping runs with no order.
    /// </remarks>
    void RequestStart(string scriptId);
}

/// <summary>The event bus.</summary>
public interface IEventSurface
{
    /// <summary>Publishes an event.</summary>
    void Publish(string eventId, string? payload = null);

    /// <summary>Subscribes a script to an event.</summary>
    void Subscribe(string scriptId, string eventId);
}

/// <summary>Messages: to the host's users, or to other plugins.</summary>
public interface IMessageSurface
{
    /// <summary>Shows a message to the pressing client, or to everyone when there is none.</summary>
    void Show(string title, string? body = null);
}

/// <summary>Modal dialogs, answered by whoever is driving the simulator.</summary>
public interface IDialogSurface
{
    /// <summary>Asks a question and returns what was answered.</summary>
    string Ask(string title, string? message = null);
}

/// <summary>One response the simulator will give without asking.</summary>
/// <param name="Url">The URL to match, or empty for any.</param>
/// <param name="Status">The status code to answer with.</param>
/// <param name="Body">The body to answer with.</param>
/// <param name="IsError">Whether the request should be reported as failed.</param>
public sealed record CannedResponse(string Url, int Status, string Body, bool IsError = false);

/// <summary>One request the simulator answered.</summary>
/// <param name="Method">The verb.</param>
/// <param name="Url">What was asked for.</param>
/// <param name="Status">The status code answered.</param>
/// <param name="Body">The body answered.</param>
/// <param name="Failed">Whether it was refused before it left the machine.</param>
/// <param name="Reason">Why it was refused, when it was.</param>
public sealed record HttpExchange(
    string Method,
    string Url,
    int Status,
    string Body,
    bool Failed = false,
    string? Reason = null);

/// <summary>HTTP, offline unless the user has said otherwise.</summary>
public interface IHttpSurface
{
    /// <summary>Every exchange so far, in order.</summary>
    IReadOnlyList<HttpExchange> Exchanges { get; }

    /// <summary>Whether requests may leave the machine.</summary>
    bool AllowRealNetwork { get; set; }

    /// <summary>The answers the simulator gives without asking.</summary>
    IReadOnlyList<CannedResponse> CannedResponses { get; }

    /// <summary>Adds or replaces a canned answer.</summary>
    void SetCannedResponse(CannedResponse response);

    /// <summary>Asks for a URL.</summary>
    string Get(string url);
}

/// <summary>Time, which the simulator advances by hand.</summary>
public interface IClock
{
    /// <summary>How far the clock has been advanced, in milliseconds.</summary>
    double ElapsedMilliseconds { get; }

    /// <summary>Advances it.</summary>
    void Advance(double milliseconds);
}

/// <summary>The log.</summary>
public interface ILogSurface
{
    /// <summary>Writes a line at a level.</summary>
    void Write(NotificationLevel level, string message);

    /// <summary>Everything written, in order.</summary>
    IReadOnlyList<string> Lines { get; }
}

/// <summary>The action's parameters, and the values this run was given.</summary>
public interface IParameterSurface
{
    /// <summary>The value of a parameter, or null when the run supplied none.</summary>
    string? Get(string name);

    /// <summary>Supplies a value for a parameter.</summary>
    void Set(string name, string? value);
}