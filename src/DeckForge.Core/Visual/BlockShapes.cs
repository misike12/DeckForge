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

    /// <summary>
    /// A block a document may hold but the palette never offers.
    /// </summary>
    /// <remarks>
    /// Exactly one kind uses it: <c>legacy.unsupported</c>, the migration's record of a statement type
    /// from a build this one does not know. It has to be a real catalog entry — the emitter and the
    /// validator both need somewhere to look, and "a kind with no descriptor" is the failure the whole
    /// design is arranged around — but it must never be draggable, because a user has no way to fill it
    /// in. A shape says both at once, where a boolean flag beside <c>Shape</c> would be a second thing
    /// to keep in step.
    /// </remarks>
    Placeholder,
}

/// <summary>
/// The kind of value a slot prefers, and — when it names something the document declares — where the
/// editor draws its dropdown from.
/// </summary>
/// <remarks>
/// <para>
/// A preference, never a restriction. Part 5.3 of the design keeps Scratch's freedom — any reporter
/// fits any slot, and the emitter coerces at the use site — so this drives editor affordances and
/// warnings only. Refusing a drop here would be the wrong kind of strictness.
/// </para>
/// <para>
/// The last nine members are not value types at all, they are <em>sources</em>: a slot marked
/// <see cref="HostVariable"/> is filled by picking one of the plugin's declared variables, not by
/// typing text. One member per source, rather than a separate descriptor type, because the editor and
/// the validator both need to ask the same question — "what may go here, and what can I offer" — and
/// one enum answers both without a second lookup table to keep in step.
/// </para>
/// </remarks>
public enum SlotType
{
    /// <summary>Anything. The common case.</summary>
    Any,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A condition.</summary>
    Boolean,

    /// <summary>A list declared in this document, referenced by name.</summary>
    List,

    /// <summary>A variable the plugin declares, picked from the manifest.</summary>
    HostVariable,

    /// <summary>A user variable the host owns, picked from the host's names.</summary>
    UserVariable,

    /// <summary>One of the action's parameters, picked from the action's own declaration.</summary>
    Parameter,

    /// <summary>An event the plugin declares.</summary>
    Event,

    /// <summary>An action the plugin declares.</summary>
    Action,

    /// <summary>A host script the plugin declares.</summary>
    Script,

    /// <summary>A widget the plugin declares.</summary>
    Widget,

    /// <summary>A configuration entry the plugin declares.</summary>
    ConfigEntry,

    /// <summary>A path on disk.</summary>
    File,

    /// <summary>An icon reference.</summary>
    Icon,

    /// <summary>A colour literal.</summary>
    Color,

    /// <summary>A variable this document declares, in the scope nearest the block.</summary>
    Variable,

    /// <summary>A procedure this document declares.</summary>
    Procedure,
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
/// <param name="Expression">
/// The emitted expression or statement, as a template.
/// </param>
/// <param name="VerifiedAgainst">The Appendix A surface this mapping was read from.</param>
/// <param name="SuppressHostGuard">
/// True for the one block that *asks* whether a host session exists, which must not be preceded by a
/// guard that returns early when it does not.
/// </param>
/// <remarks>
/// <para>
/// The expression is a template: <c>{into}</c>, <c>{level}</c>, <c>{var}</c> and friends are replaced by
/// the emitter with the block's own field or slot value, the same convention the log template already
/// uses. A template keeps a row readable as one line, which matters when there are a hundred and fifty
/// of them to review.
/// </para>
/// <para>
/// <see cref="RequiresHost"/> and <see cref="RequiresInteraction"/> are <em>derived from the
/// expression</em> rather than stored as flags. A hand-maintained flag is a second source of truth that
/// can disagree with the code, and disagreeing here is expensive: the guard the emitter places before a
/// host call returns <c>NotConnected</c> from the whole action, so a block wrongly marked as touching
/// the host would fail an action that never needed a session. Deriving it makes that mistake
/// impossible rather than merely tested for.
/// </para>
/// </remarks>
public sealed record SdkMapping(string Expression, string VerifiedAgainst, bool SuppressHostGuard = false)
{
    /// <summary>Whether the mapping was read off the real assembly rather than assumed.</summary>
    public bool IsVerified => !string.IsNullOrWhiteSpace(VerifiedAgainst);

    /// <summary>Whether the block touches <c>_integration</c> and therefore needs the null guard.</summary>
    public bool RequiresHost =>
        !SuppressHostGuard && Expression.Contains("_integration", StringComparison.Ordinal);

    /// <summary>
    /// Whether the block touches a surface on <c>ActionExecutionContext</c> instead.
    /// </summary>
    /// <remarks>
    /// Dialogs and pickers live on the execution context, not on the integration context, and they are
    /// null until a session exists — so they need a check of their own rather than the integration guard.
    /// </remarks>
    public bool RequiresInteraction =>
        Expression.Contains("context.Ui", StringComparison.Ordinal)
        || Expression.Contains("context.Interactions", StringComparison.Ordinal);
}

/// <summary>A palette category: its name, colour and glyph.</summary>
/// <param name="Category">The enum member.</param>
/// <param name="Name">The display name.</param>
/// <param name="Hue">The base colour, as <c>#RRGGBB</c>. Part 7.2 of the design lists these.</param>
/// <param name="Glyph">A <c>SymbolRegular</c> member name, checked by the markup test.</param>
/// <param name="Summary">One line explaining what the category is for.</param>
/// <remarks>
/// <see cref="BlockCategory.Media"/> has no descriptor, because it has no blocks and the rail renders
/// the descriptors it is given: a heading with nothing under it is worse than an absent heading. The
/// decision still has somewhere to live — in the enum's comment, in <c>BlockCatalog.Dropped</c> and in
/// Part 7.6 of <c>visual.md</c> — which is what an empty category needs most.
/// </remarks>
public sealed record BlockCategoryDescriptor(
    BlockCategory Category,
    string Name,
    string Hue,
    string Glyph,
    string Summary);
