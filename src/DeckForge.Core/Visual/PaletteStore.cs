using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckForge.Core.Visual;

/// <summary>
/// Where a workspace's palette memory is written: beside the canvas, in the same directory and with the
/// same atomic write.
/// </summary>
/// <remarks>
/// <para>
/// A separate file from <c>canvas.json</c>, and that is the decision worth stating: the canvas is the
/// user's script and is version-controlled with it, while this is a habit. Saving one must never dirty
/// the other, or every block a user drops makes their plugin look modified.
/// </para>
/// <para>
/// Read-side only in the common case — nothing here fails the palette. A corrupt or absent file means an
/// empty recency row, which is exactly what a new workspace looks like anyway, and a write that fails says
/// so in the return value rather than throwing out of a star click.
/// </para>
/// </remarks>
public static class PaletteStore
{
    /// <summary>The file's name inside the workspace's state directory.</summary>
    public const string FileName = "palette.json";

    /// <summary>Where this workspace's palette memory lives.</summary>
    /// <param name="workspace">The workspace.</param>
    public static string PathFor(Workspace.WorkspaceContext workspace) =>
        Path.Combine(workspace.DeckForgeStateDirectory, FileName);

    /// <summary>
    /// Reads a workspace's palette memory, with an empty one for a workspace that has none.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="known">The block kinds the catalogue has, for pruning.</param>
    /// <returns>The memory, and whether it came from a file rather than from nowhere.</returns>
    public static (PaletteMemory Memory, bool Restored) Load(
        Workspace.WorkspaceContext workspace,
        IReadOnlySet<string> known)
    {
        var path = PathFor(workspace);

        if (!File.Exists(path))
        {
            return (PaletteMemory.Restore(null, known), false);
        }

        try
        {
            var state = JsonSerializer.Deserialize<PaletteMemoryState>(
                File.ReadAllText(path),
                Json);

            return (PaletteMemory.Restore(state, known), true);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            // A palette that will not read is an empty palette, not a failed launch. The file stays on disk
            // untouched so the user can look at it or hand it to whoever maintains the workspace.
            return (PaletteMemory.Restore(null, known), false);
        }
    }

    /// <summary>
    /// Writes a workspace's palette memory, atomically.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="memory">What to write.</param>
    /// <returns>Where it went, and whether it did.</returns>
    public static (string Path, bool Saved) Save(Workspace.WorkspaceContext workspace, PaletteMemory memory)
    {
        var path = PathFor(workspace);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write then move, as the canvas does: a half-written habit file would be read back as an empty
            // palette, which is indistinguishable from never having used the palette at all.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(memory.ToState(), Json));
            File.Move(temporary, path, overwrite: true);

            return (path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (path, false);
        }
    }

    /// <summary>
    /// The serializer settings, kept here so the file format is this class's business.
    /// </summary>
    /// <remarks>
    /// Indented, because this file is small, is written when a user clicks a star, and is the sort of
    /// thing a person opens to work out why their recency row is empty. A single line answers neither.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}