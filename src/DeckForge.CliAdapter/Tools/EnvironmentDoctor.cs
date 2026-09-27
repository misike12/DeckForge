using DeckForge.CliAdapter.Processes;
using DeckForge.CliAdapter.Tools;

namespace DeckForge.CliAdapter;

public sealed record DoctorCheck(string Id, string Title, bool Ok, string? Detail, string? FixHint);

/// <summary>
/// Probes the machine for everything plugin development needs and produces the
/// Environment page's checklist: .NET SDK, ASP.NET Core shared framework, the
/// macrodeck-plugin CLI, and a Macro Deck install.
/// </summary>
public sealed class EnvironmentDoctor(DotNetCli dotnet, MacroDeckCli cli)
{
    public async Task<IReadOnlyList<DoctorCheck>> RunAllAsync(CancellationToken ct = default)
    {
        var checks = new List<DoctorCheck>();
        var dotnetVersion = await dotnet.GetVersionAsync(ct);
        checks.Add(new DoctorCheck(
            "dotnet",
            ".NET SDK",
            Version.TryParse(dotnetVersion, out var v) && v.Major >= 10,
            dotnetVersion is null ? "dotnet was not found on PATH" : $"dotnet {dotnetVersion}",
            dotnetVersion is null || (Version.TryParse(dotnetVersion, out var v2) && v2.Major < 10)
                ? "Install the .NET 10 SDK from https://dotnet.microsoft.com/download"
                : null));

        checks.Add(new DoctorCheck(
            "aspnet-framework",
            "ASP.NET Core shared framework",
            HasAspNetCoreFramework(),
            HasAspNetCoreFramework() ? "Microsoft.AspNetCore.App 10.x present" : "Microsoft.AspNetCore.App 10.x not found",
            "Install the .NET 10 SDK (includes the ASP.NET Core shared framework) - the run and test commands need it"));

        var cliVersion = await cli.GetVersionAsync(ct);
        var cliOk = cliVersion is not null;
        checks.Add(new DoctorCheck(
            "macrodeck-cli",
            "macrodeck-plugin CLI",
            cliOk,
            cliOk ? cliVersion : "not installed",
            cliOk ? null : "DeckForge can install it: dotnet tool install --global MacroDeck.Plugin.Cli --version " + Core.Plugins.MacroDeckSdkInfo.DefaultCliVersion));

        var macroDeck = FindMacroDeckInstall();
        checks.Add(new DoctorCheck(
            "macrodeck-host",
            "Macro Deck desktop app",
            macroDeck is not null,
            macroDeck ?? "not detected (the stub host works without it)",
            macroDeck is null ? "Optional: install Macro Deck from https://macro-deck.app/ to test against the real host" : null));

        return checks;
    }

    private static bool HasAspNetCoreFramework()
    {
        var root = GetDotnetRoot();
        if (root is null)
        {
            return false;
        }
        var dir = Path.Combine(root, "shared", "Microsoft.AspNetCore.App");
        return Directory.Exists(dir)
            && Directory.EnumerateDirectories(dir, "10.*").Any();
    }

    private static string? GetDotnetRoot()
    {
        var exe = FindOnPath("dotnet.exe") ?? FindOnPath("dotnet");
        if (exe is null)
        {
            return null;
        }
        return Path.GetDirectoryName(Path.GetFullPath(exe));
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
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
                // invalid path entry
            }
        }
        return null;
    }

    private static string? FindMacroDeckInstall()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroDeck", "MacroDeck.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Macro Deck", "MacroDeck.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Macro Deck", "MacroDeck.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
