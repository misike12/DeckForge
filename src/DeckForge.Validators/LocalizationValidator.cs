using System.Xml.Linq;
using DeckForge.Core.Utils;

namespace DeckForge.Validators;

/// <summary>
/// Localization validator mirroring the MDLOC001-MDLOC008 diagnostics the Macro Deck
/// analyzers emit: duplicate keys, key/group collisions, malformed culture suffixes,
/// placeholder mismatches and broken plural families.
/// </summary>
public static class LocalizationValidator
{
    public static ValidationResult ValidateProject(LocalizationDirectory dir)
    {
        var result = new ValidationResult();

        // Culture suffix shape (MDLOC005): Strings.resx plus Strings.<tag>.resx siblings.
        foreach (var file in dir.Files)
        {
            var tag = file.CultureTag;
            if (tag is not null && !MacroDeckRules.IsValidLanguageTag(tag))
            {
                result.Add("MDLOC005", ValidationSeverity.Error,
                    $"'{file.FileName}': '{tag}' is not a well-formed culture suffix (de, pt-BR, zh-Hant-TW).",
                    file.FileName);
            }
        }

        if (dir.Default is null)
        {
            result.Add("MDLOC-DEFAULT", ValidationSeverity.Error,
                "Localization/Strings.resx is missing; it is required even for a single-language plugin.");
            return result;
        }

        var defaultKeys = ReadKeys(dir.Default.Path);
        foreach (var group in defaultKeys.Keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            result.Add("MDLOC003", ValidationSeverity.Error,
                $"Duplicate key '{group.Key}' in {dir.Default.FileName}.", dir.Default.FileName);
        }

        // Dotted keys: a key that is also a prefix group of others collides (MDLOC008).
        foreach (var key in defaultKeys.Keys)
        {
            var prefix = key + ".";
            if (defaultKeys.Keys.Any(other => other.StartsWith(prefix, StringComparison.Ordinal)))
            {
                result.Add("MDLOC008", ValidationSeverity.Error,
                    $"'{key}' is both a value key and a group other keys nest under.", dir.Default.FileName);
            }
        }

        // Placeholder checks: named placeholders must agree between default and translations.
        foreach (var translation in dir.Translations)
        {
            var keys = ReadKeys(translation.Path);
            foreach (var group in keys.Keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                result.Add("MDLOC003", ValidationSeverity.Error,
                    $"Duplicate key '{group.Key}' in {translation.FileName}.", translation.FileName);
            }
            foreach (var key in keys.Keys.Where(k => !defaultKeys.ContainsKey(k)))
            {
                result.Add("MDLOC001", ValidationSeverity.Error,
                    $"'{key}' exists in {translation.FileName} but not in {dir.Default.FileName}; translations are checked against the default.", translation.FileName);
            }

            foreach (var (key, value) in keys)
            {
                if (!defaultKeys.TryGetValue(key, out var defaultValue))
                {
                    continue;
                }
                var expected = Placeholders(defaultValue).ToHashSet(StringComparer.Ordinal);
                var actual = Placeholders(value).ToHashSet(StringComparer.Ordinal);
                if (!expected.SetEquals(actual))
                {
                    var missing = expected.Except(actual);
                    var extra = actual.Except(expected);
                    var detail = missing.Any() ? $"missing {string.Join(", ", missing)}" : $"unknown {string.Join(", ", extra)}";
                    result.Add("MDLOC002", ValidationSeverity.Error,
                        $"'{key}' in {translation.FileName} has placeholder mismatch: {detail}.", translation.FileName);
                }

                // Plural families (MDLOC007): .One implies .Other on every form.
                if (key.EndsWith(".One", StringComparison.Ordinal))
                {
                    var stem = key[..^4];
                    if (!keys.ContainsKey(stem + ".Other") && !defaultKeys.ContainsKey(stem + ".Other"))
                    {
                        result.Add("MDLOC007", ValidationSeverity.Error,
                            $"'{key}' starts a plural family but '{stem}.Other' is missing.", translation.FileName);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Named {placeholder} tokens; positional {0} forms give no compile safety.</summary>
    public static IReadOnlyList<string> Placeholders(string text)
    {
        var result = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }
            var end = text.IndexOf('}', i);
            if (end < 0)
            {
                break;
            }
            var name = text[(i + 1)..end];
            if (name.Length > 0 && name.All(c => char.IsAsciiLetter(c) || char.IsAsciiDigit(c) || c == '_') && !char.IsAsciiDigit(name[0]))
            {
                result.Add(name);
            }
            i = end;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadKeys(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            return doc.Root?.Elements("data")
                .Where(e => (string?)e.Attribute("name") is not null)
                .GroupBy(e => (string)e.Attribute("name")!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Element("value")?.Value ?? string.Empty, StringComparer.Ordinal)
                ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or System.IO.IOException)
        {
            return new Dictionary<string, string>();
        }
    }
}

/// <summary>The Localization directory of a plugin project, split into default + translations.</summary>
public sealed class LocalizationDirectory
{
    private readonly List<LocalizationFile> _files;

    public LocalizationDirectory(string pluginProjectDirectory)
    {
        var locDir = Path.Combine(pluginProjectDirectory, "Localization");
        _files = Directory.Exists(locDir)
            ? Directory.GetFiles(locDir, "*.resx")
                .Select(f => new LocalizationFile(f))
                .ToList()
            : [];
    }

    public IReadOnlyList<LocalizationFile> Files => _files;
    public LocalizationFile? Default => _files.FirstOrDefault(f => f.CultureTag is null);
    public IReadOnlyList<LocalizationFile> Translations => _files.Where(f => f.CultureTag is not null).ToList();
}

public sealed class LocalizationFile
{
    public LocalizationFile(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        var stem = FileName.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)
            ? FileName[..^5]
            : FileName;
        // Strings.resx -> null; Strings.de.resx -> "de"; Strings.zh-Hant-TW.resx -> "zh-Hant-TW".
        CultureTag = stem.StartsWith("Strings.", StringComparison.Ordinal) && stem.Length > 8 ? stem[8..] : null;
    }

    public string Path { get; }
    public string FileName { get; }
    /// <summary>null for the default Strings.resx.</summary>
    public string? CultureTag { get; }
}
