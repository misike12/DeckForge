using System.Xml.Linq;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Merges resx data entries into existing Localization/Strings*.resx files of a generated
/// plugin, preserving formatting and comments. Used by capability scaffolders when adding
/// features to an already-created project, and by the Localization manager.
/// </summary>
public static class ResxMerger
{
    /// <summary>Adds missing keys to the resx at <paramref name="path"/>. Returns the keys added.</summary>
    public static IReadOnlyList<string> AddKeys(string path, IReadOnlyDictionary<string, string> entries)
    {
        var added = new List<string>();
        var doc = Load(path);

        foreach (var (key, value) in entries)
        {
            if (HasData(doc, key))
            {
                continue;
            }
            doc.Root!.Add(new XElement("data",
                new XAttribute("name", key),
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement("value", value)));
            added.Add(key);
        }

        if (added.Count > 0)
        {
            Save(doc, path);
        }
        return added;
    }

    /// <summary>Sets a key's value, creating it if missing. Returns true when the file changed.</summary>
    public static bool SetKey(string path, string key, string value)
    {
        var doc = Load(path);
        var data = FindData(doc, key);
        if (data is null)
        {
            doc.Root!.Add(new XElement("data",
                new XAttribute("name", key),
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement("value", value)));
            Save(doc, path);
            return true;
        }

        var valueEl = data.Element("value");
        if (valueEl is null || valueEl.Value != value)
        {
            valueEl?.ReplaceWith(new XElement("value", value));
            Save(doc, path);
            return true;
        }
        return false;
    }

    /// <summary>Removes a key. Returns true when the file changed.</summary>
    public static bool RemoveKey(string path, string key)
    {
        var doc = Load(path);
        var data = FindData(doc, key);
        if (data is null)
        {
            return false;
        }
        data.Remove();
        Save(doc, path);
        return true;
    }

    /// <summary>
    /// Renames a key in place, keeping its value and its position in the file. Returns true when
    /// the file changed.
    /// </summary>
    /// <remarks>
    /// The attribute is set rather than the element replaced, because replacing it would move the
    /// key to the end of the file and lose the hand-ordered grouping most resx files rely on.
    /// Renaming onto a key that already exists is refused rather than silently merging two strings.
    /// </remarks>
    public static bool RenameKey(string path, string oldKey, string newKey)
    {
        var doc = Load(path);
        var data = FindData(doc, oldKey);
        if (data is null)
        {
            return false;
        }

        if (HasData(doc, newKey))
        {
            throw new InvalidOperationException(
                $"'{newKey}' already exists in {Path.GetFileName(path)}; rename would discard one of them.");
        }

        data.SetAttributeValue("name", newKey);
        Save(doc, path);
        return true;
    }

    /// <summary>Reads all data entries as key -> value.</summary>
    public static IReadOnlyDictionary<string, string> ReadKeys(string path)
    {
        var doc = Load(path);
        return doc.Root!.Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static XDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"resx not found: {path}", path);
        }
        return XDocument.Load(path, LoadOptions.PreserveWhitespace);
    }

    private static void Save(XDocument doc, string path) => doc.Save(path);

    private static XElement? FindData(XDocument doc, string key) =>
        doc.Root!.Elements("data")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), key, StringComparison.Ordinal));

    private static bool HasData(XDocument doc, string key) => FindData(doc, key) is not null;
}
