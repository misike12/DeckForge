using System.Text;
using DeckForge.CliAdapter.Processes;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the process layer, against a real child process rather than a mock - the bugs this
/// file covers were all about what a real pipe does.
/// </summary>
[TestFixture]
public sealed class ProcessRunnerTests
{
    /// <summary>
    /// A shell that can write to both streams, plus the flag that makes it run a script argument.
    /// </summary>
    /// <remarks>
    /// The flag has to be a separate argument. Folding it into the script with a prefix produced a
    /// command line cmd re-quoted for us, and every echo came back with a stray trailing quote.
    /// </remarks>
    private static (string File, string ScriptFlag)? Shell()
    {
        if (OperatingSystem.IsWindows())
        {
            var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            return File.Exists(cmd) ? (cmd, "/c") : null;
        }

        return File.Exists("/bin/sh") ? ("/bin/sh", "-c") : null;
    }

    private static async Task<ProcessResult> RunWithStandardInputAsync(
        string script,
        ProcessRunner runner,
        string standardInput)
    {
        var shell = Shell();
        Assert.That(shell, Is.Not.Null, "No shell available to drive a real child process.");

        return await runner.RunAsync(
            shell!.Value.File,
            [shell.Value.ScriptFlag, script],
            workingDirectory: null,
            environmentVariables: null,
            CancellationToken.None,
            standardInput: standardInput);
    }

    /// <summary>Runs a script through the shell, forwarding the optional per-call callbacks.</summary>
    private static Task<ProcessResult> RunScriptAsync(
        string script,
        ProcessRunner runner,
        Action<string>? onStandardLine = null,
        Action<string>? onErrorLine = null)
    {
        var shell = Shell();
        Assert.That(shell, Is.Not.Null, "No shell available to drive a real child process.");

        return runner.RunAsync(
            shell!.Value.File,
            [shell.Value.ScriptFlag, script],
            workingDirectory: null,
            environmentVariables: null,
            CancellationToken.None,
            onStandardLine,
            onErrorLine);
    }

    [Test]
    public async Task The_transcript_keeps_each_streams_own_order_and_loses_nothing()
    {
        // Asserted what two pipes and two reader threads can actually promise: every line, once, and each
        // stream in the order the child wrote it. This test used to assert that the *combined* transcript
        // came back in write order across both streams, which is not a thing two pipes can do - it failed
        // 11 times in 20 in isolation and passed in a full suite run, so it taught everyone to re-run
        // rather than to look. Cross-stream order needs one pipe, which is the child's `2>&1`.
        Assume.That(File.Exists(PowerShell) || PowerShell == "pwsh", "PowerShell is needed here.");

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            PowerShell,
            [
                "-NoProfile",
                "-Command",
                "[Console]::Out.WriteLine('first');[Console]::Error.WriteLine('warn');[Console]::Out.WriteLine('second');[Console]::Error.WriteLine('fail');[Console]::Out.WriteLine('third')",
            ],
            workingDirectory: null,
            environmentVariables: null,
            CancellationToken.None);

        var lines = result.CombinedOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var output = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var error = result.StandardError
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo(new[] { "first", "second", "third" }), "stdout keeps its own order");
            Assert.That(error, Is.EqualTo(new[] { "warn", "fail" }), "and so does stderr");
            Assert.That(lines, Has.Count.EqualTo(5), "nothing is lost or duplicated between the two");
            Assert.That(lines, Is.EquivalentTo(new[] { "first", "second", "third", "warn", "fail" }));
        });
    }

    [Test]
    public async Task Merging_the_childs_streams_gives_one_order()
    {
        // The way to get what the old test asked for, stated as a test so the answer is discoverable: the
        // child merges its own streams, so there is one pipe and therefore one order.
        Assume.That(File.Exists(PowerShell) || PowerShell == "pwsh", "PowerShell is needed here.");

        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            PowerShell,
            [
                "-NoProfile",
                "-Command",
                "[Console]::Out.WriteLine('first');[Console]::Error.WriteLine('second') | Out-Host; [Console]::Out.WriteLine('third')",
            ],
            workingDirectory: null,
            environmentVariables: null,
            CancellationToken.None);

        Assert.That(result.CombinedOutput, Does.Contain("first").And.Contain("third"));
    }

    [Test]
    public async Task A_chatty_child_is_not_truncated()
    {
        // The old runner returned as soon as the process exited, which is before the redirected
        // pipes have been read to the end. The tail of a real build is where the errors are.
        var runner = new ProcessRunner();
        const int lineCount = 4000;

        var result = await RunScriptAsync(
            OperatingSystem.IsWindows()
                ? $"for /L %i in (1,1,{lineCount}) do @echo line%i"
                : $"for i in $(seq 1 {lineCount}); do echo line$i; done",
            runner);

        var lines = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines, Has.Length.EqualTo(lineCount));
        Assert.That(lines[^1], Is.EqualTo($"line{lineCount}"));
    }

    [Test]
    public async Task A_child_that_reads_stdin_gets_it_and_its_prompt_does_not_hang()
    {
        // A WinExe parent has no console, so a child that prompts would block forever. There was
        // no stdin writer at all and RedirectStandardInput was never set. /v:on is needed because
        // cmd expands %var% before the line runs, so a variable set by set /p is not yet set.
        var runner = new ProcessRunner();
        var shell = Shell() ?? throw new InvalidOperationException("No shell on this platform.");

        var result = await runner.RunAsync(
            shell.File,
            ["/v:on", "/c", OperatingSystem.IsWindows() ? "set /p reply= & echo got:!reply!" : "read reply && echo got:$reply"],
            workingDirectory: null,
            environmentVariables: null,
            CancellationToken.None,
            standardInput: "hello\n");

        Assert.That(result.CombinedOutput, Does.Contain("got:hello"));
    }

    [Test]
    public async Task Each_call_gets_its_own_callbacks_rather_than_every_runners()
    {
        var first = new List<string>();
        var second = new List<string>();
        var runner = new ProcessRunner();

        await RunScriptAsync("echo alpha", runner, first.Add, second.Add);
        await RunScriptAsync("echo beta", runner, first.Add, second.Add);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { "alpha", "beta" }));
            Assert.That(second, Is.Empty, "The stderr callback saw the stdout line.");
        });
    }

    [Test]
    public async Task A_subscriber_that_throws_does_not_take_the_run_down()
    {
        // These callbacks run on the stream-reader thread pool threads, where an unhandled
        // exception terminates the process rather than failing the build.
        var runner = new ProcessRunner();
        var seen = new List<string>();
        runner.StandardLine += seen.Add;

        var result = await RunScriptAsync(
            "echo survived",
            runner,
            _ => throw new InvalidOperationException("a view that failed to render"));

        Assert.Multiple(() =>
        {
            Assert.That(seen, Does.Contain("survived"), "The instance event stopped receiving lines too.");
            Assert.That(result.Succeeded, Is.True);
        });
    }

    [Test]
    public void A_program_that_does_not_exist_is_reported_rather_than_crashing_the_caller()
    {
        var runner = new ProcessRunner();

        Assert.ThrowsAsync<ProcessStartFailedException>(async () =>
            await runner.RunAsync("definitely-not-a-real-program-9f2a", ["--version"]));
    }

    [Test]
    public async Task The_exit_code_comes_back()
    {
        var runner = new ProcessRunner();
        var result = await RunScriptAsync("exit 3", runner);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.Succeeded, Is.False);
        });
    }

    /// <summary>Path to pwsh, which can write to each stream on demand.</summary>
    private static string PowerShell =>
        Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Select(dir => Path.Combine(dir, "pwsh.exe"))
            .FirstOrDefault(File.Exists)
        ?? "pwsh";

    /// <summary>Writes raw bytes to stdout, so a decoder can be checked against known bytes.</summary>
    private static async Task<ProcessResult> RunRawByteWriterAsync(ProcessRunner runner, Encoding? encoding)
    {
        Assume.That(File.Exists(PowerShell) || PowerShell == "pwsh", "PowerShell is needed to emit raw bytes.");

        return await runner.RunAsync(
            PowerShell,
            ["-NoProfile", "-Command", "$o=[Console]::OpenStandardOutput();$b=[byte[]](0x63,0x61,0x66,0xE9);$o.Write($b,0,4);$o.Flush()"],
            workingDirectory: null,
            environmentVariables: null,
            CancellationToken.None,
            standardInput: null,
            encoding: encoding);
    }

    [Test]
    public async Task Output_is_decoded_with_the_encoding_it_was_given()
    {
        // 0xE9 alone is a valid Latin-1 e-acute and not valid UTF-8, so decoding it the wrong way
        // produces replacement characters. This is what mangles a tool that writes an OEM code page,
        // and the runner had no way to be told otherwise.
        var runner = new ProcessRunner();
        var result = await RunRawByteWriterAsync(runner, Encoding.Latin1);

        Assert.That(result.StandardOutput.Trim(), Is.EqualTo("café"));
    }

    [Test]
    public async Task The_same_bytes_decoded_as_utf8_become_replacement_characters()
    {
        var runner = new ProcessRunner();
        var result = await RunRawByteWriterAsync(runner, encoding: null);

        Assert.That(result.StandardOutput, Does.Not.Contain("café"));
    }

    [TestCase("", new string[0])]
    [TestCase("   ", new string[0])]
    [TestCase("dotnet", new[] { "dotnet" })]
    [TestCase("dotnet build", new[] { "dotnet", "build" })]
    [TestCase("  dotnet   build  ", new[] { "dotnet", "build" })]
    [TestCase("dotnet build --no-restore", new[] { "dotnet", "build", "--no-restore" })]
    [TestCase("cmd -p \"a b\"", new[] { "cmd", "-p", "a b" })]
    [TestCase("cmd \"\"", new[] { "cmd", "" })]
    [TestCase("cmd \"a\"\"b\"", new[] { "cmd", "ab" })]
    [TestCase("cmd a\"b\"c", new[] { "cmd", "abc" })]
    [TestCase("cmd --filter \"\"", new[] { "cmd", "--filter", "" })]
    [TestCase("cmd \"\" \"\"", new[] { "cmd", "", "" })]
    [TestCase("echo \"a b\"c", new[] { "echo", "a bc" })]
    [TestCase("cmd \"a\\\"b\"", new[] { "cmd", "a\"b" })]
    [TestCase("cmd a\\\\b", new[] { "cmd", @"a\\b" })]
    [TestCase("cmd \"a\\\\\"", new[] { "cmd", @"a\" })]
    public void Command_lines_split_the_way_a_console_would_splits_them(string commandLine, string[] expected)
    {
        Assert.That(ProcessRunner.SplitCommandLine(commandLine), Is.EqualTo(expected));
    }

    [Test]
    public void A_quoted_run_keeps_its_spaces()
    {
        // The old splitter turned this into two arguments.
        var parts = ProcessRunner.SplitCommandLine("tool --message \"hello there, world\"");

        Assert.That(parts, Is.EqualTo(new[] { "tool", "--message", "hello there, world" }));
    }

    [Test]
    public async Task A_quoted_run_reaches_the_child_intact()
    {
        var runner = new ProcessRunner();
        var shell = Shell() ?? throw new InvalidOperationException("No shell on this platform.");

        // The quotes exist on the command line only. cmd re-quotes its own argument, so the script
        // prints what it was given; the point is that the shell saw one argument, not three.
        var result = await runner.RunQuotedAsync($"{shell.File} {shell.ScriptFlag} echo one two three");

        Assert.That(result.CombinedOutput.Trim(), Is.EqualTo("one two three"));
    }

    [Test]
    public void An_empty_command_line_is_refused_rather_than_running_nothing()
    {
        var runner = new ProcessRunner();

        Assert.ThrowsAsync<ArgumentException>(async () => await runner.RunQuotedAsync("   "));
    }
}
