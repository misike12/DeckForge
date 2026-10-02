using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>One hole in a block's label.</summary>
/// <remarks>
/// <para>
/// The same model serves three things the design asked for separately: the control drawn in the tile, the
/// row the inspector edits, and the two strings a canvas thumbnail needs. Keeping them in one object is
/// what stops the tile's editor and the inspector's editor disagreeing about what a slot is — which is
/// what happens the moment each side reads <see cref="SlotDescriptor"/> and interprets it.
/// </para>
/// <para>
/// Read-only for Phase 3: there is no editor yet, so every value here comes from the document and
/// nothing writes back. That is deliberate. Editing arrives in Phase 5 with undo, and a field that
/// changes the document before undo exists is how work gets lost.
/// </para>
/// </remarks>
public sealed partial class SlotViewModel : ObservableObject
{
    private readonly Block _block;

    public SlotViewModel(BlockNodeViewModel owner, SlotDescriptor descriptor, BlockInput? input)
    {
        Owner = owner;
        Descriptor = descriptor;
        _block = owner.Block;

        Input = input;
        Value = Describe(input);
        Placeholder = BlockLabel.Hole(descriptor);
        IsEmpty = input is null || input.IsEmpty;

        Child = input?.Block is { } nested
            ? new BlockNodeViewModel(nested)
            : null;

        IsBoolean = descriptor.Type == SlotType.Boolean;
        IsNumeric = descriptor.Type == SlotType.Number;
        IsReference = input?.Variable is not null;
    }

    /// <summary>
    /// The inspector's row for this slot, once it has been built.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="InspectorViewModel.Build"/> rather than looked up on demand, because the row is a
    /// view model with its own state — the draft a user is typing into — and re-creating one per keystroke
    /// to read one number would throw that state away. Null while the panel is showing a different block,
    /// which every caller here treats as "nothing to do" rather than as an error.
    /// </remarks>
    public SlotEditor? Editor { get; internal set; }

    /// <summary>The block this slot belongs to, so a nested reporter knows its own context.</summary>
    public BlockNodeViewModel Owner { get; }

    /// <summary>What the catalogue says about the slot.</summary>
    public SlotDescriptor Descriptor { get; }

    /// <summary>What the document holds in it.</summary>
    public BlockInput? Input { get; }

    /// <summary>The block nested in this slot, when a reporter has been dropped in.</summary>
    public BlockNodeViewModel? Child { get; }

    /// <summary>The slot's name in the catalogue, used for the inspector and for pickers.</summary>
    public string Name => Descriptor.Name;

    /// <summary>What the tile shows inside the hole.</summary>
    public string Value { get; }

    /// <summary>What an empty hole shows instead.</summary>
    public string Placeholder { get; }

    /// <summary>What the tile shows inside the hole: the value, or the placeholder when there is none.</summary>
    public string Display => IsEmpty ? Placeholder : Value;

    /// <summary>Whether nothing has been put in.</summary>
    public bool IsEmpty { get; }

    /// <summary>Whether a reporter has been dropped into this hole.</summary>
    public bool HasChild => Child is not null;

    /// <summary>What the tile's tooltip says about this hole.</summary>
    public string ToolTipText => Descriptor.Label is { Length: > 0 } label
        ? $"{label}: {Display}"
        : Display;

    /// <summary>Whether the hole prefers a condition, which is drawn as a hexagon's worth of inset.</summary>
    public bool IsBoolean { get; }

    /// <summary>Whether the hole prefers a number.</summary>
    public bool IsNumeric { get; }

    /// <summary>Whether the hole refers to something the document declares rather than holding a value.</summary>
    public bool IsReference { get; }

    /// <summary>Whether the block may be saved with this hole empty.</summary>
    public bool IsRequired => Descriptor.Required;

    /// <summary>
    /// A slot's contents as text.
    /// </summary>
    /// <remarks>
    /// One place decides, because the four members of <see cref="BlockInput"/> each have a reasonable
    /// string form and picking between them at every call site is how a boolean ends up rendering as
    /// <c>True</c> on one surface and <c>true</c> on another. Numbers use the invariant culture, which
    /// is the same rule the emitter and the sidecar follow.
    /// </remarks>
    private static string Describe(BlockInput? input) => input?.Kind switch
    {
        BlockInputKind.Text => input.Text ?? string.Empty,
        BlockInputKind.Number => input.Number?.ToString("0.############", CultureInfo.InvariantCulture) ?? string.Empty,
        BlockInputKind.Boolean => input.Boolean == true ? "true" : "false",
        BlockInputKind.Variable => input.Variable ?? string.Empty,
        _ => string.Empty,
    };
}

/// <summary>One inline dropdown on a block.</summary>
/// <remarks>
/// Drawn inside the tile, as Scratch does, rather than in the inspector only: a menu whose current
/// value is invisible on the block is a menu nobody can check without opening a side panel.
/// </remarks>
public sealed class MenuViewModel
{
    public MenuViewModel(MenuDescriptor descriptor, string? selected)
    {
        Descriptor = descriptor;
        Selected = selected ?? descriptor.Default;
        Options = descriptor.Options;
    }

    public MenuDescriptor Descriptor { get; }

    /// <summary>The menu's name in the catalogue, which is also its stable key.</summary>
    public string Name => Descriptor.Name;

    /// <summary>The options, in dropdown order.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>The option the document chose.</summary>
    public string Selected { get; }

    /// <summary>The chosen option, for the tile.</summary>
    public string Display => Selected;
}

/// <summary>One wrapped body: a label and the statements inside it.</summary>
public sealed class BodyViewModel
{
    public BodyViewModel(string name, string? label, IEnumerable<Block> statements)
    {
        Name = name;
        Label = label ?? string.Empty;
        Statements = new ObservableCollection<BlockNodeViewModel>(
            statements.Select(statement => new BlockNodeViewModel(statement)));
    }

    /// <summary>The body's key in the block's <c>bodies</c>.</summary>
    public string Name { get; }

    /// <summary>What the body is called beside it, such as "then" or "repeat".</summary>
    public string Label { get; }

    /// <summary>Whether there is a label to draw.</summary>
    public bool HasLabel => !string.IsNullOrEmpty(Label);

    /// <summary>The statements in the body, in order.</summary>
    public ObservableCollection<BlockNodeViewModel> Statements { get; }

    /// <summary>Whether nothing has been dropped in yet.</summary>
    public bool IsEmpty => Statements.Count == 0;
}