namespace DeckForge.Core.Visual;

/// <summary>What one piece of a block's label is.</summary>
public enum BlockLabelRunKind
{
    /// <summary>Words from the label template.</summary>
    Text,

    /// <summary>A slot the label has a hole for, which the canvas draws as an editable field.</summary>
    Slot,

    /// <summary>A menu the label has a hole for, which the canvas draws as a dropdown.</summary>
    Menu,
}

/// <summary>
/// One piece of a block's label: either words, or the name of the slot or menu that goes there.
/// </summary>
/// <param name="Kind">Which of the three this is.</param>
/// <param name="Text">The words, for a <see cref="BlockLabelRunKind.Text"/> run.</param>
/// <param name="Name">The slot or menu's name, for the other two.</param>
/// <param name="Slot">The slot descriptor, when the catalog declares one under that name.</param>
/// <param name="Menu">The menu descriptor, when the catalog declares one under that name.</param>
/// <remarks>
/// The plan is derived, never stored: the label template in the catalog row is the only place a block's
/// words live, and a tile that carried its own copy of them would drift from it the first time a row
/// was reworded. Part 9.6 asks for a slot editor chosen by preferred type and Part 21 for localized
/// labels; both need this split, and both get it from the same single parse.
/// </remarks>
public sealed record BlockLabelRun(
    BlockLabelRunKind Kind,
    string Text,
    string Name,
    SlotDescriptor? Slot = null,
    MenuDescriptor? Menu = null);

/// <summary>
/// Turns a catalog row's label template into the pieces a tile draws.
/// </summary>
/// <remarks>
/// The label is written once, as <c>"repeat {count}"</c>, and read from two places: the canvas needs the
/// hole for the count, and search needs the whole string. Splitting it here rather than in XAML keeps
/// both honest, and it lets a test assert the thing that actually breaks — a label naming a slot the
/// row does not declare, which renders as the literal text <c>{count}</c> on a block.
/// </remarks>
public static class BlockLabel
{
    /// <summary>
    /// The label's pieces, in order, with the empty words between two holes dropped.
    /// </summary>
    /// <remarks>
    /// A marker naming neither a slot nor a menu becomes a text run holding the marker verbatim rather
    /// than being skipped, so the defect is visible on the block instead of a silently truncated label.
    /// <c>BlockLabelTests</c> asserts no shipping row does this.
    /// </remarks>
    public static IReadOnlyList<BlockLabelRun> Plan(BlockDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var runs = new List<BlockLabelRun>();
        var label = descriptor.Label ?? string.Empty;
        var index = 0;

        void AddText(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                runs.Add(new BlockLabelRun(BlockLabelRunKind.Text, text.Trim(), string.Empty));
            }
        }

        while (index < label.Length)
        {
            var open = label.IndexOf('{', index);
            if (open < 0)
            {
                AddText(label[index..]);
                break;
            }

            AddText(label[index..open]);

            var close = label.IndexOf('}', open + 1);
            if (close < 0)
            {
                // No closing brace: the rest is words, braces and all. A malformed row must not throw
                // while the palette is being built, because the palette is how you would fix it.
                AddText(label[open..]);
                break;
            }

            var name = label[(open + 1)..close];
            var slot = descriptor.Slot(name);
            var menu = descriptor.Menu(name);

            runs.Add(slot is not null
                ? new BlockLabelRun(BlockLabelRunKind.Slot, string.Empty, name, slot, null)
                : menu is not null
                    ? new BlockLabelRun(BlockLabelRunKind.Menu, string.Empty, name, null, menu)
                    : new BlockLabelRun(BlockLabelRunKind.Text, $"{{{name}}}", name));

            index = close + 1;
        }

        return runs;
    }

    /// <summary>
    /// The label's pieces, localized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 21.1 keys a whole label rather than each of its words, and that is what makes the shape of a
    /// translation a contract: the translated string still contains <c>{count}</c>, and the same parse runs
    /// over it to find the holes. Word-level keys would survive a reworded label better but would leave the
    /// translator responsible for the order the words appear in, which is the one thing a translator
    /// should not have to guess.
    /// </para>
    /// <para>
    /// A translation whose placeholders do not match the row's slots degrades the same way an English label
    /// does: an unknown <c>{name}</c> becomes a text run holding the marker, so a mistranslated hole is
    /// visible on the block instead of silently disappearing from it.
    /// </para>
    /// </remarks>
    /// <param name="descriptor">The block's row.</param>
    /// <param name="lookup">Where translations come from.</param>
    public static IReadOnlyList<BlockLabelRun> Plan(BlockDescriptor descriptor, IBlockTextLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(lookup);

        var translated = BlockLabelKeys.Text(lookup, BlockLabelKeys.Label(descriptor), descriptor.Label ?? string.Empty);

        return translated == descriptor.Label
            ? Plan(descriptor)
            : Plan(descriptor with { Label = translated }, lookup);
    }

    /// <summary>
    /// What the label says when nothing is filled in: the words, and a hint in each hole.
    /// </summary>
    /// <remarks>
    /// Used for the palette's search results and for tooltips, where a miniature tile would be too small
    /// to read. The hole text is deliberately short - it is a placeholder, not a value, and anything
    /// longer turns "repeat {count}" into a sentence.
    /// </remarks>
    public static string PreviewText(BlockDescriptor descriptor) =>
        PreviewText(descriptor, NoTranslations.Instance);

    /// <summary>What the label says when nothing is filled in, localized.</summary>
    /// <param name="descriptor">The block's row.</param>
    /// <param name="lookup">Where translations come from.</param>
    public static string PreviewText(BlockDescriptor descriptor, IBlockTextLookup lookup) =>
        string.Join(
            " ",
            Plan(descriptor, lookup).Select(run => run.Kind switch
            {
                BlockLabelRunKind.Text => run.Text,
                BlockLabelRunKind.Menu => run.Menu?.Default ?? "?",
                _ => Hole(run.Slot),
            }));

    /// <summary>
    /// What a menu's chosen value says, translated when it is one of the row's own options.
    /// </summary>
    /// <remarks>
    /// The condition is the point: the chosen value is the user's data, so a value that is *not* a declared
    /// option - a stale field from an older row, or a block from a plugin whose catalogue has moved on - is
    /// shown as written. Translating it would be a lookup miss reported as a wrong word.
    /// </remarks>
    /// <param name="descriptor">The block's row.</param>
    /// <param name="menu">The menu.</param>
    /// <param name="chosen">The value in the document.</param>
    /// <param name="lookup">Where translations come from.</param>
    public static string MenuText(
        BlockDescriptor descriptor,
        MenuDescriptor menu,
        string? chosen,
        IBlockTextLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(lookup);

        var value = string.IsNullOrWhiteSpace(chosen) ? menu.Default ?? string.Empty : chosen!;

        return menu.Options.Contains(value, StringComparer.Ordinal)
            ? BlockLabelKeys.Text(lookup, BlockLabelKeys.Menu(descriptor, value), value)
            : value;
    }

    /// <summary>
    /// What one hole says when nothing is filled in: the row's own default if it declared one, and
    /// otherwise a hint chosen by type.
    /// </summary>
    /// <remarks>
    /// The default first, because <c>wait 1 seconds</c> says what the block is and <c>wait 0 seconds</c>
    /// says only what it accepts. By type afterwards, so "wait until &lt;&gt;" reads as a condition and
    /// "wait &lt;n&gt; ms" as a number. Part 9.6 turns each of these into a real editor in Phase 5; here
    /// they are only what an empty hole is allowed to look like.
    /// </remarks>
    public static string Hole(SlotDescriptor? slot) =>
        !string.IsNullOrWhiteSpace(slot?.DefaultText) ? slot!.DefaultText : Placeholder(slot);

    /// <summary>The words that stand in for a slot with no default and nothing in it.</summary>
    /// <remarks>
    /// By preferred type, so an empty condition reads as <c>&lt;&gt;</c> and an empty text slot as
    /// <c>text</c>. These are placeholders, not values: anything longer turns "repeat {count}" into a
    /// sentence.
    /// </remarks>
    public static string Placeholder(SlotDescriptor? slot) => slot?.Type switch
    {
        SlotType.Number => "0",
        SlotType.Boolean => "<>",
        SlotType.List => "list",
        SlotType.Variable => "var",
        SlotType.HostVariable or SlotType.UserVariable => "host var",
        SlotType.Parameter => "parameter",
        SlotType.Event => "event",
        SlotType.Action => "action",
        SlotType.Script => "script",
        SlotType.Widget => "widget",
        SlotType.ConfigEntry => "entry",
        SlotType.File => "file",
        SlotType.Icon => "icon",
        SlotType.Color => "colour",
        SlotType.Procedure => "procedure",
        _ => "text",
    };
}