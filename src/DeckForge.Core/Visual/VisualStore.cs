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
/// a legacy file that was migrated, or a newer file this build could only read.
/// </param>
/// <param name="ReadOnly">
/// The file on disk is newer than this build, and must not be written back.
/// </param>
/// <param name="KeptOriginalPath">
/// Where an untouched copy of a legacy <c>.blocks.json</c> was left, when one was made — so the page can
/// name a file the user can go and look at rather than only describing it.
/// </param>
/// <remarks>
/// A result rather than a bool because a load has four outcomes that lead to four different actions —
/// loaded, migrated with the original kept aside, read-only, refused, or not found — and a bool forces the
/// caller to discover which by re-reading the file. The same shape as <see cref="VisualLoadOutcome"/>, which is
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
    bool ReadOnly = false,
    string? KeptOriginalPath = null)
{
    /// <summary>A save that worked, or a load that read the file the user expected.</summary>
    public static VisualStoreResult Saved(string path, string message, VisualProject? project = null) =>
        new(true, path, message, project);

    /// <summary>A load that worked, from a backup or from a file that had to be migrated.</summary>
    /// <remarks>
    /// <paramref name="keptOriginal"/> is the copy <see cref="LegacyCanvas"/> made. Carried rather than
    /// folded into the message because "your original is still there" is only useful if the user can be
    /// told <em>where</em> it is, and a sentence is a worse API for that than a path is.
    /// </remarks>
    public static VisualStoreResult Migrated(
        string path,
        string message,
        VisualProject? project = null,
        string? keptOriginal = null) =>
        new(true, path, message, project, Recovered: true, KeptOriginalPath: keptOriginal);

    /// <summary>
    /// A newer file, opened and shown, and never to be written back.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Migrated"/> because the two need opposite things from a caller: a migration
    /// wants saving enabled the moment the user has seen it, and a newer file wants it disabled for the rest
    /// of the session. Both are "this is not quite what you asked for", which was true of exactly one of them
    /// before, and lumping them together is how the page ended up offering to save the sample document over
    /// a file it had just refused to read.
    /// </remarks>
    public static VisualStoreResult OpenedReadOnly(string path, string message, VisualProject project) =>
        new(true, path, message, project, Recovered: true, ReadOnly: true);

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
/// <para>
/// Two rules here exist because of what a document can be rather than where it is. A workspace with no
/// canvas but with a retired <c>.blocks.json</c> in it <strong>is</strong> a canvas, and is adopted on load
/// (<see cref="LegacyCanvas"/>); and a canvas a newer DeckForge wrote is opened rather than refused, but
/// never written back, which is why <see cref="VisualProject.ReadOnly"/> is on the model rather than on
/// this class.
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
    /// <para>
    /// The backup is on because a canvas is the only thing here a user cannot regenerate from a template.
    /// The C# can always be rebuilt from the document; if the document is lost there is nothing to rebuild
    /// it from.
    /// </para>
    /// <para>
    /// A read-only document is refused here rather than only in the page. The flag lives on the document
    /// precisely so this method can act on it, and a guard that exists in one caller is a guard the next
    /// caller does not have: the page's <c>CanSave</c> was the only thing stopping a canvas written by a
    /// newer DeckForge from being replaced by this build's reading of it, and "this build's reading" of a
    /// format it does not understand is the one file on disk that must not be overwritten at any price.
    /// </para>
    /// </remarks>
    public static VisualStoreResult Save(
        WorkspaceContext workspace,
        VisualProject project,
        bool backup = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(project);

        var path = PathFor(workspace);

        if (project.ReadOnly)
        {
            return VisualStoreResult.Failed(
                path,
                "This canvas was written by a newer DeckForge, so it is open read-only and cannot be "
                + "saved. Nothing was written - update DeckForge to edit this project.");
        }

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
    /// <para>
    /// A workspace with no <c>canvas.json</c> but with a legacy <c>.blocks.json</c> in its plugin project
    /// is the retired Blocks page's workspace, and this is where it is discovered. That used to be
    /// unreachable: the migration existed and the store reported it, but nothing went looking, so the only
    /// workspace that benefited was one somebody had already copied the file for by hand. See
    /// <see cref="LegacyCanvas"/>.
    /// </para>
    /// </remarks>
    public static VisualStoreResult Load(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var path = PathFor(workspace);

        if (!File.Exists(path))
        {
            return AdoptLegacy(workspace, path);
        }

        var primary = Read(path);

        if (primary.Ok)
        {
            return primary;
        }

        // A newer file is `Ok` and `ReadOnly`, so it never reaches here and the backup is never offered in
        // its place. That is the point: falling back would have put a two-saves-old reading of the same
        // workspace on screen, which is exactly the "silently drew the previous save" defect the backup
        // fallback was built for a corrupt file and must not be extended to.
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
    /// Adopts a legacy canvas when the workspace has one and no <c>canvas.json</c>, and says there is
    /// nothing to adopt otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The migrated document is written out here rather than left in memory for the caller to save, which
    /// is the one surprising thing this does. It is deliberate: a migration that only exists until the next
    /// action leaves the user in a state where closing the window loses the migration, and reopening finds
    /// the same legacy file and migrates it again. Writing it makes the migration happen once, which is
    /// what "migrated" has to mean if a second load is going to report an ordinary read.
    /// </para>
    /// <para>
    /// The write goes through <see cref="Save"/> with no backup, because there is nothing on disk to back
    /// up — the legacy file is the previous version, and it is being kept, untouched, under the name
    /// <see cref="LegacyCanvas.KeptPathFor"/> gives it. The original is therefore never overwritten and
    /// never deleted, and a second load finds a <c>canvas.json</c> and reports an ordinary read: the
    /// migration is idempotent because its own output is what stops it running twice.
    /// </para>
    /// </remarks>
    private static VisualStoreResult AdoptLegacy(WorkspaceContext workspace, string path)
    {
        var candidates = LegacyCanvas.Candidates(workspace);
        if (candidates.Count == 0)
        {
            return VisualStoreResult.Failed(
                path,
                "This workspace has no canvas yet. Drag a block in from the palette to start one.");
        }

        string? refused = null;

        foreach (var candidate in candidates)
        {
            var legacy = LegacyCanvas.Migrate(candidate);
            if (!legacy.Migrated)
            {
                // Remembered rather than returned: a workspace can hold more than one, and the file that
                // actually migrated is a better answer than the first one that did not.
                refused ??= legacy.Message;
                continue;
            }

            var written = Save(workspace, legacy.Project!, backup: false);
            if (!written.Ok)
            {
                return VisualStoreResult.Failed(
                    path,
                    $"{legacy.Message} The migrated canvas could not be written ({written.Message}) "
                    + "The original has not been changed.");
            }

            return VisualStoreResult.Migrated(
                path,
                $"{legacy.Message} It has been migrated to {FileName}.",
                legacy.Project,
                legacy.KeptOriginalPath);
        }

        return VisualStoreResult.Failed(
            path,
            refused
            ?? $"No old Blocks canvas in this workspace could be read. Nothing was changed.");
    }

    /// <summary>
    /// Whether a workspace has a canvas, or something that will become one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For deciding whether opening a workspace should replace the sample document. Silently replacing a
    /// real canvas with the sample would look like the user's work being wiped, and the repository has
    /// already recorded a canvas doing exactly that on plain navigation.
    /// </para>
    /// <para>
    /// A legacy sidecar counts, because <see cref="Load"/> adopts one: asking this about a workspace the
    /// retired Blocks page wrote has to say yes, or the one question a caller is asking — "is there user
    /// work here" — gets the answer "no" for a workspace holding nothing but user work.
    /// </para>
    /// </remarks>
    public static bool Exists(WorkspaceContext workspace) =>
        File.Exists(PathFor(workspace)) || LegacyCanvas.Candidates(workspace).Count > 0;

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
    /// <remarks>
    /// <para>
    /// Through <see cref="VisualProjectJson.TryLoadReadOnly"/> rather than
    /// <see cref="VisualProjectJson.TryLoad"/>, because the store's job is to show the user the file they
    /// opened. A canvas from a newer DeckForge used to come back as a refusal with no document, and the
    /// page answered a refusal by drawing the sample — so a user with real work in that file was looking at
    /// a demonstration of a blank canvas and a sentence saying theirs had not been read.
    /// </para>
    /// <para>
    /// Refusing is still what happens for everything else: not JSON, structurally wrong, or a slot holding
    /// two values. The read-only reader differs in exactly one respect, and the difference is carried on
    /// <see cref="VisualProject.ReadOnly"/> so <see cref="Save"/> can refuse to write it back.
    /// </para>
    /// </remarks>
    private static VisualStoreResult Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var outcome = VisualProjectJson.TryLoadReadOnly(json, out var project, out var message);

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
                VisualLoadOutcome.LoadedReadOnly when project is not null =>
                    VisualStoreResult.OpenedReadOnly(path, message, project),
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