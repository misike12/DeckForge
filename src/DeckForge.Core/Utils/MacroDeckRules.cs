using System.Text.RegularExpressions;

namespace DeckForge.Core.Utils;

/// <summary>
/// Shared validation rules mirroring the Macro Deck manifest/CLI rules, so the editor can
/// show the same errors the CLI will, instantly, before any build.
/// </summary>
public static partial class MacroDeckRules
{
    /// <summary>Reverse-domain plugin id: com.example.hue-lights. No underscores.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*)+$")]
    public static partial Regex PluginId();

    /// <summary>Local capability id: lowercase kebab-case, no '::'.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")]
    public static partial Regex LocalId();

    /// <summary>Variable name: [a-z0-9_].</summary>
    [GeneratedRegex(@"^[a-z0-9_]+$")]
    public static partial Regex VariableName();

    /// <summary>BCP-47-ish language tag: de, pt-BR, zh-Hant-TW.</summary>
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z]{4})?(-([A-Za-z]{2}|\d{3}))*$")]
    public static partial Regex LanguageTag();

    /// <summary>SemVer 2.0, permissive on pre-release/build metadata.</summary>
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    public static partial Regex SemVer();

    /// <summary>C# identifier, dots allowed as namespace separators (Acme.LightControl).</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    public static partial Regex CSharpIdentifier();

    public static bool IsValidPluginId(string id) => !string.IsNullOrWhiteSpace(id) && PluginId().IsMatch(id);

    public static bool IsValidLocalId(string id) => !string.IsNullOrWhiteSpace(id) && LocalId().IsMatch(id);

    public static bool IsValidVariableName(string name) => !string.IsNullOrWhiteSpace(name) && VariableName().IsMatch(name);

    public static bool IsValidLanguageTag(string tag) => !string.IsNullOrWhiteSpace(tag) && LanguageTag().IsMatch(tag);

    public static bool IsValidSemVer(string version) => !string.IsNullOrWhiteSpace(version) && SemVer().IsMatch(version);

    public static bool IsValidCSharpIdentifier(string s) => !string.IsNullOrWhiteSpace(s) && CSharpIdentifier().IsMatch(s);

    /// <summary>Derives a suggested plugin id from a display name: "Acme Light Control" by "Acme" -> com.acme.light-control.
    /// A name that already starts with the publisher does not repeat it.</summary>
    public static string SuggestPluginId(string pluginName, string publisher)
    {
        static string Kebab(string s) =>
            new string(s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray())
                .Trim('-')
                .Split('-', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
                ? string.Join('-', parts)
                : "plugin";

        var publisherSegment = Kebab(string.IsNullOrWhiteSpace(publisher) ? "example" : publisher);
        var nameSegments = Kebab(pluginName).Split('-');
        if (nameSegments.Length > 1 && nameSegments[0] == publisherSegment)
        {
            // "Acme Light Control" by "Acme" -> com.acme.light-control
            return $"com.{publisherSegment}.{string.Join("-", nameSegments.Skip(1))}";
        }
        return $"com.{publisherSegment}.{string.Join("-", nameSegments)}";
    }

    /// <summary>Derives a C# project name from a display name: "Acme Light Control" -> "Acme.LightControl".</summary>
    public static string SuggestProjectName(string pluginName)
    {
        var parts = pluginName.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "MyPlugin";
        }
        var result = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return char.IsLetter(result[0]) ? result : "Plugin" + result;
    }
}
