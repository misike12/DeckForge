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
        V("fill", "Whether the node grows to fill its box."),
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
            Properties = [.. Textual, .. Common], Events = None, OfferableEvents = None,
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
            Properties = [.. Common], Events = None, OfferableEvents = None,
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
                S("background", "A colour, or 'none'."),
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.list", ClassName = "UiList", DisplayName = "List", IsContainer = true,
            Summary = "A scrolling container that asks for more as the user reaches the end.",
            Properties = [.. Layout, .. Common], Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.segmented", ClassName = "UiSegmented", DisplayName = "Segmented", IsContainer = true,
            Summary = "A row of segments; the children are segment content, never controls.",
            Properties = [V("selected", "Index of the chosen segment."), S("levelColor", "Chosen-segment colour."), .. Common],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.transform", ClassName = "UiTransform", DisplayName = "Transform", IsContainer = true,
            Summary = "Draws its children like a layer, then rotates, scales and shifts them together.",
            Properties =
            [
                V("rotation", "Rotation in degrees."),
                V("zoom", "Scale."),
                V("offsetX", "Horizontal shift."),
                V("offsetY", "Vertical shift."),
                V("originX", "Pivot, horizontally."),
                V("originY", "Pivot, vertically."),
                .. Common,
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.responsive", ClassName = "UiResponsive", DisplayName = "Responsive", IsContainer = true,
            Summary = "Draws one of several layouts, chosen by the reader from the box it is given.",
            Properties = [.. Common], Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.modifier", ClassName = "UiModifier", DisplayName = "Modifier", IsContainer = false,
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
            Properties = [.. Interactive, N("startAngle", "Arc start, in degrees."), N("endAngle", "Arc end, in degrees."), .. Common],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.dial", ClassName = "UiDial", DisplayName = "Dial", IsContainer = false,
            Summary = "A rotary level the user turns: a gauge with a thumb.",
            Properties = [.. Interactive, N("startAngle", "Arc start, in degrees."), N("endAngle", "Arc end, in degrees."), .. Common],
            Events = DragEvents, OfferableEvents = DragEvents,
        },
        new()
        {
            WireType = "ui.slider", ClassName = "UiSlider", DisplayName = "Slider", IsContainer = false,
            Summary = "A draggable level: the interactive counterpart of a range bar.",
            Properties = [.. Interactive, S("interaction", "How the reader takes input."), .. Common],
            Events = DragEvents, OfferableEvents = DragEvents,
        },
        new()
        {
            WireType = "ui.toggle", ClassName = "UiToggle", DisplayName = "Toggle", IsContainer = false,
            Summary = "An on/off switch the user flips.",
            Properties = [B("on", "Whether it is on."), S("levelColor", "The on colour."), .. Common],
            Events = ["change"], OfferableEvents = ["change"],
        },
        new()
        {
            WireType = "ui.range-bar", ClassName = "UiRangeBar", DisplayName = "Range bar", IsContainer = false,
            Summary = "A horizontal track carrying a gradient-filled span and an optional marker.",
            Properties =
            [
                V("start", "Span start, 0 to 1."),
                V("end", "Span end, 0 to 1."),
                S("startColor", "Colour at the start of the span."),
                S("endColor", "Colour at the end of the span."),
                V("marker", "Marker position, 0 to 1."),
                V("thickness", "Track thickness."),
                .. Common,
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.chart", ClassName = "UiChart", DisplayName = "Chart", IsContainer = false,
            Summary = "A series of values drawn as a filled line; the points arrive normalised to 0..1.",
            Properties = [S("points", "Comma-separated values, each 0 to 1."), S("color", "Line colour."), V("plotTop", "Where the plot starts."), V("thickness", "Line thickness."), .. Common],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.shape", ClassName = "UiShape", DisplayName = "Shape", IsContainer = false,
            Summary = "A rectangle, rounded rectangle, circle, capsule or path, filled and stroked.",
            Properties =
            [
                S("shape", "rect | rounded-rect | circle | capsule | path."),
                V("cornerRadius", "Corner radius."),
                S("color", "Fill colour."),
                S("strokeColor", "Stroke colour."),
                V("strokeWidth", "Stroke width."),
                S("path", "Path data, for the path shape."),
                .. Common,
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.icon", ClassName = "UiIcon", DisplayName = "Icon", IsContainer = false,
            Summary = "One glyph of the built-in icon set, drawn by name.",
            Properties = [S("icon", "The glyph name."), S("color", "Glyph colour."), S("role", "A semantic role."), .. Common],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.image", ClassName = "UiImage", DisplayName = "Image", IsContainer = false,
            Summary = "An image resolved from a resource handle, fitted into a square box and never cropped.",
            Properties =
            [
                S("source", "A resource id registered through the UI resource registry."),
                S("transition", "none | fade | slide."),
                V("opacity", "Opacity, 0 to 1."),
                V("brightness", "Brightness multiplier."),
                V("saturation", "Saturation multiplier."),
                .. Common,
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "ui.text-field", ClassName = "UiTextField", DisplayName = "Text field", IsContainer = false,
            Summary = "A single line of text the user types.",
            Properties =
            [
                S("text", "The current value."),
                new("placeholder", UiPropertyKind.Text, "Text shown while empty."),
                V("size", "Font size."),
                S("color", "Text colour."),
                .. Common,
            ],
            Events = ["change"], OfferableEvents = ["change"],
        },
        new()
        {
            WireType = "macrodeck.dynamic-text", ClassName = "UiDynamicText", DisplayName = "Dynamic text", IsContainer = false,
            Summary = "Text the reader derives from a time reference it resolves itself.",
            Properties =
            [
                S("format", "time | date | zone-name, and the duration formats past those."),
                B("seconds", "Whether seconds are shown."),
                .. Textual,
                .. Common,
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "macrodeck.clock-dial", ClassName = "UiClockDial", DisplayName = "Clock dial", IsContainer = false,
            Summary = "An analogue clock face drawn from a time reference.",
            Properties = [S("color", "Face colour."), B("seconds", "Whether the second hand is drawn."), .. Common],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "macrodeck.progress-bar", ClassName = "UiProgressBar", DisplayName = "Progress bar", IsContainer = false,
            Summary = "A track the reader fills from a position that is still moving.",
            Properties =
            [
                S("positionMs", "Where the position starts, in milliseconds."),
                S("durationMs", "How long the run lasts, in milliseconds."),
                S("startColor", "Colour at the start of the fill."),
                S("endColor", "Colour at the end of the fill."),
                V("thickness", "Track thickness."),
                .. Common,
            ],
            Events = None, OfferableEvents = None,
        },
        new()
        {
            WireType = "macrodeck.progress-text", ClassName = "UiProgressText", DisplayName = "Progress text", IsContainer = false,
            Summary = "Text the reader derives from the same moving position.",
            Properties =
            [
                S("positionMs", "Where the position starts, in milliseconds."),
                S("durationMs", "How long the run lasts, in milliseconds."),
                S("format", "How the remaining time is written."),
                .. Textual,
                .. Common,
            ],
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
