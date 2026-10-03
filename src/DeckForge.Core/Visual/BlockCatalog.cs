namespace DeckForge.Core.Visual;

/// <summary>
/// One block the palette can offer.
/// </summary>
/// <param name="Kind">The stable id stored in documents. Permanent: never renamed, never reused.</param>
/// <param name="Category">The palette group, which is also the block's colour.</param>
/// <param name="Label">The label template, with <c>{slot}</c> and <c>{field}</c> markers.</param>
/// <param name="Summary">One line for the palette tooltip.</param>
/// <param name="Shape">The silhouette, which is the grammar of where the block may go.</param>
/// <param name="Sdk">How the block reaches the host, quoted from the assembly it was verified against.</param>
/// <param name="Slots">The value slots, in label order.</param>
/// <param name="Menus">The inline dropdowns.</param>
/// <param name="Bodies">The wrapped bodies, for container shapes.</param>
/// <param name="CapabilityId">The plugin capability this needs, or null. Matches <c>CapabilityCatalog</c>.</param>
/// <param name="DocsPath">The offline docs page, or null.</param>
public sealed record BlockDescriptor(
    string Kind,
    BlockCategory Category,
    string Label,
    string Summary,
    BlockShape Shape,
    SdkMapping Sdk,
    IReadOnlyList<SlotDescriptor>? Slots = null,
    IReadOnlyList<MenuDescriptor>? Menus = null,
    IReadOnlyList<BodyDescriptor>? Bodies = null,
    string? CapabilityId = null,
    string? DocsPath = null)
{
    /// <summary>Whether the mapping was read off the real assembly rather than assumed.</summary>
    public bool IsVerified => Sdk.IsVerified;

    /// <summary>Whether the block wraps a body, and so can be nested into.</summary>
    public bool IsContainer => Bodies is { Count: > 0 };

    /// <summary>
    /// Whether the palette offers this block.
    /// </summary>
    /// <remarks>
    /// Everything is offerable except <see cref="BlockShape.Placeholder"/>. The distinction matters to
    /// the palette count, to search, and to the tests that pin both: a placeholder in the palette would
    /// be a block a user can drag and cannot fill in, which is worse than not offering it.
    /// </remarks>
    public bool IsPaletteBlock => Shape != BlockShape.Placeholder;

    /// <summary>Whether the block produces a value and therefore belongs in a slot.</summary>
    public bool IsReporter => Shape is BlockShape.Reporter or BlockShape.BooleanReporter;

    /// <summary>Whether the block starts a script.</summary>
    public bool IsHat => Shape == BlockShape.Hat;

    /// <summary>A slot by name, or null.</summary>
    public SlotDescriptor? Slot(string name) =>
        Slots?.FirstOrDefault(slot => string.Equals(slot.Name, name, StringComparison.Ordinal));

    /// <summary>A menu by name, or null.</summary>
    public MenuDescriptor? Menu(string name) =>
        Menus?.FirstOrDefault(menu => string.Equals(menu.Name, name, StringComparison.Ordinal));
}

/// <summary>A block the inventory found no honest way to emit, and why.</summary>
/// <param name="Kind">The id it would have had.</param>
/// <param name="Reason">The finding from §7.16 of <c>visual.md</c>.</param>
public sealed record DroppedBlock(string Kind, string Reason);

/// <summary>
/// Every block the palette can offer.
/// </summary>
/// <remarks>
/// <para>
/// The single source of truth for what a document may contain. Data rather than code, for the same
/// reason the block node is generic: a hundred and fifty-three kinds cannot each own a class, and a
/// switch that must be extended in six places is a switch that will be extended in five.
/// </para>
/// <para>
/// Rows live in <c>BlockCatalogRows.cs</c>, one method per category, so this file holds the rules and
/// that one holds the vocabulary. Nothing else in the catalog may be edited by hand without a test
/// noticing, which is what keeps the tables in <c>visual.md</c> honest.
/// </para>
/// </remarks>
public static partial class BlockCatalog
{
    /// <summary>
    /// Every shipping block, in palette order.
    /// </summary>
    /// <remarks>
    /// Built once. The order is category order, then the order the rows are written, so two builds
    /// cannot disagree about the palette and no sort is needed to render it.
    /// </remarks>
    public static IReadOnlyList<BlockDescriptor> All { get; } =
    [
        .. ControlRows(),
        .. DeckRows(),
        .. UiRows(),
        .. EventRows(),
        .. SensingRows(),
        .. OperatorRows(),
        .. VariableRows(),
        .. ListRows(),
        .. ParameterRows(),
        .. NetworkRows(),
        .. ProcedureRows(),
    ];

    /// <summary>The palette categories, in the order the rail shows them.</summary>
    public static IReadOnlyList<BlockCategoryDescriptor> Categories { get; } =
    [
        new(BlockCategory.Control, "Control", "#FFAB19", "PlayCircle24",
            "Start, branch, repeat and finish."),
        new(BlockCategory.Deck, "Deck", "#4C97FF", "WindowApps24",
            "Folders, profiles, clients and button state."),
        new(BlockCategory.Ui, "Looks", "#9966FF", "Megaphone24",
            "Notifications, dialogs, logs and widgets."),
        new(BlockCategory.Events, "Events", "#FFBF00", "Send24",
            "Publish and send."),
        new(BlockCategory.Sensing, "Sensing", "#5CB1D6", "DataUsage24",
            "Parameters, host variables, settings, the clock and ids."),
        new(BlockCategory.Operators, "Operators", "#59C059", "Calculator24",
            "Arithmetic, comparison, logic and text."),
        new(BlockCategory.Variables, "Variables", "#FF8C1A", "Tag24",
            "This script's own variables."),
        new(BlockCategory.Lists, "Lists", "#EA7B2C", "TextBulletListSquare24",
            "Variable-length collections."),
        new(BlockCategory.Parameters, "Parameters", "#2FB8A6", "Code24",
            "Parameters and payload values, read as a type."),
        new(BlockCategory.Network, "Network", "#6B5BD2", "Globe24",
            "HTTP requests and their responses."),
        new(BlockCategory.Procedures, "My Blocks", "#FF6680", "PuzzlePiece24",
            "Procedures this document defines."),
    ];

    /// <summary>
    /// Every kind a document may hold, in palette order.
    /// </summary>
    /// <remarks>
    /// <see cref="All"/> without the placeholders: the count the design's Part 7 tables are held to.
    /// </remarks>
    public static IReadOnlyList<BlockDescriptor> Blocks { get; } =
        [.. All.Where(block => block.IsPaletteBlock)];

    /// <summary>
    /// Trigger hats that belong to non-action targets.
    /// </summary>
    /// <remarks>
    /// Real triggers with real SDK support, but no owner inside an action body: a message subscription
    /// is an <c>IAsyncDisposable</c> the integration holds for its lifetime, and event delivery reaches
    /// the host rather than an action. They ship with the Phase 9 targets, which have somewhere to
    /// dispose them, and they are listed here so their absence from the palette is a decision with a
    /// reason attached.
    /// </remarks>
    public static IReadOnlyList<BlockDescriptor> Deferred { get; } =
    [
        Hat("hat.event-received", BlockCategory.Events, "when event received {event}",
            "Runs when the host delivers a declared event.", "IEventPublisher.GetBindings / BindingsChanged"),
        Hat("hat.key-pressed", BlockCategory.Events, "when key {key} is pressed",
            "Runs when a bound key is pressed.", "host key bindings"),
        Hat("hat.client-connected", BlockCategory.Events, "when a client connects",
            "Runs when a client connects.", "IDeckNavigator.ClientChanged"),
        Hat("hat.client-disconnected", BlockCategory.Events, "when a client disconnects",
            "Runs when a client disconnects.", "IDeckNavigator.ClientChanged"),
        Hat("hat.profile-changed", BlockCategory.Events, "when the profile changes",
            "Runs when a profile becomes active.", "IDeckNavigator.GetProfiles"),
        Hat("hat.folder-opened", BlockCategory.Events, "when a folder is opened",
            "Runs when a client opens a folder.", "IDeckNavigator.GetClients"),
        Hat("hat.message-received", BlockCategory.Events, "when a message arrives on {topic}",
            "Runs for each message on a topic.", "IMessageChannel.SubscribeAsync"),
    ];

    /// <summary>
    /// Blocks the SDK inventory found no honest caller for.
    /// </summary>
    /// <remarks>
    /// Kept in code, not only in the document, because the design's original table lists them and the
    /// next person to read it would otherwise re-add them. Each reason is the finding from Part 7.16.
    /// </remarks>
    public static IReadOnlyList<DroppedBlock> Dropped { get; } =
    [
        new("media.play", MusicReason),
        new("media.pause", MusicReason),
        new("media.resume", MusicReason),
        new("media.stop", MusicReason),
        new("media.next-track", MusicReason),
        new("media.previous-track", MusicReason),
        new("media.set-volume", MusicReason),
        new("media.change-volume", MusicReason),
        new("media.seek", MusicReason),
        new("media.set-shuffle", MusicReason),
        new("media.set-repeat", MusicReason),
        new("media.volume", MusicReason),
        new("media.position", MusicReason),
        new("media.duration", MusicReason),
        new("media.current-track", MusicReason),
        new("media.is-playing", MusicReason),
        new("media.playback-state", MusicReason),
        new("ui.report-issue", "IIntegrationIssueProvider is polled by the host; it has no caller"),
        new("ui.clear-issue", "IIntegrationIssueProvider.ResolveIssueAsync is called by the host, not by an action"),
        new("ui.set-icon", "icons are supplied through IIconProviderActionDefinition, not pushed"),
        new("events.publish-and-wait", "IEventPublisher.Publish is void; there is no acknowledgement API"),
        new("events.broadcast-to-self", "no self-delivery concept in the event surface"),
        new("events.last-event-id", "no such state exists"),
        new("events.last-message-topic", "the channel is subscribe-based; there is no last message"),
        new("events.last-message-body", "the channel is subscribe-based; there is no last message to read"),
        new("events.unbind-all", "lifetime management, not an action-time operation"),
        new("http.websocket-connect", "no WebSocket client surface in the SDK"),
        new("http.websocket-send", "no WebSocket client surface in the SDK"),
    ];

    private const string MusicReason =
        "IMusicPlayer is implemented by the plugin, not called by it; use the SDK's own "
        + "MusicPlayerActionDefinition from the Actions editor instead";

    /// <summary>The descriptor for a kind, or null when the catalog does not know it.</summary>
    /// <remarks>
    /// Searches <see cref="All"/> only, so it finds placeholders too: those are kinds a document really
    /// does contain, and the emitter has to resolve them to a block that emits a comment. A deferred
    /// block is deliberately not here, so a document naming one is reported as unknown rather than
    /// emitting code for a target that does not exist yet.
    /// </remarks>
    public static BlockDescriptor? Find(string? kind) =>
        kind is null
            ? null
            : All.FirstOrDefault(block => string.Equals(block.Kind, kind, StringComparison.Ordinal));

    /// <summary>Whether the catalog knows a kind.</summary>
    public static bool IsKnown(string? kind) => Find(kind) is not null;

    /// <summary>The palette blocks of one category, in palette order.</summary>
    public static IReadOnlyList<BlockDescriptor> InCategory(BlockCategory category) =>
        [.. Blocks.Where(block => block.Category == category)];

    /// <summary>The palette blocks whose label, summary or id contains <paramref name="query"/>.</summary>
    /// <remarks>
    /// Part 9.7: a 150-block palette without search is a haystack. Matching the id as well as the visible
    /// text means a user who knows the kind can type it, and it costs one comparison.
    /// </remarks>
    public static IReadOnlyList<BlockDescriptor> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Blocks;
        }

        var trimmed = query.Trim();
        return
        [
            .. Blocks.Where(block =>
                block.Kind.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || block.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || block.Summary.Contains(trimmed, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    /// <summary>The palette descriptor for a category: its name, colour and glyph.</summary>
    public static BlockCategoryDescriptor Category(BlockCategory category) =>
        Categories.First(descriptor => descriptor.Category == category);

    // ---- row helpers ---------------------------------------------------------------------------------

    private static BlockDescriptor Stack(
        string kind, BlockCategory category, string label, string summary, SdkMapping sdk,
        IReadOnlyList<SlotDescriptor>? slots = null,
        IReadOnlyList<MenuDescriptor>? menus = null,
        string? capability = null,
        string? docs = null) =>
        new(kind, category, label, summary, BlockShape.Stack, sdk, slots, menus, null, capability, docs);

    private static BlockDescriptor Cap(
        string kind, BlockCategory category, string label, string summary, SdkMapping sdk,
        IReadOnlyList<SlotDescriptor>? slots = null,
        IReadOnlyList<MenuDescriptor>? menus = null,
        string? docs = null) =>
        new(kind, category, label, summary, BlockShape.Cap, sdk, slots, menus, null, null, docs);

    /// <param name="menus">
    /// Dropdowns. Only Phase 9's target hats have one: an event name and a config-flow hook are chosen
    /// from a fixed set rather than typed, because the writer has to be able to find the method each
    /// names, and a free-text event name would be a name nothing can resolve.
    /// </param>
    private static BlockDescriptor Hat(
        string kind, BlockCategory category, string label, string summary, string verifiedAgainst,
        IReadOnlyList<SlotDescriptor>? slots = null,
        IReadOnlyList<MenuDescriptor>? menus = null) =>
        new(kind, category, label, summary, BlockShape.Hat, Plain(string.Empty, verifiedAgainst), slots, menus);

    private static BlockDescriptor Container(
        string kind, BlockCategory category, string label, string summary, SdkMapping sdk,
        IReadOnlyList<SlotDescriptor>? slots = null,
        IReadOnlyList<MenuDescriptor>? menus = null,
        IReadOnlyList<BodyDescriptor>? bodies = null,
        string? capability = null,
        string? docs = null) =>
        new(kind, category, label, summary,
            bodies is { Count: > 1 } ? BlockShape.CIf : BlockShape.C, sdk, slots, menus, bodies, capability, docs);

    private static BlockDescriptor Reporter(
        string kind, BlockCategory category, string label, string summary, SdkMapping sdk,
        IReadOnlyList<SlotDescriptor>? slots = null,
        IReadOnlyList<MenuDescriptor>? menus = null,
        string? capability = null,
        string? docs = null) =>
        new(kind, category, label, summary, BlockShape.Reporter, sdk, slots, menus, null, capability, docs);

    private static BlockDescriptor Boolean(
        string kind, BlockCategory category, string label, string summary, SdkMapping sdk,
        IReadOnlyList<SlotDescriptor>? slots = null,
        IReadOnlyList<MenuDescriptor>? menus = null,
        string? capability = null,
        string? docs = null) =>
        new(kind, category, label, summary, BlockShape.BooleanReporter, sdk, slots, menus, null, capability, docs);

    private static BlockDescriptor Placeholder(
        string kind, BlockCategory category, string label, string summary, SdkMapping sdk,
        IReadOnlyList<SlotDescriptor>? slots = null) =>
        new(kind, category, label, summary, BlockShape.Placeholder, sdk, slots);

    private static BlockDescriptor Modifier(
        string kind, BlockCategory category, string label, string summary,
        IReadOnlyList<SlotDescriptor>? slots = null,
        string? docs = null) =>
        new(kind, category, label, summary, BlockShape.Modifier, Plain(string.Empty), slots,
            null, null, null, docs);

    /// <summary>A call on the integration context, verified against the named member.</summary>
    private static SdkMapping Host(string expression, string verifiedAgainst) => new(expression, verifiedAgainst);

    /// <summary>A call on <c>context.Ui</c> or <c>context.Interactions</c>.</summary>
    private static SdkMapping Interaction(string expression, string verifiedAgainst) =>
        new(expression, verifiedAgainst);

    /// <summary>Plain C#, or a host-less helper: nothing to cite beyond the language or the runtime.</summary>
    private static SdkMapping Plain(string expression, string verifiedAgainst = "plain C#") =>
        new(expression, verifiedAgainst);

    private static SlotDescriptor Text(string name, bool required = true, string? label = null, string? def = null) =>
        new(name, SlotType.Text, required, label, def);

    private static SlotDescriptor Num(string name, bool required = true, string? label = null, string? def = null) =>
        new(name, SlotType.Number, required, label, def);

    private static SlotDescriptor Any(string name, bool required = true, string? label = null, string? def = null) =>
        new(name, SlotType.Any, required, label, def);

    private static SlotDescriptor Bool(string name, bool required = true, string? label = null) =>
        new(name, SlotType.Boolean, required, label);

    private static SlotDescriptor ListSlot(string name, bool required = true, string? label = null) =>
        new(name, SlotType.List, required, label);

    private static SlotDescriptor Pick(string name, SlotType type, string? label = null) =>
        new(name, type, true, label);

    private static MenuDescriptor Menu(string name, string @default, string[] options, string? label = null) =>
        new(name, options, @default, label);

    private static BodyDescriptor Body(string name, string? label = null) => new(name, label);
}
