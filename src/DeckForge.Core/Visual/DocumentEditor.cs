using System.Diagnostics;

namespace DeckForge.Core.Visual;

/// <summary>What an edit attempt did, or why it refused.</summary>
/// <param name="Applied">Whether the document changed.</param>
/// <param name="Problem">Why it did not, or null when it did.</param>
/// <param name="Code">The Appendix F diagnostic code, when a problem is reported.</param>
public readonly record struct DocumentEditResult(bool Applied, string? Problem = null, string? Code = null)
{
    /// <summary>An edit that went through.</summary>
    public static DocumentEditResult Ok { get; } = new(true);

    /// <summary>An edit that was refused and nothing changed.</summary>
    public static DocumentEditResult Refused(string problem, string code) => new(false, problem, code);
}

/// <summary>
/// The one way a visual document is edited, and the undo history behind it.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.5 and Appendix G: every mutation is a command with an inverse, one gesture is one
/// transaction, and a refused edit is not pushed onto the stack. The editor owns the document it is given
/// and is the only thing that calls <see cref="DocumentCommand.Apply"/>, so the history and the document
/// cannot drift apart — a command applied without the editor's knowledge is the one change undo cannot
/// get back from, and the only way to allow one is to let people reach past this class.
/// </para>
/// <para>
/// It holds no UI, no timers and no clock of its own: <see cref="Now"/> is a property so a test can drive
/// coalescing without waiting 800 milliseconds, and the real page assigns the system clock once. That is
/// the whole reason field typing can be tested at all — a coalescing window is a rule about time, and a
/// rule you cannot control time in is a rule you cannot test.
/// </para>
/// <para>
/// It is per document and is discarded when the workspace changes (Appendix G's rule 4), which is why it
/// is constructed with the document rather than being a singleton.
/// </para>
/// </remarks>
public sealed class DocumentEditor
{
    private readonly List<DocumentCommand> _undo = [];
    private readonly List<DocumentCommand> _redo = [];
    private readonly VisualProject _project;

    public DocumentEditor(VisualProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    /// <summary>The document this editor owns. Mutate it only through here.</summary>
    public VisualProject Project => _project;

    /// <summary>
    /// The clock coalescing is measured against. Assignable so a test does not have to sleep.
    /// </summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>What the next undo would revert, for the history menu and the tooltip.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    /// <summary>What the next redo would re-apply.</summary>
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>How many entries the history holds, for the diagnostics pane.</summary>
    public int Depth => _undo.Count;

    /// <summary>
    /// Raised after anything changes the document, so the canvas, the generated code and the diagnostics
    /// can rebuild without each of them watching.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Applies one command as one history entry, unless it coalesces with the last one.
    /// </summary>
    public DocumentEditResult Execute(DocumentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refusal = Refuse(command);
        if (refusal is { } problem)
        {
            return problem;
        }

        command.Apply(_project);

        var key = command.CoalesceKey;
        if (key is not null
            && _undo.Count > 0
            && string.Equals(_undo[^1].CoalesceKey, key, StringComparison.Ordinal)
            && (Now() - _lastAppliedAt).TotalMilliseconds <= _undo[^1].CoalesceWindowMs
            && _undo[^1].Merge(command) is { } merged)
        {
            // Fold it into the entry already on the stack: the user typed another character, which is not
            // a second thing they did. The merged command reverts to before the first keystroke and
            // re-applies the newest value, so undo stops where typing began and redo lands where it ended
            // - which is the pair a text field has to get right.
            _undo[^1] = merged;
        }
        else
        {
            _undo.Add(command);
        }

        _redo.Clear();
        _lastAppliedAt = Now();
        Changed?.Invoke();
        return DocumentEditResult.Ok;
    }

    /// <summary>
    /// Applies several commands as one entry: one gesture, one undo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied one at a time and checked as it goes, rather than all checked and then all applied. The
    /// difference matters for a gesture that builds something: "drop a loop with a statement in it" names
    /// a body that does not exist until the first command has run, so checking the whole transaction
    /// against the document as it was would refuse a gesture that is perfectly legal.
    /// </para>
    /// <para>
    /// If a command is refused halfway, the ones already applied are reverted before returning. A
    /// transaction that half-applied would put the document in a state the user never asked for and that
    /// no single undo describes.
    /// </para>
    /// </remarks>
    public DocumentEditResult Transaction(string label, IReadOnlyList<DocumentCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var applied = new List<DocumentCommand>();

        foreach (var command in commands)
        {
            if (Refuse(command) is { } refusal)
            {
                for (var i = applied.Count - 1; i >= 0; i--)
                {
                    applied[i].Revert(_project);
                }

                return refusal;
            }

            try
            {
                command.Apply(_project);
                applied.Add(command);
            }
            catch (InvalidOperationException ex)
            {
                for (var i = applied.Count - 1; i >= 0; i--)
                {
                    applied[i].Revert(_project);
                }

                return DocumentEditResult.Refused(ex.Message, "vis-drop-missing-target");
            }
        }

        if (applied.Count > 0)
        {
            _undo.Add(new DocumentTransaction(label, applied));
            _redo.Clear();
            _lastAppliedAt = Now();
            Changed?.Invoke();
        }

        return DocumentEditResult.Ok;
    }

    /// <summary>Reverts the last entry, or does nothing when there is none.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Revert(_project);
        _redo.Add(command);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Re-applies the last undone entry, or does nothing when there is none.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Apply(_project);
        _undo.Add(command);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Throws the history away, which is what a workspace change does.</summary>
    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    // ---- the edit operations the gestures call -----------------------------------------------------

    /// <summary>Puts a block or a run of them into a body at an index.</summary>
    public DocumentEditResult Insert(BodyRef where, int index, IReadOnlyList<Block> run, string? label = null) =>
        Execute(new InsertRun(where, index, run, label ?? $"Insert {Describe(run)}"));

    /// <summary>Removes the run starting at a block: everything from it down, as a drag carries.</summary>
    public DocumentEditResult Delete(BodyRef where, Block first, string? label = null)
    {
        if (!Resolves(where))
        {
            return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-source");
        }

        var run = RunFrom(where, first);
        return run.Count == 0
            ? DocumentEditResult.Refused("There is nothing there to delete.", "vis-drop-missing-source")
            : Execute(new DeleteRun(where, run, label ?? $"Delete {Describe(run)}"));
    }

    /// <summary>Moves a run into another body, or to another index in the same one.</summary>
    public DocumentEditResult Move(BodyRef from, Block first, BodyRef to, int toIndex)
    {
        if (!Resolves(from))
        {
            return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-source");
        }

        var run = RunFrom(from, first);
        if (run.Count == 0)
        {
            return DocumentEditResult.Refused("There is nothing there to move.", "vis-drop-missing-source");
        }

        return Execute(new MoveRun(from, to, run, toIndex, $"Move {Describe(run)}"));
    }

    /// <summary>Puts a run inside a new container, in the container's place.</summary>
    public DocumentEditResult Wrap(Block first, Block wrapper, int count = 1)
    {
        var where = Locate(first);
        return where is null
            ? DocumentEditResult.Refused("That block is not in a stack.", "vis-drop-missing-source")
            : Execute(new WrapRun(where.Value, first, wrapper, count, $"Wrap in {Describe([wrapper])}"));
    }

    /// <summary>Splices a container's body into its parent stack and removes the container.</summary>
    public DocumentEditResult Unwrap(Block container)
    {
        if (BlockCatalog.Find(container.Kind) is not { IsContainer: true })
        {
            return DocumentEditResult.Refused("That block has nothing inside it.", "vis-unwrap-not-a-container");
        }

        var where = Locate(container);
        return where is null
            ? DocumentEditResult.Refused("That block is not in a stack.", "vis-unwrap-not-in-a-stack")
            : Execute(new UnwrapRun(where.Value, container, $"Unwrap {Describe([container])}"));
    }

    /// <summary>Switches a block off, or back on.</summary>
    public DocumentEditResult ToggleDisable(Block block) =>
        Execute(new ToggleDisable(block.Id, !block.Disabled));

    /// <summary>Sets a field or a menu's option.</summary>
    public DocumentEditResult EditField(Block block, string name, string? after, bool isMenu = false) =>
        Execute(new EditField(block.Id, name, block.Field(name), after, isMenu));

    /// <summary>Puts a value in a slot, or clears it when <paramref name="after"/> is null.</summary>
    public DocumentEditResult BindSlot(Block block, string slot, BlockInput? after) =>
        Execute(new BindSlot(
            block.Id,
            slot,
            block.Inputs.TryGetValue(slot, out var before) ? before : null,
            after));

    /// <summary>Adds a script to a target.</summary>
    public DocumentEditResult AddScript(VisualTarget target, VisualScript script, int index = -1) =>
        Execute(new AddScript(target, script, index));

    /// <summary>Removes a script.</summary>
    public DocumentEditResult DeleteScript(VisualTarget target, VisualScript script) =>
        Execute(new DeleteScript(target, script));

    /// <summary>Moves a script on the canvas, which is Part 6.1's stored position.</summary>
    public DocumentEditResult MoveScript(VisualScript script, double x, double y) =>
        Execute(new MoveScript(script, x, y));

    // ---- looking things up -------------------------------------------------------------------------

    /// <summary>
    /// The run a block drags: it and everything below it in the same body.
    /// </summary>
    /// <remarks>
    /// Part 9.5's first gesture rule, and the reason it is not "just this block": dropping the middle of a
    /// stack and leaving the top behind orphans it. Alt passes <paramref name="wholeStack"/> false to take
    /// the single block instead.
    /// </remarks>
    public IReadOnlyList<Block> RunFrom(BodyRef where, Block first, bool wholeStack = true)
    {
        var body = DocumentLists.ListFor(_project, where);
        var at = body.FindIndex(block => string.Equals(block.Id, first.Id, StringComparison.Ordinal));
        if (at < 0)
        {
            return [];
        }

        return wholeStack ? body.Skip(at).ToList() : [body[at]];
    }

    /// <summary>Which body a block is in, or null when it is not in a stack at all.</summary>
    public BodyRef? Locate(Block block)
    {
        foreach (var target in _project.Targets)
        {
            foreach (var script in target.Scripts)
            {
                if (script.Hat.Id == block.Id || script.Body.Contains(block))
                {
                    return BodyRef.ScriptBody(script.Hat.Id);
                }

                if (LocatedInNested(script.Body, block) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private BodyRef? LocatedInNested(List<Block> body, Block block)
    {
        foreach (var statement in body)
        {
            foreach (var (name, inner) in statement.Bodies)
            {
                if (inner.Contains(block))
                {
                    return new BodyRef(statement.Id, name);
                }

                if (LocatedInNested(inner, block) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>The block immediately above one in the same body, or null.</summary>
    public Block? Above(Block block)
    {
        var where = Locate(block);
        if (where is null)
        {
            return null;
        }

        var body = DocumentLists.ListFor(_project, where.Value);
        var at = body.FindIndex(candidate => candidate.Id == block.Id);
        return at > 0 ? body[at - 1] : null;
    }

    /// <summary>The body a block would land in if it were dropped straight below, or null.</summary>
    public BodyRef? BodyBelow(Block block)
    {
        var where = Locate(block);
        if (where is null)
        {
            return null;
        }

        var body = DocumentLists.ListFor(_project, where.Value);
        var at = body.FindIndex(candidate => candidate.Id == block.Id);
        return at < body.Count - 1 && BlockCatalog.Find(body[at + 1].Kind) is { IsContainer: true } next
            ? new BodyRef(body[at + 1].Id, next.Bodies![0].Name)
            : null;
    }

    // ---- refusing ----------------------------------------------------------------------------------

    /// <summary>
    /// The check every command passes before it is applied, returning null when the edit is legal.
    /// </summary>
    /// <remarks>
    /// Appendix G's third rule. Two things are refused: a move that would put a run inside itself, and a
    /// reference to a block or body that does not exist. Both would otherwise fail deep inside a command
    /// with an exception the user cannot act on, or — worse — succeed against the wrong list.
    /// </remarks>
    private DocumentEditResult? Refuse(DocumentCommand command)
    {
        switch (command)
        {
            case MoveRun move:
                if (move.Run.Count == 0)
                {
                    return DocumentEditResult.Refused("There is nothing there to move.", "vis-drop-missing-source");
                }

                if (!Resolves(move.From))
                {
                    return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-source");
                }

                if (move.From == move.To)
                {
                    return null;
                }

                var inside = DocumentLists.IdsOf(move.Run);
                if (inside.Contains(move.To.OwnerId))
                {
                    return DocumentEditResult.Refused(
                        "A block cannot be dropped inside itself.",
                        "vis-move-into-self");
                }

                if (!Resolves(move.To))
                {
                    return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-target");
                }

                break;

            case WrapRun wrap:
                if (BlockCatalog.Find(wrap.Wrapper.Kind) is not { IsContainer: true })
                {
                    return DocumentEditResult.Refused("That block cannot wrap anything.", "vis-wrap-not-a-container");
                }

                if (!Resolves(wrap.Where))
                {
                    return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-source");
                }

                break;

            case UnwrapRun unwrap:
                if (unwrap.Container.Bodies.Values.All(body => body.Count == 0))
                {
                    return DocumentEditResult.Refused("There is nothing inside it to unwrap.", "vis-unwrap-empty");
                }

                break;

            case InsertRun insert:
                if (!Resolves(insert.Where))
                {
                    return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-target");
                }

                break;

            case DeleteRun delete:
                if (!Resolves(delete.Where))
                {
                    return DocumentEditResult.Refused("That place no longer exists.", "vis-drop-missing-target");
                }

                break;

            case MoveScript moveScript:
                if (!double.IsFinite(moveScript.X) || !double.IsFinite(moveScript.Y))
                {
                    return DocumentEditResult.Refused("That is not a place on the canvas.", "vis-move-off-canvas");
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Whether a body reference names somewhere a command can actually put things.
    /// </summary>
    /// <remarks>
    /// A container's bodies count even before anything has been put in them, because the catalogue says
    /// they exist and <see cref="Block.Body"/> creates them on demand. Checking the live dictionary
    /// instead would refuse "drop a loop with a statement in it" — a gesture whose second half names a
    /// body that the first half has not emptied into yet.
    /// </remarks>
    private bool Resolves(BodyRef where)
    {
        if (_project.Targets
            .SelectMany(target => target.Scripts)
            .Any(candidate => candidate.Hat.Id == where.OwnerId))
        {
            return true;
        }

        return DocumentLists.Find(_project, where.OwnerId) is { } owner
            && (where.IsScriptBody || owner.Bodies.ContainsKey(where.BodyName)
                || BlockCatalog.Find(owner.Kind)?.Bodies?.Any(body => body.Name == where.BodyName) == true);
    }

    private static string Describe(IReadOnlyList<Block> run) => run.Count switch
    {
        0 => "block",
        1 => BlockCatalog.Find(run[0].Kind)?.Label.Replace('{', ' ').Replace('}', ' ').Trim() ?? "block",
        _ => $"{run.Count} blocks",
    };

    private DateTimeOffset _lastAppliedAt = DateTimeOffset.MinValue;
}