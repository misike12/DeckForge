namespace DeckForge.App.Services;

/// <summary>DI-friendly facade over the CodeGen ResxMerger.</summary>
/// <remarks>
/// Every resx mutation a view model performs goes through here. Two of them used to reach past the
/// facade into the static <c>ResxMerger</c> to call a method it did not expose, which meant the
/// facade was not the boundary it looked like and the same file could be written twice in a row
/// with two different notions of what "write" means.
/// </remarks>
public sealed class ResxMergerService
{
    /// <summary>Adds keys that are missing. Returns the keys actually added.</summary>
    public IReadOnlyList<string> AddKeys(string resxPath, IReadOnlyDictionary<string, string> entries) =>
        CodeGen.Generation.ResxMerger.AddKeys(resxPath, entries);

    /// <summary>Sets one key's value, creating it if missing. Returns true when the file changed.</summary>
    public bool SetKey(string resxPath, string key, string value) =>
        CodeGen.Generation.ResxMerger.SetKey(resxPath, key, value);

    /// <summary>Renames a key, keeping its position and value. Returns true when the file changed.</summary>
    public bool RenameKey(string resxPath, string oldKey, string newKey) =>
        CodeGen.Generation.ResxMerger.RenameKey(resxPath, oldKey, newKey);

    public IReadOnlyDictionary<string, string> ReadKeys(string resxPath) =>
        CodeGen.Generation.ResxMerger.ReadKeys(resxPath);
}
