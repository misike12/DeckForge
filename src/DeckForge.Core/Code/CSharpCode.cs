using System.Globalization;
using System.Text;

namespace DeckForge.Core.Code;

/// <summary>
/// C# source emission primitives shared by every DeckForge code generator.
/// </summary>
/// <remarks>
/// Every generator used to roll its own escaping, and every one of them got it wrong in the
/// same way: backslash and quote were handled, newline was not, and sanitised identifiers were
/// still allowed to be C# keywords. Those three defects are why a generated file could fail to
/// compile for reasons that had nothing to do with the user's input.
/// </remarks>
public static class CSharpCode
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>
    /// Contextual keywords: legal as identifiers, but not as the name of a type or an alias.
    /// </summary>
    /// <remarks>
    /// A resx key segment becomes a nested class in the generated <c>Strings</c> type, and the
    /// Macro Deck generator cannot emit one for <c>file</c> - it fails with CS9056, "Types and
    /// aliases cannot be named 'file'". A parameter called <c>file</c> is ordinary input (the SDK
    /// itself has a <c>File</c> editor type), so the segment is prefixed rather than refused.
    /// </remarks>
    private static readonly HashSet<string> ContextualKeywords = new(StringComparer.Ordinal)
    {
        "add", "alias", "and", "ascending", "args", "async", "await", "by", "descending", "dynamic",
        "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let", "managed",
        "nameof", "nint", "not", "notnull", "nuint", "on", "or", "orderby", "partial", "record",
        "remove", "required", "scoped", "select", "set", "unmanaged", "value", "var", "when", "where",
        "with", "yield",
    };

    /// <summary>All 77 C# reserved words, for verbatim identifier escaping.</summary>
    public static IReadOnlySet<string> ReservedWords => Keywords;

    /// <summary>
    /// Escapes <paramref name="value"/> as the body of a C# string literal, without the quotes.
    /// Handles every character a literal can contain, including the control characters and the
    /// line/paragraph separators that are legal in a file but not in a literal.
    /// </summary>
    public static string EscapeLiteralBody(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\0': sb.Append("\\0"); break;
                case '\a': sb.Append("\\a"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\v': sb.Append("\\v"); break;
                // U+2028/U+2029 terminate a line for the C# lexer even though they are not \r or \n.
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;
                default:
                    if (char.IsControl(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>A complete C# string literal, quotes included.</summary>
    public static string StringLiteral(string? value) => $"\"{EscapeLiteralBody(value)}\"";

    /// <summary>
    /// A C# <c>bool</c> literal. <see cref="bool.TryParse"/> semantics, so "TRUE" and " true "
    /// both work; anything unrecognised is <c>false</c>.
    /// </summary>
    public static string BoolLiteral(string? value) => bool.TryParse(value?.Trim(), out var parsed) && parsed ? "true" : "false";

    /// <summary>
    /// A C# numeric literal for <paramref name="value"/>, invariant-culture so a designer on a
    /// comma-decimal locale still emits <c>1.5</c> and not <c>1,5</c>. Falls back to the raw text
    /// when it is not a number, which keeps a malformed range visible in the generated file
    /// rather than silently coercing it to zero.
    /// </summary>
    public static string NumberLiteral(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return "0";
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("R", CultureInfo.InvariantCulture)
            : text;
    }

    /// <summary>A C# numeric literal for a value that is already a number.</summary>
    public static string NumberLiteral(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A C# numeric literal for an optional value, or <c>0</c> when absent.</summary>
    public static string NumberLiteral(double? value) => value is { } v ? NumberLiteral(v) : "0";

    /// <summary>A C# <c>char</c> literal.</summary>
    public static string CharLiteral(char value) => $"'\\u{(int)value:x4}'";

    /// <summary>
    /// Turns arbitrary designer text into a legal C# identifier: each character that cannot
    /// appear in an identifier becomes <c>_</c>, a leading digit is prefixed, and a result that
    /// collides with a keyword is prefixed with <c>@</c>.
    /// </summary>
    public static string Identifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var sb = new StringBuilder(value.Length + 2);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        var result = sb.ToString();
        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        return Keywords.Contains(result) ? "@" + result : result;
    }

    /// <summary>
    /// <see cref="Identifier"/> for a designer's resx key segment. The Macro Deck localization
    /// source generator applies exactly this transformation, so generating the reference the
    /// same way is what keeps a resx key and its C# accessors in agreement.
    /// </summary>
    /// <summary>
    /// A resx key segment that the SDK's localization generator will turn into the member name the
    /// generated code actually calls.
    /// </summary>
    /// <remarks>
    /// The generator maps <c>@</c> to <c>_</c>, so a key segment written as <c>@object</c> - which is
    /// what <see cref="Identifier"/> returns for a C# keyword, and a parameter may legitimately be
    /// called <c>object</c> - becomes the member <c>_object</c>. Referencing <c>…Parameters.@object</c>
    /// is then a CS0117. The prefix is mapped here rather than left to chance.
    /// </remarks>
    public static string ResxSegment(string? value)
    {
        var identifier = Identifier(value);

        // The generator maps `@` to `_`, and cannot emit a type for a contextual keyword at all.
        identifier = identifier.Replace("@", "_", StringComparison.Ordinal);
        return ContextualKeywords.Contains(identifier) ? "_" + identifier : identifier;
    }

    /// <summary>Pascal-cases a kebab-case or snake_case id, e.g. <c>set-volume</c> to <c>SetVolume</c>.</summary>
    public static string ToPascal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Value";
        }

        var parts = value.Split(['-', '_', ' ', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "Value";
        }

        var sb = new StringBuilder(value.Length);
        foreach (var part in parts)
        {
            if (char.IsDigit(part[0]))
            {
                sb.Append('_').Append(part);
                continue;
            }

            sb.Append(char.ToUpperInvariant(part[0]));
            sb.Append(part[1..]);
        }

        var result = sb.ToString();
        return Keywords.Contains(result) ? "@" + result : result;
    }

    /// <summary>Camel-cases an id, e.g. <c>my-action</c> to <c>myAction</c>.</summary>
    public static string ToCamel(string? value)
    {
        var pascal = ToPascal(value).TrimStart('@');
        return string.IsNullOrEmpty(pascal) ? "value" : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>Escapes text for an XML attribute or element body.</summary>
    public static string Xml(string? value) => System.Security.SecurityElement.Escape(value ?? string.Empty);

    /// <summary>
    /// A type name built from an id, safe to use as a class name and as a file name.
    /// </summary>
    /// <remarks>
    /// <see cref="ToPascal"/> and <see cref="Identifier"/> prefix a C# keyword with <c>@</c>, which
    /// is right for an identifier but wrong for a type name: the suffix is appended afterwards, so
    /// the id <c>int</c> became <c>@intAction</c>. In a file name that is worse than a compile error -
    /// csc fails on the response file before it ever compiles the project, with CS2011.
    /// <para>
    /// So the <c>@</c> is dropped first, the suffix applied, and the finished name checked again -
    /// by which point <c>IntAction</c> is not a keyword and needs no escape.
    /// </para>
    /// </remarks>
    public static string TypeName(string? pascalOrId, string suffix = "")
    {
        var stem = (pascalOrId ?? "").TrimStart('@');
        var name = stem + suffix;
        return Keywords.Contains(name) ? "@" + name : name;
    }

    /// <summary>Escapes text for an XML attribute, quotes included.</summary>
    public static string XmlAttribute(string? value) => Xml(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>Escapes a URL for an XML attribute.</summary>
    public static string XmlUrl(string? value) => Xml(value).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("&", "&amp;", StringComparison.Ordinal);

    /// <summary>Indents every line of <paramref name="text"/> by <paramref name="spaces"/>.</summary>
    public static string Indent(string? text, int spaces)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return Indent(text, new string(' ', Math.Max(0, spaces)));
    }

    /// <summary>
    /// Indents every line of <paramref name="text"/> by an exact prefix. The official template
    /// is tab-indented, so a space count would rewrite its shape.
    /// </summary>
    public static string Indent(string? text, string prefix)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (prefix.Length == 0)
        {
            return text;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => line.Length == 0 ? line : prefix + line));
    }

    /// <summary>
    /// Joins emitted members one per line, guaranteeing a trailing newline so the next
    /// <c>AppendLine</c> starts on its own line. The Widget designer lost whole subtrees to a
    /// missing newline before this existed.
    /// </summary>
    public static string JoinLines(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.Append(line);
            sb.Append('\n');
        }

        return sb.ToString().Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }
}
