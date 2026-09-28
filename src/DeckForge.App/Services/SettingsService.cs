using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckForge.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>App-level settings persisted under %LOCALAPPDATA%/DeckForge/settings.json.</summary>
/// <remarks>
/// Every property here is read by something. A setting that is written to disk and never read is
/// worse than no setting: it appears in the JSON, it invites a user to change it, and nothing
/// happens. There were five such properties; each is now wired to the feature it names.
/// </remarks>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string Accent { get; set; } = "Deck Blue";

    /// <summary>Folder the New Plugin wizard offers first. Empty means Documents.</summary>
    public string? DefaultProjectsDirectory { get; set; }

    /// <summary>Ask the update source for a newer release when the app starts.</summary>
    public bool CheckForUpdatesOnStart { get; set; } = true;

    /// <summary>GitHub account the Publish page uses to name the owner of a release.</summary>
    public string? GitHubAccount { get; set; }

    /// <summary>
    /// Extension ids the user has switched off.
    /// </summary>
    /// <remarks>
    /// Default is off, not on. An extension is third-party code that runs in this process the
    /// moment it is discovered, so installing one is not consent to load it - the user has to say
    /// so. Everything found is listed either way, so a disabled extension is visible rather than
    /// invisible.
    /// </remarks>
    public List<string> DisabledExtensions { get; set; } = [];

    /// <summary>
    /// The macrodeck-plugin version DeckForge targets. The Environment page gates on it, because a
    /// wrong CLI version fails in ways that look like a DeckForge bug rather than a version skew.
    /// </summary>
    public string? MacroDeckCliVersion { get; set; }

    /// <summary>Velopack update source (https folder or releases URL); empty disables update checks.</summary>
    public string? VelopackUpdateUrl { get; set; }

    public List<string> RecentWorkspaces { get; set; } = [];

    /// <summary>
    /// Replaces a list that the JSON made null, so a hand-edited or truncated settings file cannot
    /// leave a caller with a null where it expects a list.
    /// </summary>
    /// <remarks>
    /// An explicit <c>"recentWorkspaces": null</c> deserialises to a null property, and the next
    /// <c>Insert</c> on it is a NullReferenceException - from opening a project, which is the one
    /// action the user cannot avoid. The property initialiser does not help: the deserialiser
    /// overwrites it with the null it read.
    /// </remarks>
    public void RepairNulls()
    {
        RecentWorkspaces ??= [];
        DisabledExtensions ??= [];
    }

    [JsonIgnore]
    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeckForge", "settings.json");
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Raised when the settings could not be read, with the reason.</summary>
    public event Action<string>? LoadFailed;

    public AppSettings Settings { get; private set; } = new();

    public event Action? SettingsChanged;

    public void Load()
    {
        if (!File.Exists(AppSettings.StorePath))
        {
            Settings = new AppSettings();
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(AppSettings.StorePath), Options) ?? new AppSettings();

            // An explicit JSON null reads back as a null list, and the next Add on it throws - from
            // opening a project, which is the one thing the user cannot avoid doing.
            Settings.RepairNulls();
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

    /// <summary>Writes the settings, replacing the previous file only once the new one is complete.</summary>
    /// <remarks>
    /// This used <c>File.WriteAllText</c>, which truncates first. A crash, a full disk or a losing
    /// race partway through left an empty or half-written settings.json, and the next start read
    /// that as corrupt and reset everything. The temporary file is written and flushed first, so
    /// the file on disk is either the old one or the new one.
    /// </remarks>
    public void Save()
    {
        var path = AppSettings.StorePath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(Settings, Options);
        var temporary = path + ".tmp";

        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        // File.Move with overwrite is atomic where the platform supports it, and a rename within a
        // directory is atomic everywhere.
        File.Move(temporary, path, overwrite: true);

        SettingsChanged?.Invoke();
    }

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

    public void RemoveRecentWorkspace(string solutionPath)
    {
        if (Settings.RecentWorkspaces.Remove(solutionPath))
        {
            Save();
        }
    }

    /// <summary>Applies a change and persists it in one step.</summary>
    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        change(Settings);
        Save();
    }
}
