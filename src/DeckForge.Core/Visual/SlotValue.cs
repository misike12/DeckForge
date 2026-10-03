using System.Globalization;

namespace DeckForge.Core.Visual;

/// <summary>
/// What a slot or a menu currently holds, in a form an editor can bind to without parsing anything.
/// </summary>
/// <remarks>
/// Part 9.6 lists ten editor types by slot type, and the temptation is one editor class per type with the
/// switch on the slot's type in every one of them. This is the other shape: the value is read once into a
/// small set of properties, and each editor binds to the property it cares about. The alternative puts the
/// type switch in the view, ten times, and the tenth one is the one that will be wrong.
/// </remarks>
public sealed record SlotValue
{
    /// <summary>Whether the slot holds a nested reporter rather than a literal.</summary>
    public bool HasBlock { get; init; }

    /// <summary>The nested reporter's kind, when there is one.</summary>
    public string? BlockKind { get; init; }

    /// <summary>The nested reporter's label, when there is one.</summary>
    public string? BlockLabel { get; init; }

    /// <summary>The literal as typed, for text slots.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The number, for numeric slots.</summary>
    public double Number { get; init; }

    /// <summary>The flag, for boolean slots.</summary>
    public bool Boolean { get; init; }

    /// <summary>The name, for a slot that references something the document declares.</summary>
    public string? Reference { get; init; }

    /// <summary>Whether nothing at all is set.</summary>
    /// <summary>
    /// Whether a reference slot holds no name.
    /// </summary>
    /// <remarks>
    /// Not called <c>IsEmpty</c> on purpose, because "empty" means two different things here. A number of
    /// zero and a text slot holding nothing are both <em>set</em> — a count of 0 is a real answer, and
    /// clearing it is a different gesture from setting it. A reference slot holds only a name, so an
    /// empty name really does mean nothing is chosen, and that is the one case a "clear" control has to be
    /// able to produce.
    /// </remarks>
    public bool HasNoReference => !HasBlock && string.IsNullOrEmpty(Reference);

    /// <summary>The value as a number, parsed from text, for a numeric slot.</summary>
    /// <remarks>
    /// Invariant culture, for the same reason the emitter and the sidecar are: a document typed on a
    /// machine with a comma decimal separator would otherwise emit a different number on every other
    /// machine, and the failure is a wrong value rather than a parse error.
    /// </remarks>
    public static double ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>The value as text, formatted the same way.</summary>
    public static string FormatNumber(double value) =>
        value.ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary>Reads a slot's current value out of a block.</summary>
    public static SlotValue Read(Block block, SlotDescriptor slot)
    {
        if (block.Inputs.GetValueOrDefault(slot.Name) is not { } input)
        {
            return new SlotValue();
        }

        if (input.Block is { } nested)
        {
            var descriptor = BlockCatalog.Find(nested.Kind);
            return new SlotValue
            {
                HasBlock = true,
                BlockKind = nested.Kind,
                BlockLabel = descriptor?.Label ?? nested.Kind,
                Text = descriptor is null ? nested.Kind : Core.Visual.BlockLabel.PreviewText(descriptor),
            };
        }

        return new SlotValue
        {
            Text = Describe(input),
            Number = input.Number ?? ParseNumber(input.Text ?? string.Empty),
            Boolean = input.Boolean ?? ParseBoolean(input.Text),
            Reference = input.Variable,
        };
    }

    /// <summary>Reads a menu's current option out of a block.</summary>
    public static string ReadMenu(Block block, MenuDescriptor menu) =>
        block.Fields.GetValueOrDefault(menu.Name) ?? menu.Default;

    /// <summary>
    /// Builds the input a slot should hold for a typed value.
    /// </summary>
    /// <param name="slot">The slot, which decides which member of the input carries the value.</param>
    /// <param name="text">What the user typed.</param>
    /// <remarks>
    /// One input per slot type, because <see cref="BlockInput"/> has one member per source and the emitter
    /// reads them separately. Putting a number in <c>Text</c> and a name in <c>Number</c> would be a
    /// document that validates and then emits the wrong thing.
    /// </remarks>
    public static BlockInput From(SlotDescriptor slot, string text) => slot.Type switch
    {
        SlotType.Number => new BlockInput { Number = ParseNumber(text) },
        SlotType.Boolean => new BlockInput { Boolean = ParseBoolean(text) },
        SlotType.List or SlotType.HostVariable or SlotType.UserVariable or SlotType.Parameter
            or SlotType.Event or SlotType.Action or SlotType.Script or SlotType.Widget
            or SlotType.ConfigEntry or SlotType.Icon or SlotType.Color or SlotType.Variable
            or SlotType.Procedure or SlotType.File
            // One member, not two. Both were set so that either reader found the value, but
            // BlockInput.IsAmbiguous counts non-null members and the loader refuses a document that has more
            // than one - so the editor was writing files its own reader would reject the moment a second
            // writer appeared or one field was added by hand. `Text` is the fallback the emitter already
            // prefers, so `Variable` alone carries it.
            => new BlockInput { Variable = text },
        _ => new BlockInput { Text = text },
    };

    /// <summary>
    /// Whether a typed value is acceptable in a slot, and why not when it is not.
    /// </summary>
    /// <remarks>
    /// Checked here rather than only by the validator so the editor can refuse a keystroke rather than
    /// accepting it and then showing a warning about a document the user is still typing. The validator
    /// keeps its own check, because a document can also arrive from a file or a migration.
    /// </remarks>
    public static string? Problem(SlotDescriptor slot, string text) => slot.Type switch
    {
        SlotType.Number when text.Trim().Length > 0 && !IsNumber(text) => "That is not a number.",
        SlotType.Number => null,
        SlotType.Boolean when text.Trim().Length > 0 && !IsBoolean(text) => "That is not true or false.",
        SlotType.Boolean => null,
        SlotType.Text when text.Contains('\n', StringComparison.Ordinal) => "A single value cannot contain a line break.",
        _ => null,
    };

    private static bool IsNumber(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool IsBoolean(string text) =>
        text.Trim().ToLowerInvariant() is "true" or "false" or "1" or "0" or "yes" or "no";

    private static bool ParseBoolean(string? text) =>
        text?.Trim().ToLowerInvariant() is "true" or "1" or "yes";

    /// <summary>
    /// What the block currently has in this slot, for handing to a command that needs the old value to
    /// undo with.
    /// </summary>
    /// <param name="block">The block being edited.</param>
    /// <param name="slot">The slot.</param>
    /// <remarks>
    /// The input itself rather than a copy, because <see cref="BindSlot"/> copies what it stores and a
    /// value read here is only ever used as the <c>before</c> of an edit. Null when the slot is empty,
    /// which is what the inverse needs: restoring "nothing was here" is removing the key, and restoring an
    /// input whose members are all null would leave an empty object behind that the validator reads as a
    /// set-but-blank value.
    /// </remarks>
    public static BlockInput? Current(Block block, SlotDescriptor slot) =>
        block.Inputs.GetValueOrDefault(slot.Name);

    /// <summary>One input as text, whichever member of it carries the value.</summary>
    private static string Describe(BlockInput input) => input.Kind switch
    {
        BlockInputKind.Text => input.Text ?? string.Empty,
        BlockInputKind.Number => FormatNumber(input.Number ?? 0),
        BlockInputKind.Boolean => (input.Boolean ?? false) ? "true" : "false",
        BlockInputKind.Variable => input.Variable ?? string.Empty,
        _ => string.Empty,
    };
}
