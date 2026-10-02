using System.Globalization;

namespace DeckForge.Core.Visual.Runtime;

/// <summary>
/// How the interpreter reads a value, matching <c>VisualRuntime</c> in the generated plugin exactly.
/// </summary>
/// <remarks>
/// <para>
/// These four methods are the parity surface. The generated runtime's <c>ToText</c>, <c>ToNumber</c>,
/// <c>ToBool</c> and its string interpolation decide what a block's value becomes in the plugin; the
/// interpreter has to agree on all of it or the debugger lies about the program it is debugging.
/// </para>
/// <para>
/// The rules are transcribed rather than shared, because one side is C# text emitted into a plugin that
/// targets the host's runtime and the other is this library — and a shared implementation would mean either
/// a shared project or generated code, both of which put a compiler between a block's value and the file
/// it lands in. What keeps them honest is <see cref="BlockSemantics"/>: every rule below is asserted, and
/// the emitter's own tests assert the text they generate.
/// </para>
/// </remarks>
public static class Values
{
    /// <summary>The value as text, the way the runtime's <c>ToText</c> would render it.</summary>
    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>The value as a number, the way the runtime's <c>ToNumber</c> would read it.</summary>
    /// <remarks>
    /// Anything unparseable is zero rather than an error, because that is what the generated runtime does
    /// and a debugger that failed where the plugin would not is worse than one that shows a wrong number.
    /// The divergence is worth naming though, so <see cref="IsNumber"/> exists for the diagnostics that
    /// want to say "that is not a number" without failing the run.
    /// </remarks>
    public static double Number(object? value) => value switch
    {
        null => 0,
        double number => number,
        float number => number,
        decimal number => (double)number,
        bool flag => flag ? 1 : 0,
        _ when value is sbyte or byte or short or ushort or int or uint or long or ulong
            => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        string text when double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0,
    };

    /// <summary>Whether text reads as a number at all.</summary>
    public static bool IsNumber(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>The value as a boolean, the way the runtime's <c>ToBool</c> would read it.</summary>
    public static bool Boolean(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        double number => number != 0,
        string text when bool.TryParse(text, out var parsed) => parsed,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number != 0,
        string text => text.Length > 0,
        _ => true,
    };

    /// <summary>
    /// Fills a log template's <c>{name}</c> holes from the run's parameters.
    /// </summary>
    /// <remarks>
    /// A hole with no parameter behind it is left as written rather than blanked. Blanking it produces a log
    /// line that reads "Running for  items" and tells the user nothing about which parameter is missing,
    /// while the written hole names it.
    /// </remarks>
    public static string FillTemplate(
        string template,
        IParameterSurface parameters,
        IReadOnlyList<string> declared)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(declared);

        return declared.Aggregate(
            template,
            (text, name) => text.Replace("{" + name + "}", parameters.Get(name) ?? "{" + name + "}"));
    }
}