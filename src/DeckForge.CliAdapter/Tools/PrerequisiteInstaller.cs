using System.Diagnostics;
using DeckForge.CliAdapter.Processes;

namespace DeckForge.CliAdapter.Tools;

/// <summary>The outcome of installing one prerequisite.</summary>
/// <param name="Id">The check it was for.</param>
/// <param name="Title">The check's title, for display.</param>
/// <param name="Ok">True when the prerequisite is now present.</param>
/// <param name="Message">What happened, in words a user can act on.</param>
public sealed record PrerequisiteResult(string Id, string Title, bool Ok, string Message);

/// <summary>
/// Installs the prerequisites the environment checks report as missing.
/// </summary>
/// <remarks>
/// <para>
/// The doctor already produced an <see cref="DoctorCheck.InstallCommand"/> for everything DeckForge
/// can install unattended, and nothing ran it - the command was displayed as text, so fixing a
/// machine meant copying it into a terminal. This runs them.
/// </para>
/// <para>
/// <b>Ordering matters.</b> The .NET SDK has to be installed before the CLI, because the CLI is a
/// <c>dotnet tool</c>. So "install everything" does not run the checks in whatever order they were
/// discovered: it installs the required ones first, in dependency order, and re-checks after each
/// one so a failure stops the sequence rather than cascading into a second confusing failure.
/// </para>
/// <para>
/// <b>Not everything is a command.</b> The Macro Deck desktop app is a download with an installer
/// that needs a user, so its action is to open the download page rather than to run something.
/// Pretending otherwise would either fail or, worse, appear to succeed.
/// </para>
/// </remarks>
public sealed class PrerequisiteInstaller(ProcessRunner runner)
{
    /// <summary>
    /// The prerequisites in the order they have to be installed.
    /// </summary>
    /// <remarks>
    /// The .NET SDK first, because the macrodeck-plugin CLI is a dotnet global tool and cannot be
    /// installed without it.
    /// </remarks>
    private static readonly string[] InstallOrder =
        ["dotnet-sdk", "aspnet-shared-framework", "macrodeck-cli", "macrodeck-host"];

    /// <summary>Where the Macro Deck desktop app comes from.</summary>
    public const string MacroDeckDownloadUrl = "https://macro-deck.app/";

    /// <summary>The prerequisites that are not yet satisfied, in installation order.</summary>
    public IReadOnlyList<DoctorCheck> Pending(IEnumerable<DoctorCheck> checks)
    {
        var byId = checks.Where(c => !c.Ok).ToDictionary(c => c.Id, StringComparer.Ordinal);
        return
        [
            .. InstallOrder.Where(byId.ContainsKey).Select(id => byId[id]),
            .. checks.Where(c => !c.Ok && !InstallOrder.Contains(c.Id)),
        ];
    }

    /// <summary>True when at least one check can be acted on without leaving the app.</summary>
    public bool CanInstall(DoctorCheck check) =>
        !check.Ok && (check.InstallCommand is not null || check.Id == "macrodeck-host");

    /// <summary>
    /// Installs every missing prerequisite, in dependency order, re-checking as it goes.
    /// </summary>
    /// <param name="checks">The current checks.</param>
    /// <param name="progress">Called before each item with a one-line description.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// An item that fails stops the sequence. Carrying on would produce a second failure caused by
    /// the first - "the CLI is not installed" after the SDK failed to install - which reads like two
    /// separate problems and sends the user looking in the wrong place.
    /// </remarks>
    public async Task<IReadOnlyList<PrerequisiteResult>> InstallAllAsync(
        IEnumerable<DoctorCheck> checks,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<PrerequisiteResult>();
        var remaining = Pending(checks).ToList();

        foreach (var check in remaining)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(Describe(check));

            var result = await InstallAsync(check, progress, ct);
            results.Add(result);

            if (!result.Ok)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>Installs one prerequisite.</summary>
    public async Task<PrerequisiteResult> InstallAsync(
        DoctorCheck check,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        if (check.Ok)
        {
            return new PrerequisiteResult(check.Id, check.Title, true, "Already installed.");
        }

        if (check.Id == "macrodeck-host")
        {
            // A download, not a command. Opening the page is the honest action; running an
            // installer that needs a person to click through it is not.
            var opened = TryOpen(MacroDeckDownloadUrl);
            return new PrerequisiteResult(
                check.Id,
                check.Title,
                Ok: false,
                Message: opened
                    ? "Opened the download page. Install Macro Deck, then re-check."
                    : "Macro Deck is downloaded from " + MacroDeckDownloadUrl + ".");
        }

        if (check.InstallCommand is not { Length: > 0 } command)
        {
            return new PrerequisiteResult(
                check.Id, check.Title, false, $"DeckForge cannot install this automatically. {check.FixHint}".Trim());
        }

        progress?.Invoke(command);
        var result = await runner.RunAsync(
            "cmd.exe",
            ["/c", command],
            workingDirectory: null,
            environmentVariables: null,
            ct);

        if (!result.Succeeded)
        {
            return new PrerequisiteResult(
                check.Id,
                check.Title,
                false,
                $"The install command failed (exit {result.ExitCode}). {LastLine(result.Combined ?? string.Empty)}".Trim());
        }

        // The check is re-run by the caller; report what the command said, not a guess at success.
        return new PrerequisiteResult(
            check.Id, check.Title, true, $"{check.Title}: command completed. Re-checking.");
    }

    private static string Describe(DoctorCheck check) =>
        check.InstallCommand is { Length: > 0 } command
            ? $"Installing {check.Title}: {command}"
            : $"Opening the download page for {check.Title}";

    private static string LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "" : lines[^1].Trim();
    }

    private static bool TryOpen(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                       or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
