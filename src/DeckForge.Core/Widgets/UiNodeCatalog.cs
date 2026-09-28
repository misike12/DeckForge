namespace DeckForge.Core.Widgets;

/// <summary>The C# literal kind a property value needs.</summary>
public enum UiPropertyKind
{
    /// <summary>A quoted string.</summary>
    Text,

    /// <summary>An unquoted numeric literal.</summary>
    Number,

    /// <summary>A <c>true</c>/<c>false</c> literal.</summary>
    Flag,

    /// <summary>A <c>double</c> passed to an implicit <c>UiValue&lt;T&gt;</c> conversion.</summary>
    ValueNumber,

    /// <summary>A comma-separated list of numbers for an <c>UiValue&lt;IReadOnlyList&lt;double&gt;&gt;</c>.</summary>
    Points,

    /// <summary>A resource id for a <c>UiValue&lt;UiResource&gt;</c>.</summary>
    Resource,
}

/// <summary>One settable property of one node type, transcribed from the real DSL class.</summary>
public sealed record UiPropertyDescriptor(string Name, UiPropertyKind Kind, string Summary);

/// <summary>One of the twenty-four component-profile node types.</summary>
public sealed record UiNodeTypeInfo
{
    public required string WireType { get; init; }

    /// <summary>The DSL class in <c>MacroDeck.Ui.Components</c>.</summary>
    public required string ClassName { get; init; }

    public required string DisplayName { get; init; }

    public required string Summary { get; init; }

    /// <summary>True when the node accepts children.</summary>
    public required bool IsContainer { get; init; }

    /// <summary>Properties the designer can set, in declaration order.</summary>
    public required IReadOnlyList<UiPropertyDescriptor> Properties { get; init; }

    /// <summary>
    /// Members the SDK marks <c>required</c>, which the generator always has to set.
    /// </summary>
    /// <remarks>
    /// <c>UiResponsive.Default</c> and <c>UiModifier.Child</c> are <c>required UiElement</c>. A record
    /// with a required member that the object initializer does not set is a CS9035, so the
    /// generator fills these with the node's first child - which is what the type means: a
    /// responsive node picks a layout, and a modifier wraps exactly one.
    /// </remarks>
    public IReadOnlyList<string> RequiredChildMembers { get; init; } = [];

    /// <summary>Event names the node advertises.</summary>
    public required IReadOnlyList<string> Events { get; init; }

    /// <summary>Which events the designer offers for this type.</summary>
    public required IReadOnlyList<string> OfferableEvents { get; init; }

    public bool Has(string property) => Properties.Any(p => p.Name == property);
}

/// <summary>
/// The twenty-four widget component node types: twenty <c>ui.*</c> from the framework plus four
/// <c>macrodeck.*</c> whose displayed value the reader derives from a time or progress reference.
/// </summary>
/// <remarks>
/// <para>
/// The previous designer offered twenty-one type names, missing <c>ui.transform</c>,
/// <c>ui.modifier</c> and <c>ui.responsive</c>, and it only knew how to build four of them - the
/// other seventeen fell through to a text run, so a gauge rendered as a label.
/// </para>
/// <para>
/// The four <c>macrodeck.*</c> types take a <c>UiTimeReference</c> or <c>UiProgressReference</c>
/// rather than a literal value, which is why they live in a second namespace: a reader cannot draw
/// them from the tree alone. A designer should say so rather than offer a level slider for one.
/// </para>
/// </remarks>
public static class UiNodeCatalog
{
    private static UiPropertyDescriptor S(string name, string summary) => new(name, UiPropertyKind.Text, summary);
    private static UiPropertyDescriptor N(string name, string summary) => new(name, UiPropertyKind.Number, summary);
    private static UiPropertyDescriptor B(string name, string summary) => new(name, UiPropertyKind.Flag, summary);
    private static UiPropertyDescriptor V(string name, string summary) => new(name, UiPropertyKind.ValueNumber, summary);

/// <summary>A list of numbers, for a property typed <c>UiValue&lt;IReadOnlyList&lt;double&gt;&c>.</summary>
private static UiPropertyDescriptor P(string name, string summary) => new(name, UiPropertyKind.Points, summary);

/// <summary>A resource id, for a property typed <c>UiValue&lt;UiResource&gt;</c>.</summary>
private static UiPropertyDescriptor R(string name, string summary) => new(name, UiPropertyKind.Resource, summary);

    /// <summary>Every event name the component profile declares.</summary>
    public static IReadOnlyList<string> AllEvents { get; } =
    [
        "press", "long-press", "press-start", "press-end", "double-press",
        "change", "adjust", "tap",
    ];

    private static readonly UiPropertyDescriptor[] Common =
    [
        S("background", "A colour, or 'none'."),
        V("mainSize", "The node's share of the box, as a fraction of the widget basis."),
        B("fill", "Whether the node grows to fill its box."),
        V("columnSpan", "Columns to span, in a ui.grid parent."),
        V("rowSpan", "Rows to span, in a ui.grid parent."),
    ];

    private static readonly UiPropertyDescriptor[] Layout =
    [
        S("direction", "row | column for a stack; horizontal for a list."),
        S("justify", "How free space is distributed along the main axis."),
        S("align", "How children are aligned on the cross axis."),
        V("gap", "Space between children."),
        V("padding", "Space inside the node's own edge."),
    ];

    /// <summary>
    /// Drops named properties from a composed list.
    /// </summary>
    /// <remarks>
    /// The groups above are shared, and a shared group is only as good as its least compatible
    /// member: most of the twenty-four types have no <c>Background</c>, a list has no
    /// <c>Justify</c> or <c>Align</c>, and a gauge has no <c>Step</c>. Listing a property a type
    /// does not have produces a generated assignment that does not compile - and nothing caught it,
    /// because the compile test that would have shown it wrote the widget to the solution root,
    /// outside every project, so nothing was ever built. The affected entries now say which
    /// properties they drop, and UiNodeCatalogTests fails on any property the SDK type lacks.
    /// </remarks>
    private static IReadOnlyList<UiPropertyDescriptor> Without(
        IReadOnlyList<UiPropertyDescriptor> source,
        params string[] names)
    {
        var drop = new HashSet<string>(names, StringComparer.Ordinal);
        return [.. source.Where(p => !drop.Contains(p.Name))];
    }

    private static readonly UiPropertyDescriptor[] Textual =
    [
        new("text", UiPropertyKind.Text, "The literal text."),
        V("size", "Font size, as a fraction of the widget basis."),
        V("minSize", "A floor on the size."),
        S("weight", "normal | medium | semibold | bold."),
        S("role", "A semantic role the renderer styles."),
        S("color", "A text colour."),
        V("maxLines", "Lines before the text is elided."),
        B("wrap", "Whether the text wraps."),
        S("fontFace", "A font family name."),
        V("digits", "Fixed decimal places."),
    ];

    private static readonly UiPropertyDescriptor[] Interactive =
    [
        V("level", "The current level, 0 to 1."),
        V("step", "The step a drag or adjust moves by."),
        S("levelColor", "The colour the level is drawn in."),
    ];

    private static readonly IReadOnlyList<string> PressEvents = ["press", "long-press", "double-press", "tap"];
    private static readonly IReadOnlyList<string> DragEvents = ["adjust", "change", "press-start", "press-end"];
    private static readonly IReadOnlyList<string> None = [];

    public static IReadOnlyList<UiNodeTypeInfo> All { get; } =
    [
        new()
        {
            WireType = "ui.stack", ClassName = "UiStack", DisplayName = "Stack", IsContainer = true,
            Summary = "A one-directional layout container. The root of a widget is one of these.",
            Properties = [.. Layout, .. Common], Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.text", ClassName = "UiTextRun", DisplayName = "Text", IsContainer = false,
            Summary = "A single run of text, sized as a fraction of the widget basis.",
            Properties = Without([..  Textual, .. Common], "background"), Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.button", ClassName = "UiButton", DisplayName = "Button", IsContainer = true,
            Summary = "A pressable container: stack layout plus its own artwork and its own ring.",
            Properties =
            [
                .. Layout,
                S("fit", "How the artwork is fitted."),
                V("zoom", "Artwork scale."),
                V("offsetX", "Artwork horizontal offset."),
                V("offsetY", "Artwork vertical offset."),
                S("tint", "A tint applied to the artwork."),
                S("borderStyle", "none | solid | subtle."),
                S("borderColor", "The ring colour."),
                S("corner", "Ring corner style."),
                .. Common,
            ],
            Events = PressEvents, OfferableEvents = PressEvents,
        },
        new()
        {
            WireType = "ui.layer", ClassName = "UiLayer", DisplayName = "Layer", IsContainer = true,
            Summary = "Stacks children through the depth of the box, first furthest back.",
            Properties = Without([..  Common], "background"), Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.grid", ClassName = "UiGrid", DisplayName = "Grid", IsContainer = true,
            Summary = "Equal rows and columns, with spans.",
            Properties =
            [
                V("columns", "How many columns."),
                V("rows", "How many rows."),
                V("gap", "Space between cells."),
                V("padding", "Space inside the grid."),
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.list", ClassName = "UiList", DisplayName = "List", IsContainer = true,
            Summary = "A scrolling container that asks for more as the user reaches the end.",
            Properties = Without([..  Layout, .. Common], "justify", "align"), Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.segmented", ClassName = "UiSegmented", DisplayName = "Segmented", IsContainer = true,
            Summary = "A row of segments; the children are segment content, never controls.",
            Properties = Without([V("selected", "Index of the chosen segment."), S("levelColor", "Chosen-segment colour."), .. Common], "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.transform", ClassName = "UiTransform", DisplayName = "Transform", IsContainer = true,
            Summary = "Draws its children like a layer, then rotates, scales and shifts them together.",
            Properties =
                Without(
                [
                    V("rotation", "Rotation in degrees."),
                    V("zoom", "Scale."),
                    V("offsetX", "Horizontal shift."),
                    V("offsetY", "Vertical shift."),
                    V("originX", "Pivot, horizontally."),
                    V("originY", "Pivot, vertically."),
                    .. Common,
                ],
                "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.responsive", ClassName = "UiResponsive", DisplayName = "Responsive", IsContainer = true,
            RequiredChildMembers = ["Default"],
            Summary = "Draws one of several layouts, chosen by the reader from the box it is given.",
            Properties = Without([..  Common], "background"), Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.modifier", ClassName = "UiModifier", DisplayName = "Modifier", IsContainer = true,
            RequiredChildMembers = ["Child"],
            Summary = "Wraps exactly one child to pad, frame, clip, mask or fade it.",
            Properties =
            [
                S("clip", "none | rect | circle."),
                V("padding", "Space inside the modifier."),
                S("background", "A colour, or 'none'."),
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.gauge", ClassName = "UiGauge", DisplayName = "Gauge", IsContainer = false,
            Summary = "A level drawn as an arc; over a full turn it is a ring.",
            Properties = Without([.. Interactive, N("startAngle", "Arc start, in degrees."), N("endAngle", "Arc end, in degrees."), .. Common], "step", "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.dial", ClassName = "UiDial", DisplayName = "Dial", IsContainer = false,
            Summary = "A rotary level the user turns: a gauge with a thumb.",
            Properties = Without([.. Interactive, N("startAngle", "Arc start, in degrees."), N("endAngle", "Arc end, in degrees."), .. Common], "background"),
            Events = DragEvents, OfferableEvents = DragEvents,
        },
        new()
        {
            WireType = "ui.slider", ClassName = "UiSlider", DisplayName = "Slider", IsContainer = false,
            Summary = "A draggable level: the interactive counterpart of a range bar.",
            Properties = Without([.. Interactive, S("interaction", "How the reader takes input."), .. Common], "background"),
            Events = DragEvents, OfferableEvents = DragEvents,
        },
        new()
        {
            WireType = "ui.toggle", ClassName = "UiToggle", DisplayName = "Toggle", IsContainer = false,
            Summary = "An on/off switch the user flips.",
            Properties = Without([B("on", "Whether it is on."), S("levelColor", "The on colour."), .. Common], "background"),
            Events = ["change"], OfferableEvents = ["change"],
        },
        new()
        {
            WireType = "ui.range-bar", ClassName = "UiRangeBar", DisplayName = "Range bar", IsContainer = false,
            Summary = "A horizontal track carrying a gradient-filled span and an optional marker.",
            Properties =
                Without(
                [
                    V("start", "Span start, 0 to 1."),
                    V("end", "Span end, 0 to 1."),
                    S("startColor", "Colour at the start of the span."),
                    S("endColor", "Colour at the end of the span."),
                    V("marker", "Marker position, 0 to 1."),
                    V("thickness", "Track thickness."),
                    .. Common,
                ],
                "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.chart", ClassName = "UiChart", DisplayName = "Chart", IsContainer = false,
            Summary = "A series of values drawn as a filled line; the points arrive normalised to 0..1.",
            Properties = Without([P("points", "Comma-separated values, each 0 to 1."), S("color", "Line colour."), V("plotTop", "Where the plot starts."), V("thickness", "Line thickness."), .. Common], "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.shape", ClassName = "UiShape", DisplayName = "Shape", IsContainer = false,
            Summary = "A rectangle, rounded rectangle, circle, capsule or path, filled and stroked.",
            Properties =
                Without(
                [
                    S("shape", "rect | rounded-rect | circle | capsule | path."),
                    V("cornerRadius", "Corner radius."),
                    S("color", "Fill colour."),
                    S("strokeColor", "Stroke colour."),
                    V("strokeWidth", "Stroke width."),
                    S("path", "Path data, for the path shape."),
                    .. Common,
                ],
                "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.icon", ClassName = "UiIcon", DisplayName = "Icon", IsContainer = false,
            Summary = "One glyph of the built-in icon set, drawn by name.",
            Properties = Without([S("icon", "The glyph name."), S("color", "Glyph colour."), S("role", "A semantic role."), .. Common], "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.image", ClassName = "UiImage", DisplayName = "Image", IsContainer = false,
            Summary = "An image resolved from a resource handle, fitted into a square box and never cropped.",
            Properties =
                Without(
                [
                    R("source", "A resource id registered through the UI resource registry."),
                    S("transition", "none | fade | slide."),
                    V("opacity", "Opacity, 0 to 1."),
                    V("brightness", "Brightness multiplier."),
                    V("saturation", "Saturation multiplier."),
                    .. Common,
                ],
                "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.text-field", ClassName = "UiTextField", DisplayName = "Text field", IsContainer = false,
            Summary = "A single line of text the user types.",
            Properties =
                Without(
                [
                    S("text", "The current value."),
                    new("placeholder", UiPropertyKind.Text, "Text shown while empty."),
                    V("size", "Font size."),
                    .. Common,
                ],
                "color", "background"),
            Events = ["change"], OfferableEvents = ["change"],
        },
        new()
        {
            WireType = "macrodeck.dynamic-text", ClassName = "UiDynamicText", DisplayName = "Dynamic text", IsContainer = false,
            Summary = "Text the reader derives from a time reference it resolves itself.",
            Properties =
                Without(
                [
                    S("format", "time | date | zone-name, and the duration formats past those."),
                    B("seconds", "Whether seconds are shown."),
                    .. Textual,
                    .. Common,
                ],
                "text", "maxLines", "wrap", "fontFace", "digits", "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "macrodeck.clock-dial", ClassName = "UiClockDial", DisplayName = "Clock dial", IsContainer = false,
            Summary = "An analogue clock face drawn from a time reference.",
            Properties = Without([S("color", "Face colour."), B("seconds", "Whether the second hand is drawn."), .. Common], "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "macrodeck.progress-bar", ClassName = "UiProgressBar", DisplayName = "Progress bar", IsContainer = false,
            Summary = "A track the reader fills from a position that is still moving.",
            Properties =
                Without(
                [
                    S("startColor", "Colour at the start of the fill."),
                    S("endColor", "Colour at the end of the fill."),
                    V("thickness", "Track thickness."),
                    .. Common,
                ],
                "positionMs", "durationMs", "background"),
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "macrodeck.progress-text", ClassName = "UiProgressText", DisplayName = "Progress text", IsContainer = false,
            Summary = "Text the reader derives from the same moving position.",
            Properties =
                Without(
                [
                    S("format", "How the remaining time is written."),
                ],
                "positionMs", "durationMs", "text", "size", "minSize", "weight", "role", "color",
                "align", "maxLines", "wrap", "fontFace", "digits", "mainSize", "fill", "columnSpan",
                "rowSpan", "background"),
            Events = None, OfferableEvents = None,
        },
    ];

    private static readonly Dictionary<string, UiNodeTypeInfo> ByWireType =
        All.ToDictionary(t => t.WireType, StringComparer.Ordinal);

    /// <summary>The wire type strings, in catalog order.</summary>
    public static IReadOnlyList<string> WireTypes { get; } = [.. All.Select(t => t.WireType)];

    /// <summary>The DSL class names, in catalog order.</summary>
    public static IReadOnlyList<string> ClassNames { get; } = [.. All.Select(t => t.ClassName)];

    public static UiNodeTypeInfo? Find(string? wireType) =>
        wireType is not null && ByWireType.TryGetValue(wireType, out var info) ? info : null;

    public static bool IsKnown(string? wireType) => Find(wireType) is not null;

    /// <summary>The types that accept children.</summary>
    public static IReadOnlyList<UiNodeTypeInfo> Containers { get; } = [.. All.Where(t => t.IsContainer)];

    /// <summary>True when <paramref name="wireType"/> may hold the node named by <paramref name="candidate"/>.</summary>
    public static bool CanContain(string parentWireType, string candidateWireType) =>
        Find(parentWireType)?.IsContainer == true;
}
