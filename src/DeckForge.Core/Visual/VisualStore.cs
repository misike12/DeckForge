using DeckForge.Core.Visual;
using DeckForge.Core.Workspace;

namespace DeckForge.Core.Visual;

/// <summary>What a save or a load did.</summary>
/// <param name="Ok">Whether it worked.</param>
/// <param name="Path">Where the document was read from or written to.</param>
/// <param name="Message">A sentence naming what happened, for the status line.</param>
/// <param name="Project">The document a load produced, or null when there was none to read.</param>
/// <param name="Recovered">
/// Whether the document came from somewhere other than the file the user expects to be editing — a backup,
/// or a legacy file that was migrated.
/// </param>
/// <remarks>
/// A result rather than a bool because a load has four outcomes that lead to four different actions —
/// loaded, migrated with the original kept aside, refused, or not found — and a bool forces the caller to
/// discover which by re-reading the file. The same shape as <see cref="VisualLoadOutcome"/>, which is
/// what made it obvious that the file layer already knew this.
///
/// The document rides along rather than being an out-parameter so a failed load cannot be half-used: there
/// is no way to read the property and get a default-constructed project that validates as empty.
/// </remarks>
public sealed record VisualStoreResult(
    bool Ok,
    string Path,
    string Message,
    VisualProject? Project = null,
    bool Recovered = false,
    bool ReadOnly = false)
{
    /// <summary>
    /// The file on disk is newer than this build and must not be written back.
    /// </summary>
    /// <remarks>
    /// Carried through from the load rather than left as a message. The load refused the file and said it
    /// "will not be overwritten"; the page then showed the sample document with Save enabled, and Save put
    /// the sample where the newer file had been - after which the backup held the newer file and the
    /// promise had been kept in the only sense that still had the old text available. A refusal the caller
    /// cannot act on is not a refusal.
    /// </remarks>
    /// <summary>A save that worked, or a load that read the file the user expected.</summary>
    public static VisualStoreResult Saved(string path, string message, VisualProject? project = null) =>
        new(true, path, message, project);

    /// <summary>A load that worked, from a backup or from a file that had to be migrated.</summary>
    public static VisualStoreResult Migrated(string path, string message, VisualProject? project = null) =>
        new(true, path, message, project, Recovered: true);

    /// <summary>A save or load that did not, with nothing changed on disk.</summary>
    public static VisualStoreResult Failed(string path, string message) =>
        new(false, path, message);
}

/// <summary>
/// Where a visual document lives, and how it gets there and back.
/// </summary>
/// <remarks>
/// <para>
/// P5's "save to plugin" and "reload restores it exactly". The document is a sidecar beside the plugin —
/// <c>.deckforge/canvas.json</c> — rather than inside the action source, because the action source is
/// generated and anything hand-edited inside it is lost on the next write. The sidecar is the document; the
/// C# is a projection of it.
/// </para>
/// <para>
/// Atomic writes, because a canvas is the one thing a user is most likely to lose work in, and this
/// repository has already recorded the shape of that failure: a file written in place and cut short by a
/// crash is a document that cannot be opened, and there is nothing to recover it from. Write beside, then
/// replace.
/// </para>
/// </remarks>
public static class VisualStore
{
    /// <summary>The sidecar's name inside the workspace's DeckForge state directory.</summary>
    public const string FileName = "canvas.json";

    /// <summary>
    /// Where a workspace keeps its canvas document.
    /// </summary>
    /// <param name="workspace">The open workspace.</param>
    /// <remarks>
    /// Inside <c>.deckforge</c> rather than beside the manifest, because that directory is already
    /// DeckForge's own and putting private state next to the plugin's own files invites a user to edit it
    /// by hand and then wonder why the canvas came back different.
    /// </remarks>
    public static string PathFor(WorkspaceContext workspace) =>
        Path.Combine(workspace.DeckForgeStateDirectory, FileName);

    /// <summary>
    /// Writes the document, creating the state directory if it is not there.
    /// </summary>
    /// <param name="workspace">The open workspace.</param>
    /// <param name="project">The document to write.</param>
    /// <param name="backup">
    /// Whether to keep the previous file as <c>canvas.previous.json</c> before replacing it.
    /// </param>
    /// <remarks>
    /// The backup is on because a canvas is the only thing here a user cannot regenerate from a template.
    /// The C# can always be rebuilt from the document; if the document is lost there is nothing to rebuild
    /// it from.
    /// </remarks>
    public static VisualStoreResult Save(
        WorkspaceContext workspace,
        VisualProject project,
        bool backup = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(project);

        var path = PathFor(workspace);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Serialised first, into a temporary file, and only then does anything on disk change. The
            // backup used to be copied before the temporary was written, so a failure after that point
            // left `canvas.previous.json` holding what had been in `canvas.json` before *this* save - the
            // save before last - while the failure message said nothing on disk had changed. Ordering the
            // write first means the claim is true: if anything below throws, both files are as they were.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, VisualProjectJson.Serialize(project));

            if (backup && File.Exists(path))
            {
                File.Copy(path, BackupPathFor(workspace), overwrite: true);
            }

            // Beside, then replace. A write in place that is interrupted leaves a file that cannot be
            // parsed, and a canvas that cannot be parsed is a canvas that is gone.
            File.Move(temporary, path, overwrite: true);

            var blocks = project.Targets.Sum(target => target.Scripts.Count);
            return VisualStoreResult.Saved(
                path,
                $"Saved {blocks} script{(blocks == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return VisualStoreResult.Failed(path, $"The canvas could not be saved ({ex.Message}). "
                + "Nothing on disk was changed.");
        }
    }

    /// <summary>Where the previous save is kept.</summary>
    public static string BackupPathFor(WorkspaceContext workspace) =>
        Path.Combine(workspace.DeckForgeStateDirectory, "canvas.previous.json");

    /// <summary>
    /// Reads the document, or the backup when the current file is unreadable.
    /// </summary>
    /// <param name="workspace">The open workspace.</param>
    /// <remarks>
    /// <para>
    /// Falling back to the backup is deliberate and it is the whole reason the backup exists. A canvas
    /// that cannot be parsed but whose previous version can is not lost work — it is a save that went wrong,
    /// and the honest answer is the last good state with a warning, not an empty canvas.
    /// </para>
    /// <para>
    /// The current file is left exactly as it is when the backup is used, right up until the user saves.
    /// It is evidence: they may want to look at it, and overwriting the thing that went wrong with the
    /// thing that worked destroys the only copy of whatever happened. Which is why the message says that
    /// saving replaces it — the warning is only useful if it names the consequence.
    /// </para>
    /// </remarks>
    public static VisualStoreResult Load(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var path = PathFor(workspace);

        if (!File.Exists(path))
        {
            return VisualStoreResult.Failed(
                path,
                "This workspace has no canvas yet. Drag a block in from the palette to start one.");
        }

        var primary = Read(path);

        if (primary.Ok)
        {
            return primary;
        }

        var backup = BackupPathFor(workspace);
        if (!File.Exists(backup))
        {
            return primary;
        }

        var fallback = Read(backup);

        return fallback.Ok
            ? VisualStoreResult.Migrated(
                backup,
                $"The saved canvas could not be read, so the previous save was opened instead. "
                + $"{Path.GetFileName(path)} has been left as it was — saving will replace it.",
                fallback.Project)
            : primary;
    }

    /// <summary>
    /// Whether a workspace has a canvas on disk.
    /// </summary>
    /// <remarks>
    /// For deciding whether opening a workspace should replace the sample document. Silently replacing a
    /// real canvas with the sample would look like the user's work being wiped, and the repository has
    /// already recorded a canvas doing exactly that on plain navigation.
    /// </remarks>
    public static bool Exists(WorkspaceContext workspace) =>
        File.Exists(PathFor(workspace));

    /// <summary>
    /// The text the workspace would save, for comparing a document against its file.
    /// </summary>
    /// <remarks>
    /// Serialization goes through the same writer as <see cref="Save"/>, so "is this dirty" is a real
    /// comparison of what is on screen with what is on disk rather than a flag someone remembered to set.
    /// </remarks>
    public static string Serialize(VisualProject project) => VisualProjectJson.Serialize(project);

    /// <summary>
    /// Whether the document differs from what is on disk.
    /// </summary>
    /// <param name="workspace">The open workspace.</param>
    /// <param name="project">The document on screen.</param>
    /// <remarks>
    /// Compared as text, with whitespace and line endings normalised. A document that differs only in
    /// indentation is not a document the user changed, and telling them it is unsaved work every time they
    /// open the window trains them to ignore the indicator entirely — which is the same as having none.
    /// </remarks>
    public static bool IsDirty(WorkspaceContext workspace, VisualProject project)
    {
        var path = PathFor(workspace);

        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            return !Normalise(File.ReadAllText(path)).Equals(Normalise(Serialize(project)), StringComparison.Ordinal);
        }
        catch (IOException)
        {
            // Unreadable is not "unchanged". Reporting dirty sends the user to Save, which is the right
            // next action; reporting clean would let them close the window with the only copy on screen.
            return true;
        }
    }

    /// <summary>Reads one file, reporting rather than throwing.</summary>
    private static VisualStoreResult Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var outcome = VisualProjectJson.TryLoad(json, out var project, out var message);

            return outcome switch
            {
                VisualLoadOutcome.Loaded => VisualStoreResult.Saved(
                    path,
                    $"Opened {Path.GetFileName(path)}.",
                    project),
                VisualLoadOutcome.LoadedFromLegacy => VisualStoreResult.Migrated(
                    path,
                                        // The file that is still on disk, not a copy nothing makes. This named
                    // `x.migrated.json`, which no writer in the project produces, so a user who believed it
                    // went looking for a safety copy that does not exist - while the real original was
                    // sitting untouched under the name they already had.
                    $"{message} The original {Path.GetFileName(path)} has not been changed.",
                    project),
                VisualLoadOutcome.UnsupportedSchema => new VisualStoreResult(
                    false, path, message, ReadOnly: true),
                _ => VisualStoreResult.Failed(path, message),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return VisualStoreResult.Failed(path, $"The canvas could not be read ({ex.Message}).");
        }
    }

    /// <summary>Line endings and indentation normalised, so a round trip is not a change.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
}