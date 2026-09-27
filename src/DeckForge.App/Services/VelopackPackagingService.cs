using System.IO;
using DeckForge.CliAdapter.Processes;

namespace DeckForge.App.Services;

/// <summary>
/// Runs the Velopack CLI (vpk) to produce the DeckForge installer, portable build and
/// delta packages. Velopack is authoritative for packaging; this service only shells out
/// to the installed `vpk` global tool and reports its output.
/// </remarks>
public sealed class VelopackPackagingService
{
    private readonly ProcessRunner _runner;

    public VelopackPackagingService(ProcessRunner runner) => _runner = runner;

    public sealed record PackResult(bool Succeeded, string Output, string? ReleasesDirectory);

    /// <summary>The path to the vpk tool, or null when it is not installed.</summary>
    /// <remarks>
    /// Only the .exe name was tried, so this found nothing off Windows, and the catch was for
    /// <see cref="IOException"/> when building a path throws <see cref="ArgumentException"/> - the
    /// case that actually happens on a malformed PATH entry. It also had to be static and reached
    /// for Process directly, so it bypassed the one runner every other external tool goes through
    /// and inherited none of its fixes.
    /// </remarks>
    public static string? FindVpk()
    {
        var executable = OperatingSystem.IsWindows() ? "vpk.exe" : "vpk";

        var toolsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", executable);
        if (File.Exists(toolsPath))
        {
            return toolsPath;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is a reason to keep looking, not to stop.
            }
            catch (IOException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// Packs the already-published app output into Velopack releases (setup + portable +
    /// delta against the last packaged version when present).
    /// </summary>
    public async Task<PackResult> PackAsync(
        string publishDirectory,
        string outputDirectory,
        string packageName,
        string version,
        string iconPath,
        CancellationToken cancellationToken = default)
    {
        var vpk = FindVpk();
        if (vpk is null)
        {
            return new PackResult(false, "vpk not found - install with: dotnet tool install -g vpk", null);
        }

        Directory.CreateDirectory(outputDirectory);

        // ArgumentList, not a quoted string. Quote() wrapped a value in double quotes and stopped
        // there, so a path containing a quote broke the command line and a path with a trailing
        // backslash swallowed the closing quote. ArgumentList has no parsing step to get wrong.
        var arguments = new List<string>
        {
            "pack",
            "--packId", packageName,
            "--packVersion", version,
            "--packDir", publishDirectory,
            "--outDir", outputDirectory,
        };

        if (File.Exists(iconPath))
        {
            arguments.Add("--icon");
            arguments.Add(iconPath);
        }

        var result = await _runner.RunAsync(
            vpk,
            arguments,
            workingDirectory: outputDirectory,
            environmentVariables: null,
            cancellationToken);

        var releasesDir = Path.Combine(outputDirectory, "Releases");
        return new PackResult(
            result.Succeeded,
            result.CombinedOutput.Trim(),
            Directory.Exists(releasesDir) ? releasesDir : outputDirectory);
    }
}
