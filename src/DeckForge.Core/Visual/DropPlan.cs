namespace DeckForge.Core.Visual;

/// <summary>
/// What a drag is carrying, as the document knows it.
/// </summary>
/// <remarks>
/// Every refusal from this file carries a <c>vis-</c> code, in one namespace with the validator's. They
/// used to be six codes of their own - <c>drop-shape</c>, <c>landing-stale</c>, <c>slot-no-name</c>,
/// <c>onto-self</c>, <c>hat-needs-canvas</c>, <c>canvas-needs-hat</c> - which were in no table and covered
/// by no test, so anything switching on <c>DocumentEditResult.Code</c> had to handle two vocabularies and a
/// wrong code here was invisible. Prefixed, and Appendix F now lists them.
/// </remarks>
/// <param name="Source">The body the run came from, or null when it came from the palette.</param>
/// <param name="First">The block that was grabbed, or null for a palette drag.</param>
/// <param name="Run">
/// The blocks the gesture moves: the grabbed block and everything below it, because that is what grabbing
/// statement three of a stack means. Null for a palette drag, which has no run yet — one is built on the
/// drop.
/// </param>
/// <param name="Kind">The catalogue kind of the top of the run, which decides what accepts it.</param>
/// <param name="IsPalette">Whether this is a new block from the palette rather than a move.</param>
/// <param name="IsSingle">
/// Whether Alt narrowed the drag to one block (Part 9.5). Kept because the alternative is a user who grabs
/// the middle of a five-block stack with no way to move that block on its own.
/// </param>
public sealed record DragPayload(
    BodyRef? Source,
    Block? First,
    IReadOnlyList<Block>? Run,
    string Kind,
    bool IsPalette,
    bool IsSingle)
{
    /// <summary>A new block of a kind, not yet in any document.</summary>
    public static DragPayload FromPalette(string kind) => new(null, null, null, kind, IsPalette: true, IsSingle: false);

    /// <summary>
    /// A run already in the document.
    /// </summary>
    /// <param name="editor">The editor that owns the document.</param>
    /// <param name="where">The body the grabbed block is in.</param>
    /// <param name="first">The block that was grabbed.</param>
    /// <param name="wholeStack">
    /// False narrows the run to the grabbed block, which is what Alt does. Everything below it stays where
    /// it is, and that is the whole reason the run is re-read rather than carried: the statements after the
    /// grab point are not going anywhere.
    /// </param>
    public static DragPayload FromDocument(
        DocumentEditor editor,
        BodyRef where,
        Block first,
        bool wholeStack = true) =>
        new(where, first, editor.RunFrom(where, first, wholeStack), first.Kind, IsPalette: false, IsSingle: !wholeStack);

    /// <summary>Whether this carries a run that is already in the document.</summary>
    public bool IsMove => !IsPalette && Run is { Count: > 0 } && First is not null && Source is not null;
}

/// <summary>
/// Turns a landing zone into the commands that carry it out.
/// </summary>
/// <remarks>
/// <para>
/// The pointer half of Part 9.5, with no window in it. It takes the dragged payload and the winning
/// <see cref="DropCandidate"/> and decides what the drop <em>means</em> — which is a question about the
/// document and not about the screen, so it belongs in Core beside the commands it produces. The canvas
/// keeps the other half: where the pointer is, what the candidates are, and what to draw.
/// </para>
/// <para>
/// Every rule here is one a user hits by accident, which is the only reason any of them are written down:
/// a stack dropped onto itself must do nothing rather than complain; a reporter pulled out of a hole has to
/// leave the hole's old value behind for undo; a free canvas accepts hats and nothing else. None of these
/// are discoverable from the outside, and all of them are invisible to a test that only drives the window.
/// </para>
/// </remarks>
public static class DropPlan
{
    /// <summary>
    /// Applies a drop.
    /// </summary>
    /// <param name="editor">The editor that owns the document.</param>
    /// <param name="target">The target a new script would be added to.</param>
    /// <param name="payload">What the drag is carrying.</param>
    /// <param name="landing">The winning candidate.</param>
    /// <param name="pointerX">The pointer's x, for a drop that creates a script.</param>
    /// <param name="pointerY">The pointer's y, for a drop that creates a script.</param>
    public static DocumentEditResult Apply(
        DocumentEditor editor,
        VisualTarget target,
        DragPayload payload,
        DropCandidate landing,
        double pointerX = 0,
        double pointerY = 0)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(payload);

        var descriptor = BlockCatalog.Find(payload.Kind);

        // The two refusals with something useful to say come before the shape filter. The canvas never
        // offers these zones to these shapes, so it will never meet them - but the keyboard's pick-up mode
        // does, and "a hat block starts a script rather than joining one" teaches the rule where
        // "nothing of this shape goes there" does not.
        if (descriptor is { IsHat: true } && landing.Kind is not (DropTargetKind.Canvas or DropTargetKind.HatSlot))
        {
            return DocumentEditResult.Refused(
                "A hat block starts a script rather than joining one. Drop it on empty canvas to make one.",
                "hat-needs-canvas");
        }

        if (descriptor is { IsHat: false } && landing.Kind == DropTargetKind.HatSlot)
        {
            return DocumentEditResult.Refused(
                "Only a hat block starts a script, so this is not where one goes.",
                "hat-needs-canvas");
        }

        // The zone's shape filter is a hard filter, and re-running it here is what makes this entry point
        // safe to call from anywhere: a mistake in the canvas's enumeration refuses the drop rather than
        // putting a boolean where a statement goes.
        if (descriptor is null || !DropResolver.Accepts(landing, descriptor))
        {
            return DocumentEditResult.Refused("Nothing of this shape goes there.", "drop-shape");
        }

        return landing.Kind switch
        {
            DropTargetKind.ValueSlot => IntoSlot(editor, payload, landing),
            DropTargetKind.OntoStatement => OntoStatement(editor, payload, landing),
            DropTargetKind.Canvas => AsNewScript(editor, target, payload, descriptor, pointerX, pointerY),
            _ => IntoBody(editor, payload, landing),
        };
    }

    /// <summary>
    /// A drop into a gap or a mouth: a move when the run came from the document, an insert when it came
    /// from the palette.
    /// </summary>
    /// <remarks>
    /// A container dropped into a gap is <em>inserted</em>, not wrapped. Part 9.5's "dropping a stack onto
    /// a C-block's mouth wraps it in one gesture" reads as though the mouth wraps what arrives, but the
    /// mouth is where the body starts, so the same sentence taken literally would put the C-block inside
    /// its own body. Wrapping is <see cref="WrapRun"/>'s job and happens when a container from the palette
    /// lands on a statement — see <see cref="OntoStatement"/>.
    /// </remarks>
    private static DocumentEditResult IntoBody(DocumentEditor editor, DragPayload payload, DropCandidate landing)
    {
        // From the candidate rather than rebuilt from its two names: a procedure body and a script body are
        // addressed differently, and rebuilding the reference from the id alone produces a script
        // reference to a hat that does not exist - so a legal drop into a procedure was reported as a
        // refusal about a missing body.
        var where = landing.Where;

        if (!payload.IsMove)
        {
            var descriptor = BlockCatalog.Find(payload.Kind)!;
            return editor.Insert(where, landing.Index, [BlockFactory.Preview(descriptor)], $"Add {descriptor.Label}");
        }

        if (IsInsideOwnRun(editor, payload, where, landing.Index))
        {
            // A gap inside the run itself. The pointer is over the very blocks being carried, and the only
            // candidate nearby is one of their own boundaries, so "no-op" is the honest answer. The editor
            // would refuse this as move-into-self, which is technically correct and reads to a user as the
            // canvas complaining about something they did not do.
            return DocumentEditResult.Ok;
        }

        // The run is built here rather than handed to DocumentEditor.Move, which re-reads the whole tail
        // from the block it is given. That is the right default for a caller that wants a stack drag, and
        // the wrong thing entirely for one that has already decided what the run is - which is exactly
        // The catalogue's label rather than the kind, so the pointer path and the keyboard path agree:
        // this read "Undo Move control.forever" where DocumentEditor.Describe reads "Undo Move repeat 10",
        // and the same gesture had two names in the same session.
        var described = BlockCatalog.Find(payload.Kind)?.Label is { } label
            ? label.Replace('{', ' ').Replace('}', ' ').Trim()
            : payload.Kind;

        // what Alt means. Passing the payload's run through keeps the narrowed drag narrowed.
        return editor.Execute(new MoveRun(
            payload.Source!.Value, where, payload.Run!, landing.Index, $"Move {described}"));
    }

    /// <summary>
    /// A drop onto a statement's own footprint rather than onto a gap beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two entirely different gestures share the zone, and which one it is depends on what is being
    /// dragged rather than on where it was dropped:
    /// </para>
    /// <list type="bullet">
    /// <item>A container from the palette <b>wraps</b> the statement, which is Part 9.5's one-gesture wrap.</item>
    /// <item>Anything else <b>replaces</b> the statement.</item>
    /// </list>
    /// <para>
    /// The replacement is a delete and an insert rather than a move, because the statement being replaced
    /// is being thrown away and its run is not travelling anywhere.
    /// </para>
    /// </remarks>
    private static DocumentEditResult OntoStatement(DocumentEditor editor, DragPayload payload, DropCandidate landing)
    {
        var where = landing.Where;
        var body = DocumentLists.ListFor(editor.Project, where);

        if (landing.Index < 0 || landing.Index >= body.Count)
        {
            return DocumentEditResult.Refused("That statement has moved on.", "landing-stale");
        }

        var victim = body[landing.Index];
        var descriptor = BlockCatalog.Find(payload.Kind)!;

        // The wrap gesture: a container from the palette drops onto one statement and swallows it. The
        // anchor is the statement itself and the count is one, so whatever was below it stays below the
        // wrapper in the same order.
        if (payload.IsPalette && descriptor.IsContainer)
        {
            return editor.Execute(new WrapRun(
                where, victim, BlockFactory.Preview(descriptor), 1, $"Wrap in {descriptor.Label}"));
        }

        if (!payload.IsMove)
        {
            return editor.Transaction(
                $"Replace {descriptor.Label}",
                [new DeleteRun(where, [victim]), new InsertRun(where, landing.Index, [BlockFactory.Preview(descriptor)])]);
        }

        if (DocumentLists.IdsOf(payload.Run!).Contains(victim.Id))
        {
            // The statement under the pointer is one of the blocks being carried, which happens whenever
            // the run is longer than the thing it is dropped on. Dropping a stack onto itself replaces it
            // with itself and deletes everything below it — so this refuses rather than trusting the
            // arithmetic to notice.
            return DocumentEditResult.Refused(
                "A stack cannot replace one of its own blocks.", "onto-self");
        }

        // Delete before insert, with the index adjusted, because the two commands have to be told about
        // the same body and inserting first would put the carried run above the statement it is replacing
        // and then shift the index out from under itself. When the run came out of this very body the
        // removal also shifts everything after it up by one.
        var sameBody = payload.Source == where;
        var from = DocumentLists.IndexOf(editor.Project, payload.Source!.Value, payload.Run!);
        var wanted = landing.Index - (sameBody && from >= 0 && from < landing.Index ? payload.Run!.Count : 0);

        return editor.Transaction(
            $"Replace {victim.Kind}",
            [new DeleteRun(where, [victim]), new MoveRun(payload.Source!.Value, where, payload.Run!, wanted)]);
    }

    /// <summary>
    /// A drop into a value slot: bind the block, and take it out of wherever it was.
    /// </summary>
    /// <remarks>
    /// One transaction, because it is one gesture and because a reporter bound into a hole while still
    /// listed in its old body is a document with the same block in two places — which the emitter would
    /// happily emit twice. The slot's previous value rides along so undo puts the old one back rather than
    /// clearing the hole.
    /// </remarks>
    private static DocumentEditResult IntoSlot(DocumentEditor editor, DragPayload payload, DropCandidate landing)
    {
        if (landing.SlotName is not { } slotName)
        {
            return DocumentEditResult.Refused("That hole cannot hold anything.", "slot-no-name");
        }

        var before = DocumentLists.Find(editor.Project, landing.ParentId)?.Inputs.GetValueOrDefault(slotName);
        var reporter = payload.First ?? BlockFactory.Preview(BlockCatalog.Find(payload.Kind)!);
        var bind = new BindSlot(landing.ParentId, slotName, before, new BlockInput { Block = reporter });

        return payload.IsMove
            ? editor.Transaction("Move into slot", [new DeleteRun(payload.Source!.Value, payload.Run!), bind])
            : editor.Execute(bind);
    }

    /// <summary>
    /// A drop onto empty canvas: a hat becomes a script, and anything else is refused.
    /// </summary>
    /// <remarks>
    /// The refusal is a correction to Part 9.5's table, which offers "free canvas: stacks (creates a new
    /// script) or hats". A stack has no hat of its own, and inventing one — whatever the target's kind
    /// happens to suggest — is how a document ends up with two scripts that both say "when action clicked".
    /// Scratch refuses it too, for the same reason. Hats are still accepted, and a run dragged out of a
    /// C-block can be given a script by dragging a hat beside it afterwards.
    /// </remarks>
    private static DocumentEditResult AsNewScript(
        DocumentEditor editor,
        VisualTarget target,
        DragPayload payload,
        BlockDescriptor descriptor,
        double x,
        double y)
    {
        if (!descriptor.IsHat)
        {
            return DocumentEditResult.Refused(
                "Only a hat block starts a script. Drag a hat here, or drop this one into a script.",
                "canvas-needs-hat");
        }

        // A hat dragged off its own script moves that script rather than creating a second one, and it
        // reuses the script object: anything holding a reference to it — the script strip, the canvas
        // column — keeps describing the script that is actually in the document.
        if (payload.First is { } first
            && target.Scripts.FirstOrDefault(script => ReferenceEquals(script.Hat, first)) is { } existing)
        {
            return editor.MoveScript(existing, x, y);
        }

        var hat = payload.First ?? BlockFactory.Preview(descriptor);

        return editor.AddScript(target, new VisualScript
        {
            Id = $"script-{Guid.NewGuid().ToString("n")[..8]}",
            Name = descriptor.Label,
            Hat = hat,
            X = x,
            Y = y,
        });
    }

    /// <summary>
    /// Whether the landing index is inside the run being carried, in the body it came from.
    /// </summary>
    /// <remarks>
    /// Only the same body counts. A run dropped into a nested body is always a real move, and a run
    /// dropped into a <em>different</em> script is always a real move; the self-reference rule exists for
    /// one situation only, which is the pointer being over the blocks it is already holding.
    /// </remarks>
    private static bool IsInsideOwnRun(
        DocumentEditor editor,
        DragPayload payload,
        BodyRef where,
        int index)
    {
        if (payload.Source != where)
        {
            return false;
        }

        var from = DocumentLists.IndexOf(editor.Project, where, payload.Run!);
        return index > from && index <= from + payload.Run!.Count;
    }
}
