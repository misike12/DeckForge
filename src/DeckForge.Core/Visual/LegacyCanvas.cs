using DeckForge.Core.Workspace;

namespace DeckForge.Core.Visual;

/// <summary>
/// A legacy <c>.blocks.json</c> found in a workspace, and the copy of it that was kept.
/// </summary>
/// <param name="Path">
/// Where the legacy file is, or null when there was none to read. A migration that finds a file it cannot
/// read has found something worth naming, which is why absence is a value here rather than a separate
/// result type.
/// </param>
/// <param name="Project">The migrated document, or null when the file was not one this build could read.</param>
/// <param name="KeptOriginalPath">
/// The untouched copy, or null when no copy was made — either because there was nothing to preserve or
/// because the copy failed, and the message says which.
/// </param>
/// <param name="AlreadyKept">
/// Whether the copy was already there. This is the whole of the idempotency rule, and it is a value rather
/// than a re-copy because re-copying is the one thing that must not happen twice.
/// </param>
/// <param name="Message">A sentence naming what happened, suitable for the status line.</param>
public sealed record LegacyCanvasResult(
    string? Path,
    VisualProject? Project,
    string? KeptOriginalPath,
    bool AlreadyKept,
    string Message)
{
    /// <summary>No legacy file anywhere in the workspace, which is the ordinary case.</summary>
    public static LegacyCanvasResult None { get; } = new(null, null, null, false, string.Empty);

    /// <summary>A legacy file was found and migrated.</summary>
    public bool Migrated => Project is not null;
}

/// <summary>
/// Finds the retired Blocks canvas inside a workspace, and keeps a copy of it before anything moves.
/// </summary>
/// <remarks>
/// <para>
/// §28.5 row 10 read "half": <c>VisualProjectJson</c> could migrate a legacy document it was
/// <em>handed</em> and <c>VisualStore</c> reported the migration, but nothing ever went looking for one, so
/// the only workspace that benefited was the one whose file had already been copied to
/// <c>.deckforge/canvas.json</c> by hand. A user who built a canvas on the retired page had exactly that
/// file and nothing else, so the migration existed and was unreachable — which is the same failure as an
/// extension point nothing calls.
/// </para>
/// <para>
/// <strong>Why a sibling, and why that name.</strong> Appendix F's hint says the originals were kept under
/// <c>.deckforge/visual/legacy/</c>, and this does not: the copy sits beside the file it came from, named
/// <c>&lt;action&gt;.blocks.original.json</c>. A directory that only ever holds migrations is one the user
/// has to know about before they can find it, and the sibling keeps the two names adjacent in every file
/// listing, in an explorer and in a diff. The action id is kept in the name so two actions' originals in
/// one plugin directory stay distinguishable, and <c>.original.json</c> does not end in
/// <see cref="Suffix"/>, so a preserved copy is never itself a candidate for migration — the shape of the
/// name is load-bearing, not decorative.
/// </para>
/// <para>
/// <strong>The original is never overwritten.</strong> The copy is made with overwrite off, and a copy that
/// is already there is reported rather than refreshed. A refresh would be the quiet way to destroy the only
/// surviving record of what a user's workspace looked like before DeckForge touched it, which is exactly
/// the thing the copy exists to preserve.
/// </para>
/// </remarks>
public static class LegacyCanvas
{
    /// <summary>The suffix the retired Blocks page wrote, and the only shape discovery accepts.</summary>
    public const string Suffix = ".blocks.json";

    /// <summary>What a preserved original is called, before the extension.</summary>
    public const string KeptSuffix = ".original.json";

    /// <summary>
    /// The legacy sidecars in this workspace, in a deterministic order.
    /// </summary>
    /// <param name="workspace">The open workspace.</param>
    /// <remarks>
    /// <para>
    /// <c>&lt;action&gt;.blocks.json</c> in the plugin project directory, which is where the retired page
    /// wrote it: its own <c>SidecarPath()</c> was <c>PluginProjectDirectory</c> joined with
    /// <c>BlockProgramJson.FileNameFor(program)</c>. The search is recursive because the file name is
    /// derived from an action id a user chose and nothing stopped that id being one of the few that puts
    /// the action in a folder.
    /// </para>
    /// <para>
    /// Sorted by full path with an ordinal comparison, so two runs over the same workspace produce the same
    /// list in the same order. A workspace with two legacy sidecars migrates the same one every time, and a
    /// migration that picked at random would leave the user with a document that is half of one action and
    /// half of another.
    /// </para>
    /// <para>
    /// <c>obj</c> and <c>bin</c> are skipped because a build output directory is not where a user put
    /// anything, and a copied sidecar in there would be a second candidate for the same work.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Candidates(WorkspaceContext workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var directory = workspace.PluginProjectDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory
                    .EnumerateFiles(directory, "*" + Suffix, SearchOption.AllDirectories)
                    .Where(file => !UnderBuildOutput(file))
                    .OrderBy(file => file, StringComparer.Ordinal),
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A workspace whose plugin directory cannot be listed is a workspace with no legacy canvas in
            // it as far as anything can tell. Throwing here would take the whole Visual page down over a
            // directory permission, which is the opposite of what a scan is for.
            return [];
        }
    }

    /// <summary>
    /// Where the untouched copy of <paramref name="legacyPath"/> goes.
    /// </summary>
    /// <remarks>
    /// Beside the original rather than under a <c>legacy/</c> directory, for the reason
    /// <see cref="Candidates"/> gives. Public because the message a user reads has to name the file, and a
    /// caller that rebuilt the name itself would eventually rebuild it differently.
    /// </remarks>
    public static string KeptPathFor(string legacyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);

        return Path.Combine(
            Path.GetDirectoryName(legacyPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(legacyPath) + KeptSuffix);
    }

    /// <summary>
    /// Reads a legacy sidecar, migrates it, and keeps the untouched original beside it.
    /// </summary>
    /// <param name="legacyPath">The file to read.</param>
    /// <remarks>
    /// <para>
    /// The original is preserved before the migration is handed over, and it is preserved whether or not
    /// the migration succeeded — a legacy file this build cannot read is still the user's work, and it is
    /// still the only copy of it. The migration itself goes through <see cref="VisualProjectJson.TryLoad"/>,
    /// so legacy detection stays in one place: a <c>.blocks.json</c> that is not a <c>BlockProgram</c> is
    /// reported rather than half-parsed, which is the same structural check
    /// <c>VisualProjectJson</c> makes on every file.
    /// </para>
    /// <para>
    /// Order matters for one reason only, and it is the reason this is not simply "read, then save": the
    /// copy must exist before anything writes a new file into the workspace, so that a failure after the
    /// migration has produced a document but before it has been kept still leaves the user's original where
    /// it always was.
    /// </para>
    /// </remarks>
    public static LegacyCanvasResult Migrate(string legacyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);

        var kept = Keep(legacyPath);

        if (kept.Problem is not null)
        {
            return new LegacyCanvasResult(
                legacyPath,
                null,
                null,
                false,
                $"The old Blocks canvas {Path.GetFileName(legacyPath)} was found but could not be copied "
                + $"aside ({kept.Problem}), so nothing was changed. {kept.Message}");
        }

        string text;
        try
        {
            text = File.ReadAllText(legacyPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new LegacyCanvasResult(
                legacyPath,
                null,
                kept.Path,
                kept.AlreadyKept,
                $"The old Blocks canvas {Path.GetFileName(legacyPath)} was found but could not be read "
                + $"({error.Message}). It has been left alone.");
        }

        var outcome = VisualProjectJson.TryLoad(text, out var project, out var message);

        if (outcome != VisualLoadOutcome.LoadedFromLegacy || project is null)
        {
            // Named, and left alone. A file that ends in .blocks.json but is not one is either somebody's
            // own JSON or a legacy format from before this one, and in both cases the only honest thing is
            // to say which file and leave it for the user to look at.
            return new LegacyCanvasResult(
                legacyPath,
                null,
                kept.Path,
                kept.AlreadyKept,
                $"{Path.GetFileName(legacyPath)} is in the plugin project directory but is not an old "
                + $"Blocks canvas ({message}). It has been left alone.");
        }

        return new LegacyCanvasResult(
            legacyPath,
            project,
            kept.Path,
            kept.AlreadyKept,
            kept.Message);
    }

    /// <summary>
    /// Copies the original aside, never over a copy that is already there.
    /// </summary>
    /// <remarks>
    /// The check and the copy are both needed, and the check is not redundant: <c>File.Copy</c> with
    /// overwrite off throws rather than skipping, so a second migration would surface as a failure instead of
    /// as the idempotent no-op the user expects. Loading twice must not be an error, and must not quietly
    /// refresh the preserved copy either.
    /// </remarks>
    private static LegacyKept Keep(string legacyPath)
    {
        var kept = KeptPathFor(legacyPath);

        if (File.Exists(kept))
        {
            return new LegacyKept(
                kept,
                true,
                null,
                $"The original {Path.GetFileName(kept)} was already kept and has not been overwritten.");
        }

        try
        {
            var directory = Path.GetDirectoryName(kept);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(legacyPath, kept, overwrite: false);

            return new LegacyKept(
                kept,
                false,
                null,
                $"The original {Path.GetFileName(legacyPath)} has been kept as "
                + $"{Path.GetFileName(kept)}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new LegacyKept(kept, false, error.Message, string.Empty);
        }
    }

    private static bool UnderBuildOutput(string file)
    {
        var separator = Path.DirectorySeparatorChar;

        return file.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where the copy went, whether it was already there, and what went wrong if anything did.</summary>
    private sealed record LegacyKept(string Path, bool AlreadyKept, string? Problem, string Message);
}