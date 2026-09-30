using System.Globalization;

namespace DeckForge.Core.Visual;

/// <summary>
/// Builds blocks: a block of a given kind, filled in with something plausible.
/// </summary>
/// <remarks>
/// <para>
/// Two callers, one rule. The palette needs a miniature of every block, and the sample document on the
/// Phase 3 page needs every block again with real values in it. Writing them by hand would be a hundred
/// and fifty blocks that exist only for those two screens, and any catalog row added afterwards would
/// be missing from them — which is exactly the drift Part 2.3's table of past defects is about. Here a
/// fresh block is derived from its row, so a new row appears in both for free.
/// </para>
/// <para>
/// Every slot is filled, not just the required ones: a thumbnail whose holes are empty says nothing
/// about what the block is for, and <c>repeat {count}</c> is <c>repeat 10</c> while <c>repeat {count}</c>
/// is nothing.
/// </para>
/// </remarks>
public static class BlockFactory
{
    /// <summary>
    /// A block of this kind with every slot and menu filled in, taking its ids from
    /// <paramref name="nextId"/>.
    /// </summary>
    public static Block Create(BlockDescriptor descriptor, Func<string> nextId)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(nextId);

        var block = new Block { Kind = descriptor.Kind, Id = nextId() };

        foreach (var slot in descriptor.Slots ?? [])
        {
            block.Inputs[slot.Name] = DefaultInput(descriptor, slot);
        }

        foreach (var menu in descriptor.Menus ?? [])
        {
            block.Fields[menu.Name] = menu.Default;
        }

        return block;
    }

    /// <summary>
    /// A block of this kind with ids of its own, for a thumbnail that is never part of a document.
    /// </summary>
    /// <remarks>
    /// The ids are prefixed so a preview can never collide with a document's <c>b1</c>, <c>b2</c>
    /// sequence — the palette holds a hundred and fifty of these at once and they share one id space
    /// with anything the editor creates.
    /// </remarks>
    public static Block Preview(BlockDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var counter = 0;
        return Create(descriptor, () => $"p:{descriptor.Kind}#{counter++}");
    }

    /// <summary>The value a fresh block of this kind starts with in this slot.</summary>
    /// <remarks>
    /// Four cases, in the order they matter. A row's own <see cref="SlotDescriptor.DefaultText"/> wins,
    /// because it is the author saying what the block means — <c>wait 1 seconds</c> rather than
    /// <c>wait 0 seconds</c>. Then the two literal types, which need a literal. Then anything that
    /// names something the document declares, which is a reference rather than a value and must be one
    /// the caller can see is missing. Everything else is a short piece of text.
    /// </remarks>
    public static BlockInput DefaultInput(BlockDescriptor descriptor, SlotDescriptor slot)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(slot);

        if (!string.IsNullOrWhiteSpace(slot.DefaultText))
        {
            return slot.Type == SlotType.Number
                   && double.TryParse(
                       slot.DefaultText,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out var number)
                ? BlockInput.Of(number)
                : BlockInput.Of(slot.DefaultText);
        }

        return slot.Type switch
        {
            SlotType.Number => BlockInput.Of(0),
            SlotType.Boolean => BlockInput.Of(false),

            // A named source is a reference to something the document declares, so the default is a
            // name and not a value. Deriving it from the slot's own name rather than the block's kind
            // is what lets `var.set` and `var.get` refer to the same variable: two rows, one name, and
            // the declaration the sample writes is the one both of them meant.
            SlotType.Variable or SlotType.List => BlockInput.OfVariable(slot.Name),
            SlotType.Procedure => BlockInput.OfVariable("myProcedure"),
            SlotType.Parameter => BlockInput.OfVariable("text"),
            SlotType.HostVariable or SlotType.UserVariable => BlockInput.OfVariable("hostVar"),
            SlotType.Event => BlockInput.OfVariable("myEvent"),
            SlotType.Action => BlockInput.OfVariable("myAction"),
            SlotType.Script => BlockInput.OfVariable("myScript"),
            SlotType.Widget => BlockInput.OfVariable("myWidget"),
            SlotType.ConfigEntry => BlockInput.OfVariable("myEntry"),

            // A path, an icon and a colour are text the host will read; there is nothing to declare.
            SlotType.File => BlockInput.Of("path/to/file"),
            SlotType.Icon => BlockInput.Of("myIcon"),
            SlotType.Color => BlockInput.Of("#4C97FF"),

            _ => BlockInput.Of(BlockLabel.Placeholder(slot)),
        };
    }
}