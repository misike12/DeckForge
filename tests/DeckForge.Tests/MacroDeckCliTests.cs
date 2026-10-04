using System.Diagnostics;
using DeckForge.CliAdapter.Processes;
using DeckForge.CliAdapter.Tools;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Checks the <c>macrodeck-plugin</c> invocations against the real tool.
/// </summary>
/// <remarks>
/// <para>
/// Four of the thirteen commands DeckForge shells out to were wrong in a way that produced exit
/// code 2 every time: <c>sign</c> and <c>verify</c> take a positional package rather than
/// <c>--artifact</c>, and both <c>icon-pack</c> subcommands take <c>--source</c> rather than
/// <c>--project</c>. Nothing caught it, because a usage error is just a non-zero exit code and
/// the callers only tested for zero.
/// </para>
/// <para>
/// These tests run the real tool. When it is not installed they skip rather than fail, so a
/// machine without the CLI still gets a green run - but a machine with it will catch a
/// regression in the next preview release, which is exactly when the flags would drift.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class MacroDeckCliTests
{
    private static bool CliPresent { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            var psi = new ProcessStartInfo("macrodeck-plugin")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(30_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    private static MacroDeckCli Cli() =>
        new(new ProcessRunner());

    private static bool Skip => !CliPresent;

    [SetUp]
    public void RequireCli()
    {
        if (Skip)
        {
            Assert.Ignore("macrodeck-plugin is not on PATH.");
        }
    }

    // ---------------------------------------------------------------- exit codes

    [Test]
    public void Exit_code_meanings_match_the_tool()
    {
        // The constants are the tool's own, transcribed from its ExitCode class.
        Assert.Multiple(() =>
        {
            Assert.That(MacroDeckCli.ExitSuccess, Is.EqualTo(0));
            Assert.That(MacroDeckCli.ExitSubjectInvalid, Is.EqualTo(1));
            Assert.That(MacroDeckCli.ExitUsage, Is.EqualTo(2));
            Assert.That(MacroDeckCli.ExitInputUnreadable, Is.EqualTo(3));
            Assert.That(MacroDeckCli.ExitCancelled, Is.EqualTo(4));
            Assert.That(MacroDeckCli.ExitInternalError, Is.EqualTo(70));
        });
    }

    [Test]
    public void An_unknown_command_is_reported_as_a_usage_error()
    {
        var runner = new ProcessRunner();
        var result = runner.RunAsync("macrodeck-plugin", ["definitely-not-a-command"]).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(MacroDeckCli.ExitUsage));
            Assert.That(MacroDeckCli.IsUsageError(result), Is.True);
            Assert.That(MacroDeckCli.ExplainExitCode(result.ExitCode), Does.Contain("not understood"));
        });
    }

    [Test]
    public void A_missing_input_is_reported_as_unreadable()
    {
        var runner = new ProcessRunner();
        var result = runner.RunAsync(
            "macrodeck-plugin",
            ["validate", "--artifact", Path.Combine(Path.GetTempPath(), "no-such-artifact.macroDeckPlugin")])
            .GetAwaiter().GetResult();

        Assert.That(result.ExitCode, Is.EqualTo(MacroDeckCli.ExitInputUnreadable));
        Assert.That(MacroDeckCli.IsInputMissing(result), Is.True);
    }

    // ---------------------------------------------------------------- flag forms

    [Test]
    public void Verify_takes_a_positional_package()
    {
        // The old form was `verify --artifact <p>`, which the tool rejects outright.
        var missing = Path.Combine(Path.GetTempPath(), "no-such-package.macroDeckPlugin");
        var result = Cli().VerifyAsync(missing).GetAwaiter().GetResult();

        // A missing file is exit 3, not the exit 2 an unrecognized argument produces.
        Assert.That(result.ExitCode, Is.Not.EqualTo(MacroDeckCli.ExitUsage), result.CombinedOutput);
        Assert.That(result.ExitCode, Is.EqualTo(MacroDeckCli.ExitInputUnreadable));
    }

    [Test]
    public void Sign_reports_missing_required_options_rather_than_unrecognized_arguments()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-package.macroDeckPlugin");
        var result = Cli().SignAsync(
            missing,
            missing + ".signed",
            "certificate.json",
            "certificate.sig",
            "key.private").GetAwaiter().GetResult();

        // Certificate/key are unreadable, so the tool got past argument parsing. An
        // unrecognized argument would have failed earlier with exit 2.
        Assert.That(result.CombinedOutput, Does.Not.Contain("Unrecognized command or argument"), result.CombinedOutput);
    }

    [Test]
    public void Icon_pack_list_takes_source()
    {
        var result = Cli().IconPackListAsync(Path.GetTempPath()).GetAwaiter().GetResult();

        Assert.That(result.CombinedOutput, Does.Not.Contain("Unrecognized command or argument"), result.CombinedOutput);
    }

    [Test]
    public void Icon_pack_add_takes_a_positional_path_and_source()
    {
        var pack = Path.Combine(Path.GetTempPath(), "no-such-pack.macroDeckIconPack");
        var result = Cli().IconPackAddAsync(pack, Path.GetTempPath()).GetAwaiter().GetResult();

        Assert.That(result.CombinedOutput, Does.Not.Contain("Unrecognized command or argument"), result.CombinedOutput);
    }

    [Test]
    public void Validate_accepts_every_level_the_tool_offers()
    {
        foreach (var level in new[]
                 {
                     MacroDeckCli.ValidationLevel.Development,
                     MacroDeckCli.ValidationLevel.Package,
                     MacroDeckCli.ValidationLevel.Publication,
                 })
        {
            var result = Cli().ValidateManifestAsync(
                Path.Combine(Path.GetTempPath(), "no-such-manifest.json"), level).GetAwaiter().GetResult();

            Assert.That(result.CombinedOutput, Does.Not.Contain("not a recognized --level"),
                $"{level}: {result.CombinedOutput}");
        }
    }

    [Test]
    public void Keygen_writes_the_filenames_the_adapter_expects()
    {
        using var temp = new TempDirectory("deckforge-keygen");
        var dir = temp.Root;

        var result = Cli().KeygenAsync(dir, "probe-key").GetAwaiter().GetResult();
        Assert.That(result.Succeeded, Is.True, result.CombinedOutput);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(MacroDeckCli.KeyPublicPath(dir, "probe-key")), Is.True);
            Assert.That(File.Exists(MacroDeckCli.KeyPrivatePath(dir, "probe-key")), Is.True);
        });

        // And the old ".key"/".pem" glob the Ship page used would have found neither.
        Assert.That(
            Directory.GetFiles(dir).Any(f => f.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
                                          || f.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)),
            Is.False);
    }

    [Test]
    public void A_reserved_key_name_is_refused()
    {
        using var temp = new TempDirectory("deckforge-keygen");
        var result = Cli().KeygenAsync(temp.Root, "macrodeck-root").GetAwaiter().GetResult();

        Assert.That(result.ExitCode, Is.EqualTo(MacroDeckCli.ExitUsage), result.CombinedOutput);
    }

    [Test]
    public void Testing_more_than_one_subject_is_refused_before_the_tool_runs()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await Cli().TestAsync("project", artifact: "artifact.macroDeckPlugin"));
    }

    [Test]
    public void The_installed_version_is_readable()
    {
        var version = Cli().GetVersionAsync().GetAwaiter().GetResult();

        Assert.That(version, Is.Not.Null, "macrodeck-plugin --version produced nothing.");
        Assert.That(version, Does.StartWith(MacroDeckCliVersionStartsWith), version);
        Assert.That(version!.Length, Is.LessThanOrEqualTo(64));
    }

    private const string MacroDeckCliVersionStartsWith = "3.0.0";
}
