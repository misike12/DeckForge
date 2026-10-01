using DeckForge.Core.Visual;

namespace DeckForge.Core.Visual;

/// <summary>
/// What the arrow keys, Delete and Ctrl+D do to the selected block.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.5's last rule is "nothing requires a mouse", and the parts of that which are actually hard are
/// not the key bindings — WPF does those — they are the questions of <em>where</em> the cursor is and
/// <em>what</em> the gesture means. Both are document questions, so both live here where they can be
/// tested: a traversal that can only be exercised by pressing arrow keys in a window is a traversal whose
/// behaviour at the ends of a stack, inside a loop body, and across a script boundary nobody has checked.
/// </para>
/// <para>
/// This type holds no state. Every method takes the current selection and returns the next one, which is
/// what makes the whole keyboard layer a pure function of (document, selection, key) and therefore
/// exhaustively testable.
/// </para>
/// </remarks>
public static class KeyboardMoves
{
    /// <summary>
    /// The block above or below the selected one, or null at the end of the stack.
    /// </summary>
    /// <param name="project">The document.</param>
    /// <param name="selected">The block to move from.</param>
    /// <param name="down">True for the next statement, false for the previous one.</param>
    /// <remarks>
    /// <para>
    /// Traversal stays inside a body. Moving from the last statement of a loop's body does not jump out to
    /// the container: a user pressing Down at the foot of a loop is asking about what follows inside it, and
    /// being thrown onto a different stack answers a question they did not ask. A flat document gives them
    /// nothing sensible either way, which is the gap analysis of Part 17.
    /// </para>
    /// <para>
    /// The hat is skipped in the downward direction, because a hat is not a statement and a stack's
    /// statements are the only things with neighbours. Selecting the hat is still how a user reaches a
    /// script, so traversal upward from the first statement does land on it.
    /// </para>
    /// </remarks>
    public static Block? Step(VisualProject project, Block? selected, bool down)
    {
        if (selected is null || DocumentLists.Locate(project, selected) is not { } where)
        {
            return null;
        }

        var body = DocumentLists.ListFor(project, where);
        var at = body.FindIndex(block => string.Equals(block.Id, selected.Id, StringComparison.Ordinal));

        if (at < 0)
        {
            return null;
        }

        // Up from the first statement reaches the hat, so a script is traversable from its own contents.
        if (!down && at == 0 && where.IsScriptBody)
        {
            return project.Targets
                .SelectMany(target => target.Scripts)
                .FirstOrDefault(script => string.Equals(script.Hat.Id, where.OwnerId, StringComparison.Ordinal))
                ?.Hat;
        }

        var next = at + (down ? 1 : -1);
        return next >= 0 && next < body.Count ? body[next] : null;
    }

    /// <summary>
    /// Every block in traversal order from the selected one, wrapping around.
    /// </summary>
    /// <remarks>
    /// For Tab and for "the next block after this one", neither of which should stop dead at the end of a
    /// stack: a user tabbing through slots wants to come back round, and a control that stops at the end
    /// gives no sign that anything else exists.
    /// </remarks>
    public static IEnumerable<Block> Cycle(VisualProject project, Block? selected, bool forward)
    {
        var all = project.Blocks().Where(block => !IsHat(project, block)).ToList();

        if (all.Count == 0)
        {
            return [];
        }

        var at = selected is null ? -1 : all.FindIndex(block => string.Equals(block.Id, selected.Id, StringComparison.Ordinal));

        if (at < 0)
        {
            return forward ? all : all.AsEnumerable().Reverse();
        }

        return Enumerable.Range(0, all.Count).Select(step => all[(at + (forward ? 1 : -1) * (step + 1) + all.Count * 2) % all.Count]);
    }

    /// <summary>
    /// Where a block would land if it moved one place up or down in its own body.
    /// </summary>
    /// <param name="project">The document.</param>
    /// <param name="block">The block to move.</param>
    /// <param name="down">True to move it later in the stack.</param>
    /// <returns>A command, or null when the move is not possible.</returns>
    /// <remarks>
    /// <para>
    /// This moves <em>one block</em> and swaps it with its neighbour, which is not what a pointer drag
    /// does, and the difference is worth stating plainly because the first version of this method got it
    /// wrong.
    /// </para>
    /// <para>
    /// A drag carries the whole tail, because grabbing statement <em>n</em> and leaving 1..n-1 behind
    /// orphans them. A keyboard's Ctrl+Down has no such problem to solve: the user has named exactly which
    /// block they mean by having selected it, and "move this one down" is the one gesture that reorders a
    /// stack. Carrying the tail would make Ctrl+Down a no-op for every block except the top of the stack,
    /// since everything below a block is already at the bottom of the stack — a binding that looks
    /// present and does nothing.
    /// </para>
    /// <para>
    /// Returns null rather than a refusing command at the ends of a stack, because "that is not possible"
    /// and "that is a thing you asked for and I declined" want different answers. The first is silence;
    /// the second is a complaint, and a user holding Ctrl+Down at the bottom of a stack does not need to
    /// be told the stack ends.
    /// </para>
    /// </remarks>
    public static DocumentCommand? Nudge(VisualProject project, Block block, bool down)
    {
        if (DocumentLists.Locate(project, block) is not { } where)
        {
            return null;
        }

        var body = DocumentLists.ListFor(project, where);
        var at = body.FindIndex(candidate => string.Equals(candidate.Id, block.Id, StringComparison.Ordinal));

        if (at < 0)
        {
            return null;
        }

        var neighbour = at + (down ? 1 : -1);
        if (neighbour < 0 || neighbour >= body.Count)
        {
            return null;
        }

        // A delete and an insert, not a MoveRun. MoveRun's destination index is counted against the list
        // *before* the run is lifted out, and it shifts that index down by the run's own length when the
        // run came from the same body above the destination — which is right for a stack drag and wrong
        // here. Lifting one block from index 0 and inserting at index 1 would be shifted to 0, so the move
        // would do nothing at all. Naming both steps explicitly says what a swap is: take this block out,
        // put it back where its neighbour was.
        return new DocumentTransaction(
            down ? "Move block later" : "Move block earlier",
            [
                new DeleteRun(where, [block], "Move block"),
                new InsertRun(where, neighbour, [block], "Move block"),
            ]);
    }

    /// <summary>
    /// The command that deletes the selected block, or null when it is not a statement.
    /// </summary>
    /// <remarks>
    /// Null for a hat. Deleting a hat deletes its whole script, which is a different gesture with a
    /// different meaning and deserves its own confirmation; §9.5's Delete is about a block in a stack.
    /// </remarks>
    public static DocumentCommand? Delete(VisualProject project, Block block)
    {
        if (IsHat(project, block) || DocumentLists.Locate(project, block) is not { } where)
        {
            return null;
        }

        var body = DocumentLists.ListFor(project, where);
        var at = body.FindIndex(candidate => string.Equals(candidate.Id, block.Id, StringComparison.Ordinal));

        // One block, not the run below it, and the two are genuinely different gestures rather than the
        // same one by another route. A drag carries the tail because grabbing statement n and leaving
        // 1..n-1 behind orphans them. Delete has no such problem to solve: the stack closes up behind the
        // gap, which is what every editor does and what a user pressing one key expects. Deleting the tail
        // as well would turn a keystroke meant to remove one block into the loss of everything after it,
        // and that is a data-loss trap wearing the costume of a shortcut.
        return at < 0 ? null : new DeleteRun(where, [block], "Delete block");
    }

    /// <summary>
    /// The command that copies the selected block and its whole run, inserted after itself.
    /// </summary>
    /// <param name="project">The document.</param>
    /// <param name="block">The block to duplicate.</param>
    /// <param name="nextId">A source of fresh block ids, so the copy is not a shared reference.</param>
    /// <returns>A command, or null when there is nothing to duplicate.</returns>
    /// <remarks>
    /// <para>
    /// Deep-copied, and that is the whole point. A shallow copy would leave the duplicate's nested bodies
    /// and its slot children shared with the original, so editing one would change the other — a document
    /// with two blocks that are secretly one.
    /// </para>
    /// <para>
    /// Ids are regenerated for the same reason. Two nodes sharing an id is a document the validator
    /// reports, the emitter emits twice, and <see cref="DocumentEditor"/> cannot undo correctly, because
    /// every command finds its block by id.
    /// </para>
    /// </remarks>
    public static DocumentCommand? Duplicate(VisualProject project, Block block, Func<string>? nextId = null)
    {
        if (DocumentLists.Locate(project, block) is not { } where)
        {
            return null;
        }

        var body = DocumentLists.ListFor(project, where);
        var at = body.FindIndex(candidate => string.Equals(candidate.Id, block.Id, StringComparison.Ordinal));

        if (at < 0)
        {
            return null;
        }

        var run = body.Skip(at).ToList();
        var fresh = nextId ?? DefaultId;
        var copies = run.Select(source => Copy(source, fresh)).ToList();

        // The insert goes after the run, and the index is counted before anything is taken out - the same
        // convention MoveRun uses, so a move and a duplicate that both end up above the block below them
        // land in the same place.
        return new DocumentTransaction(
            "Duplicate block",
            [new InsertRun(where, at + run.Count, copies, "Duplicate block")]);
    }

    /// <summary>
    /// Deep-copies a block, giving every node in the subtree a fresh id.
    /// </summary>
    public static Block Copy(Block source, Func<string> nextId)
    {
        var copy = new Block
        {
            Id = nextId(),
            Kind = source.Kind,
            Disabled = source.Disabled,
            Comment = source.Comment,
        };

        foreach (var (name, value) in source.Fields)
        {
            copy.Fields[name] = value;
        }

        foreach (var (name, input) in source.Inputs)
        {
            // A nested reporter is a block like any other, so it is copied rather than shared. Its own
            // inputs are copied in turn by the recursive call.
            copy.Inputs[name] = input.Block is { } nested
                ? new BlockInput { Block = Copy(nested, nextId) }
                : new BlockInput
                {
                    Text = input.Text,
                    Number = input.Number,
                    Boolean = input.Boolean,
                    Variable = input.Variable,
                };
        }

        foreach (var (name, children) in source.Bodies)
        {
            copy.Bodies[name] = children.Select(child => Copy(child, nextId)).ToList();
        }

        return copy;
    }

    /// <summary>Whether a block is the hat of a script.</summary>
    public static bool IsHat(VisualProject project, Block block) =>
        project.Targets
            .SelectMany(target => target.Scripts)
            .Any(script => string.Equals(script.Hat.Id, block.Id, StringComparison.Ordinal));

    /// <summary>
    /// The next candidate a keyboard drag could land on.
    /// </summary>
    /// <param name="project">The document.</param>
    /// <param name="current">The candidate the cursor is on, or null to start at the first one.</param>
    /// <param name="draggedKind">The kind being carried, so the walk skips zones that refuse it.</param>
    /// <param name="step">How far to advance, which may be negative.</param>
    /// <returns>The candidate, or null when there is none this shape can use.</returns>
    /// <remarks>
    /// <para>
    /// The same zone table the pointer uses, walked in document order rather than scored by distance. A
    /// keyboard has no pointer, so there is nothing to score against — the choice is "the next thing that
    /// would accept this", which is the only ordering a keyboard user can predict.
    /// </para>
    /// <para>
    /// Order is by the owning block's position, so a walk goes down the stack it is in before moving to
    /// the next one. That matches how a script is read and means a keyboard drag over a long script does
    /// not require stepping through every statement in every other one first.
    /// </para>
    /// </remarks>
    public static DropCandidate? StepZone(
        VisualProject project,
        DropCandidate? current,
        string draggedKind,
        int step = 1)
    {
        if (BlockCatalog.Find(draggedKind) is not { } descriptor)
        {
            return null;
        }

        var all = Zones(project, descriptor);
        if (all.Count == 0)
        {
            return null;
        }

        var at = current is null ? -1 : all.ToList().FindIndex(candidate => Same(candidate, current));

        // From nothing, start at the beginning going forwards and at the end going backwards, rather than
        // always starting at the top. A keyboard user stepping up expects to meet the last zone first.
        if (at < 0)
        {
            return step >= 0 ? all[0] : all[^1];
        }

        var next = (at + step + all.Count) % all.Count;
        return all[next];
    }

    /// <summary>
    /// Every drop zone in the document that accepts a shape, in reading order.
    /// </summary>
    /// <param name="project">The document.</param>
    /// <param name="descriptor">The shape being carried.</param>
    /// <remarks>
    /// Positions come from <see cref="StackLayout"/>, because there is no canvas here to measure: a
    /// keyboard cursor names a zone by what it is rather than by where it is, and the coordinates are
    /// only there to keep the candidate the same type as the pointer's.
    /// </remarks>
    public static IReadOnlyList<DropCandidate> Zones(VisualProject project, BlockDescriptor descriptor)
    {
        var zones = new List<DropCandidate>();

        foreach (var target in project.Targets)
        {
            foreach (var script in target.Scripts)
            {
                var rects = StackLayout.Layout(script);
                var candidates = DropResolver.CandidatesFor(script, rects);

                foreach (var candidate in candidates)
                {
                    if (DropResolver.Accepts(candidate, descriptor))
                    {
                        zones.Add(candidate);
                    }
                }

                zones.AddRange(Slots(script, descriptor));

                if (DropResolver.Accepts(new DropCandidate(DropTargetKind.Canvas, string.Empty), descriptor))
                {
                    // Last in its own script, so a forward walk reaches it only after everything inside the
                    // script it belongs to - which is the reading order, and it means dropping a hat on the
                    // canvas is the end of the walk rather than the start.
                    zones.Add(new DropCandidate(DropTargetKind.Canvas, script.Hat.Id, X: script.X, Y: script.Y));
                }
            }
        }

        return zones;
    }

    /// <summary>
    /// Every value slot in a script that accepts a shape, in reading order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The canvas measures its slots off the live <c>InputSlotView</c>s and there is nothing to measure
    /// here, so these candidates are enumerated from the catalogue and placed at the owning block's own
    /// rectangle with an offset along the label line.
    /// </para>
    /// <para>
    /// The coordinates are approximate and it does not matter. A keyboard cursor names a zone by what it
    /// <em>is</em> — "the seconds hole in repeat" — and never by where it is on screen, so these exist
    /// only to keep the candidate the same type as the pointer's. Omitting them instead would have meant a
    /// reporter's entire zone list was empty, and the one block shape whose whole purpose is filling holes
    /// would be the one shape the keyboard could not place.
    /// </para>
    /// </remarks>
    private static IEnumerable<DropCandidate> Slots(
        VisualScript script,
        BlockDescriptor descriptor)
    {
        if (!descriptor.IsReporter)
        {
            yield break;
        }

        var rects = StackLayout.Layout(script);

        var blocks = new List<Block> { script.Hat };
        blocks.AddRange(script.Body.SelectMany(statement => statement.Walk()));

        foreach (var block in blocks)
        {
            if (!rects.TryGetValue(block.Id, out var rect))
            {
                continue;
            }

            var row = BlockCatalog.Find(block.Kind);
            var slotIndex = 0;

            foreach (var slot in row?.Slots ?? [])
            {
                yield return new DropCandidate(
                    DropTargetKind.ValueSlot,
                    block.Id,
                    SlotName: slot.Name,
                    // Spaced along the header so two slots on one block are distinguishable in a list and
                    // so a slot's x sits inside its own block rather than at its left edge.
                    X: rect.X + BlockMetrics.TilePaddingX + (slotIndex * 24),
                    Y: rect.Y + BlockOutline.NotchDepth);

                slotIndex++;
            }
        }
    }

    /// <summary>Whether two candidates name the same place.</summary>
    private static bool Same(DropCandidate left, DropCandidate right) =>
        left.Kind == right.Kind
        && string.Equals(left.ParentId, right.ParentId, StringComparison.Ordinal)
        && string.Equals(left.BodyName ?? string.Empty, right.BodyName ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(left.SlotName ?? string.Empty, right.SlotName ?? string.Empty, StringComparison.Ordinal)
        && left.Index == right.Index;

    /// <summary>A fresh block id, for a duplicate with no id source supplied.</summary>
    private static string DefaultId() => $"b-{Guid.NewGuid().ToString("n")[..8]}";
}
