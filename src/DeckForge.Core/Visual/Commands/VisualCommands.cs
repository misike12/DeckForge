namespace DeckForge.Core.Visual.Commands;

/// <summary>Where a command can be pressed.</summary>
/// <remarks>
/// A scope rather than a page, because the interesting conflicts are not between pages: a canvas key that
/// fires while a text field has focus is a worse bug than two pages disagreeing about a gesture, and the
/// rule that prevents it is "a focused field wins", which only works if the palette knows a field has
/// focus.
/// </remarks>
public enum CommandScope
{
    /// <summary>The block palette's search box.</summary>
    Palette,

    /// <summary>The canvas.</summary>
    Canvas,

    /// <summary>The stage: the simulated host and its transport.</summary>
    Stage,

    /// <summary>Anywhere in the application, including a text field.</summary>
    Application,
}

/// <summary>
/// One command: what it is called, what presses it, and where it belongs.
/// </summary>
/// <remarks>
/// A record rather than a delegate, because the table is the *document*. It is the palette's list, the
/// shortcut sheet's rows, the keys the page dispatches, and the test that says two commands cannot share
/// a gesture — all one source, so a sheet that lists a shortcut nothing does is impossible rather than
/// merely unlikely.
/// </remarks>
/// <param name="Id">Stable identifier, used by the dispatcher and by tests.</param>
/// <param name="Title">What the palette and the sheet call it.</param>
/// <param name="Gesture">The keys, written the way a user would say them: "Ctrl+K", "F10".</param>
/// <param name="Scope">Where it belongs.</param>
/// <param name="Keywords">Extra words the palette searches, so "zoom out" finds "Zoom out".</param>
/// <param name="NeedsSelection">Whether it is refused with nothing selected.</param>
public sealed record VisualCommand(
    string Id,
    string Title,
    string Gesture,
    CommandScope Scope,
    IReadOnlyList<string>? Keywords = null,
    bool NeedsSelection = false)
{
    /// <summary>The lower-cased words the palette matches against.</summary>
    public IReadOnlyList<string> SearchTerms =>
        [Title.ToLowerInvariant(), .. (Keywords ?? []).Select(keyword => keyword.ToLowerInvariant())];
}

/// <summary>
/// Every command the Visual page offers, in the order the palette and the sheet show them.
/// </summary>
/// <remarks>
/// Ordered by scope and then by title rather than by declaration, so the sheet reads as a document a
/// person can scan: palette, canvas, stage, application. The tests pin the parts of that which are easy
/// to break silently — no gesture in two places, every gesture spelled consistently, every stage gesture
/// naming a command the stage can actually perform.
/// </remarks>
public static class VisualCommands
{
    /// <summary>Add a block from the palette to the canvas.</summary>
    public const string AddBlock = "palette.add-block";

    /// <summary>Focus the palette's search box.</summary>
    public const string FocusPaletteSearch = "palette.focus-search";

    /// <summary>Show the shortcut sheet.</summary>
    public const string ShowShortcuts = "application.shortcuts";

    /// <summary>Open the documentation.</summary>
    public const string OpenDocs = "application.docs";

    /// <summary>Save into the plugin.</summary>
    public const string Save = "application.save";

    /// <summary>Undo.</summary>
    public const string Undo = "canvas.undo";

    /// <summary>Redo.</summary>
    public const string Redo = "canvas.redo";

    /// <summary>Duplicate the selected run.</summary>
    public const string Duplicate = "canvas.duplicate";

    /// <summary>Copy the selected run to the clipboard.</summary>
    public const string Copy = "canvas.copy";

    /// <summary>Copy the selected run and delete it.</summary>
    public const string Cut = "canvas.cut";

    /// <summary>Drop whatever the clipboard holds as a run.</summary>
    public const string Paste = "canvas.paste";

    /// <summary>Write the selected run to a <c>.dfblock</c> file.</summary>
    public const string ExportBlock = "canvas.export-block";

    /// <summary>Read a run from a <c>.dfblock</c> file.</summary>
    public const string ImportBlock = "canvas.import-block";

    /// <summary>Delete the selected run.</summary>
    public const string Delete = "canvas.delete";

    /// <summary>Pick the selected run up, or drop the one in hand.</summary>
    public const string PickUpOrDrop = "canvas.carry";

    /// <summary>Cancel whatever is in progress.</summary>
    public const string Cancel = "canvas.cancel";

    /// <summary>Open the command palette.</summary>
    public const string OpenPalette = "canvas.command-palette";

    /// <summary>
    /// Select every block on the canvas, and clear the selection again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added with the rubber band, and for the same reason the band needed a reason. Part 18.1 gives a
    /// pointer gesture for selecting several blocks and nothing at all in §18.3 for the keyboard, and a
    /// feature that only the mouse can reach is a defect this design calls out by name (§17.2 item 6, "every
    /// operation, including structural moves, is reachable without a pointer").
    /// </para>
    /// <para>
    /// <c>Ctrl+A</c> rather than something invented, because it is the chord every other editor on the
    /// machine already means by this, and <see cref="Cancel"/> (<c>Esc</c>) already clears the selection —
    /// so the pair is <c>Ctrl+A</c> then <c>Esc</c>, which is the same two gestures a rubber band and a
    /// click on empty canvas are.
    /// </para>
    /// </remarks>
    public const string SelectAll = "canvas.select-all";

    /// <summary>Run the selected script in the stage.</summary>
    public const string StageRun = "stage.run";

    /// <summary>Step over one block.</summary>
    public const string StageStep = "stage.step";

    /// <summary>Step into one block.</summary>
    public const string StageStepInto = "stage.step-into";

    /// <summary>
    /// Suspend a running script at the next block boundary, and resume one that is suspended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One command rather than two, because they are one button. A debugger's transport shows Pause while
    /// a run is going and Continue the moment it is not, and two commands with two gestures would have to
    /// claim two chords for one state — and the table's own test forbids two commands sharing a gesture in
    /// a scope, which is the right rule and would make this impossible to do honestly.
    /// </para>
    /// <para>
    /// <c>F6</c> because the function keys around it are the transport: F5 runs, F9 sets a breakpoint, F10
    /// steps, F11 steps in, Shift+F5 stops. F6 is the free key between Run and Breakpoint, and a chord
    /// nobody has to be told is worth more here than a mnemonic — the sheet and the menu both print the
    /// gesture, so the name has to agree with the key rather than the other way round.
    /// </para>
    /// </remarks>
    public const string StagePause = "stage.pause";

    /// <summary>Stop the run.</summary>
    public const string StageStop = "stage.stop";

    /// <summary>Reset the stage.</summary>
    public const string StageReset = "stage.reset";

    /// <summary>Toggle a breakpoint on the focused block.</summary>
    public const string ToggleBreakpoint = "stage.toggle-breakpoint";

    /// <summary>Every command, grouped and ordered.</summary>
    public static IReadOnlyList<VisualCommand> All { get; } =
    [
        new(AddBlock, "Add block", "Ctrl+K then a name", CommandScope.Palette,
            ["insert", "drop", "new block"]),
        new(FocusPaletteSearch, "Search blocks", "Ctrl+F", CommandScope.Palette, ["find", "filter"]),

        new(OpenPalette, "Command palette", "Ctrl+K", CommandScope.Canvas, ["commands", "actions"]),
        new(Undo, "Undo", "Ctrl+Z", CommandScope.Canvas, ["revert", "back"]),
        new(Redo, "Redo", "Ctrl+Y", CommandScope.Canvas, ["again", "forward"]),
        new(Duplicate, "Duplicate block", "Ctrl+D", CommandScope.Canvas, ["copy", "clone"], NeedsSelection: true),
        new(Copy, "Copy block", "Ctrl+C", CommandScope.Canvas, ["clipboard", "take"], NeedsSelection: true),
        new(Cut, "Cut block", "Ctrl+X", CommandScope.Canvas, ["clipboard", "remove"], NeedsSelection: true),
        new(Paste, "Paste block", "Ctrl+V", CommandScope.Canvas, ["clipboard", "insert"]),
        new(ExportBlock, "Export block as .dfblock", "Ctrl+Shift+C", CommandScope.Canvas, ["save", "file"]),
        new(ImportBlock, "Import a .dfblock", "Ctrl+Shift+V", CommandScope.Canvas, ["open", "file"]),
        new(Delete, "Delete block", "Delete", CommandScope.Canvas, ["remove", "erase"], NeedsSelection: true),
        new(PickUpOrDrop, "Pick up or drop", "Space", CommandScope.Canvas, ["move", "carry", "drag"]),
        new(Cancel, "Cancel", "Esc", CommandScope.Canvas, ["escape", "stop", "clear"]),
        new(SelectAll, "Select every block", "Ctrl+A", CommandScope.Canvas, ["all", "everything", "rubber band", "marquee"]),

        new(StageRun, "Run", "F5", CommandScope.Stage, ["play", "start", "go"]),
        new(StageStep, "Step", "F10", CommandScope.Stage, ["over", "one block"]),
        new(StageStepInto, "Step into", "F11", CommandScope.Stage, ["inside", "one block"]),
        new(StagePause, "Pause or resume", "F6", CommandScope.Stage,
            ["pause", "resume", "continue", "suspend", "hold", "break"]),
        new(ToggleBreakpoint, "Toggle breakpoint", "F9", CommandScope.Stage, ["break", "pause", "stop here"],
            // Marked here and not when it was written. A breakpoint belongs to a block, and the page's F9
            // handler already declines without one - so "refused with nothing selected" was always true and
            // the table just said otherwise. Nothing read the flag until Part 18.5's stage menu arrived and
            // wanted to grey the row out rather than offer a menu item that does nothing.
            NeedsSelection: true),
        new(StageStop, "Stop", "Shift+F5", CommandScope.Stage, ["halt", "end"]),
        new(StageReset, "Reset stage", "", CommandScope.Stage, ["clear", "start over"]),

        new(ShowShortcuts, "Keyboard shortcuts", "Ctrl+/", CommandScope.Application, ["keys", "sheet", "cheatsheet"]),
        new(Save, "Save", "Ctrl+S", CommandScope.Application, ["write", "persist"]),
        new(OpenDocs, "Documentation", "F1", CommandScope.Application, ["help", "manual"]),
    ];

    /// <summary>The command with that id, or null.</summary>
    public static VisualCommand? Find(string id) =>
        All.FirstOrDefault(command => string.Equals(command.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// The commands that belong to one place, in table order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added for Part 18.5's three context menus, which are the third reader of this table beside the
    /// palette and the shortcut sheet. They were going to be written out as literal menu items, and a menu
    /// written out is a menu that drifts: it would show "Undo Ctrl+Z" beside a key handler that had since
    /// been rebound, and nothing in the build or the suite would notice — which is the exact defect
    /// <c>VisualCommandsTests</c> exists to prevent for the sheet.
    /// </para>
    /// <para>
    /// A filter rather than a second list on purpose. It means a menu can only ever offer commands that are
    /// really in the table, and it means a new command appears in the menu that lists its scope without
    /// anybody remembering to add it — the same property the sheet has.
    /// </para>
    /// </remarks>
    /// <param name="scope">Where the menu was opened.</param>
    public static IReadOnlyList<VisualCommand> InScope(CommandScope scope) =>
        [.. All.Where(command => command.Scope == scope)];

    /// <summary>
    /// The command a gesture names, or null — and null is a legitimate answer.
    /// </summary>
    /// <remarks>
    /// Matching is on the gesture's *prefix* rather than the whole string, because two of them are
    /// "Ctrl+K then a name": a chord and a chord-then-text cannot both be one key, and a palette that
    /// insists on an exact match would not find its own entry.
    /// </remarks>
    /// <param name="gesture">What the key handler saw, such as "Ctrl+K".</param>
    /// <param name="scope">Where it was pressed, so two scopes may use the same keys.</param>
    public static VisualCommand? ForGesture(string gesture, CommandScope scope) =>
        All.FirstOrDefault(command =>
            command.Scope == scope
            && command.Gesture.Length > 0
            && (string.Equals(command.Gesture, gesture, StringComparison.OrdinalIgnoreCase)
                || gesture.StartsWith(command.Gesture, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The commands a palette search matches, best first.
    /// </summary>
    /// <remarks>
    /// Ranked rather than filtered, because a palette that lists forty commands and makes the user read
    /// is a file dialog. A title that *starts* with the query beats one that merely contains it, which
    /// beats a keyword match — so typing "step" puts "Step into" above "Toggle breakpoint".
    /// </remarks>
    /// <param name="query">What the user typed. Empty returns everything, in table order.</param>
    public static IReadOnlyList<VisualCommand> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        // Lower-cased here rather than at the comparison, because the comparison is Ordinal - and a
        // palette that finds nothing when the user typed a capital letter is a palette that silently
        // returns every command instead of the one they asked for.
        var terms = query
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.ToLowerInvariant())
            .ToList();

        return All
            .Select(command => (Command: command, Rank: Rank(command, terms)))
            .Where(pair => pair.Rank > 0)
            .OrderBy(pair => pair.Rank)
            .ThenBy(pair => pair.Command.Title, StringComparer.Ordinal)
            .Select(pair => pair.Command)
            .ToList();
    }

    /// <summary>How well a command matches a query; zero means not at all.</summary>
    private static int Rank(VisualCommand command, IReadOnlyList<string> terms)
    {
        var best = int.MaxValue;

        foreach (var term in terms)
        {
            var rank = command.SearchTerms
                .Select(word => word.StartsWith(term, StringComparison.Ordinal) ? 1 : word.Contains(term, StringComparison.Ordinal) ? 2 : 0)
                .Where(score => score > 0)
                .DefaultIfEmpty(0)
                .Min();

            if (rank == 0)
            {
                // Every term has to match something: a palette that ORs its words offers "add save" when
                // the user typed two unrelated words and hands them a command neither word named.
                return 0;
            }

            best = Math.Min(best, rank);
        }

        return best;
    }
}