using DeckForge.CliAdapter.Processes;

namespace DeckForge.CliAdapter.Tools;

/// <summary>
/// Typed wrapper over the <c>macrodeck-plugin</c> CLI.
/// </summary>
/// <remarks>
/// <para>
/// Every command's argument array here was checked against the CLI's own option registration
/// and then executed. Four of them were wrong and failed with exit code 2: <c>sign</c> and
/// <c>verify</c> take a positional package path rather than <c>--artifact</c>, and the
/// <c>icon-pack</c> subcommands take <c>--source</c> rather than <c>--project</c>.
/// </para>
/// <para>
/// Three asymmetries in the tool are worth knowing, because they are all easy to get wrong:
/// <c>build --output</c> is a <em>directory</em>, <c>pack --output</c> and <c>test --output</c>
/// are <em>file paths</em>, and <c>keygen --output</c> is a directory again. <c>--output</c> is
/// only an output <em>format</em> on <c>validate</c>, <c>inspect</c> and <c>verify</c>.
/// </para>
/// <para>
/// The exit codes are declared as constants and are now actually compared, which they never
/// were: the previous type documented six meanings and then collapsed every failure to
/// "non-zero".
/// </para>
/// </remarks>
public sealed class MacroDeckCli
{
    /// <summary>Valid, or conformant.</summary>
    public const int ExitSuccess = 0;

    /// <summary>Validation failed, or a required conformance check failed.</summary>
    public const int ExitSubjectInvalid = 1;

    /// <summary>Bad arguments, an unknown command, or an unknown check id.</summary>
    public const int ExitUsage = 2;

    /// <summary>A file was missing, was not a ZIP, or could not be read.</summary>
    public const int ExitInputUnreadable = 3;

    /// <summary>Ctrl-C.</summary>
    public const int ExitCancelled = 4;

    /// <summary>Something the tool did not anticipate.</summary>
    public const int ExitInternalError = 70;

    /// <summary>The manifest requirement level, which changes what is reported and how severely.</summary>
    public enum ValidationLevel
    {
        /// <summary>Every runtime field and manifest/entrypoint/compatibility shape. The only level the host enforces.</summary>
        Development,

        /// <summary>Adds packaged-content checks; missing publication fields become warnings.</summary>
        Package,

        /// <summary>Missing publication fields become errors.</summary>
        Publication,
    }

    /// <summary>The file name <c>keygen</c> writes the private half to, without the extension.</summary>
    public const string DefaultKeyName = "macrodeck-creator";

    /// <summary>Key names the tool refuses, because they are reserved for the platform's own keys.</summary>
    public static IReadOnlyList<string> ReservedKeyNames { get; } =
        ["root", "macrodeck-root", "registry", "macrodeck-registry"];

    private readonly ProcessRunner _runner;

    public MacroDeckCli(ProcessRunner runner) => _runner = runner;

    /// <summary>Full command used for display, e.g. "macrodeck-plugin build --output artifacts".</summary>
    public static string Describe(string args) => "macrodeck-plugin " + args;

    /// <summary>A human-readable meaning for an exit code.</summary>
    public static string ExplainExitCode(int exitCode) => exitCode switch
    {
        ExitSuccess => "Success.",
        ExitSubjectInvalid => "The subject was rejected: validation failed, or a required conformance check did not pass.",
        ExitUsage => "The command line was not understood.",
        ExitInputUnreadable => "An input file was missing or unreadable.",
        ExitCancelled => "Cancelled.",
        ExitInternalError => "The tool hit an error it did not anticipate.",
        _ => $"Unknown exit code {exitCode}.",
    };

    /// <summary>True when the failure is a usage problem rather than a problem with the subject.</summary>
    public static bool IsUsageError(ProcessResult result) => result.ExitCode == ExitUsage;

    /// <summary>True when the artifact or manifest simply was not there.</summary>
    public static bool IsInputMissing(ProcessResult result) => result.ExitCode == ExitInputUnreadable;

    // ------------------------------------------------------------------ discovery

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _runner.RunAsync("macrodeck-plugin", ["--version"], ct: ct);
            return result.ExitCode is ExitSuccess or ExitUsage;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not on PATH. This is a "no", not a crash.
            return false;
        }
    }

    /// <summary>
    /// The installed CLI version, or null when it is not installed.
    /// </summary>
    /// <remarks>
    /// A cancelled probe reports "not installed" rather than propagating. That is deliberate for
    /// the environment checklist, which has no cancel button, and wrong for anything else -
    /// so cancellation is not swallowed here; the caller decides.
    /// </remarks>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync("macrodeck-plugin", ["--version"], ct: ct);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        if (!result.Succeeded)
        {
            return null;
        }

        // A tool's first line is its version. Anything longer is a banner, not a version.
        var firstLine = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return firstLine is not null && firstLine.Length <= 64 ? firstLine : null;
    }

    // ------------------------------------------------------------------ scaffold

    /// <summary>
    /// Scaffolds a new project. <paramref name="name"/>, <c>--id</c>, <c>--publisher</c> and
    /// <c>--license</c> are passed by name; <c>--platform</c> may repeat.
    /// </summary>
    public Task<ProcessResult> NewAsync(
        string name,
        string pluginId,
        string publisher,
        string outputDirectory,
        IReadOnlyList<string>? platforms = null,
        string? description = null,
        string? repository = null,
        string? homepage = null,
        string license = "MIT",
        bool noRestore = false,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "new", "--name", name, "--id", pluginId, "--publisher", publisher,
            "--license", license, "--output", outputDirectory, "--non-interactive",
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            args.AddRange(["--description", description]);
        }

        if (!string.IsNullOrWhiteSpace(repository))
        {
            args.AddRange(["--repository", repository]);
        }

        if (!string.IsNullOrWhiteSpace(homepage))
        {
            args.AddRange(["--homepage", homepage]);
        }

        foreach (var platform in platforms ?? [])
        {
            args.AddRange(["--platform", platform]);
        }

        if (noRestore)
        {
            args.Add("--no-restore");
        }

        return _runner.RunAsync("macrodeck-plugin", args, workingDirectory, null, ct);
    }

    // ------------------------------------------------------------------ build and pack

    /// <summary>
    /// Builds every declared runtime identifier and packages the result.
    /// </summary>
    /// <param name="projectDir">The plugin project, passed as <c>--source</c>.</param>
    /// <param name="outputDir">A <em>directory</em>. The artifact is named for the plugin and lands inside it.</param>
    /// <param name="rid">Restricts the build to one runtime identifier, which also names the artifact after it.</param>
    /// <param name="force">Overwrites an existing artifact without asking.</param>
    public Task<ProcessResult> BuildAsync(
        string projectDir,
        string outputDir,
        string? rid = null,
        bool force = true,
        CancellationToken ct = default)
    {
        var args = new List<string> { "build", "--source", projectDir, "--output", outputDir };
        if (rid is not null)
        {
            args.AddRange(["--rid", rid]);
        }

        if (force)
        {
            args.Add("--force");
        }

        return _runner.RunAsync("macrodeck-plugin", args, projectDir, null, ct);
    }

    /// <summary>Packages a staged payload tree. <paramref name="outputArtifact"/> is a <em>file path</em>.</summary>
    public Task<ProcessResult> PackAsync(string payloadDir, string outputArtifact, bool force = true, bool showDigest = false, CancellationToken ct = default)
    {
        var args = new List<string> { "pack", "--source", payloadDir, "--output", outputArtifact };
        if (force)
        {
            args.Add("--force");
        }

        if (showDigest)
        {
            args.Add("--show-digest");
        }

        return _runner.RunAsync("macrodeck-plugin", args, payloadDir, null, ct);
    }

    // ------------------------------------------------------------------ validate and inspect

    /// <summary>Validates a loose manifest file.</summary>
    public Task<ProcessResult> ValidateManifestAsync(string manifestPath, ValidationLevel level = ValidationLevel.Development, CancellationToken ct = default) =>
        _runner.RunAsync(
            "macrodeck-plugin",
            ["validate", "--manifest", manifestPath, "--level", Level(level), "--output", "json"],
            ct: ct);

    /// <summary>Validates a version directory.</summary>
    public Task<ProcessResult> ValidateDirectoryAsync(string directory, ValidationLevel level = ValidationLevel.Development, CancellationToken ct = default) =>
        _runner.RunAsync(
            "macrodeck-plugin",
            ["validate", "--directory", directory, "--level", Level(level), "--output", "json"],
            ct: ct);

    /// <summary>
    /// Validates a packaged artifact. The level is not passed: the tool implies
    /// <see cref="ValidationLevel.Package"/> for an artifact, which is what the Store path wants.
    /// </summary>
    public Task<ProcessResult> ValidateArtifactAsync(string artifact, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["validate", "--artifact", artifact, "--output", "json"], ct: ct);

    /// <summary>Reports what installing an artifact or version directory would find.</summary>
    public Task<ProcessResult> InspectAsync(string artifact, string format = "text", bool showDigest = false, CancellationToken ct = default)
    {
        var args = new List<string> { "inspect", "--artifact", artifact, "--output", format };
        if (showDigest)
        {
            args.Add("--show-digest");
        }

        return _runner.RunAsync("macrodeck-plugin", args, ct: ct);
    }

    /// <summary>Inspects a version directory rather than an artifact.</summary>
    public Task<ProcessResult> InspectDirectoryAsync(string directory, string format = "text", CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["inspect", "--directory", directory, "--output", format], ct: ct);

    // ------------------------------------------------------------------ run and test

    /// <summary>Runs the plugin against a disposable stub host. No Macro Deck install needed.</summary>
    public Task<ProcessResult> RunStubHostAsync(string projectDir, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["run", "--project", projectDir, "--stub-host"], projectDir, null, ct);

    /// <summary>Runs the plugin against the desktop host; a pairing prompt appears in Macro Deck.</summary>
    public Task<ProcessResult> RunRealHostAsync(string projectDir, CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["run", "--project", projectDir], projectDir, null, ct);

    /// <summary>
    /// Runs the conformance suite. <paramref name="reportPath"/> is a <em>file path</em>.
    /// </summary>
    /// <remarks>
    /// Exactly one subject selector is needed. <paramref name="projectDir"/> builds and tests a
    /// project; <paramref name="artifact"/> tests an already-packed one, which is what the Ship
    /// page wants after packaging. <c>--report</c> is a format here - unlike <c>build</c>, whose
    /// <c>--output</c> is a directory and <c>pack</c>'s a file.
    /// </remarks>
    public Task<ProcessResult> TestAsync(
        string projectDir,
        string? reportPath = null,
        string report = "text",
        bool requiredOnly = false,
        string? artifact = null,
        string? executable = null,
        IReadOnlyList<string>? categories = null,
        IReadOnlyList<string>? checks = null,
        int? timeoutSeconds = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "test" };

        var selectors = new[] { projectDir, artifact, executable }.Count(s => !string.IsNullOrWhiteSpace(s));
        if (selectors > 1)
        {
            throw new ArgumentException(
                "Give at most one of a project, an artifact or an executable to test.");
        }

        if (!string.IsNullOrWhiteSpace(artifact))
        {
            args.AddRange(["--artifact", artifact]);
        }
        else if (!string.IsNullOrWhiteSpace(executable))
        {
            args.AddRange(["--executable", executable]);
        }
        else
        {
            args.AddRange(["--project", projectDir]);
        }

        args.AddRange(["--report", report]);
        if (reportPath is not null)
        {
            args.AddRange(["--output", reportPath]);
        }

        if (requiredOnly)
        {
            args.Add("--required-only");
        }

        foreach (var category in categories ?? [])
        {
            args.AddRange(["--category", category]);
        }

        foreach (var check in checks ?? [])
        {
            args.AddRange(["--check", check]);
        }

        if (timeoutSeconds is { } seconds)
        {
            args.AddRange(["--timeout", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        return _runner.RunAsync("macrodeck-plugin", args, projectDir, null, ct);
    }

    /// <summary>Lists the conformance checks without running them.</summary>
    public Task<ProcessResult> ListConformanceChecksAsync(CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["test", "--list-checks"], ct: ct);

    // ------------------------------------------------------------------ signing

    /// <summary>
    /// Generates a creator key pair in <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="outputDirectory">A <em>directory</em>.</param>
    /// <param name="keyName">
    /// Base name for the two files. Reserved names are refused by the tool. Note that a key
    /// pair is not a certificate: the Creator Portal issues those, so signing still needs one.
    /// </param>
    public Task<ProcessResult> KeygenAsync(
        string outputDirectory,
        string keyName = DefaultKeyName,
        CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["keygen", "--output", outputDirectory, "--key-name", keyName], ct: ct);

    /// <summary>The file <c>keygen</c> writes for <paramref name="keyName"/>.</summary>
    public static string KeyPublicPath(string directory, string keyName = DefaultKeyName) =>
        Path.Combine(directory, keyName + ".public");

    /// <summary>The file <c>keygen</c> writes for <paramref name="keyName"/>.</summary>
    public static string KeyPrivatePath(string directory, string keyName = DefaultKeyName) =>
        Path.Combine(directory, keyName + ".private");

    /// <summary>
    /// Signs a package.
    /// </summary>
    /// <remarks>
    /// The package is a <em>positional</em> argument and four options are all required. The
    /// previous form passed <c>--artifact</c> and <c>--key</c>, which the tool does not accept:
    /// both come back as "unrecognized argument" and the missing required options follow.
    /// </remarks>
    public Task<ProcessResult> SignAsync(
        string packagePath,
        string outputPath,
        string certificatePath,
        string certificateSignaturePath,
        string privateKeyPath,
        string? issuerCertificatePath = null,
        string? issuerCertificateSignaturePath = null,
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "sign", packagePath,
            "--output", outputPath,
            "--certificate", certificatePath,
            "--certificate-signature", certificateSignaturePath,
            "--private-key", privateKeyPath,
        };

        if (issuerCertificatePath is not null || issuerCertificateSignaturePath is not null)
        {
            if (issuerCertificatePath is null || issuerCertificateSignaturePath is null)
            {
                throw new ArgumentException(
                    "An issuer certificate and its signature must be given together.");
            }

            args.AddRange(["--issuer-certificate", issuerCertificatePath,
                "--issuer-certificate-signature", issuerCertificateSignaturePath]);
        }

        return _runner.RunAsync("macrodeck-plugin", args, ct: ct);
    }

    /// <summary>
    /// Verifies a package signature. The package is a <em>positional</em> argument.
    /// </summary>
    public Task<ProcessResult> VerifyAsync(
        string packagePath,
        string format = "text",
        string? rootPublicKeyPath = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "verify", packagePath, "--output", format };
        if (rootPublicKeyPath is not null)
        {
            args.AddRange(["--root-public", rootPublicKeyPath]);
        }

        return _runner.RunAsync("macrodeck-plugin", args, ct: ct);
    }

    // ------------------------------------------------------------------ icon packs

    /// <summary>Lists the icon packs bundled by a plugin project.</summary>
    public Task<ProcessResult> IconPackListAsync(string projectDir, string format = "text", CancellationToken ct = default) =>
        _runner.RunAsync("macrodeck-plugin", ["icon-pack", "list", "--source", projectDir, "--output", format], projectDir, null, ct);

    /// <summary>
    /// Adds a <c>.macroDeckIconPack</c> to a plugin project. The pack path is <em>positional</em>
    /// and the project is <c>--source</c>, not <c>--project</c>.
    /// </summary>
    public Task<ProcessResult> IconPackAddAsync(
        string packPath,
        string projectDir,
        string? key = null,
        bool copy = false,
        bool force = false,
        CancellationToken ct = default)
    {
        var args = new List<string> { "icon-pack", "add", packPath, "--source", projectDir };
        if (key is not null)
        {
            args.AddRange(["--key", key]);
        }

        if (copy)
        {
            args.Add("--copy");
        }

        if (force)
        {
            args.Add("--force");
        }

        return _runner.RunAsync("macrodeck-plugin", args, projectDir, null, ct);
    }

    // ------------------------------------------------------------------ merge

    /// <summary>Merges per-RID packages into one. <paramref name="outputDir"/> is a directory.</summary>
    public Task<ProcessResult> MergeAsync(
        IReadOnlyList<string> artifacts,
        string outputDir,
        bool force = false,
        CancellationToken ct = default)
    {
        var args = new List<string> { "merge" };
        args.AddRange(artifacts);
        args.AddRange(["--output", outputDir]);
        if (force)
        {
            args.Add("--force");
        }

        return _runner.RunAsync("macrodeck-plugin", args, outputDir, null, ct);
    }

    private static string Level(ValidationLevel level) => level switch
    {
        ValidationLevel.Package => "package",
        ValidationLevel.Publication => "publication",
        _ => "development",
    };
}
