using System.IO;

namespace DeckForge.App.Services;

/// <summary>DI-friendly facade over the CodeGen ResxMerger.</summary>
/// <remarks>
/// <para>
/// Every resx mutation a view model performs goes through here. Two of them used to reach past the
/// facade into the static <c>ResxMerger</c> to call a method it did not expose, which meant the
/// facade was not the boundary it looked like and the same file could be written twice in a row
/// with two different notions of what "write" means.
/// </para>
/// <para>
/// Reads here are tolerant and writes are not, because the two failures mean different things. A resx
/// is hand-edited, and a project can be mid-edit when the Localization page is opened: a malformed
/// file threw <see cref="System.Xml.XmlException"/> out of the page, and the shell's handler exits the
/// process after three faults - so one unclosed tag cost a translator their session. A *write* that
/// fails still throws, because a save that silently did nothing is worse than one that says so.
/// </para>
/// </remarks>
public sealed class ResxMergerService
{
    /// <summary>Why the last read produced nothing, or null when it read a file.</summary>
    /// <remarks>
    /// Said here rather than thrown, and held here rather than returned per call, because every caller
    /// wants the same two answers - the keys and whether the file was readable - and a tuple that can say
    /// "no keys, no reason" is how a corrupt file came to be reported as an empty one.
    /// </remarks>
    public string? LastReadError { get; private set; }

    /// <summary>Adds keys that are missing. Returns the keys actually added.</summary>
    public IReadOnlyList<string> AddKeys(string resxPath, IReadOnlyDictionary<string, string> entries) =>
        CodeGen.Generation.ResxMerger.AddKeys(resxPath, entries);

    /// <summary>Sets one key's value, creating it if missing. Returns true when the file changed.</summary>
    public bool SetKey(string resxPath, string key, string value) =>
        CodeGen.Generation.ResxMerger.SetKey(resxPath, key, value);

    /// <summary>Renames a key, keeping its position and value. Returns true when the file changed.</summary>
    public bool RenameKey(string resxPath, string oldKey, string newKey) =>
        CodeGen.Generation.ResxMerger.RenameKey(resxPath, oldKey, newKey);

    /// <summary>Removes a key. Returns true when the file changed.</summary>
    /// <remarks>
    /// On the facade because it is the one destructive mutation, and the next view model that needs it
    /// would otherwise reach past the boundary this class exists to hold - which is exactly the
    /// regression its own header records having been fixed once.
    /// </remarks>
    public bool RemoveKey(string resxPath, string key) =>
        CodeGen.Generation.ResxMerger.RemoveKey(resxPath, key);

    /// <summary>The keys in a resx, or nothing at all when the file cannot be read.</summary>
    /// <remarks>
    /// Records <see cref="LastReadError"/> instead of throwing. The Localization page reads on every
    /// navigation, so a resx that is one stray character from valid must not take the page - and through
    /// the crash handler, the application - down with it.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ReadKeys(string resxPath)
    {
        LastReadError = null;

        try
        {
            return CodeGen.Generation.ResxMerger.ReadKeys(resxPath);
        }
        catch (Exception error) when (error is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            LastReadError = $"{Path.GetFileName(resxPath)} could not be read: {error.Message}";
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}