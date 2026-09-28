using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DeckForge.CliAdapter.Processes;

/// <param name="Combined">
/// stdout and stderr interleaved in the order the child actually wrote them. Null only for results
/// built by hand in a test.
/// </param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string? Combined = null)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// The transcript in the order the child produced it. The fallback is only for a result
    /// constructed without a captured transcript; a real run always has one.
    /// </summary>
    public string CombinedOutput => Combined ?? string.Join(
        Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Runs external processes with streaming callbacks so the Console page and run panels can show
/// live output. All DeckForge external tools go through this one type.
/// </summary>
/// <remarks>
/// The instance <see cref="StandardLine"/>/<see cref="ErrorLine"/> events belong to the Console
/// page, which is the one place a transcript is genuinely global. Every other caller passes its own
/// callbacks per call: Build &amp; Run, Ship, Publish and the icon pack designer all stream at the
/// same time, and sharing one event meant each panel showed all four.
/// </remarks>
public sealed class ProcessRunner
{
    /// <summary>How long to keep draining the redirected pipes after the child has exited.</summary>
    /// <remarks>
    /// A grandchild that inherited the pipe and is still alive keeps it open, so waiting for end of
    /// stream unconditionally can hang a build forever. Five seconds is long enough for a real
    /// process to flush and short enough that a stray handle is not a hang.
    /// </remarks>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Line received on stdout. Fires for every run on this instance.</summary>
    public event Action<string>? StandardLine;

    /// <summary>Line received on stderr. Fires for every run on this instance.</summary>
    public event Action<string>? ErrorLine;

    /// <summary>
    /// Runs <paramref name="fileName"/> and returns its result.
    /// </summary>
    /// <param name="onStandardLine">Per-call stdout callback, in addition to <see cref="StandardLine"/>.</param>
    /// <param name="onErrorLine">Per-call stderr callback, in addition to <see cref="ErrorLine"/>.</param>
    /// <param name="standardInput">
    /// Text to write to the child's stdin, which is then closed. Null leaves stdin inherited, so a
    /// child that prompts reads from the parent console instead of blocking forever.
    /// </param>
    /// <param name="encoding">
    /// How to decode the child's output. Defaults to UTF-8, which is what dotnet, git and gh all
    /// emit. A child that writes OEM or ANSI code pages needs its own encoding passed in - there
    /// is no way to detect it from here.
    /// </param>
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default,
        Action<string>? onStandardLine = null,
        Action<string>? onErrorLine = null,
        string? standardInput = null,
        Encoding? encoding = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding ?? Encoding.UTF8,
            StandardErrorEncoding = encoding ?? Encoding.UTF8,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var combined = new StringBuilder();
        var gate = new object();

        // WaitForExitAsync completes when the child exits, which is not the same as the redirected
        // pipes reaching end of stream. Signalling EOF explicitly is what makes the transcript
        // complete: relying on the exit alone truncates the last few lines of a chatty build.
        var stdoutDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDrained.TrySetResult();
                return;
            }

            lock (gate)
            {
                stdout.AppendLine(e.Data);
                combined.AppendLine(e.Data);
            }

            Raise(StandardLine, onStandardLine, e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDrained.TrySetResult();
                return;
            }

            lock (gate)
            {
                stderr.AppendLine(e.Data);
                combined.AppendLine(e.Data);
            }

            Raise(ErrorLine, onErrorLine, e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessStartFailedException(fileName, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (standardInput is not null)
        {
            await WriteStandardInputAsync(process, standardInput);
        }

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }

        await DrainAsync(stdoutDrained.Task, stderrDrained.Task, ct);

        lock (gate)
        {
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), combined.ToString());
        }
    }

    /// <summary>
    /// Waits for both redirected pipes to reach end of stream, bounded by <see cref="DrainTimeout"/>.
    /// </summary>
    /// <remarks>
    /// The bound is real. The previous version started a five-second timer and then, on the other
    /// branch, awaited the drain unbounded - so the timeout only began an unlimited wait. A
    /// grandchild that inherited the pipe keeps it open for as long as it lives, and
    /// <c>msbuild</c> with node reuse does exactly that: measured at 31 s against a grandchild that
    /// lived 30 s, with the five-second budget ignored. Every panel that shells out goes through
    /// this one runner, so one lingering handle stalled the UI with no way to cancel.
    /// </remarks>
    private static async Task DrainAsync(Task stdout, Task stderr, CancellationToken ct)
    {
        var drained = Task.WhenAll(stdout, stderr);
        var timeout = Task.Delay(DrainTimeout, ct);
        var completed = await Task.WhenAny(drained, timeout);
        if (completed == drained)
        {
            return;
        }

        // Either the pipes are still held open by something that outlived the child, or the caller
        // cancelled. Either way the child is gone, so returning the lines captured so far is right
        // and a cancelled wait should still surface as cancellation.
        ct.ThrowIfCancellationRequested();
    }

    private static async Task WriteStandardInputAsync(Process process, string standardInput)
    {
        try
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // A child that never reads stdin closes the pipe; that is not a failure to report.
        }
        finally
        {
            process.StandardInput.Close();
        }
    }

    /// <summary>
    /// Invokes a line callback without letting it take the reader thread down.
    /// </summary>
    /// <remarks>
    /// These run on the thread-pool threads the stream readers use. An unhandled exception there
    /// does not fail the build - it terminates the process. A subscriber that throws while
    /// rendering a line would take DeckForge with it.
    /// </remarks>
    private static void Raise(Action<string>? global, Action<string>? perCall, string line)
    {
        TryInvoke(global, line);
        TryInvoke(perCall, line);
    }

    private static void TryInvoke(Action<string>? handler, string line)
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(line);
        }
        catch
        {
            // A console view that failed to append one line must not abort the read loop.
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the wait and the kill.
        }
        catch (Win32Exception)
        {
            // The process tree could not be reached - also already gone, or beyond our rights.
        }
        catch (NotSupportedException)
        {
            // Remote or otherwise unkillable; nothing to do.
        }
    }

    /// <summary>Splits a command line the way a console would, honouring quotes and backslashes.</summary>
    /// <remarks>
    /// The old splitter had no escapes, dropped every empty argument, and stripped quotes from
    /// inside a quoted run - so <c>-p "a b"</c> became two arguments and <c>--filter ""</c>
    /// vanished entirely. This follows the CommandLineToArgvW rules: a run of backslashes is
    /// literal unless it precedes a quote, in which case half of them escape it.
    /// </remarks>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;

        // An explicit cursor rather than a for-loop: the backslash case has to decide whether the
        // character after the run is part of this argument or the next one, and "continue" in a
        // for-loop always advances the cursor, which ate the character after an escaped quote.
        var i = 0;
        while (i < commandLine.Length)
        {
            var c = commandLine[i];

            if (c == '\\')
            {
                var backslashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    // 2n backslashes mean n literal backslashes and a quote that still delimits.
                    // 2n+1 mean n literal backslashes and a literal quote.
                    current.Append('\\', backslashes / 2);
                    if (backslashes % 2 == 0)
                    {
                        inQuotes = !inQuotes;
                    }
                    else
                    {
                        current.Append('"');
                    }

                    i++;
                }
                else
                {
                    current.Append('\\', backslashes);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                started = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (started || current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                i++;
                continue;
            }

            current.Append(c);
            started = true;
            i++;
        }

        if (started || current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Runs a command line typed into the Console page, splitting it as a shell would.
    /// </summary>
    public Task<ProcessResult> RunQuotedAsync(
        string commandLine,
        string? workingDirectory = null,
        CancellationToken ct = default,
        Action<string>? onStandardLine = null,
        Action<string>? onErrorLine = null)
    {
        var parts = SplitCommandLine(commandLine);
        if (parts.Count == 0)
        {
            throw new ArgumentException("The command line is empty.", nameof(commandLine));
        }

        return RunAsync(
            parts[0],
            [.. parts.Skip(1)],
            workingDirectory,
            environmentVariables: null,
            ct,
            onStandardLine,
            onErrorLine);
    }
}

/// <summary>A child process could not be started at all.</summary>
public sealed class ProcessStartFailedException(string fileName, Exception inner)
    : Exception($"Could not start '{fileName}': {inner.Message}", inner)
{
    public string FileName { get; } = fileName;
}
