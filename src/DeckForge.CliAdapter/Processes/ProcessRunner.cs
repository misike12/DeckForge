using System.Diagnostics;
using System.Text;

namespace DeckForge.CliAdapter.Processes;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
    public string CombinedOutput => string.Join(Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Runs external processes with streaming callbacks so the Console page and run panels can
/// show live output. All DeckForge external tools go through this one type.
/// </summary>
public sealed class ProcessRunner
{
    /// <summary>Line received on stdout (also fires for the merged console view).</summary>
    public event Action<string>? StandardLine;
    public event Action<string>? ErrorLine;

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                psi.EnvironmentVariables[key] = value;
            }
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }
            lock (stdout)
            {
                stdout.AppendLine(e.Data);
            }
            StandardLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
            }
            ErrorLine?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // already exited
            }
            throw;
        }

        lock (stdout)
        {
            lock (stderr)
            {
                return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
            }
        }
    }

    /// <summary>Fire-and-forget helper for tools like `where`/`--version` probes.</summary>
    public async Task<ProcessResult> RunQuotedAsync(string commandLine, string? workingDirectory = null, CancellationToken ct = default)
    {
        var parts = SplitCommandLine(commandLine);
        return await RunAsync(parts[0], parts.Skip(1).ToList(), workingDirectory, null, ct);
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }
        return [.. result];
    }
}
