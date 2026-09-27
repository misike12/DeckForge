namespace DeckForge.Core.Code;

/// <summary>
/// The 29 parameter editor types, transcribed from the real
/// <c>MacroDeck.Sdk.Actions.ActionParameter</c> factory signatures.
/// </summary>
/// <remarks>
/// <para>
/// This table is the single source of truth. The previous approach special-cased 12 of 29 types
/// in each of three separate files and let the remaining 17 fall through to a
/// <c>ActionParameter.{Name}(...)</c> guess. That produced a call with a missing required
/// argument for <c>Code</c>, <c>Object</c>, <c>Array</c>, <c>Slider</c> and <c>Choice</c>, and
/// an unknown named argument for <c>Duration</c>, which has no <c>defaultValue</c> parameter at
/// all.
/// </para>
/// <para>
/// Three asymmetries in the real API are captured here and are easy to get wrong:
/// <c>Slider</c> has required min/max and no <c>required</c> flag; <c>MultiSelect</c> has no
/// <c>placeholder</c>; and <c>WidgetTarget</c>'s simple overload defaults <c>required</c> to
/// <c>true</c>, so an unticked box would still emit a required parameter.
/// </para>
/// </remarks>
public static class ActionParameterTypes
{
    public static IReadOnlyList<ActionParameterTypeInfo> All { get; } =
    [
        new()
        {
            Name = "Text", WireType = "String",
            SupportsPlaceholder = true, SupportsDescription = true, SupportsDefaultValue = true,
            SupportsRequired = true, SupportsValidationRegex = true, SupportsMaxLength = true,
            Summary = "A single line of text.",
        },
        new()
        {
            Name = "MultilineText", WireType = "String",
            SupportsPlaceholder = true, SupportsDescription = true, SupportsDefaultValue = true,
            SupportsRequired = true, SupportsMaxLength = true,
            Summary = "Multiple lines of text. The SDK has no validationRegex on this overload.",
        },
        new()
        {
            Name = "Number", WireType = "Number",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsRequired = true,
            SupportsRange = true,
            Summary = "A number, optionally bounded by a range.",
        },
        new()
        {
            Name = "Slider", WireType = "Number",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsRange = true,
            Summary = "A number as a slider. Min and max are required; the type has no required flag.",
        },
        new()
        {
            Name = "Toggle", WireType = "Boolean",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsLiteralOnly = true,
            Summary = "An on/off switch. The default must be a bool literal, not a string.",
        },
        new()
        {
            Name = "Password", WireType = "Password",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A masked value the user is expected to retype.",
        },
        new()
        {
            Name = "Secret", WireType = "Secret",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A stored secret. Reading it back needs the host:config permission.",
        },
        new()
        {
            Name = "Choice", WireType = "Choice",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsRequired = true,
            SupportsOptions = true,
            Summary = "One option from a fixed list. The list is required.",
        },
        new()
        {
            Name = "DynamicChoice", WireType = "DynamicChoice",
            SupportsPlaceholder = true, SupportsDescription = true, SupportsRequired = true,
            SupportsOptionsSourceId = true,
            Summary = "One option resolved at run time from a named source. There is no options parameter.",
        },
        new()
        {
            Name = "Autocomplete", WireType = "Autocomplete",
            SupportsPlaceholder = true, SupportsDescription = true, SupportsRequired = true,
            SupportsOptions = true, SupportsOptionsSourceId = true,
            Summary = "Text with suggestions, static or from a named source.",
        },
        new()
        {
            Name = "MultiSelect", WireType = "MultiSelect",
            SupportsDescription = true, SupportsRequired = true,
            SupportsOptions = true, SupportsOptionsSourceId = true,
            Summary = "Several options. The SDK has no placeholder parameter on this overload.",
        },
        new()
        {
            Name = "Color", WireType = "Color",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsSupportsReset = true,
            Summary = "A colour, with an optional reset to the default.",
        },
        new()
        {
            Name = "File", WireType = "File",
            SupportsDescription = true, SupportsRequired = true, SupportsFileExtensions = true,
            Summary = "A file path, optionally filtered by extension.",
        },
        new()
        {
            Name = "Folder", WireType = "Folder",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A folder path.",
        },
        new()
        {
            Name = "Hotkey", WireType = "Hotkey",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A key combination.",
        },
        new()
        {
            Name = "Duration", WireType = "Duration",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsRequired = true,
            SupportsRange = true,
            Summary = "A span of time. The default parameter is defaultMilliseconds, not defaultValue.",
        },
        new()
        {
            Name = "DateTime", WireType = "DateTime",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A date and time.",
        },
        new()
        {
            Name = "Json", WireType = "Json",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsRequired = true,
            Summary = "Structured JSON.",
        },
        new()
        {
            Name = "Code", WireType = "Code",
            SupportsDescription = true, SupportsDefaultValue = true, SupportsRequired = true,
            SupportsLanguage = true,
            Summary = "Source code. The language is required.",
        },
        new()
        {
            Name = "KeyValue", WireType = "KeyValue",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A set of key/value pairs.",
        },
        new()
        {
            Name = "Object", WireType = "Object",
            SupportsDescription = true, SupportsChildren = true,
            Summary = "A nested object. The child parameters are required.",
        },
        new()
        {
            Name = "Array", WireType = "Array",
            SupportsDescription = true, SupportsItemTemplate = true,
            Summary = "A repeated item. The item template is required.",
        },
        new()
        {
            Name = "IpAddress", WireType = "IpAddress",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "An IPv4 or IPv6 address.",
        },
        new()
        {
            Name = "Url", WireType = "Url",
            SupportsPlaceholder = true, SupportsDescription = true, SupportsRequired = true,
            SupportsAutoPrefixHttps = true,
            Summary = "A URL, optionally prefixing https for the user.",
        },
        new()
        {
            Name = "Icon", WireType = "Icon",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "An icon reference.",
        },
        new()
        {
            Name = "Image", WireType = "Image",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "An image reference.",
        },
        new()
        {
            Name = "KeyboardSequence", WireType = "KeyboardSequence",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A recorded key sequence.",
        },
        new()
        {
            Name = "KeyboardCombo", WireType = "KeyboardCombo",
            SupportsDescription = true, SupportsRequired = true,
            Summary = "A key combination resolved by the host.",
        },
        new()
        {
            Name = "WidgetTarget", WireType = "WidgetTarget",
            SupportsDescription = true, SupportsRequired = true, SupportsWidgetTargetOptions = true,
            Summary = "A widget on a deck. The SDK's simple overload defaults required to true, so DeckForge always states it.",
        },
    ];

    private static readonly Dictionary<string, ActionParameterTypeInfo> ByName =
        All.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every editor type name, in SDK order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. All.Select(t => t.Name)];

    /// <summary>Every wire type name, in SDK order. Matches <c>ActionParameterType</c>.</summary>
    public static IReadOnlyList<string> WireNames { get; } = [.. All.Select(t => t.WireType).Distinct()];

    public static ActionParameterTypeInfo? Find(string? name) =>
        name is not null && ByName.TryGetValue(name, out var info) ? info : null;

    public static bool IsKnown(string? name) => Find(name) is not null;

    /// <summary>
    /// The language identifiers the Code editor understands. The SDK forwards the string to the
    /// host, so this is a convenience list rather than a closed vocabulary.
    /// </summary>
    public static IReadOnlyList<string> CodeLanguages { get; } =
    [
        "plaintext", "csharp", "javascript", "typescript", "json", "xml", "html", "css",
        "bash", "powershell", "python", "sql", "yaml", "ini", "csv", "markdown", "regex",
    ];
}
