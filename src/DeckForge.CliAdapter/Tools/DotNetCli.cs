using System.Text.RegularExpressions;
using DeckForge.CliAdapter.Processes;

namespace DeckForge.CliAdapter.Tools;

/// <summary>Typed wrapper over the dotnet CLI for the plugin's own build/test lifecycle.</summary>
public sealed partial class DotNetCli
{
    private readonly ProcessRunner _runner;

    public DotNetCli(ProcessRunner runner) => _runner = runner;

    /// <summary>What <c>dotnet --version</c> actually said.</summary>
    /// <param name="Version">The parsed version, or null when the command failed or said nothing usable.</param>
    /// <param name="Output">Everything the command printed, kept so a diagnostic can be shown verbatim.</param>
    public sealed record SdkProbe(Version? Version, string Output)
    {
        public bool Found => Version is not null;
    }

    /// <summary>
    /// Reads the installed SDK version.
    /// </summary>
    /// <remarks>
    /// This used to return the whole trimmed stdout. <c>dotnet --version</c> is perfectly capable
    /// of printing several lines instead - an SDK that is not installed prints a "the SDK to resolve
    /// ... was not found" message - and the Environment page then reported that message as though
    /// it were the version. Only a line that parses as a version is accepted, and everything the
    /// command said is kept for the detail line.
    /// </remarks>
    public async Task<SdkProbe> ProbeAsync(CancellationToken ct = default)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync("dotnet", ["--version"], ct: ct);
        }
        catch (ProcessStartFailedException)
        {
            return new SdkProbe(null, "dotnet was not found on PATH");
        }

        if (!result.Succeeded)
        {
            return new SdkProbe(null, result.CombinedOutput.Trim());
        }

        var output = result.CombinedOutput;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = VersionPattern().Match(line);
            if (match.Success && Version.TryParse(match.Value, out var version))
            {
                return new SdkProbe(version, output.Trim());
            }
        }

        return new SdkProbe(null, output.Trim());
    }

    /// <summary>The installed SDK version, or null when dotnet is missing or said nothing usable.</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default) =>
        (await ProbeAsync(ct)).Version?.ToString();

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        (await ProbeAsync(ct)).Found;

    public Task<ProcessResult> RestoreAsync(string projectOrSolution, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["restore", projectOrSolution], ct: ct);

    public Task<ProcessResult> BuildAsync(string projectOrSolution, string configuration = "Debug", CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["build", projectOrSolution, "-c", configuration, "--nologo"], ct: ct);

    public Task<ProcessResult> PublishAsync(string projectOrSolution, string configuration, string runtime, string outputDirectory, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["publish", projectOrSolution, "-c", configuration, "-r", runtime, "-o", outputDirectory, "--nologo"], ct: ct);

    public Task<ProcessResult> TestAsync(string projectOrSolution, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["test", projectOrSolution, "--nologo"], ct: ct);

    /// <summary>
    /// Lists a project's packages.
    /// </summary>
    /// <remarks>
    /// <c>dotnet list package</c> is deprecated in favour of <c>dotnet package list</c> and prints a
    /// warning on every invocation. The old form still works, so nothing was visibly broken.
    /// </remarks>
    public Task<ProcessResult> ListPackagesAsync(string project, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["package", "list", project, "--include-transitive"], ct: ct);

    /// <summary>Whether the macrodeck-plugin global tool is already installed.</summary>
    public async Task<bool> IsMacroDeckCliInstalledAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _runner.RunAsync(
                "dotnet",
                ["tool", "list", "--global", "--id", "MacroDeck.Plugin.Cli"],
                ct: ct);
            return result.Succeeded
                && result.CombinedOutput.Contains("MacroDeck.Plugin.Cli", StringComparison.Ordinal);
        }
        catch (ProcessStartFailedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs the macrodeck-plugin global tool, or updates it when it is already there.
    /// </summary>
    /// <remarks>
    /// The method name promised install-or-update but only ever ran <c>dotnet tool install</c>,
    /// which fails with "tool is already installed" - so the button that was supposed to repair a
    /// wrong version could never fix it. Uninstalling first would also work but throws away the
    /// install for no reason when <c>update</c> does the right thing.
    /// </remarks>
    public async Task<ProcessResult> InstallMacroDeckCliAsync(string version, CancellationToken ct = default)
    {
        var alreadyInstalled = await IsMacroDeckCliInstalledAsync(ct);
        var verb = alreadyInstalled ? "update" : "install";

        return await _runner.RunAsync(
            "dotnet",
            ["tool", verb, "--global", "MacroDeck.Plugin.Cli", "--version", version, "--allow-prerelease"],
            ct: ct);
    }

    /// <summary>A version number, possibly with a prerelease or build suffix.</summary>
    [GeneratedRegex(@"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.\-+]+)?")]
    private static partial Regex VersionPattern();
}
