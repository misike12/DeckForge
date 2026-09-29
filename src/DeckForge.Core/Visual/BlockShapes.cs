namespace DeckForge.Core.Visual;

/// <summary>
/// The silhouette of a block, which is also the grammar of where it may go.
/// </summary>
/// <remarks>
/// The shape is not decoration. It is the type system made visible: a user cannot put a value where a
/// statement goes because the shapes will not connect, and the drop resolver (Part 9.5 of
/// <c>visual.md</c>) refuses the drop before any code generation happens. Keeping the vocabulary here,
/// in Core, is what lets that refusal be unit-tested without a window.
/// </remarks>
public enum BlockShape
{
    /// <summary>Starts a script. Flat top, no notch above.</summary>
    Hat,

    /// <summary>A plain statement: notch above, tab below.</summary>
    Stack,

    /// <summary>Ends a flow. Rounded bottom.</summary>
    Cap,

    /// <summary>Wraps one body.</summary>
    C,

    /// <summary>Wraps a then body and an else body.</summary>
    CIf,

    /// <summary>Wraps a chain of alternating condition and body.</summary>
    CChain,

    /// <summary>An oval that produces a value.</summary>
    Reporter,

    /// <summary>A hexagon that produces a boolean.</summary>
    BooleanReporter,

    /// <summary>An inline dropdown inside another block. Not draggable.</summary>
    Menu,

    /// <summary>Attaches to a block: a comment or a disable marker.</summary>
    Modifier,
}

/// <summary>
/// The kind of value a slot prefers.
/// </summary>
/// <remarks>
/// A preference, never a restriction. Part 5.3 of the design keeps Scratch's freedom — any reporter
/// fits any slot, and the emitter coerces at the use site — so this drives editor affordances and
/// warnings only. Refusing a drop here would be the wrong kind of strictness.
/// </remarks>
public enum SlotType
{
    Any,
    Text,
    Number,
    Boolean,
    List,
    HostVariable,
    UserVariable,
    Parameter,
    Event,
    Action,
    Script,
    Widget,
    ConfigEntry,
    File,
    Icon,
    Color,
    Menu,
}

/// <summary>The palette grouping a block belongs to, which is also its colour.</summary>
public enum BlockCategory
{
    Control,
    Deck,
    Ui,
    Events,
    Sensing,
    Operators,
    Variables,
    Lists,
    Parameters,
    Network,
    Procedures,
    /// <summary>Media is deliberately empty: see Part 7.16 — the SDK offers no host caller for it.</summary>
    Media,
}

/// <summary>One value slot on a block.</summary>
/// <param name="Name">The key used in the block's <c>inputs</c> object.</param>
/// <param name="Type">The preferred type, used for affordances and warnings.</param>
/// <param name="Required">Whether the block may be saved with the slot empty.</param>
/// <param name="Label">The label shown in the inspector; null means derive it from <paramref name="Name"/>.</param>
/// <param name="DefaultText">The literal a fresh block starts with, when the slot holds a value.</param>
public sealed record SlotDescriptor(
    string Name,
    SlotType Type,
    bool Required = true,
    string? Label = null,
    string? DefaultText = null);

/// <summary>One inline dropdown on a block.</summary>
/// <param name="Name">The key used in the block's <c>fields</c> object.</param>
/// <param name="Options">The option keys, in the order they appear in the dropdown.</param>
/// <param name="Default">The option a fresh block starts with. Must be one of <paramref name="Options"/>.</param>
/// <param name="Label">The label shown in the inspector.</param>
/// <remarks>
/// Menu keys are stable identifiers, not display text: they are what the emitter switches on and what
/// the user's document stores. Display text comes from the resx key in Part 21, so renaming a label
/// never invalidates a saved document.
/// </remarks>
public sealed record MenuDescriptor(
    string Name,
    IReadOnlyList<string> Options,
    string Default,
    string? Label = null);

/// <summary>A named body a container block wraps, such as <c>then</c> or <c>else</c>.</summary>
/// <param name="Name">The key used in the block's <c>bodies</c> object.</param>
/// <param name="Label">The label shown beside the body in the canvas.</param>
/// <param name="Required">Whether an empty body is a warning.</param>
public sealed record BodyDescriptor(string Name, string? Label = null, bool Required = false);

/// <summary>
/// How a block reaches the host, quoted from the assembly the mapping was verified against.
/// </summary>
/// <param name="Expression">The generated expression or statement shape, without the guard.</param>
/// <param name="RequiresHost">True when the block calls <c>_integration</c> and needs the null guard.</param>
/// <param name="RequiresInteraction">True when the block calls <c>context.Ui</c> or <c>context.Interactions</c>, which are null until a session exists and need a check of their own.</param>
/// <param name="VerifiedAgainst">The Appendix A row this mapping came from, or null when unverified.</param>
public sealed record SdkMapping(
    string Expression,
    bool RequiresHost = true,
    bool RequiresInteraction = false,
    string? VerifiedAgainst = null)
{
    /// <summary>Whether the mapping was read off the real assembly rather than assumed.</summary>
    public bool IsVerified => !string.IsNullOrWhiteSpace(VerifiedAgainst);
}

/// <summary>A palette category: its name, colour and glyph.</summary>
/// <param name="Category">The enum member.</param>
/// <param name="Name">The display name.</param>
/// <param name="Hue">The base colour, as <c>#RRGGBB</c>. Part 7.2 of the design lists these.</param>
/// <param name="Glyph">A <c>SymbolRegular</c> member name, checked by the markup test.</param>
/// <param name="Summary">One line explaining what the category is for.</param>
public sealed record BlockCategoryDescriptor(
    BlockCategory Category,
    string Name,
    string Hue,
    string Glyph,
    string Summary);
