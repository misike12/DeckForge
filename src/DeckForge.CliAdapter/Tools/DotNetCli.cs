using DeckForge.CliAdapter.Processes;

namespace DeckForge.CliAdapter.Tools;

/// <summary>Typed wrapper over the dotnet CLI for the plugin's own build/test lifecycle.</summary>
public sealed class DotNetCli
{
    private readonly ProcessRunner _runner;

    public DotNetCli(ProcessRunner runner) => _runner = runner;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _runner.RunAsync("dotnet", ["--version"], ct: ct);
            return result.Succeeded;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _runner.RunAsync("dotnet", ["--version"], ct: ct);
            return result.Succeeded ? result.StandardOutput.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public Task<ProcessResult> RestoreAsync(string projectOrSolution, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["restore", projectOrSolution], ct: ct);

    public Task<ProcessResult> BuildAsync(string projectOrSolution, string configuration = "Debug", CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["build", projectOrSolution, "-c", configuration, "--nologo"], ct: ct);

    public Task<ProcessResult> PublishAsync(string projectOrSolution, string configuration, string runtime, string outputDirectory, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["publish", projectOrSolution, "-c", configuration, "-r", runtime, "-o", outputDirectory, "--nologo"], ct: ct);

    public Task<ProcessResult> TestAsync(string projectOrSolution, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["test", projectOrSolution, "--nologo"], ct: ct);

    public Task<ProcessResult> ListPackagesAsync(string project, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["list", project, "package", "--include-transitive"], ct: ct);

    /// <summary>Installs or updates the macrodeck-plugin global tool at a specific version.</summary>
    public Task<ProcessResult> InstallMacroDeckCliAsync(string version, CancellationToken ct = default) =>
        _runner.RunAsync("dotnet", ["tool", "install", "--global", "MacroDeck.Plugin.Cli", "--version", version, "--allow-prerelease"], ct: ct);
}
