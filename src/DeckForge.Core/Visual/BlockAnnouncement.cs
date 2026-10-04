namespace DeckForge.Core.Visual;

/// <summary>
/// What a screen reader should be told about one block.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.8 asks for exactly this: a tile exposes "repeat 10, C block, 3 statements inside". The tile
/// itself is WPF, and an automation peer cannot be reached from a test in this repository — so the text a
/// peer announces is computed here, where a test can assert the words and not merely that an object
/// exists.
/// </para>
/// <para>
/// In Core for the same reason <see cref="BlockLabel"/> is. A screen-reader string is a second reading of
/// the block, and two readings of the same block written in two places is how the canvas ends up saying
/// "repeat 10" and the reader saying "repeat". The label, the shape and the body's contents are all already
/// facts about the document, and this puts all three in one sentence.
/// </para>
/// <para>
/// The wording is deliberately plain and deliberately says the shape. Shape is not decoration in this
/// editor — it is the type system made visible, and Part 9.5 refuses a drop because two silhouettes do not
/// fit. A user who cannot see the silhouettes therefore cannot know why a drop was refused, which is the
/// one fact the whole part of the design rests on.
/// </para>
/// </remarks>
public static class BlockAnnouncement
{
    /// <summary>
    /// The block's label with its holes filled, as the tile draws it.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="lookup">The localised strings, or null for the catalogue's own.</param>
    /// <remarks>
    /// <para>
    /// Written into the catalogue's own template rather than assembled from
    /// <see cref="BlockLabel.Plan"/>, and that is a deliberate difference. The plan is what a <em>palette</em>
    /// needs: its pieces are laid out separately and it drops the empty words between two holes, because
    /// "set … to 1" with three separate runs is three separate text elements. Assembling a sentence from
    /// those pieces loses the spacing — the first version of this read "setvariable countto42", which is
    /// technically the label and useless aloud.
    /// </para>
    /// <para>
    /// The hole list comes from the descriptor rather than from the block's own inputs, so a hole the
    /// document never filled still says something. A repeated block with no <c>count</c> in it reads
    /// "repeat empty" instead of "repeat {count}" — the first is a fact about the document and the second is
    /// the shape of a template leaking into a sentence somebody is listening to.
    /// </para>
    /// <para>
    /// This is also the interpreter's label, which used to be a second implementation of the same reading.
    /// One reading, so a trace and a screen reader cannot disagree about what a block says.
    /// </para>
    /// </remarks>
    public static string Label(Block block, IBlockTextLookup? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (BlockCatalog.Find(block.Kind) is not { } descriptor)
        {
            // The kind rather than an empty string. A block from a newer build still has an id and a kind,
            // and "mystery.block" is a sentence a reader can repeat back; silence is not.
            return block.Kind;
        }

        var text = descriptor.Label ?? string.Empty;

        foreach (var slot in descriptor.Slots ?? [])
        {
            text = text.Replace("{" + slot.Name + "}", Hole(block, slot.Name));
        }

        // Menus come out of Fields, not Inputs, and a hole this forgets to fill shows the marker itself -
        // so a trace read "log {level} Action ran" for a block that had chosen a level.
        foreach (var menu in descriptor.Menus ?? [])
        {
            text = text.Replace(
                "{" + menu.Name + "}",
                BlockLabel.MenuText(descriptor, menu, block.Field(menu.Name), lookup ?? NoTranslations.Instance));
        }

        return string.IsNullOrWhiteSpace(text) ? descriptor.Label ?? block.Kind : text;
    }

    /// <summary>
    /// The whole announcement: the label, what shape it is, and how much is inside it.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="lookup">The localised strings, or null for the catalogue's own.</param>
    /// <param name="position">
    /// Where the block sits among the blocks on its canvas, as a one-based number, and how many there are
    /// in all. Zero for either one means "not known", which is the honest answer for a palette row that is
    /// not in any document.
    /// </param>
    /// <param name="of">
    /// How many blocks the position is out of.
    /// </param>
    /// <remarks>
    /// <para>
    /// The wording is Part 9.8's own: "repeat 10, C block, 3 statements inside". It is quoted rather than
    /// paraphrased because the design wrote one concrete sentence and a paraphrase would be a second
    /// opinion about what a screen reader should hear.
    /// </para>
    /// <para>
    /// Position last, and only when it is known. Part 9.8 asks a screen reader to announce the block;
    /// where it is on the canvas is a second thing, and it is what lets somebody who cannot see the canvas
    /// follow "block 4 of 11" through a trace. It is appended rather than leading because the label is what
    /// identifies the block and everything after it is orientation.
    /// </para>
    /// </remarks>
    public static string Describe(
        Block block,
        IBlockTextLookup? lookup = null,
        int position = 0,
        int of = 0)
    {
        ArgumentNullException.ThrowIfNull(block);

        var parts = new List<string> { Label(block, lookup), Shape(block) };

        if (block.Disabled)
        {
            // Said before the shape because "disabled" changes what the sentence means: everything after it
            // is a description of a block that will not run.
            parts.Add("disabled");
        }

        var inside = Inside(block);
        if (inside > 0)
        {
            parts.Add($"{inside} statement{(inside == 1 ? string.Empty : "s")} inside");
        }
        else if (block.Disabled is false && IsContainer(block) && HasEmptyBody(block))
        {
            // An empty C-block and a C-block holding nothing are the same thing, and saying so is the
            // difference between a reader that tells the user the block is an unfinished thought and one
            // that stops at "C block".
            parts.Add("nothing inside yet");
        }

        if (position > 0 && of > 0)
        {
            parts.Add($"block {position} of {of}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The shape in words, which is the part of the announcement the eye would otherwise supply.
    /// </summary>
    /// <param name="block">The block.</param>
    public static string Shape(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (BlockCatalog.Find(block.Kind) is not { } descriptor)
        {
            return "block from a newer version of DeckForge";
        }

        return descriptor.Shape switch
        {
            BlockShape.Hat => "hat block",
            BlockShape.Stack => "stack block",
            BlockShape.Cap => "cap block",
            BlockShape.C => "C block",
            BlockShape.CIf => "if block",
            BlockShape.CChain => "if-else-if chain",
            BlockShape.Reporter => "reporter, holds a value",
            BlockShape.BooleanReporter => "boolean reporter, holds true or false",
            BlockShape.Menu => "dropdown",
            BlockShape.Modifier => "modifier",
            _ => "block",
        };
    }

    /// <summary>
    /// How many statements are wrapped inside a block, bodies and slots together.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <remarks>
    /// Counted through <see cref="Block.Walk"/> rather than through the descriptor's bodies, so a block
    /// with three statements in its body and a reporter in one of them says "4 statements inside" — which
    /// is true, and which is also how many things a user has to look at inside it.
    /// </remarks>
    public static int Inside(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var count = 0;

        foreach (var body in block.Bodies.Values)
        {
            foreach (var statement in body)
            {
                foreach (var nested in statement.Walk())
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Whether the block is a container in the catalogue's terms.</summary>
    private static bool IsContainer(Block block) =>
        BlockCatalog.Find(block.Kind)?.IsContainer == true;

    /// <summary>Whether a container has a body and it is empty.</summary>
    private static bool HasEmptyBody(Block block) =>
        block.Bodies.Count > 0 && block.Bodies.Values.All(body => body.Count == 0);

    /// <summary>
    /// The value in a slot, as the tile shows it.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="name">The slot's name.</param>
    /// <remarks>
    /// <para>
    /// The same reading order as <see cref="SlotValue"/>, and for the same reason: the canvas must not say
    /// 5 where the tile says "abc", or a reader and a sighted user are looking at two different blocks.
    /// </para>
    /// <para>
    /// A variable slot reads as the bare name, not as "variable count". That is what the tile draws, and
    /// the reading is short enough to say out loud; <c>BlockSemantics</c>'s <c>NameSlot</c> flag exists
    /// because <c>set {var}</c> and <c>get {var}</c> hold the same kind of input and mean opposite things
    /// by it, and a sentence reading "set variable count to 1" would be an answer to a question nobody
    /// asked.
    /// </para>
    /// </remarks>
    private static string Hole(Block block, string name)
    {
        if (!block.Inputs.TryGetValue(name, out var input))
        {
            return "empty";
        }

        if (input.Block is { } nested)
        {
            return Label(nested);
        }

        return input.Kind switch
        {
            BlockInputKind.Number => (input.Number ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture),
            BlockInputKind.Boolean => (input.Boolean ?? false) ? "true" : "false",
            BlockInputKind.Variable => input.Variable ?? "empty",
            _ => string.IsNullOrEmpty(input.Text) ? "empty" : input.Text,
        };
    }
}
