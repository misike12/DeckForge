using System.Runtime.InteropServices;
using DeckForge.CliAdapter.Tools;

namespace DeckForge.CliAdapter;

/// <summary>How a failed check should be shown.</summary>
public enum DoctorSeverity
{
    /// <summary>DeckForge cannot build or test without this.</summary>
    Required,

    /// <summary>Something is nicer to have. Missing is not an error.</summary>
    Optional,
}

/// <param name="Severity">
/// Required or Optional. The Macro Deck desktop app is optional - the stub host runs without it -
/// and the checklist rendered it as a red failure anyway, so the one item a user is not missing
/// anything about was the only one shown in red.
/// </param>
/// <param name="InstallCommand">
/// The command that would fix this, when DeckForge can run it. Null when the fix is a download or
/// something only the user can decide.
/// </param>
public sealed record DoctorCheck(
    string Id,
    string Title,
    bool Ok,
    string? Detail,
    string? FixHint,
    DoctorSeverity Severity = DoctorSeverity.Required,
    string? InstallCommand = null);

/// <summary>
/// Probes the machine for everything plugin development needs and produces the
/// Environment page's checklist: .NET SDK, ASP.NET Core shared framework, the
/// macrodeck-plugin CLI, and a Macro Deck install.
/// </summary>
public sealed class EnvironmentDoctor(DotNetCli dotnet, MacroDeckCli cli)
{
    /// <summary>
    /// Overrides the expected CLI version. Set from the user's setting so a DeckForge pointed at a
    /// different SDK can gate on that rather than on the constant it was built with.
    /// </summary>
    public string? ExpectedCliVersionOverride { get; set; }

    private string ExpectedCliVersion() =>
        string.IsNullOrWhiteSpace(ExpectedCliVersionOverride)
            ? Core.Plugins.MacroDeckSdkInfo.DefaultCliVersion
            : ExpectedCliVersionOverride.Trim();

    /// <summary>
    /// Whether an installed version satisfies what DeckForge expects.
    /// </summary>
    /// <remarks>
    /// Compared as SemVer-with-prerelease rather than by string equality: the tool prints a build
    /// metadata suffix, and a strict comparison reports a mismatch the user cannot act on.
    /// </remarks>
    private static bool VersionsMatch(string? installed, string? expected)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        static string Normalise(string value) => value.Trim()
            .Split('+')[0]   // build metadata is not part of precedence
            .Split('-')[0];  // a prerelease of the same version is not a different version for this purpose

        return string.Equals(
            Normalise(installed),
            Normalise(expected),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeCli(string? installed, string expected, bool ok)
    {
        if (ok)
        {
            return $"macrodeck-plugin {installed}";
        }

        return installed is null
            ? $"not installed - DeckForge targets {expected} and uses the CLI to validate, pack, sign and test"
            : $"macrodeck-plugin {installed} - DeckForge targets {expected}";
    }

    public async Task<IReadOnlyList<DoctorCheck>> RunAllAsync(CancellationToken ct = default)
    {
        var checks = new List<DoctorCheck>();
        var sdk = await dotnet.ProbeAsync(ct);
        var sdkOk = sdk.Version is { Major: >= 10 };

        checks.Add(new DoctorCheck(
            "dotnet",
            ".NET SDK",
            sdkOk,
            sdk.Version is null
                ? sdk.Output
                : $"dotnet {sdk.Version} ({(sdk.Version.Major >= 10 ? "supported" : "too old - the SDK needs .NET 10 or later")})",
            sdkOk
                ? null
                : sdk.Version is null
                    ? "Install the .NET 10 SDK from https://dotnet.microsoft.com/download"
                    : $"The installed SDK is {sdk.Version}. DeckForge and the SDK it generates both target .NET 10.",
            InstallCommand: sdk.Version is null
                ? "winget install Microsoft.DotNet.SDK.10"
                : null));

        // Evaluated once. It was called twice per run, and it enumerates a directory tree, so the
        // second call could throw a different answer than the first if the folder changed underneath.
        var hasAspNet = TryHasAspNetCoreFramework(out var aspNetDetail);
        checks.Add(new DoctorCheck(
            "aspnet-framework",
            "ASP.NET Core shared framework",
            hasAspNet,
            aspNetDetail,
            hasAspNet
                ? null
                : "Install the .NET 10 SDK (it includes the ASP.NET Core shared framework) - the run and test commands need it",
            InstallCommand: hasAspNet ? null : "winget install Microsoft.DotNet.SDK.10"));

        var cliVersion = await cli.GetVersionAsync(ct);
        var expectedVersion = ExpectedCliVersion();
        var cliOk = cliVersion is not null;
        var matchesExpected = cliOk && VersionsMatch(cliVersion, expectedVersion);
        var installCommand = CliInstallCommand();

        checks.Add(new DoctorCheck(
            "macrodeck-cli",
            "macrodeck-plugin CLI",
            matchesExpected,
            DescribeCli(cliVersion, expectedVersion, matchesExpected),
            matchesExpected
                ? null
                // The version is the point: a CLI at the wrong version fails in ways that look like
                // a DeckForge bug - an unrecognised flag, a manifest field the tool does not know
                // about - rather than as a version skew.
                : $"DeckForge targets macrodeck-plugin {expectedVersion}. "
                  + (cliOk
                      ? $"The installed one is {cliVersion}, which is a different version."
                      : "It is not installed."),
            InstallCommand: matchesExpected ? null : installCommand));

        var macroDeck = FindMacroDeckInstall();
        checks.Add(new DoctorCheck(
            "macrodeck-host",
            "Macro Deck desktop app",
            macroDeck is not null,
            macroDeck ?? "not detected - the bundled stub host runs without it",
            macroDeck is null
                ? "Optional: install Macro Deck from https://macro-deck.app/ to test against the real host"
                : null,
            Severity: DoctorSeverity.Optional));

        return checks;
    }

    /// <summary>The exact command that installs the CLI at the version DeckForge targets.</summary>
    public string CliInstallCommand() =>
        "dotnet tool install --global MacroDeck.Plugin.Cli --version "
        + ExpectedCliVersion() + " --allow-prerelease";

    /// <summary>The command that updates an already-installed CLI to the version DeckForge targets.</summary>
    public string CliUpdateCommand() =>
        "dotnet tool update --global MacroDeck.Plugin.Cli --version "
        + ExpectedCliVersion() + " --allow-prerelease";

    private static bool TryHasAspNetCoreFramework(out string detail)
    {
        detail = "Microsoft.AspNetCore.App 10.x not found";
        try
        {
            var root = GetDotnetRoot();
            if (root is null)
            {
                return false;
            }

            var dir = Path.Combine(root, "shared", "Microsoft.AspNetCore.App");
            if (!Directory.Exists(dir))
            {
                return false;
            }

            // Reading the versions present is more useful than "present", because a machine with
            // both 8.x and 10.x installed was reporting the same answer as one with only 8.x.
            var versions = Directory
                .EnumerateDirectories(dir, "10.*")
                .Select(d => Path.GetFileName(d))
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (versions.Count == 0)
            {
                return false;
            }

            detail = "Microsoft.AspNetCore.App " + string.Join(", ", versions);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder we cannot read is not a folder we should report as a broken install.
            detail = "Microsoft.AspNetCore.App could not be read: " + ex.Message;
            return false;
        }
    }

    private static string? GetDotnetRoot()
    {
        var exe = FindOnPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return exe is null ? null : Path.GetDirectoryName(Path.GetFullPath(exe));
    }

    /// <summary>
    /// Finds a program on PATH.
    /// </summary>
    /// <remarks>
    /// This split on a hardcoded <c>';'</c>, which works on Windows and silently finds nothing
    /// anywhere else. Path.PathSeparator is the platform's own separator.
    /// </remarks>
    public static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // An invalid PATH entry is not a reason to stop looking.
            }
        }

        return null;
    }

    /// <summary>
    /// Looks for a Macro Deck install.
    /// </summary>
    /// <remarks>
    /// The three candidates disagreed about the product's own name - one said <c>MacroDeck</c> and
    /// two said <c>Macro Deck</c> - so it could only ever be found in one of those spellings
    /// depending on how it was installed. Both are now tried, and the per-user and Start Menu
    /// locations that a normal installer uses are included.
    /// </remarks>
    public static string? FindMacroDeckInstall()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var candidates = new List<string>();
        foreach (var folder in new[] { local, programFiles, programFilesX86 })
        {
            foreach (var name in ProductFolderNames)
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    candidates.Add(Path.Combine(folder, name, "MacroDeck.exe"));
                }
            }
        }

        foreach (var root in new[] { local, startMenu, appData })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            // Programs\ is where a per-user installer puts itself; the Start Menu is where a
            // system-wide one leaves a shortcut. Both are searched for the .lnk so a machine that
            // only has a shortcut still counts as having it.
            foreach (var relative in new[] { "Programs", @"Microsoft\Windows\Start Menu\Programs" })
            {
                var directory = Path.Combine(root, relative);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                try
                {
                    candidates.AddRange(Directory
                        .EnumerateFiles(directory, "MacroDeck*.lnk", SearchOption.AllDirectories)
                        .Take(4));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A Start Menu we cannot enumerate is not evidence of an install.
                }
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Both spellings of the product folder, because the installer picks one of them.</summary>
    private static readonly string[] ProductFolderNames = ["MacroDeck", "Macro Deck"];

    /// <summary>True on Windows, where the Macro Deck install paths above apply at all.</summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
