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
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string Accent { get; set; } = "Deck Blue";
    public string? DefaultProjectsDirectory { get; set; }
    public bool CheckForUpdatesOnStart { get; set; } = true;
    public bool TelemetryEnabled { get; set; }
    public List<string> RecentWorkspaces { get; set; } = [];
    public string? MacroDeckCliVersion { get; set; }
    public string? GitHubAccount { get; set; }
    /// <summary>Velopack update source (https folder or releases URL); empty disables update checks.</summary>
    public string? VelopackUpdateUrl { get; set; }

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

    public AppSettings Settings { get; private set; } = new();

    public event Action? SettingsChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(AppSettings.StorePath))
            {
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettings.StorePath), Options) ?? new AppSettings();
            }
        }
        catch (JsonException)
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.StorePath)!);
        File.WriteAllText(AppSettings.StorePath, JsonSerializer.Serialize(Settings, Options));
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
        Settings.RecentWorkspaces.Remove(solutionPath);
        Save();
    }
}
