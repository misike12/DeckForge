using DeckForge.CliAdapter.Processes;

namespace DeckForge.CliAdapter.Tools;

/// <summary>
/// Typed wrapper over the `macrodeck-plugin` CLI. Exit-code semantics follow the CLI docs:
/// 0 success/conformant, 1 subject wrong, 2 usage, 3 input unreadable, 4 cancelled, 70 unexpected.
/// </summary>
public sealed class MacroDeckCli
{
    public const int ExitSubjectInvalid = 1;
    public const int ExitUsage = 2;
    public const int ExitInputUnreadable = 3;

    private readonly ProcessRunner _runner;

    public MacroDeckCli(ProcessRunner runner) => _runner = runner;

    /// <summary>Full command used for display, e.g. "macrodeck-plugin build --output artifacts".</summary>
    public static string Describe(string args) => "macrodeck-plugin " + args;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _runner.RunAsync("macrodeck-plugin", ["--help"], ct: ct);
            return result.Succeeded || result.ExitCode == ExitUsage;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _runner.RunAsync("macrodeck-plugin", ["--version"], ct: ct);
            var line = result.StandardOutput.Trim().Split('\n').FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public Task<ProcessResult> NewAsync(string args, string workingDirectory, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", Tokenize(args), workingDirectory, null, ct);

    public Task<ProcessResult> BuildAsync(string projectDir, string outputDir, string? rid = null, CancellationToken ct = default)
    {
        var args = new List<string> { "build", "--source", projectDir, "--output", outputDir, "--force" };
        if (rid is not null)
        {
            args.AddRange(["--rid", rid]);
        }
        return _runner.RunAsync("macrodeck-plugin", args, projectDir, null, ct);
    }

    public Task<ProcessResult> ValidateManifestAsync(string manifestPath, string level = "Development", CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["validate", "--manifest", manifestPath, "--level", level, "--output", "Json"], ct: ct);

    public Task<ProcessResult> ValidateDirectoryAsync(string directory, string level = "Development", CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["validate", "--directory", directory, "--level", level, "--output", "Json"], ct: ct);

    public Task<ProcessResult> ValidateArtifactAsync(string artifact, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["validate", "--artifact", artifact, "--output", "Json"], ct: ct);

    public Task<ProcessResult> InspectAsync(string artifact, string format = "text", CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["inspect", "--artifact", artifact, "--output", format], ct: ct);

    /// <summary>Runs the plugin against a disposable stub host (no Macro Deck install needed).</summary>
    public Task<ProcessResult> RunStubHostAsync(string projectDir, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["run", "--project", projectDir, "--stub-host"], projectDir, null, ct);

    /// <summary>Runs the plugin against the desktop host (pairing prompt appears in Macro Deck).</summary>
    public Task<ProcessResult> RunRealHostAsync(string projectDir, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["run", "--project", projectDir], projectDir, null, ct);

    /// <summary>Runs the conformance suite; writes the report (Text/Json/Markdown) to reportPath when given.</summary>
    public Task<ProcessResult> TestAsync(string projectDir, string? reportPath = null, string report = "Text", bool requiredOnly = false, CancellationToken ct = default)
    {
        var args = new List<string> { "test", "--project", projectDir, "--report", report };
        if (reportPath is not null)
        {
            args.AddRange(["--output", reportPath]);
        }
        if (requiredOnly)
        {
            args.Add("--required-only");
        }
        return _runner.RunAsync("macrodeck-plugin", args, projectDir, null, ct);
    }

    public Task<ProcessResult> ListConformanceChecksAsync(CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["test", "--list-checks"], ct: ct);

    public Task<ProcessResult> PackAsync(string payloadDir, string outputArtifact, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["pack", "--source", payloadDir, "--output", outputArtifact, "--force"], payloadDir, null, ct);

    public Task<ProcessResult> KeygenAsync(string outputDirectory, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["keygen", "--output", outputDirectory], ct: ct);

    public Task<ProcessResult> SignAsync(string artifact, string keyPath, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["sign", "--artifact", artifact, "--key", keyPath], ct: ct);

    public Task<ProcessResult> VerifyAsync(string artifact, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["verify", "--artifact", artifact], ct: ct);

    public Task<ProcessResult> IconPackListAsync(string projectDir, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["icon-pack", "list", "--project", projectDir], projectDir, null, ct);

    public Task<ProcessResult> IconPackAddAsync(string projectDir, string packPath, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["icon-pack", "add", "--project", projectDir, "--pack", packPath], projectDir, null, ct);

    internal static IReadOnlyList<string> Tokenize(string args) => args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
