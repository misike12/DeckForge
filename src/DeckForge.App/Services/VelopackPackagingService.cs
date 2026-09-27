using System.Diagnostics;
using System.IO;

namespace DeckForge.App.Services;

/// <summary>
/// Runs the Velopack CLI (vpk) to produce the DeckForge installer, portable build and
/// delta packages. Velopack is authoritative for packaging; this service only shells out
/// to the installed `vpk` global tool and reports its output.
/// </summary>
public sealed class VelopackPackagingService
{
    public sealed record PackResult(bool Succeeded, string Output, string? ReleasesDirectory);

    /// <summary>True when the vpk tool is available on PATH or in the dotnet tools dir.</summary>
    public static string? FindVpk()
    {
        var toolsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "vpk.exe");
        if (File.Exists(toolsPath))
        {
            return toolsPath;
        }
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "vpk.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
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
        var args = $"pack --packId {Quote(packageName)} --packVersion {Quote(version)} --packDir {Quote(publishDirectory)} --outDir {Quote(outputDirectory)}";
        if (File.Exists(iconPath))
        {
            args += $" --icon {Quote(iconPath)}";
        }

        var psi = new ProcessStartInfo(vpk, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask) + (await errorTask);

        var releasesDir = Path.Combine(outputDirectory, "Releases");
        return new PackResult(process.ExitCode == 0, output.Trim(), Directory.Exists(releasesDir) ? releasesDir : outputDirectory);
    }

    private static string Quote(string value) => $"\"{value}\"";
}
