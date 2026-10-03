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
/// <para>
/// A procedure body is addressed the same way, by the declaration's id, and needs the extra flag because
/// "the owner with this id" is otherwise ambiguous: a hat id and a procedure id come from different
/// sequences and can collide. The flag is part of the reference rather than a convention, so a stale
/// reference fails loudly in <see cref="DocumentLists.ListFor"/> instead of quietly editing the wrong
/// body.
/// </para>
/// </remarks>
public readonly record struct BodyRef(string OwnerId, string BodyName = "", bool IsProcedure = false)
{
    /// <summary>The body a script's statements live in.</summary>
    public static BodyRef ScriptBody(string hatId) => new(hatId);

    /// <summary>The body a procedure's statements live in.</summary>
    public static BodyRef ProcedureBody(string procedureId) => new(procedureId, "", IsProcedure: true);

    /// <summary>Whether this addresses a script rather than a wrapped body.</summary>
    public bool IsScriptBody => string.IsNullOrEmpty(BodyName);

    /// <summary>Whether this addresses a procedure body rather than a script's.</summary>
    public bool IsProcedureBody => IsProcedure && string.IsNullOrEmpty(BodyName);

    public override string ToString() => IsProcedureBody
        ? $"procedure {OwnerId}"
        : IsScriptBody ? $"script {OwnerId}" : $"{OwnerId}.{BodyName}";
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

/// <summary>
/// Sets or clears a block's comment.
/// </summary>
/// <remarks>
/// A command like <see cref="ToggleDisable"/> rather than a property write, because the comment is part of
/// the document and a property write would put it outside the undo stack — so Ctrl+Z after typing a
/// comment would undo whatever the user did last instead, which is the single most confusing thing an
/// editor can do.
///
/// It coalesces like the slot editors do, because a comment is typed one character at a time and twenty
/// undo steps to get back from a typo in a sentence is not undo, it is a chore.
/// </remarks>
public sealed class SetComment : DocumentCommand
{
    public SetComment(string blockId, string? after)
    {
        BlockId = blockId;
        After = after;
    }

    /// <summary>The block whose comment this is.</summary>
    public string BlockId { get; }

    /// <summary>
    /// The comment after the command, or null when it is being cleared.
    /// </summary>
    /// <remarks>
    /// Settable because merging edits it. The merged entry is one undo step covering every keystroke, so
    /// its <em>after</em> is the newest text while its <see cref="Before"/> stays the oldest.
    /// </remarks>
    public string? After { get; private set; }

    /// <summary>The comment before the command, kept so undo is a write and not a guess.</summary>
    public string? Before { get; private set; }

    /// <summary>
    /// Whether <see cref="Before"/> has been read off the document yet.
    /// </summary>
    /// <remarks>
    /// Separate from the value, because null is a real comment — the one a block has when the user has not
    /// written one — and a flag that could not tell "no comment yet" from "no comment" would lose it. The
    /// merge below picks the *older* entry's value, which with null means picking nothing at all.
    /// </remarks>
    private bool _beforeCaptured;

    public override string Label => "Comment on block";

    /// <summary>
    /// Typed text coalesces into one undo entry.
    /// </summary>
    /// <remarks>
    /// Same rule as the slot editors, and for the same reason: the window is long enough for a pause but
    /// short enough that two separate sentences a minute apart are still one edit to the user.
    /// </remarks>
    public override string? CoalesceKey => $"comment:{BlockId}";

    public override int CoalesceWindowMs => 800;

    public override void Apply(VisualProject project)
    {
        if (DocumentLists.Find(project, BlockId) is not { } block)
        {
            return;
        }

        if (!_beforeCaptured)
        {
            Before = block.Comment;
            _beforeCaptured = true;
        }

        block.Comment = After;
    }

    public override void Revert(VisualProject project)
    {
        if (DocumentLists.Find(project, BlockId) is { } block)
        {
            block.Comment = Before;
        }
    }

    /// <summary>
    /// Two comments in a row on one block are one edit.
    /// </summary>
    /// <remarks>
    /// The earlier one's <see cref="Before"/> is kept, so the merged entry still undoes to the comment
    /// that was there before the user started typing. Merging the other way round would undo to the
    /// intermediate state — three characters in — which is the sort of thing that looks like a bug in the
    /// undo stack and is really a bug here.
    /// </remarks>
    public override DocumentCommand? Merge(DocumentCommand newer)
    {
        if (newer is not SetComment comment || comment.BlockId != BlockId)
        {
            return null;
        }

        if (!_beforeCaptured)
        {
            Before = comment.Before;
            _beforeCaptured = true;
        }

        After = comment.After;

        return this;
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

/// <summary>
/// Renames a script, or swaps the hat it starts with.
/// </summary>
/// <remarks>
/// One command for both because they are the same gesture as far as undo is concerned — the strip's rename
/// field and hat dropdown belong to one row, and a user who changes a hat and then presses Ctrl+Z expects
/// to be back where they were, not to have to remember which of the two they did last.
///
/// The hat is swapped by <em>moving the body onto a new hat</em> rather than by mutating the old hat's
/// kind, because a hat's id addresses its script body: changing the kind in place would leave a
/// "when event received" script carrying the id of a "when action runs" hat, and the id is what
/// <see cref="BodyRef"/> and every undo entry still hold.
/// </remarks>
public sealed class EditScript : DocumentCommand
{
    public EditScript(VisualScript script, string? name = null, Block? hat = null, string? beforeName = null)
    {
        ArgumentNullException.ThrowIfNull(script);

        Script = script;
        Name = name;
        Hat = hat;
        _beforeName = beforeName ?? script.Name;
        _replacedHat = hat is null ? null : script.Hat;
    }

    public VisualScript Script { get; }

    /// <summary>The new name, or null to leave it alone.</summary>
    public string? Name { get; }

    /// <summary>The new hat, or null to leave the existing one alone.</summary>
    public Block? Hat { get; }

    /// <summary>Set by the editor for a rename, so typing a new name is one undo entry.</summary>
    public string? MergeKey
    {
        init => _mergeKey = value;
    }

    private string? _mergeKey;

    public override string? CoalesceKey => _mergeKey;

    /// <summary>
    /// The same 800ms per-field window <see cref="EditField"/> uses.
    /// </summary>
    /// <remarks>
    /// A key with a window of zero never merges, which is the default for everything that is not typing —
    /// and it is why the window is written out here rather than left to the base class. A rename arriving
    /// from a text field is typing, and typing has to be one <c>Ctrl+Z</c>.
    /// </remarks>
    public override int CoalesceWindowMs => 800;

    /// <summary>
    /// Folds a second rename of the same script into this one.
    /// </summary>
    /// <remarks>
    /// The merged command keeps <em>this</em> command's remembered name and the newer command's name, which
    /// is the only combination that makes one Ctrl+Z after four keystrokes restore the original name: a
    /// merged command that captured the current name would restore the third character.
    /// </remarks>
    public override DocumentCommand? Merge(DocumentCommand newer) =>
        newer is EditScript { Name: { } newerName, Hat: null }
            ? new EditScript(Script, newerName, beforeName: _beforeName) { MergeKey = _mergeKey }
            : null;

    public override string Label => Hat is null ? "Rename script" : "Change script hat";

    /// <summary>
    /// The name the script had when this command was built.
    /// </summary>
    /// <remarks>
    /// Captured in the constructor rather than on the first <see cref="Apply"/>. The document is the same
    /// object the command mutates, so "read the current value when I am about to apply" records the value
    /// from the previous keystroke — which is why an undo of a merged rename used to land on the middle of
    /// the word rather than before it.
    /// </remarks>
    private readonly string _beforeName;

    /// <summary>The hat this command replaced, kept so the inverse can put that exact block back.</summary>
    private readonly Block? _replacedHat;

    public override void Apply(VisualProject project)
    {
        if (Name is not null)
        {
            Script.Name = Name;
        }

        if (Hat is not null)
        {
            Script.Hat = Hat;
        }
    }

    public override void Revert(VisualProject project)
    {
        if (Name is not null)
        {
            Script.Name = _beforeName;
        }

        if (Hat is not null)
        {
            Script.Hat = _replacedHat ?? Script.Hat;
        }
    }
}

/// <summary>
/// Adds, deletes or edits a procedure declaration.
/// </summary>
/// <remarks>
/// One command with three shapes rather than three commands, because the panel's three gestures share the
/// property that matters: a procedure's body is addressed by its declaration's id, so an edit that changed
/// that id would strand every reference to the body. This command never touches <see cref="ProcedureDeclaration.Id"/>.
/// </remarks>
public sealed class EditProcedure : DocumentCommand
{
internal EditProcedure(
        ProcedureDeclaration? before,
        ProcedureDeclaration? after,
        string label,
        string? beforeName = null)
    {
        Before = before;
        After = after;
        LabelText = label;

        // Snapshotted here for the same reason <see cref="EditScript"/> does it in its constructor: the
        // declaration in the document is the same object this command edits, so reading its name when
        // about to apply records the value from the previous keystroke. It also has to be a snapshot
        // rather than the object, because the apply step copies the new name *into* that object.
        //
        // `beforeName` is only ever passed by Merge, and it is what makes a merged rename restore the
        // name from before the first keystroke rather than from before the last one.
        _beforeName = beforeName ?? before?.Name ?? string.Empty;
        _beforeParameters = [.. before?.Parameters.Select(parameter => new ProcedureParameter
        {
            Name = parameter.Name,
            Type = parameter.Type,
        }) ?? []];
        _beforeReturns = before?.Returns ?? false;
    }

    /// <summary>The declaration looked like before, or null for an add.</summary>
    internal ProcedureDeclaration? Before { get; }

    /// <summary>What it looks like, or null for a delete.</summary>
    internal ProcedureDeclaration? After { get; }

    /// <summary>Where it was, so a delete's inverse puts it back in the same slot.</summary>
    public int Index { get; init; } = -1;

    /// <summary>
    /// Whether this edit changed the parameter list, which decides if a later rename can merge into it.
    /// </summary>
    /// <remarks>
    /// Merging two edits is only safe when they are the same edit. A rename folded into a parameter change
    /// would restore the name but lose the parameters, so the two are told apart rather than assumed equal.
    /// </remarks>
    public bool ParametersChanged { get; init; }

    /// <summary>Set by the editor for a rename, so typing a new name is one undo entry.</summary>
    public string? MergeKey
    {
        init => _mergeKey = value;
    }

    private string? _mergeKey;

    public override string? CoalesceKey => _mergeKey;

    /// <summary>The same 800ms per-field window <see cref="EditField"/> uses; see <see cref="EditScript"/>.</summary>
    public override int CoalesceWindowMs => 800;

    /// <summary>Folds a second rename of the same procedure into this one, oldest name first.</summary>
    public override DocumentCommand? Merge(DocumentCommand newer) =>
        newer is EditProcedure { After: { } renamed, Before: not null } edit && !edit.ParametersChanged
            ? new EditProcedure(Before, renamed, LabelText, _beforeName) { MergeKey = _mergeKey }
            : null;

    public override string Label => LabelText;

    private string LabelText { get; }

    private readonly string _beforeName;

    private readonly List<ProcedureParameter> _beforeParameters;

    private readonly bool _beforeReturns;

    public override void Apply(VisualProject project)
    {
        if (After is null)
        {
            var removed = Math.Clamp(Index, 0, project.Procedures.Count);
            project.Procedures.RemoveAt(removed);
            return;
        }

        var existing = Before is null
            ? -1
            : project.Procedures.FindIndex(candidate => candidate.Id == Before.Id);

        if (existing >= 0)
        {
            // In place rather than remove-and-insert: replacing the list slot keeps every reference to
            // this declaration — including the Body property the canvas is projecting — pointing at the
            // same object, and a remove-and-insert would leave the canvas editing a detached copy.
            Copy(After, project.Procedures[existing]);
            return;
        }

        // -1 means the end. Clamping it to zero would put every new procedure at the top of the list,
        // which is why an add and a delete have to be read as different intents despite sharing a command.
        var insertion = Index < 0 ? project.Procedures.Count : Math.Clamp(Index, 0, project.Procedures.Count);
        project.Procedures.Insert(insertion, After);
    }

    public override void Revert(VisualProject project)
    {
        if (Before is null)
        {
            var at = project.Procedures.FindIndex(candidate => candidate.Id == After?.Id);
            if (at >= 0)
            {
                project.Procedures.RemoveAt(at);
            }

            return;
        }

        var existing = project.Procedures.FindIndex(candidate => candidate.Id == Before.Id);

        if (existing >= 0)
        {
            // The remembered state wins over <see cref="Before"/>, because Before is the live object this
            // command has been writing into: on a redo-then-undo it already holds the intermediate name.
            // The user's "before" is the snapshot taken when the command was built.
            Restore(project.Procedures[existing]);
            return;
        }

        // Deleted and then undone: put the declaration back with the state it had, in the slot it was in.
        Before.Name = _beforeName;
        Before.Returns = _beforeReturns;
        Before.Parameters.Clear();
        Before.Parameters.AddRange(_beforeParameters.Select(parameter => new ProcedureParameter
        {
            Name = parameter.Name,
            Type = parameter.Type,
        }));

        var insertion = Index < 0 ? project.Procedures.Count : Math.Clamp(Index, 0, project.Procedures.Count);
        project.Procedures.Insert(insertion, Before);
    }

    /// <summary>Puts the remembered name, parameters and returning flag back onto a declaration.</summary>
    private void Restore(ProcedureDeclaration declaration)
    {
        declaration.Name = _beforeName;
        declaration.Returns = _beforeReturns;

        declaration.Parameters.Clear();
        declaration.Parameters.AddRange(_beforeParameters.Select(parameter => new ProcedureParameter
        {
            Name = parameter.Name,
            Type = parameter.Type,
        }));
    }

    /// <summary>Copies a declaration's editable members onto the object already in the document.</summary>
    private static void Copy(ProcedureDeclaration from, ProcedureDeclaration to)
    {
        to.Name = from.Name;
        to.Returns = from.Returns;

        to.Parameters.Clear();
        to.Parameters.AddRange(from.Parameters.Select(parameter => new ProcedureParameter
        {
            Name = parameter.Name,
            Type = parameter.Type,
        }));
    }
}

/// <summary>
/// The three procedure commands, as the editor's callers build them.
/// </summary>
/// <remarks>
/// Factories rather than static methods on <see cref="EditProcedure"/> itself, for a reason worth writing
/// down: <c>DocumentEditor</c> has three methods called <c>AddProcedure</c>, <c>DeleteProcedure</c> and
/// <c>EditProcedure</c>, and a method group shadows a type name inside the class that declares it — so
/// <c>EditProcedure.Edit(…)</c> inside the editor resolves against the method, not the type, and fails
/// with a parse error about a brace that is perfectly fine. Naming the factory separately keeps both.
/// </remarks>
public static class ProcedureEdits
{
    /// <summary>Adds a procedure.</summary>
    public static EditProcedure Add(ProcedureDeclaration procedure) =>
        new(null, procedure, "Add procedure");

    /// <summary>Removes a procedure, keeping it so the inverse can put it back where it was.</summary>
    public static EditProcedure Delete(ProcedureDeclaration procedure, int index) =>
        new(procedure, null, "Delete procedure") { Index = index };

    /// <summary>
    /// Replaces a procedure's name, parameters or returning flag, leaving its id and body alone.
    /// </summary>
    /// <param name="procedure">The declaration to change.</param>
    /// <param name="name">The new name.</param>
    /// <param name="parameters">The new parameter list, or null to keep the existing one.</param>
    /// <param name="returning">Whether it returns a value, or null to keep the existing flag.</param>
    /// <param name="mergeKey">
    /// The coalescing key, or null. A parameter rather than an object initializer on the call: the editor
    /// calls this from inside a class that has its own <c>EditProcedure</c> method, and building the command
    /// with an initializer there is a shape that does not survive the parser.
    /// </param>
    /// <remarks>
    /// The copy shares the original's <see cref="Block"/> list rather than cloning it. A parameter edit
    /// that deep-copied the body would quietly detach the procedure from every block the canvas is
    /// projecting, and the body would appear to lose everything the moment a name was typed.
    /// </remarks>
    public static EditProcedure Edit(
        ProcedureDeclaration procedure,
        string name,
        IReadOnlyList<ProcedureParameter>? parameters = null,
        bool? returning = null,
        string? mergeKey = null)
    {
        ArgumentNullException.ThrowIfNull(procedure);

        var copy = new ProcedureDeclaration
        {
            Id = procedure.Id,
            Name = name,
            Parameters = parameters is null
                ? [.. procedure.Parameters]
                : [.. parameters.Select(parameter => new ProcedureParameter
                {
                    Name = parameter.Name,
                    Type = parameter.Type,
                })],
            Returns = returning ?? procedure.Returns,
            Body = procedure.Body,
        };

        return new EditProcedure(procedure, copy, "Edit procedure")
        {
            MergeKey = mergeKey,
            ParametersChanged = parameters is not null,
        };
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

        if (where.IsProcedureBody)
        {
            // A procedure body is found by id rather than by hat, and the id belongs to the declaration —
            // which is why a rename does not move the body. Nothing is thrown for a missing one here: the
            // editor calls this while checking whether a command's reference still resolves, and an
            // exception there would turn "the document moved on" into an unhandled error.
            return project.FindProcedureById(where.OwnerId)?.Body ?? [];
        }

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

        // Procedure bodies last, and they are reachable at all only since Phase 7 made a procedure
        // editable. Before this, a block inside one was found by `Find` — so it was selectable, and then
        // nothing could say which body it was in, and every edit aimed at it was refused.
        foreach (var procedure in project.Procedures)
        {
            if (procedure.Body.Contains(block))
            {
                return BodyRef.ProcedureBody(procedure.Id);
            }

            if (LocatedIn(procedure.Body, block) is { } nested)
            {
                return nested;
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