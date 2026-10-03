using System.Globalization;
using System.Xml.Linq;

namespace DeckForge.Core.Visual;

/// <summary>
/// An <see cref="IBlockTextLookup"/> over the workspace's own <c>Strings.resx</c> files.
/// </summary>
/// <remarks>
/// <para>
/// Part 21 says block labels must go through the localization DeckForge already has a manager and a
/// validator for. That manager's file is <c>&lt;workspace&gt;/Localization/Strings.resx</c>, so this reads
/// exactly what the Localization page edits and the diagnostics validate: a translator fills in a row there
/// and the block says the new words, with no second place to keep in step.
/// </para>
/// <para>
/// Tolerant by design, and the reason is the same as the English fallback. A resx is a hand-edited file
/// and a project can be mid-edit when a block canvas is opened; throwing here would take the page down over
/// a stray character, and the labels are the least important thing on it. So a missing, unreadable or
/// malformed file means "no translations", which is a state this lookup already knows how to be in.
/// </para>
/// </remarks>
public sealed class ResxTextLookup : IBlockTextLookup
{
    private readonly string _directory;
    private readonly string _culture;
    private readonly Dictionary<string, string> _neutral = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _translated = new(StringComparer.Ordinal);

    /// <summary>Whether the files were read at all, which is what a test and the diagnostics ask.</summary>
    public bool Loaded { get; private set; }

    /// <summary>
    /// A lookup over one workspace's localization directory.
    /// </summary>
    /// <param name="directory">Where <c>Strings.resx</c> and <c>Strings.&lt;culture&gt;.resx</c> live.</param>
    /// <param name="culture">
    /// The culture to read, or null for the neutral file alone.
    /// </param>
    /// <remarks>
    /// Neither file is required. A project that has never been translated answers nothing, and a project
    /// with only a neutral file answers from that.
    /// </remarks>
    public ResxTextLookup(string? directory, string? culture = null)
    {
        _directory = directory ?? string.Empty;
        _culture = string.IsNullOrWhiteSpace(culture) ? string.Empty : culture!;
        Reload();
    }

    /// <summary>
    /// Re-read the files, for when the Localization page has just saved one.
    /// </summary>
    public void Reload()
    {
        _neutral.Clear();
        _translated.Clear();
        Loaded = false;

        if (_directory.Length == 0 || !Directory.Exists(_directory))
        {
            return;
        }

        Copy(Read(Path.Combine(_directory, "Strings.resx")), _neutral);

        if (_culture.Length > 0)
        {
            Copy(Read(Path.Combine(_directory, $"Strings.{_culture}.resx")), _translated);
        }

        Loaded = _neutral.Count > 0 || _translated.Count > 0;
    }

    /// <summary>
    /// The translation, preferring the culture's file and falling back to the neutral one.
    /// </summary>
    /// <remarks>
    /// Culture first, because a partially translated file is normal and the neutral file is the safety net
    /// rather than a competitor: a missing row in the culture file should show English, not nothing.
    /// </remarks>
    /// <param name="key">The §21.1 key.</param>
    public string? Find(string key) =>
        _translated.TryGetValue(key, out var translated) ? translated
        : _neutral.TryGetValue(key, out var neutral) ? neutral
        : null;

    private static void Copy(IReadOnlyDictionary<string, string> from, Dictionary<string, string> to)
    {
        foreach (var pair in from)
        {
            // First one wins, matching ResxMerger's reader: a resx with a duplicated key is malformed,
            // and the convention every other reader here follows beats inventing a winner.
            to.TryAdd(pair.Key, pair.Value);
        }
    }

    private static IReadOnlyDictionary<string, string> Read(string path)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return keys;
        }

        XDocument document;

        try
        {
            document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return keys;
        }
        catch (IOException)
        {
            return keys;
        }

        if (document.Root is null)
        {
            return keys;
        }

        foreach (var element in document.Root.Elements("data"))
        {
            var name = element.Attribute("name")?.Value;

            if (!string.IsNullOrEmpty(name))
            {
                keys.TryAdd(name, element.Element("value")?.Value ?? string.Empty);
            }
        }

        return keys;
    }

    /// <summary>The current UI culture's name, or empty when it is the invariant one.</summary>
    /// <remarks>
    /// The invariant culture is the empty string on purpose: it is the one for which
    /// <c>Strings..resx</c> is a different file from <c>Strings.resx</c>, and reading a file named
    /// <c>Strings..resx</c> would quietly find nothing.
    /// </remarks>
    public static string CurrentCultureName =>
        CultureInfo.CurrentUICulture.Name.Length > 0 ? CultureInfo.CurrentUICulture.Name : string.Empty;
}
