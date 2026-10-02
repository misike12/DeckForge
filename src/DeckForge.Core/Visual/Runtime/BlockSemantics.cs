using System.Globalization;

namespace DeckForge.Core.Visual.Runtime;

/// <summary>What one block does, stated once and asserted by both engines.</summary>
/// <param name="Kind">The catalogue kind.</param>
/// <param name="Shape">Whether it is a statement, a reporter, a hat or a container.</param>
/// <param name="HostSurface">
/// The host surface it calls, or null when it only touches the document.
/// </param>
/// <param name="HostMember">The member it calls on that surface.</param>
/// <param name="Waits">Whether it waits, and so moves the simulated clock.</param>
/// <param name="NameSlot">The slot that names a thing rather than reading one, where there is one.</param>
/// <param name="Notes">
/// The one thing about this block that is easy to get subtly wrong, quoted so a change has to argue with
/// it.
/// </param>
/// <remarks>
/// <paramref name="NameSlot"/> exists because Scratch's variable drop is ambiguous and the block's own
/// meaning is what settles it: in `get {var}` the slot holds a name and the block reads that name's value,
/// while in `set {var} to {value}` the very same slot holds a name and the block writes to it. Both slots
/// are the same kind of input, so nothing else in the document can tell them apart.
/// </remarks>
public sealed record BlockSemantic(
    string Kind,
    string Shape,
    string? HostSurface = null,
    string? HostMember = null,
    bool Waits = false,
    string? NameSlot = null,
    string? Notes = null);

/// <summary>
/// The table Part 10.5 calls for: what each interpreted block means, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Two engines read a document — the compiler that writes C#, and the interpreter that walks it in the
/// window — and they were written years apart in intent even if not in time. The parity between them cannot
/// be asserted from the C#, because the C# is a string; it has to be asserted from this table. Every entry
/// is checked by the interpreter's own tests, and the emitter's tests check that the generated expression is
/// the one this table names.
/// </para>
/// <para>
/// A block that is not in the table has no defined meaning here, and the interpreter says so in the trace
/// rather than pretending to run it. That is the honest state: a preview that quietly skips the blocks it
/// does not know produces a trace that looks complete and is not.
/// </para>
/// </remarks>
public static class BlockSemantics
{
    private static readonly Dictionary<string, BlockSemantic> Table = new(StringComparer.Ordinal)
    {
        // Control: waiting is the only thing these do that the host can see.
        ["control.wait-seconds"] = Semantics("control.wait-seconds", "stack", waits: true,
            notes: "seconds * 1000, and the clock is advanced by exactly that much"),
        ["control.wait-ms"] = Semantics("control.wait-ms", "stack", waits: true,
            notes: "the milliseconds as written"),
        ["control.forever"] = Semantics("control.forever", "container", waits: true,
            notes: "unbounded by design; the interpreter stops it on a step budget or a breakpoint"),
        ["control.repeat"] = Semantics("control.repeat", "container", notes: "runs its body count times"),
        ["control.repeat-until"] = Semantics("control.repeat-until", "container", waits: true,
            notes: "tests the condition *after* the body, once per pass, which is Scratch's order and not C#'s"),
        ["control.if"] = Semantics("control.if", "container", notes: "runs its body when the condition holds"),
        ["control.if-else"] = Semantics("control.if-else", "container",
            notes: "one of the two bodies, and the condition is tested once"),
        ["control.if-else-if"] = Semantics("control.if-else-if", "container",
            notes: "the conditions are tested in order and the first true one wins, Scratch's order and not C#'s"),
        ["control.while"] = Semantics("control.while", "container", waits: true,
            notes: "the condition is tested before every pass, so an empty body is not an infinite loop"),
        ["http.retry"] = Semantics("http.retry", "container",
            notes: "its body is retried while the attempt count is below the limit, which is what the "
                + "budget is for when the condition never turns false"),
        ["control.stop-script"] = Semantics("control.stop-script", "stack", notes: "ends the script from here"),

        // Variables: the document's own state, which the watch table reads.
        ["var.set"] = Semantics("var.set", "stack", "variables", "set", nameSlot: "var",
            notes: "the `var` slot names the variable and is not read as its value; the value keeps its type"),
        ["var.change"] = Semantics("var.change", "stack", "variables", "change", nameSlot: "var",
            notes: "an unset or non-numeric variable changes from zero, not from an error"),
        ["var.watch"] = Semantics("var.watch", "stack", nameSlot: "var",
            notes: "adds the name to the watch table if it is not already there"),
        ["var.unwatch"] = Semantics("var.unwatch", "stack", nameSlot: "var",
            notes: "removes it, and says so when it was not there"),

        // Logging: what the trace shows and what the host would show.
        ["ui.log"] = Semantics("ui.log", "stack", "log", "write",
            notes: "the template's {holes} come from the action's parameters, and a hole with no value behind it is left written"),

        // Lists, events and messages: what the stage's watch table and log exist to show.
        ["list.define"] = Semantics("list.define", "stack", "lists", "set", nameSlot: "name",
            notes: "declaring a list the run has already written to must not empty it"),
        ["list.add"] = Semantics("list.add", "stack", "lists", "add", nameSlot: "list",
            notes: "appending to a list nothing has declared is how a script's first write happens"),
        ["list.clear"] = Semantics("list.clear", "stack", "lists", "set", nameSlot: "list",
            notes: "empties the list it names, and the index a `delete` would have used no longer exists"),

        ["events.publish"] = Semantics("events.publish", "stack", "events", "publish",
            notes: "publishing to nobody is not an error; a simulator has no subscribers to complain about"),
        ["events.publish-with-payload"] = Semantics("events.publish-with-payload", "stack", "events", "publish",
            notes: "the payload is one value's text, not a second event"),
        ["events.send-message"] = Semantics("events.send-message", "stack", "messages", "show",
            notes: "the topic is the title, and the body is empty rather than null when the slot is"),

        // Notifications, and the replace-by-key that makes a progress toast sit still.
        ["ui.notify"] = Semantics("ui.notify", "stack", "notifications", "show",
            notes: "an absent message is null, not an empty string, because the host shows the two differently"),
        ["ui.notify-level"] = Semantics("ui.notify-level", "stack", "notifications", "show",
            notes: "the level comes from a menu key, not from a slot, so a malformed menu reads as "
                + "information rather than throwing"),
        ["ui.notify-key"] = Semantics("ui.notify-key", "stack", "notifications", "show",
            notes: "the key replaces the previous notification carrying it, in place"),
        ["ui.clear-notification"] = Semantics("ui.clear-notification", "stack", "notifications", "clear",
            notes: "clearing removes the card carrying the key rather than blanking it, because a blank "
                + "card still occupies the notification area"),

        // The deck, as a folder graph rather than a device.
        ["deck.open-folder"] = Semantics("deck.open-folder", "stack", "deck", "openFolder",
            notes: "a folder the simulator has never heard of is still shown as the call that happened, "
                + "because a dry run that stops there teaches nothing about the rest of the script"),
        ["deck.open-folder-on-client"] = Semantics("deck.open-folder-on-client", "stack", "deck", "openFolder",
            notes: "a named client rather than the pressing one"),
        ["deck.go-to-parent"] = Semantics("deck.go-to-parent", "stack", "deck", "goToParent",
            notes: "at the root there is nowhere to go, and the run carries on rather than ending"),
        ["deck.go-back"] = Semantics("deck.go-back", "stack", "deck", "goBack",
            notes: "back at the root is not an error; there is simply nowhere further to go"),
        ["deck.set-button-state"] = Semantics("deck.set-button-state", "stack", "deck", "setButtonState",
            notes: "the state name is written onto the tile, so a stage shows which widget the run turned on"),

        // Operators: the two the preview needs for its own examples, chosen because both are
        // nondeterministic in a way a dry run has to be able to reproduce.
        ["ops.random"] = Semantics("ops.random", "reporter",
            notes: "inclusive of the high end, and seeded, so the same seed gives the same trace"),
        ["ops.random-item"] = Semantics("ops.random-item", "reporter", "lists", "item", nameSlot: "list",
            notes: "an empty list yields empty text rather than throwing, because a list the user has "
                + "not filled yet is not a mistake"),
    };

    /// <summary>The table, by kind.</summary>
    public static IReadOnlyDictionary<string, BlockSemantic> All => Table;

    /// <summary>What a kind means, or null when the table does not define it.</summary>
    public static BlockSemantic? For(string kind) => Table.TryGetValue(kind, out var semantics) ? semantics : null;

    /// <summary>Whether the interpreter has a meaning for this block.</summary>
    public static bool Knows(string kind) => Table.ContainsKey(kind);

    private static BlockSemantic Semantics(
        string kind,
        string shape,
        string? surface = null,
        string? member = null,
        bool waits = false,
        string? nameSlot = null,
        string? notes = null) =>
        new(kind, shape, surface, member, waits, nameSlot, notes);

    /// <summary>
    /// A sentence about what is not here, for the trace when a block has no defined meaning.
    /// </summary>
    /// <remarks>
    /// Written to name the block and what to do, because a trace that says "not implemented" and stops
    /// leaves the user reading a step list wondering whether the run finished.
    /// </remarks>
    public static string NotInterpreted(string kind, string label) =>
        $"\"{label}\" is not interpreted yet, so this preview stops here. The block is in the document and "
        + "the generated plugin will run it; only the simulator does not know what it means.";
}