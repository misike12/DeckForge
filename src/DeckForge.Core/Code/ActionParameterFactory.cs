using System.Text;

namespace DeckForge.Core.Code;

/// <summary>One option of a <c>Choice</c>-family parameter.</summary>
public sealed record ParameterOption(string Value, string Label)
{
    public static ParameterOption Parse(string line)
    {
        var parts = line.Split('=', 2);
        var value = parts[0].Trim();
        return new ParameterOption(value, parts.Length > 1 ? parts[1].Trim() : value);
    }
}

/// <summary>
/// Everything one <c>ActionParameter</c> can express, for all 29 editor types.
/// </summary>
/// <remarks>
/// This replaces the three divergent private copies of a parameter model that the Actions,
/// Events and Config Flow editors each carried. All three disagreed about which fields exist,
/// and two of them could not produce a compiling call.
/// </remarks>
public sealed record ActionParameterSpec
{
    public string Name { get; init; } = "value";
    public string EditorType { get; init; } = "Text";
    public string Label { get; init; } = "Value";
    public string Description { get; init; } = "";
    public string Placeholder { get; init; } = "";
    public bool Required { get; init; }
    public string DefaultValue { get; init; } = "";

    public IReadOnlyList<ParameterOption> Options { get; init; } = [];
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }

    public string OptionsSourceId { get; init; } = "";
    public IReadOnlyList<string> FileExtensions { get; init; } = [];
    public string Language { get; init; } = "csharp";
    public string ValidationRegex { get; init; } = "";
    public int? MaxLength { get; init; }

    public bool AutoPrefixHttps { get; init; }
    public bool SupportsReset { get; init; }
    public bool LiteralOnly { get; init; }
    public bool AllowSelf { get; init; } = true;
    public IReadOnlyList<string> WidgetTypes { get; init; } = [];

    public string OnlyWhenParameter { get; init; } = "";
    public string OnlyWhenValue { get; init; } = "";

    /// <summary>Child parameters, for <c>Object</c>.</summary>
    public IReadOnlyList<ActionParameterSpec> Children { get; init; } = [];

    /// <summary>Item template, for <c>Array</c>.</summary>
    public ActionParameterSpec? ItemTemplate { get; init; }

    /// <summary>Parsed <see cref="DefaultValue"/> for duration parameters, in milliseconds.</summary>
    public double? DefaultMilliseconds =>
        double.TryParse(DefaultValue, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
}

/// <summary>
/// What one editor type supports, transcribed from the real
/// <c>MacroDeck.Sdk.Actions.ActionParameter</c> factory signatures.
/// </summary>
/// <remarks>
/// Transcribing the signatures once is the whole point. The previous approach special-cased 12
/// of 29 types in each of three files and let the other 17 fall through to a
/// <c>ActionParameter.{Name}(...)</c> guess, which produced a call with a missing required
/// argument for <c>Code</c>, <c>Object</c>, <c>Array</c>, <c>Slider</c> and <c>Choice</c> and an
/// unknown named argument for <c>Duration</c>.
/// </remarks>
public sealed record ActionParameterTypeInfo
{
    /// <summary>The <c>ActionParameter</c> factory name.</summary>
    public required string Name { get; init; }

    /// <summary>The matching <c>ActionParameterType</c> enum name, which is the wire value.</summary>
    public required string WireType { get; init; }

    public bool SupportsPlaceholder { get; init; }
    public bool SupportsDescription { get; init; }
    public bool SupportsDefaultValue { get; init; }
    public bool SupportsRequired { get; init; }
    public bool SupportsOptions { get; init; }
    public bool SupportsRange { get; init; }

    /// <summary>
    /// Whether the range parameters include a step.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SupportsRange"/> because <c>Duration</c> takes <c>min</c> and
    /// <c>max</c> and no <c>step</c>. Emitting one anyway is a CS1739 in the user's plugin, and the
    /// designer's Step box is pre-filled, so it happened on every Duration with no user input.
    /// </remarks>
    public bool SupportsStep { get; init; } = true;
    public bool SupportsOptionsSourceId { get; init; }
    public bool SupportsFileExtensions { get; init; }
    public bool SupportsLanguage { get; init; }
    public bool SupportsValidationRegex { get; init; }
    public bool SupportsMaxLength { get; init; }
    public bool SupportsChildren { get; init; }
    public bool SupportsItemTemplate { get; init; }
    public bool SupportsAutoPrefixHttps { get; init; }
    public bool SupportsSupportsReset { get; init; }
    public bool SupportsLiteralOnly { get; init; }
    public bool SupportsWidgetTargetOptions { get; init; }

    /// <summary>One line shown in the designer.</summary>
    public required string Summary { get; init; }

    /// <summary>True when the type carries a value at all, so a default is meaningful.</summary>
    public bool IsValue => WireType is not ("String" or "Password" or "Secret" or "File" or "Folder"
        or "Hotkey" or "DateTime" or "KeyValue" or "IpAddress" or "Icon" or "Image"
        or "KeyboardSequence" or "KeyboardCombo");

    /// <summary>True when the editor presents a value as a number rather than text.</summary>
    public bool IsNumeric => WireType is "Number" or "Duration";
}

/// <summary>
/// Emits a correct <c>ActionParameter.…</c> call for any of the 29 editor types.
/// </summary>
/// <remarks>
/// The universal rule that makes this correct: the parameter <c>name</c> is the only argument
/// passed positionally, and everything else is passed by name. C# binds named arguments first
/// and then fills the remaining parameters in declaration order, so a positional argument
/// emitted for an optional parameter silently steals the slot of whatever comes after it — which
/// is exactly how <c>Slider(0, 100, 5, label: …)</c> ended up putting an <c>int</c> where a
/// <c>string</c> belonged.
/// </remarks>
public static class ActionParameterFactory
{
    /// <summary>
    /// Renders <paramref name="spec"/> as a C# expression.
    /// </summary>
    /// <param name="spec">The parameter to render.</param>
    /// <param name="accessor">
    /// Produces the C# expression for a localized string, given the property name
    /// (<c>Label</c>, <c>Description</c> or <c>Placeholder</c>). Actions, Events and Config Flow
    /// each have their own resx key prefix, so this is injected rather than hard-coded.
    /// </param>
    /// <param name="indent">Indent applied to continuation lines.</param>
    public static string Emit(ActionParameterSpec spec, Func<string, string> accessor, int indent = 0)
    {
        var info = ActionParameterTypes.Find(spec.EditorType) ?? ActionParameterTypes.All[0];
        var named = new List<string>();

        if (info.SupportsDescription && !string.IsNullOrWhiteSpace(spec.Description))
        {
            named.Add($"description: {accessor("Description")}");
        }

        if (info.SupportsPlaceholder && !string.IsNullOrWhiteSpace(spec.Placeholder))
        {
            named.Add($"placeholder: {accessor("Placeholder")}");
        }

        // Always state `required` explicitly for the types that take it. WidgetTarget's simple
        // overload defaults it to true, so omitting the argument for an optional target would
        // silently produce a required one - the user unchecks the box and the generated code
        // disagrees with them.
        if (info.SupportsRequired)
        {
            named.Add($"required: {(spec.Required ? "true" : "false")}");
        }

        // Range, but only for the types whose range parameters are min/max/step. Duration has min
        // and max and no step, so treating it as a step-capable type emitted `step:` and every
        // generated Duration failed with CS1739.
        if (info.SupportsRange)
        {
            // Slider's min and max are required, so they are always emitted - and emitted here,
            // once, rather than also appearing in RequiredArguments, which had it producing a
            // duplicate `max:`.
            var (min, max) = info.Name == "Slider" ? SliderBounds(spec) : (spec.Min, spec.Max);

            if (min is { } minValue)
            {
                named.Add($"min: {CSharpCode.NumberLiteral(minValue.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
            }

            if (max is { } maxValue)
            {
                named.Add($"max: {CSharpCode.NumberLiteral(maxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
            }

            if (info.SupportsStep && spec.Step is { } step)
            {
                named.Add($"step: {CSharpCode.NumberLiteral(step.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
            }
        }

        // A few overloads have a parameter with no default at all, so omitting it when the designer's
        // box is blank is a CS7036 in the user's plugin rather than a default. They are emitted
        // unconditionally, falling back to something valid.
        foreach (var required in RequiredArguments(info, spec, accessor, indent))
        {
            named.Add(required);
        }

        // Both, not one or the other. The earlier `else if` meant a spec carrying a static list and
        // a source id kept only the list - and the designer pre-fills the list, so a named source
        // was silently dropped. A type that requires its options gets an empty list when there are
        // none, which RequiredArguments already emitted; emitting it twice is a CS0102.
        var optionsRequired = info.Name == "Choice";
        if (info.SupportsOptions && (spec.Options.Count > 0 || optionsRequired))
        {
            // Autocomplete's list is optional; Choice's is required, so it is emitted whenever present.
            named.Add($"options: {RenderOptions(spec.Options, indent)}");
        }

        if (info.SupportsOptionsSourceId && !string.IsNullOrWhiteSpace(spec.OptionsSourceId))
        {
            named.Add($"optionsSourceId: {CSharpCode.StringLiteral(spec.OptionsSourceId)}");
        }

        if (info.SupportsFileExtensions && spec.FileExtensions.Count > 0)
        {
            var list = string.Join(", ", spec.FileExtensions.Select(CSharpCode.StringLiteral));
            named.Add($"fileExtensions: [{list}]");
        }

        if (info.SupportsLanguage)
        {
            named.Add($"language: {CSharpCode.StringLiteral(string.IsNullOrWhiteSpace(spec.Language) ? "plaintext" : spec.Language)}");
        }

        if (info.SupportsValidationRegex && !string.IsNullOrWhiteSpace(spec.ValidationRegex))
        {
            named.Add($"validationRegex: {CSharpCode.StringLiteral(spec.ValidationRegex)}");
        }

        if (info.SupportsMaxLength && spec.MaxLength is { } maxLength)
        {
            named.Add($"maxLength: {maxLength.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (info.SupportsAutoPrefixHttps && spec.AutoPrefixHttps)
        {
            named.Add("autoPrefixHttps: true");
        }

        if (info.SupportsSupportsReset && spec.SupportsReset)
        {
            named.Add("supportsReset: true");
        }

        if (info.SupportsLiteralOnly && spec.LiteralOnly)
        {
            named.Add("literalOnly: true");
        }

        // A duration's default is a span in milliseconds and its parameter is named accordingly.
        if (info.WireType == "Duration")
        {
            if (spec.DefaultMilliseconds is { } ms)
            {
                named.Add($"defaultMilliseconds: {CSharpCode.NumberLiteral(ms.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
            }
        }
        else if (info.SupportsDefaultValue && !string.IsNullOrWhiteSpace(spec.DefaultValue))
        {
            named.Add($"defaultValue: {RenderDefaultValue(spec, info)}");
        }

        // Children and item templates are emitted by RequiredArguments, because the overloads
        // require them. Emitting them here as well produced `children:` twice, which is CS1740.
        if (info.SupportsChildren && spec.Children.Count > 0 && info.Name != "Object")
        {
            var children = string.Join(", ", spec.Children.Select(c => Emit(c, accessor, indent + 4)));
            named.Add($"children: [{children}]");
        }

        if (info.SupportsItemTemplate && spec.ItemTemplate is { } item && info.Name != "Array")
        {
            named.Add($"itemTemplate: {Emit(item, accessor, indent + 4)}");
        }

        named.Add($"label: {accessor("Label")}");

        // WidgetTarget's options-carrying overload, when the designer asked for one. Label,
        // Description and Required live *inside* WidgetTargetOptions on this overload - passing them
        // as named arguments alongside it is a CS1744.
        if (info.SupportsWidgetTargetOptions && (!spec.AllowSelf || spec.WidgetTypes.Count > 0))
        {
            var parts = new List<string>
            {
                "Label = " + accessor("Label"),
                "Description = " + accessor("Description"),
                "Required = " + CSharpCode.BoolLiteral(spec.Required ? "true" : "false"),
                "AllowSelf = " + CSharpCode.BoolLiteral(spec.AllowSelf ? "true" : "false"),
            };
            if (spec.WidgetTypes.Count > 0)
            {
                parts.Add($"WidgetTypes = [{string.Join(", ", spec.WidgetTypes.Select(CSharpCode.StringLiteral))}]");
            }

            return $"ActionParameter.{info.Name}({CSharpCode.StringLiteral(spec.Name)}, "
                + $"new WidgetTargetOptions {{ {string.Join(", ", parts)} }})";
        }

        var all = new List<string> { CSharpCode.StringLiteral(spec.Name) };
        all.AddRange(named);
        return $"ActionParameter.{info.Name}({string.Join(", ", all)})";
    }

    /// <summary>
    /// The arguments an overload requires and has no default for.
    /// </summary>
    /// <remarks>
    /// <c>Object</c> needs its children and <c>Array</c> its item template; the designer leaves both
    /// boxes blank, and omitting a parameter with no default is a CS7036 in the user's plugin.
    /// <c>Slider</c>'s min and max are also required, but they are emitted from the range block
    /// above so that there is one source for them - having it in both places produced a duplicate
    /// <c>max:</c>, which is its own compile error.
    /// </remarks>
    private static IEnumerable<string> RequiredArguments(
        ActionParameterTypeInfo info,
        ActionParameterSpec spec,
        Func<string, string> accessor,
        int indent)
    {
        if (info.Name == "Object")
        {
            var children = spec.Children.Count > 0
                ? string.Join(", ", spec.Children.Select(c => Emit(c, accessor, indent + 4)))
                : "";
            yield return $"children: [{children}]";
        }

        if (info.Name == "Array")
        {
            // An array with no item template cannot render a row, so an empty text field stands in
            // for one: the user sees a field per element and a value they can type. The label is an
            // empty literal because the item template's own strings are not in the action's resx.
            var template = spec.ItemTemplate ?? new ActionParameterSpec { Name = "value" };
            yield return $"itemTemplate: {Emit(template, _ => "\"\"", indent + 4)}";
        }
    }

    /// <summary>
    /// A Slider's effective min and max, which the overload requires and has no default for.
    /// </summary>
    /// <remarks>
    /// Kept in order: a missing bound falls back towards the other one, never below it, because an
    /// inverted range is a slider the host rejects at configuration time.
    /// </remarks>
    private static (double? Min, double? Max) SliderBounds(ActionParameterSpec spec)
    {
        static double Parse(double? value) =>
            value ?? 0D;

        if (spec.Min is null && spec.Max is null)
        {
            return (0D, 100D);
        }

        if (spec.Min is null)
        {
            return (System.Math.Min(0D, Parse(spec.Max)), spec.Max);
        }

        return (spec.Min, spec.Max is null ? System.Math.Max(100D, Parse(spec.Min)) : spec.Max);
    }

    private static string RenderOptions(IReadOnlyList<ParameterOption> options, int indent)
    {
        var items = options.Select(o =>
            $"new ActionParameterOption {{ Value = {CSharpCode.StringLiteral(o.Value)}, Label = {CSharpCode.StringLiteral(o.Label)} }}");
        return $"[{string.Join(", ", items)}]";
    }

    private static string RenderDefaultValue(ActionParameterSpec spec, ActionParameterTypeInfo info) => info.WireType switch
    {
        "Boolean" => CSharpCode.BoolLiteral(spec.DefaultValue),
        "Number" => CSharpCode.NumberLiteral(spec.DefaultValue),
        "Color" => CSharpCode.StringLiteral(spec.DefaultValue),
        "Json" or "Code" => CSharpCode.StringLiteral(spec.DefaultValue),
        _ => CSharpCode.StringLiteral(spec.DefaultValue),
    };
}
