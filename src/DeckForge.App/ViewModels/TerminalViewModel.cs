using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CliAdapter.Processes;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    private readonly ProcessRunner _runner;
    private readonly WorkspaceManager _workspaces;
    private int _historyIndex = -1;
    private readonly List<string> _history = [];
    private readonly Queue<TerminalLine> _pendingLines = [];
    private bool _flushScheduled;

    public TerminalViewModel(ProcessRunner runner, WorkspaceManager workspaces)
    {
        _runner = runner;
        _workspaces = workspaces;
        _runner.StandardLine += line => Append(line);
        _runner.ErrorLine += line => Append(line, isError: true);

        Services.ShellMessenger.WorkspaceChanged += _ =>
        {
            Append($"workspace: {Cwd}", isCommand: true);
        };

        Append("DeckForge terminal - type 'help' for built-in commands, or run any CLI directly.");
    }

    [ObservableProperty]
    private string _input = "";

    [ObservableProperty]
    private string _cwd = "(no workspace - commands run in DeckForge's directory)";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<TerminalLine> Lines { get; } = [];

    private string EffectiveCwd => _workspaces.Current?.RootDirectory ?? Environment.CurrentDirectory;

    /// <summary>
    /// Built-in commands, with the explanation shown by 'help' and 'explain &lt;command&gt;'.
    /// </summary>
    public static IReadOnlyList<TerminalCommandDoc> BuiltIns { get; } =
    [
        new("help", "List built-in commands (this list). 'help <command>' explains one in detail."),
        new("clear", "Clear the terminal output."),
        new("cwd", "Print the current working directory (the open plugin's root)."),
        new("explain", "Explain a DeckForge or macrodeck-plugin concept: try 'explain permissions', 'explain events', 'explain configflow', 'explain manifest', 'explain widget'."),
        new("dotnet build", "Build the open plugin project with the real .NET SDK."),
        new("dotnet test", "Run the plugin's test project."),
        new("macrodeck-plugin build", "Publish + pack the plugin into a .macroDeckPlugin artifact."),
        new("macrodeck-plugin run --stub-host", "Run the plugin against Macro Deck's stub host without a full Macro Deck."),
        new("macrodeck-plugin validate --artifact <file>", "Validate a packed artifact (conformance, entrypoints, manifest)."),
        new("macrodeck-plugin inspect --artifact <file>", "Print an artifact's identity, entrypoints and languages."),
        new("macrodeck-plugin keygen <dir>", "Generate a creator key for out-of-Store signing."),
        new("macrodeck-plugin sign <artifact> <key>", "Creator-sign an artifact for direct distribution."),
        new("gh release create", "Publish a GitHub release (used by the Publish page)."),
        new("vpk pack", "Build DeckForge's own installer/portable/delta releases (Velopack)."),
    ];

    /// <summary>Short concept explanations for 'explain'.</summary>
    private static IReadOnlyDictionary<string, string> Explanations { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["permissions"] = """
            Permissions declare which host surfaces your plugin touches. They live in
            manifest.json under "permissions" and are grouped as:
              host:*    - Host APIs: variables, config, deck, widgets, devices, ...
              events:*  - Publishing events (events:publish)
              assets:*  - Uploading icons/resources (assets:upload)
              net:*     - Outbound network (net:outbound)
              fs:*      - User files (fs:user-files)
              process:* - Spawning processes (process:spawn)
              device:*  - Hardware access (device:usb)
            Only host:adb is hard-enforced; the rest are declarations shown to the user.
            Edit them visually on the Manifest page.
            """,
        ["events"] = "Events let users trigger automations when something happens in your plugin. Declare them with IEventProvider (Events page) and publish with context.Events.Publish(id, payload). Configuration parameters filter occurrences; payload parameters carry data.",
        ["configflow"] = "A setup flow walks users through connecting your integration: form steps, validation, secrets (encrypted by the host) and optional OAuth handoff. Implement IConfigFlowProvider + IConfigFlow - the Setup Flow page generates it all.",
        ["manifest"] = "manifest.json declares your plugin's identity (id, name, version), entrypoints per platform, publication metadata and permissions. The five runtime fields are required; everything else is recommended. See the Manifest page or 'Docs -> Manifest reference'.",
        ["widget"] = "A widget type is a custom deck tile. IWidgetTypeProvider registers it; IUiProvider draws it on widget/preview/config surfaces. The Widget Designer page generates both from a visual tree.",
        ["stubhost"] = "The stub host (macrodeck-plugin run --stub-host) runs your plugin against a minimal Macro Deck implementation: the plugin protocol connects, your endpoints serve /_macrodeck/health etc. Build & Run page automates it (F5).",
        ["artifact"] = "A .macroDeckPlugin file is a zip with manifest.json, the per-platform runtimes/ layout and file digests. Build it with macrodeck-plugin build (or the Ship page), then validate/inspect/sign it.",
        ["sdk"] = "DeckForge pins Macro Deck SDK 3.0.0-beta.14 (the version the docs describe). The pin lives in MacroDeckSdkInfo.cs - bump it only when Macro Deck ships a new beta.",
    };

    [RelayCommand]
    private async Task RunAsync(CancellationToken ct)
    {
        var commandLine = Input.Trim();
        if (commandLine.Length == 0 || IsBusy)
        {
            return;
        }

        PushHistory(commandLine);
        Append($"> {commandLine}", isCommand: true);
        Input = "";

        var parts = SplitCommandLine(commandLine);
        if (parts.Length == 0)
        {
            return;
        }

        // Built-ins so the console feels native and never touches a process.
        switch (parts[0].ToLowerInvariant())
        {
            case "clear":
                Lines.Clear();
                return;
            case "cwd":
                Append(EffectiveCwd);
                return;
            case "help":
                RunHelp(parts.Length > 1 ? parts[1] : null);
                return;
            case "explain":
                RunExplain(parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null);
                return;
        }

        var workingDir = Directory.Exists(EffectiveCwd) ? EffectiveCwd : null;
        Cwd = workingDir ?? "(default)";

        IsBusy = true;
        try
        {
            var result = await _runner.RunAsync(parts[0], [.. parts.Skip(1)], workingDir, null, ct);
            Append($"[exit {result.ExitCode}]", isCommand: true);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Append($"command not found: {parts[0]} ({ex.Message}) - try 'help' for built-ins.", isError: true);
        }
        catch (OperationCanceledException)
        {
            Append("[cancelled]", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RunHelp(string? topic)
    {
        if (topic is not null)
        {
            var match = BuiltIns.FirstOrDefault(c =>
                c.Name.Equals(topic, StringComparison.OrdinalIgnoreCase)
                || c.Name.Split(' ')[0].Equals(topic, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                Append($"{match.Name} - {match.Explanation}");
                return;
            }
            if (Explanations.TryGetValue(topic, out var concept))
            {
                Append(concept.TrimEnd());
                return;
            }
            Append($"No help for '{topic}'. Try 'help' for the command list or 'explain' for concepts.", isError: true);
            return;
        }

        Append("Built-in commands:");
        foreach (var command in BuiltIns)
        {
            Append($"  {command.Name,-40} {command.Explanation}");
        }
        Append("Concepts: explain permissions | events | configflow | manifest | widget | stubhost | artifact | sdk");
    }

    private void RunExplain(string? topic)
    {
        if (topic is null)
        {
            Append("Explain what? e.g. explain permissions, explain events, explain configflow, explain manifest, explain widget, explain stubhost, explain artifact, explain sdk");
            return;
        }
        if (Explanations.TryGetValue(topic.Trim(), out var text))
        {
            Append(text.TrimEnd());
            return;
        }
        if (BuiltIns.FirstOrDefault(c => c.Name.Equals(topic.Trim(), StringComparison.OrdinalIgnoreCase)) is { } command)
        {
            Append($"{command.Name} - {command.Explanation}");
            return;
        }
        Append($"No explanation for '{topic}'. Try: {string.Join(", ", Explanations.Keys)}", isError: true);
    }

    /// <summary>History navigation with ArrowUp/ArrowDown (wired from the page).</summary>
    public string? HistoryUp()
    {
        if (_history.Count == 0)
        {
            return null;
        }
        _historyIndex = Math.Min(_historyIndex + 1, _history.Count - 1);
        return _history[^(_historyIndex + 1)];
    }

    public string? HistoryDown()
    {
        if (_historyIndex < 0)
        {
            return null;
        }
        _historyIndex--;
        return _historyIndex < 0 ? "" : _history[^(_historyIndex + 1)];
    }

    private void PushHistory(string command)
    {
        if (_history.LastOrDefault() != command)
        {
            _history.Add(command);
        }
        _historyIndex = -1;
    }

    /// <summary>
    /// Buffers streamed lines and flushes them coalesced on the UI thread. A flood of output
    /// (cmd's `help`, builds) becomes one collection change instead of hundreds of synchronous
    /// Dispatcher.Invoke calls - which previously re-entered the page's ScrollIntoView and
    /// crashed the app.
    /// </summary>
    private void Append(string text, bool isCommand = false, bool isError = false)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }
        lock (_pendingLines)
        {
            _pendingLines.Enqueue(new TerminalLine(text, isCommand, isError));
            if (_flushScheduled)
            {
                return;
            }
            _flushScheduled = true;
        }
        app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, FlushPending);
    }

    private void FlushPending()
    {
        List<TerminalLine> batch;
        lock (_pendingLines)
        {
            _flushScheduled = false;
            if (_pendingLines.Count == 0)
            {
                return;
            }
            batch = [.. _pendingLines];
            _pendingLines.Clear();
        }
        foreach (var line in batch)
        {
            Lines.Add(line);
        }
        TrimToTail(2000);
    }

    /// <summary>Keeps the buffer bounded so long builds cannot grow memory without limit.</summary>
    private void TrimToTail(int maxLines)
    {
        while (Lines.Count > maxLines)
        {
            Lines.RemoveAt(0);
        }
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
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

public sealed record TerminalLine(string Text, bool IsCommand = false, bool IsError = false);

/// <summary>One documented built-in command.</summary>
public sealed record TerminalCommandDoc(string Name, string Explanation);
