using System.IO;
using System.Net.Http;
using DeckForge.Core.Plugins;
using DeckForge.Core.Settings;

namespace DeckForge.App.Services;

/// <summary>What an update check found.</summary>
public sealed record UpdateCheckResult(
    bool Available,
    string Version,
    string? CurrentVersion,
    string Message)
{
    public static UpdateCheckResult UpToDate(string current) =>
        new(false, current, current, $"DeckForge {current} is up to date.");

    public static UpdateCheckResult NotConfigured() =>
        new(false, "", null, "No update source is configured.");

    public static UpdateCheckResult NotInstalled(string? current) =>
        new(false, current ?? "", current,
            "This copy is not an installed build (a dev run or an unpackaged folder), so updates "
            + "do not apply to it.");
}

/// <summary>
/// Asks the configured Velopack source whether a newer DeckForge exists.
/// </summary>
/// <remarks>
/// This lived in the Settings page's click handler, which meant the <c>CheckForUpdatesOnStart</c>
/// setting had nothing to switch on: the check only ever ran when a button was pressed. It is a
/// service so the app can run it on start and the page can show the same result.
/// </remarks>
public sealed class UpdateCheckService(SettingsService settings)
{
    /// <summary>The running version, from the entry assembly.</summary>
    public static string CurrentVersion =>
        typeof(UpdateCheckService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Asks the update source. Never throws: a network failure is a result, not an error.</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var source = settings.Settings.VelopackUpdateUrl;
        if (string.IsNullOrWhiteSpace(source))
        {
            return UpdateCheckResult.NotConfigured();
        }

        try
        {
            var manager = new Velopack.UpdateManager(new Velopack.Sources.SimpleWebSource(source));

            // A dev run is not installed, so it can never be updated in place. Saying so is more
            // useful than an exception from the update library.
            if (!manager.IsInstalled)
            {
                return UpdateCheckResult.NotInstalled(CurrentVersion);
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return UpdateCheckResult.UpToDate(CurrentVersion);
            }

            return new UpdateCheckResult(
                true,
                update.TargetFullRelease.Version.ToString(),
                CurrentVersion,
                $"DeckForge {update.TargetFullRelease.Version} is available; you have {CurrentVersion}.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or UriFormatException)
        {
            // No network, a bad URL, a proxy that refused: all of them mean "could not check", and
            // an unhandled exception here would take down startup when the setting is on.
            return new UpdateCheckResult(
                false,
                "",
                CurrentVersion,
                $"Could not reach the update source: {ex.Message}");
        }
    }
}
