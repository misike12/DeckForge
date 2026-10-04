using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckForge.Core.Settings;

/// <summary>
/// Reads and writes one settings file, and tells listeners when either happened.
/// </summary>
/// <remarks>
/// <para>
/// Moved here from <c>DeckForge.App.Services</c> with the model it persists, because the file layer and
/// the file format are the same decision: the keys on <see cref="AppSettings"/>, the camel-casing, and
/// the fall-back-to-defaults behaviour are one contract, and splitting them across two assemblies means
/// the half that can be tested and the half that can be wrong are different halves.
/// </para>
/// <para>
/// Nothing here knows about a window, which is the whole reason it can be tested from the test project -
/// which deliberately does not reference the WPF app.
/// </para>
/// </remarks>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The file this service reads and writes.</summary>
    /// <remarks>
    /// A constructor argument rather than a constant, so a test can point at a temporary file. The
    /// default is the real per-user location; nothing in production constructs this with an argument.
    /// </remarks>
    public string Path { get; }

    /// <summary>Raised when the settings could not be read, with the reason.</summary>
    public event Action<string>? LoadFailed;

    /// <summary>Raised once for each value the load had to adjust, saying what it became.</summary>
    /// <remarks>
    /// Separate from <see cref="LoadFailed"/> because the two are opposites: one means the file was not
    /// read at all and everything defaulted, the other means it was read and one value in it was outside
    /// what this build accepts. Reporting the second through the first would call a working file a failed
    /// read, and not reporting it at all would make a silently clamped value look like a bug - "the magnet
    /// radius setting ignores me" is the report a quiet clamp produces.
    /// </remarks>
    public event Action<string>? SettingsRepaired;

    /// <summary>
    /// What is currently in force. Never null, and never a half-read file: every failure path below ends
    /// at a complete <see cref="AppSettings"/>.
    /// </summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Raised after a successful save, so the theme and the motion policy can be re-applied without
    /// waiting for a restart.
    /// </summary>
    /// <remarks>
    /// Raised only on a successful write. A save that could not replace the file has changed nothing, and
    /// announcing it as a change would make the theme re-apply itself for no reason and the status line
    /// say "saved" over a file that still holds the old values.
    /// </remarks>
    public event Action? SettingsChanged;

    /// <summary>Creates a service for the real per-user settings file.</summary>
    public SettingsService()
        : this(AppSettings.StorePath)
    {
    }

    /// <summary>Creates a service for a named file.</summary>
    /// <param name="path">
    /// The file to read and write. A path in a directory that does not exist yet is fine; the save creates
    /// it.
    /// </param>
    /// <remarks>
    /// Overload rather than a defaulted parameter so that <c>new SettingsService()</c> keeps meaning the
    /// one thing it meant before: the user's own file. There are four calls in the application and none of
    /// them pass a path.
    /// </remarks>
    public SettingsService(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>
    /// Reads the file, or leaves the defaults in place and says why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file that cannot be read never stops the application starting. That is the whole contract, and
    /// it was written the hard way: a corrupt file used to be replaced silently, so the user lost their
    /// accent, their recent list and their CLI version with no way to know. The reason is now reported and
    /// the file is kept exactly as it is, so it can be inspected or repaired by hand.
    /// </para>
    /// <para>
    /// A file that <em>can</em> be read but holds impossible values is a different case, and it is handled
    /// inside <see cref="AppSettings.Normalise"/> rather than here: those values are clamped and repaired
    /// individually, so one absurd magnet radius does not cost a user their recent workspaces the way a
    /// whole-file reset would.
    /// </para>
    /// </remarks>
    public void Load()
    {
        if (!File.Exists(Path))
        {
            Settings = new AppSettings();
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options)
                ?? new AppSettings();

            // An explicit JSON null reads back as a null list, and the next Add on it throws - from
            // opening a project, which is the one thing the user cannot avoid doing. A number outside its
            // range is clamped here for the same reason: an unusable value is a failure the user did not
            // cause and should not pay for. Each clamp is announced, because a value that changed without
            // saying so is indistinguishable from one that was ignored.
            foreach (var repair in Settings.Normalise())
            {
                SettingsRepaired?.Invoke(repair);
            }
        }
        catch (JsonException ex)
        {
            // A corrupt file used to be replaced silently, so the user lost their accent, their
            // recent list and their CLI version with no way to know. The reason is reported, and
            // the file is kept so it can be inspected or repaired.
            LoadFailed?.Invoke(ex.Message);
            Settings = new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by another process, or a permissions problem. Not corrupt, so the defaults
            // are a fallback rather than a reset, and the reason matters.
            LoadFailed?.Invoke(ex.Message);
            Settings = new AppSettings();
        }
    }

    /// <summary>
    /// Writes the settings, replacing the previous file only once the new one is complete.
    /// </summary>
    /// <returns>True when the file on disk is now this service's settings.</returns>
    /// <remarks>
    /// This used <c>File.WriteAllText</c>, which truncates first. A crash, a full disk or a losing
    /// race partway through left an empty or half-written settings.json, and the next start read
    /// that as corrupt and reset everything. The temporary file is written and flushed first, so
    /// the file on disk is either the old one or the new one.
    /// <para>
    /// Write beside, then replace - the same rule <c>VisualStore</c> follows for the canvas, and for the
    /// same reason: a settings file that cannot be parsed is a settings file whose loss is invisible until
    /// the user notices an accent has reverted. Canvas made the rule explicit for the document a user
    /// cannot regenerate; settings is smaller but it is the only record of a preference somebody chose on
    /// purpose, so it earns the same treatment.
    /// </para>
    /// <para>
    /// A bool rather than nothing, and never an exception. This runs on the UI thread from a settings
    /// change, and a file that is locked by another process is not a reason to hand the user a crash
    /// banner over an accent. The callers in this class ignore the answer; the Settings page does not, and
    /// says so rather than leaving a preference silently unsaved.
    /// </para>
    /// </remarks>
    public bool Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var temporary = Path + ".tmp";

        try
        {
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(Settings, Options);

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // File.Move with overwrite is atomic where the platform supports it, and a rename within a
            // directory is atomic everywhere.
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiscardTemporary(temporary);
            return false;
        }

        SettingsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Deletes the temporary file a failed save left behind.
    /// </summary>
    /// <param name="temporary">The path that was being written.</param>
    /// <remarks>
    /// Best effort, and it has to be: this runs while handling a failure, and a second failure here must
    /// not replace the first one. Nothing reads the temporary file - the load path looks only at the real
    /// one - so leaving it would be harmless, but the Settings page has a button that opens this very
    /// folder for a user to look at, and a folder of stray <c>settings.json.tmp</c> files is a question
    /// nobody can answer.
    /// </remarks>
    private static void DiscardTemporary(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Already the failure path; this one is not worth compounding.
        }
    }

    /// <summary>Puts a workspace at the top of the recent list, trimming the tail.</summary>
    /// <param name="solutionPath">The workspace's solution file.</param>
    /// <remarks>
    /// Removed before it is inserted so that reopening a workspace moves it to the top instead of
    /// appearing twice, and capped at ten because a recency list is a shortcut to the last few things and
    /// stops being one when it is a history.
    /// </remarks>
    public void AddRecentWorkspace(string solutionPath)
    {
        Settings.RecentWorkspaces.Remove(solutionPath);
        Settings.RecentWorkspaces.Insert(0, solutionPath);
        while (Settings.RecentWorkspaces.Count > 10)
        {
            Settings.RecentWorkspaces.RemoveAt(Settings.RecentWorkspaces.Count - 1);
        }

        Save();
    }

    /// <summary>Forgets a workspace, and saves only if it was there.</summary>
    /// <param name="solutionPath">The workspace's solution file.</param>
    /// <remarks>
    /// The <c>if</c> is the whole method. Removing something that was not in the list changes nothing, and
    /// saving anyway would rewrite the file - and touch it, and make every watcher of
    /// <see cref="SettingsChanged"/> re-apply the theme - over a click that changed nothing.
    /// </remarks>
    public void RemoveRecentWorkspace(string solutionPath)
    {
        if (Settings.RecentWorkspaces.Remove(solutionPath))
        {
            Save();
        }
    }

    /// <summary>Applies a change and persists it in one step.</summary>
    /// <param name="change">The change to make.</param>
    /// <returns>True when the change reached the disk.</returns>
    /// <remarks>
    /// One method rather than "mutate then save" so that a caller cannot forget the second half. Every
    /// settings write in the application goes through here, which is what makes
    /// <see cref="SettingsChanged"/> a complete signal rather than a hopeful one.
    /// </remarks>
    public bool Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        change(Settings);
        return Save();
    }
}