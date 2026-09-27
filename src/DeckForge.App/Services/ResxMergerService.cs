namespace DeckForge.App.Services;

/// <summary>DI-friendly facade over the CodeGen ResxMerger.</summary>
public sealed class ResxMergerService
{
    public void AddKeys(string resxPath, IReadOnlyDictionary<string, string> entries) =>
        CodeGen.Generation.ResxMerger.AddKeys(resxPath, entries);

    public IReadOnlyDictionary<string, string> ReadKeys(string resxPath) =>
        CodeGen.Generation.ResxMerger.ReadKeys(resxPath);
}
