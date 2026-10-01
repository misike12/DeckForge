namespace DeckForge.Core.Visual;

/// <summary>
/// Where a run of statements lives: a script's own body, or a named body inside some block.
/// </summary>
/// <param name="OwnerId">
/// The hat block's id for a script body, or the containing block's id for a wrapped one.
/// </param>
/// <param name="BodyName">
/// The body's key in the owner's <c>bodies</c>, or empty for a script body.
/// </param>
/// <remarks>
/// <para>
/// A pair of names rather than a path of indices, because a path goes stale the moment anything above it
/// moves — and undo replays those moves in reverse. Block ids are stable for the life of the document
/// (Part 6.2), which is exactly the lifetime an undo entry lives for.
/// </para>
/// <para>
/// A hat's id addresses its script body, rather than the script's own id, so one rule resolves both cases
/// and there is no second thing to keep in step.
/// </para>
/// </remarks>
public readonly record struct BodyRef(string OwnerId, string BodyName = "")
{
    /// <summary>The body a script's statements live in.</summary>
    public static BodyRef ScriptBody(string hatId) => new(hatId);

    /// <summary>Whether this addresses a script rather than a wrapped body.</summary>
    public bool IsScriptBody => string.IsNullOrEmpty(BodyName);

    public override string ToString() => IsScriptBody ? $"script {OwnerId}" : $"{OwnerId}.{BodyName}";
}

/// <summary>
/// One undoable change to a document.
/// </summary>
/// <remarks>
/// <para>
/// The inverse is part of the command rather than a snapshot of the document, which is Appendix G's rule
/// and the reason it matters: undoing an edit that moved two hundred blocks costs two list operations
/// rather than replacing the document, and a redo restores exactly what the inverse removed instead of
/// whatever the document happened to look like.
/// </para>
/// <para>
/// <para>
/// Commands that move things <b>remember the position they acted at</b>, rather than being handed one
/// again when they are reverted. That is not an optimisation, it is the whole mechanism: an undo stack is
/// strictly last-in-first-out, so by the time a command is reverted every command applied after it has
/// already been undone, and the document is in exactly the shape it was in the moment the command ran. A
/// remembered position is therefore correct; a recomputed one is a guess that happens to be right until
/// anything else touches the same body.
/// </para>
/// </para>
/// <para>
/// Nothing outside <see cref="DocumentEditor"/> should call <see cref="Apply"/> or
/// <see cref="Revert"/>: a command applied outside the editor changes the document with nothing on the
/// stack, which is the one state undo cannot get back from.
/// </para>
/// </remarks>
public abstract class DocumentCommand
{
    /// <summary>What the history shows this entry as.</summary>
    public abstract string Label { get; }

    /// <summary>Makes the change, remembering anything its inverse will need.</summary>
    public abstract void Apply(VisualProject project);

    /// <summary>Puts it back exactly as it was.</summary>
    public abstract void Revert(VisualProject project);

    /// <summary>
    /// Two commands with the same non-null key merge when they run close together, or null for never.
    /// </summary>
    /// <remarks>
    /// Only field typing merges (Part 9.5's 800ms per field). Everything else is its own entry: a drag
    /// is one gesture and must be one <c>Ctrl+Z</c>, and a merge key on anything else would fold two
    /// deliberate actions into one undo.
    /// </remarks>
    public virtual string? CoalesceKey => null;

    /// <summary>How close together two commands with the same key must be to merge, in milliseconds.</summary>
    public virtual int CoalesceWindowMs => 0;

    /// <summary>
    /// Folds a newer command with the same <see cref="CoalesceKey"/> into this one, or returns null when
    /// they cannot merge.
    /// </summary>
    /// <remarks>
    /// The merged command keeps <em>this</em> command's "before" and the newer one's "after". Keeping
    /// the newer "before" would make undo stop one keystroke short, and keeping this one's "after" would
    /// make redo put back the first character the user typed — which is exactly the wrong answer in a
    /// field, where the value is the whole point.
    /// </remarks>
    public virtual DocumentCommand? Merge(DocumentCommand newer) => null;
}

/// <summary>Puts a run of statements into a body at an index.</summary>
public sealed class InsertRun : DocumentCommand
{
    public InsertRun(BodyRef where, int index, IReadOnlyList<Block> run, string label = "Insert block")
    {
        Where = where;
        Index = index;
        Run = run.ToList();
        Label = label;
    }

    /// <summary>The body to put them in.</summary>
    public BodyRef Where { get; }

    /// <summary>Where in it.</summary>
    public int Index { get; }

    /// <summary>The statements, in order.</summary>
    public IReadOnlyList<Block> Run { get; }

    public override string Label { get; }

    private int _at = -1;

    public override void Apply(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        _at = Math.Clamp(Index, 0, body.Count);
        body.InsertRange(_at, Run);
    }

    public override void Revert(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);

        // The run is in the body right now, so its own ids are the better way back if anything has moved
        // since; the remembered index is the fallback.
        var at = DocumentLists.IndexOf(project, Where, Run, orLast: false);
        if (at < 0)
        {
            at = Math.Clamp(_at, 0, Math.Max(0, body.Count - Run.Count));
        }

        body.RemoveRange(at, Run.Count);
    }
}

/// <summary>Takes a run of statements out of a body, remembering them so the inverse can put them back.</summary>
public sealed class DeleteRun : DocumentCommand
{
    public DeleteRun(BodyRef where, IReadOnlyList<Block> removed, string label = "Delete block")
    {
        Where = where;
        Removed = removed.ToList();
        Label = label;
    }

    /// <summary>The body to take them out of.</summary>
    public BodyRef Where { get; }

    /// <summary>The statements as they were, in order. Held, not recomputed.</summary>
    public IReadOnlyList<Block> Removed { get; }

    public override string Label { get; }

    private int _at = -1;

    public override void Apply(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        var at = DocumentLists.IndexOf(project, Where, Removed);
        _at = at;
        body.RemoveRange(at, Removed.Count);
    }

    public override void Revert(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        body.InsertRange(Math.Clamp(_at, 0, body.Count), Removed);
    }
}

/// <summary>
/// Moves a run of statements from one place to another.
/// </summary>
/// <remarks>
/// <para>
/// The source is found from the run's own ids when the command runs, so a caller does not have to hand
/// over an index it worked out against a document that has since moved — which is the ordinary case,
/// because the caller decides the drop against what is on screen and the command runs a moment later.
/// </para>
/// <para>
/// <paramref name="toIndex"/> counts positions in the destination <em>before</em> the run is taken out,
/// because that is the index a user picks when they aim at a gap. Within one body it is shifted by the
/// run's own length when the destination is below it, so "move this down two" lands two places down
/// rather than one. The command remembers both ends as it found them, which is what makes the inverse
/// exact.
/// </para>
/// </remarks>
public sealed class MoveRun : DocumentCommand
{
    public MoveRun(BodyRef from, BodyRef to, IReadOnlyList<Block> run, int toIndex = -1, string label = "Move block")
    {
        From = from;
        To = to;
        Run = run.ToList();
        ToIndex = toIndex;
        Label = label;
    }

    /// <summary>Where the run came from.</summary>
    public BodyRef From { get; }

    /// <summary>Where the run goes. The same as <see cref="From"/> for a reorder within one body.</summary>
    public BodyRef To { get; }

    /// <summary>The statements being moved, in order.</summary>
    public IReadOnlyList<Block> Run { get; }

    /// <summary>Where in the destination, counting positions before the run is removed.</summary>
    public int ToIndex { get; }

    public override string Label { get; }

    private int _fromAt = -1;
    private int _toAt = -1;

    public override void Apply(VisualProject project)
    {
        var source = DocumentLists.ListFor(project, From);
        _fromAt = DocumentLists.IndexOf(project, From, Run);
        source.RemoveRange(_fromAt, Run.Count);

        var wanted = From == To && ToIndex > _fromAt ? ToIndex - Run.Count : ToIndex;
        var destination = DocumentLists.ListFor(project, To);
        _toAt = wanted < 0 ? destination.Count : Math.Clamp(wanted, 0, destination.Count);
        destination.InsertRange(_toAt, Run);
    }

    public override void Revert(VisualProject project)
    {
        var destination = DocumentLists.ListFor(project, To);
        destination.RemoveRange(Math.Clamp(_toAt, 0, Math.Max(0, destination.Count - Run.Count)), Run.Count);

        var source = DocumentLists.ListFor(project, From);
        source.InsertRange(Math.Clamp(_fromAt, 0, source.Count), Run);
    }
}

/// <summary>
/// Puts a run of statements inside a new wrapper, in the wrapper's place.
/// </summary>
/// <remarks>
/// Part 9.5's wrap, in the direction the context menu and the keyboard need: "put these three statements
/// inside a repeat". The run is located by the anchor block's id rather than by an index, and is held
/// once applied, because the inverse has to put those exact statements back where the anchor was — and
/// after the move the anchor is no longer anywhere.
public sealed class WrapRun : DocumentCommand
{
    public WrapRun(BodyRef where, Block anchor, Block wrapper, int count, string label = "Wrap in block")
    {
        Where = where;
        Anchor = anchor;
        Wrapper = wrapper;
        Count = count;
        Label = label;
    }

    /// <summary>The body the wrapped run was in.</summary>
    public BodyRef Where { get; }

    /// <summary>The first statement of the run, found by id when the command runs.</summary>
    public Block Anchor { get; }

    /// <summary>The container placed around it.</summary>
    public Block Wrapper { get; }

    /// <summary>How many statements from the anchor go inside.</summary>
    public int Count { get; }

    public override string Label { get; }

    private int _at = -1;
    private List<Block> _run = [];

    public override void Apply(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        _at = body.FindIndex(block => string.Equals(block.Id, Anchor.Id, StringComparison.Ordinal));
        if (_at < 0)
        {
            return;
        }

        _run = body.Skip(_at).Take(Math.Max(0, Count)).ToList();
        body.RemoveRange(_at, _run.Count);
        body.Insert(_at, Wrapper);
        DocumentLists.BodyOf(Wrapper).AddRange(_run);
    }

    public override void Revert(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        if (_at < 0 || _at > body.Count || !ReferenceEquals(body[_at], Wrapper))
        {
            return;
        }

        DocumentLists.BodyOf(Wrapper).Clear();
        body.RemoveAt(_at);
        body.InsertRange(_at, _run);
    }
}

/// <summary>Splices a container's body into its parent stack and removes the container.</summary>
/// <remarks>
/// Part 9.5's unwrap, which Scratch users reach for constantly. Alt+click on a C-block's shoulder and the
/// context menu's Unwrap are the same command.
public sealed class UnwrapRun : DocumentCommand
{
    public UnwrapRun(BodyRef where, Block container, string label = "Unwrap block")
    {
        Where = where;
        Container = container;
        Label = label;
    }

    /// <summary>The body the container was in.</summary>
    public BodyRef Where { get; }

    /// <summary>The container to remove, keeping what was inside it.</summary>
    public Block Container { get; }

    public override string Label { get; }

    private int _at = -1;
    private List<Block> _inner = [];

    public override void Apply(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        _at = body.IndexOf(Container);
        if (_at < 0)
        {
            return;
        }

        _inner = DocumentLists.BodyOf(Container).ToList();
        DocumentLists.BodyOf(Container).Clear();
        body.RemoveAt(_at);
        body.InsertRange(_at, _inner);
    }

    public override void Revert(VisualProject project)
    {
        var body = DocumentLists.ListFor(project, Where);
        if (_at < 0 || _at > body.Count)
        {
            return;
        }

        body.RemoveRange(_at, _inner.Count);
        body.Insert(_at, Container);
        DocumentLists.BodyOf(Container).AddRange(_inner);
    }
}

/// <summary>Changes one field or one menu's option, keeping the value it had.</summary>
public sealed class EditField : DocumentCommand
{
    public EditField(string blockId, string name, string? before, string? after, bool isMenu = false)
    {
        BlockId = blockId;
        Name = name;
        Before = before;
        After = after;
        IsMenu = isMenu;
    }

    public string BlockId { get; }

    /// <summary>The field's or menu's key.</summary>
    public string Name { get; }

    public string? Before { get; }

    public string? After { get; }

    /// <summary>Whether this is a menu's option rather than free text.</summary>
    public bool IsMenu { get; }

    public override string Label => IsMenu ? "Choose option" : "Edit value";

    /// <summary>
    /// One key per block and field, so typing coalesces and two fields never do.
    /// </summary>
    /// <remarks>
    /// The block id is in the key as well as the name because a menu and a field can share a name on the
    /// same block — <c>var.set-from-parameter</c> has a <c>param</c> slot and a <c>type</c> menu — and
    /// without it one edit's undo would swallow the other's.
    /// </remarks>
    public override string? CoalesceKey => $"{(IsMenu ? "menu" : "field")}:{BlockId}:{Name}";

    public override int CoalesceWindowMs => 800;

    public override DocumentCommand? Merge(DocumentCommand newer) =>
        newer is EditField { IsMenu: var menu } field
        && menu == IsMenu
        && string.Equals(field.BlockId, BlockId, StringComparison.Ordinal)
        && string.Equals(field.Name, Name, StringComparison.Ordinal)
            ? new EditField(BlockId, Name, Before, field.After, IsMenu)
            : null;

    public override void Apply(VisualProject project) => Set(project, After);

    public override void Revert(VisualProject project) => Set(project, Before);

    private void Set(VisualProject project, string? value)
    {
        if (DocumentLists.Find(project, BlockId) is not { } block)
        {
            return;
        }

        if (value is null)
        {
            block.Fields.Remove(Name);
        }
        else
        {
            block.Fields[Name] = value;
        }
    }
}

/// <summary>Changes what is in a slot, keeping what was there.</summary>
public sealed class BindSlot : DocumentCommand
{
    public BindSlot(string blockId, string slot, BlockInput? before, BlockInput? after)
    {
        BlockId = blockId;
        Slot = slot;
        Before = before;
        After = after;
    }

    public string BlockId { get; }

    /// <summary>The slot's key.</summary>
    public string Slot { get; }

    /// <summary>What was in it, or null for empty.</summary>
    public BlockInput? Before { get; }

    /// <summary>What goes in it, or null to clear it.</summary>
    public BlockInput? After { get; }

    public override string Label => "Change slot";

    public override void Apply(VisualProject project) => Set(project, After);

    public override void Revert(VisualProject project) => Set(project, Before);

    private void Set(VisualProject project, BlockInput? input)
    {
        if (DocumentLists.Find(project, BlockId) is not { } block)
        {
            return;
        }

        if (input is null)
        {
            block.Inputs.Remove(Slot);
        }
        else
        {
            // Copied rather than shared, so a later edit of the same input cannot reach back into the
            // value this command is holding — which is how undo hands back something that has changed.
            block.Inputs[Slot] = new BlockInput
            {
                Text = input.Text,
                Number = input.Number,
                Boolean = input.Boolean,
                Variable = input.Variable,
                Block = input.Block,
            };
        }
    }
}

/// <summary>Switches a block's disabled flag, which the emitter and the interpreter both honour.</summary>
public sealed class ToggleDisable : DocumentCommand
{
    public ToggleDisable(string blockId, bool after)
    {
        BlockId = blockId;
        After = after;
    }

    public string BlockId { get; }

    /// <summary>The flag's value after the command.</summary>
    public bool After { get; }

    public override string Label => "Enable or disable block";

    public override void Apply(VisualProject project) => Set(project, After);

    public override void Revert(VisualProject project) => Set(project, !After);

    private void Set(VisualProject project, bool value)
    {
        if (DocumentLists.Find(project, BlockId) is { } block)
        {
            block.Disabled = value;
        }
    }
}

/// <summary>Adds a script to a target.</summary>
public sealed class AddScript : DocumentCommand
{
    public AddScript(VisualTarget target, VisualScript script, int index = -1)
    {
        Target = target;
        Script = script;
        Index = index;
    }

    public VisualTarget Target { get; }

    public VisualScript Script { get; }

    /// <summary>Where to put it, or -1 for the end.</summary>
    public int Index { get; }

    public override string Label => "Add script";

    public override void Apply(VisualProject project)
    {
        var at = Index < 0 || Index > Target.Scripts.Count ? Target.Scripts.Count : Index;
        Target.Scripts.Insert(at, Script);
    }

    public override void Revert(VisualProject project) => Target.Scripts.Remove(Script);
}

/// <summary>Removes a script, keeping it so the inverse can put it back where it was.</summary>
public sealed class DeleteScript : DocumentCommand
{
    public DeleteScript(VisualTarget target, VisualScript script)
    {
        Target = target;
        Script = script;
    }

    public VisualTarget Target { get; }

    public VisualScript Script { get; }

    public override string Label => "Delete script";

    private int _at = -1;

    public override void Apply(VisualProject project)
    {
        _at = Target.Scripts.IndexOf(Script);
        Target.Scripts.RemoveAt(_at);
    }

    public override void Revert(VisualProject project)
    {
        var at = Math.Clamp(_at, 0, Target.Scripts.Count);
        Target.Scripts.Insert(at, Script);
    }
}

/// <summary>Moves a script to a new canvas position.</summary>
public sealed class MoveScript : DocumentCommand
{
    public MoveScript(VisualScript script, double x, double y)
    {
        Script = script;
        X = x;
        Y = y;
    }

    public VisualScript Script { get; }

    /// <summary>Where it went, in workspace units.</summary>
    public double X { get; }

    /// <summary>Where it went, in workspace units.</summary>
    public double Y { get; }

    public override string Label => "Move script";

    private double _beforeX;
    private double _beforeY;
    private bool _captured;

    public override void Apply(VisualProject project)
    {
        if (!_captured)
        {
            _beforeX = Script.X;
            _beforeY = Script.Y;
            _captured = true;
        }

        Script.X = X;
        Script.Y = Y;
    }

    public override void Revert(VisualProject project)
    {
        Script.X = _beforeX;
        Script.Y = _beforeY;
    }
}

/// <summary>Several commands applied and undone as one history entry.</summary>
/// <remarks>
/// One gesture is one transaction (Part 9.5). A drag that moves three blocks and rewrites one field is
/// one <c>Ctrl+Z</c>, not four, and a wrapper that pulls a whole run inside itself is one entry rather
/// than one per statement.
public sealed class DocumentTransaction : DocumentCommand
{
    public DocumentTransaction(string label, IReadOnlyList<DocumentCommand> commands)
    {
        Label = label;
        Commands = commands.ToList();
    }

    public override string Label { get; }

    public IReadOnlyList<DocumentCommand> Commands { get; }

    /// <summary>Whether there is anything to undo.</summary>
    public bool IsEmpty => Commands.Count == 0;

    public override void Apply(VisualProject project)
    {
        foreach (var command in Commands)
        {
            command.Apply(project);
        }
    }

    public override void Revert(VisualProject project)
    {
        // Backwards, and that ordering is the whole point: a later command may depend on an earlier one,
        // and undoing them in order would leave it applied to a state it never saw.
        for (var i = Commands.Count - 1; i >= 0; i--)
        {
            Commands[i].Revert(project);
        }
    }
}

/// <summary>
/// Finds the list of statements a <see cref="BodyRef"/> names, and a block anywhere in the document.
/// </summary>
/// <remarks>
/// The one place that knows how a body is addressed, so every command agrees on it and none of them
/// re-implements the lookup. Commands throw <see cref="InvalidOperationException"/> when a reference does
/// not resolve, because the editor checks references before it builds a command: a command that reached
/// its way onto the stack without one is a bug, and silently doing nothing is what lets that kind of bug
/// survive.
/// </remarks>
public static class DocumentLists
{
    /// <summary>The statements of the body a reference names.</summary>
    /// <exception cref="InvalidOperationException">The reference does not resolve.</exception>
    public static List<Block> ListFor(VisualProject project, BodyRef where)
    {
        ArgumentNullException.ThrowIfNull(project);

        var script = project.Targets
            .SelectMany(target => target.Scripts)
            .FirstOrDefault(candidate => string.Equals(candidate.Hat.Id, where.OwnerId, StringComparison.Ordinal));

        if (script is not null)
        {
            return script.Body;
        }

        return DocumentLists.Find(project, where.OwnerId) is { } owner
            ? owner.Body(where.BodyName)
            : throw new InvalidOperationException($"No script or block is called '{where.OwnerId}'.");
    }

    /// <summary>
    /// Where the first of <paramref name="anchor"/> sits in a body, or -1 when none of them is there.
    /// </summary>
    /// <param name="anchor">
    /// Blocks whose ids locate the position. Null finds nothing; an empty list finds the end.
    /// </param>
    /// <param name="orLast">Whether to report the end of the body when the anchor is not found.</param>
    public static int IndexOf(VisualProject project, BodyRef where, IReadOnlyList<Block>? anchor, bool orLast = true)
    {
        var body = ListFor(project, where);

        if (anchor is null)
        {
            return -1;
        }

        if (anchor.Count == 0)
        {
            return orLast ? body.Count : -1;
        }

        var at = body.FindIndex(block => string.Equals(block.Id, anchor[0].Id, StringComparison.Ordinal));
        return at < 0 && orLast ? body.Count : at;
    }

    /// <summary>The wrapper's body: the first one its catalogue row declares.</summary>
    public static List<Block> BodyOf(Block container)
    {
        var name = BlockCatalog.Find(container.Kind)?.Bodies?.FirstOrDefault()?.Name;
        return name is null ? [] : container.Body(name);
    }

    /// <summary>A block anywhere in the document: hats, statements, nested slots and procedure bodies.</summary>
    public static Block? Find(VisualProject project, string blockId) =>
        project.Blocks().FirstOrDefault(block => string.Equals(block.Id, blockId, StringComparison.Ordinal));

    /// <summary>
    /// The body a block is a statement of, or null for a hat, a nested reporter, or nothing at all.
    /// </summary>
    /// <remarks>
    /// On <see cref="DocumentEditor"/> rather than here originally, which made it unavailable to every
    /// caller that had a document and not an editor. It is a lookup over the document and asks nothing of
    /// the history, so a static is the honest shape — and a keyboard gesture that can only be aimed with a
    /// constructed editor is a keyboard gesture whose aiming cannot be tested.
    /// </remarks>
    public static BodyRef? Locate(VisualProject project, Block block)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(block);

        foreach (var target in project.Targets)
        {
            foreach (var script in target.Scripts)
            {
                if (script.Body.Contains(block))
                {
                    return BodyRef.ScriptBody(script.Hat.Id);
                }

                if (LocatedIn(script.Body, block) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static BodyRef? LocatedIn(IReadOnlyList<Block> statements, Block block)
    {
        foreach (var statement in statements)
        {
            foreach (var (name, children) in statement.Bodies)
            {
                if (children.Contains(block))
                {
                    return new BodyRef(statement.Id, name);
                }

                if (LocatedIn(children, block) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>Every id in a run's subtrees, which is how a move into its own descendant is refused.</summary>
    public static HashSet<string> IdsOf(IEnumerable<Block> run) =>
        [.. run.SelectMany(block => block.Walk()).Select(block => block.Id)];
}