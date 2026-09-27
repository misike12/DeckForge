using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Blocks;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

public partial class BlockActionViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;

    public BlockActionViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _compiledPreview = "// Blocks appear here once you add statements.";

    public ObservableCollection<BlockStatement> Statements { get; } = [];

    // New-statement form state.
    [ObservableProperty]
    private string _newLogTemplate = "Hello from my plugin";

    [ObservableProperty]
    private string _newVariableName = "message";

    [ObservableProperty]
    private string _newParameterName = "message";

    [ObservableProperty]
    private string _selectedParameterType = "string";

    public string[] ParameterTypes { get; } = ["string", "number", "bool"];

    [ObservableProperty]
    private int _newDelayMs = 500;

    [ObservableProperty]
    private string _newUrl = "https://example.com/api";

    [ObservableProperty]
    private string _newNotifyTitle = "Done";

    [ObservableProperty]
    private string _newNotifyMessage = "The action finished.";

    public void Load()
    {
        var ws = _workspaces.Current;
        HasWorkspace = ws is not null;
        if (ws is null)
        {
            return;
        }
        Recompile();
    }

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => Load();

    [RelayCommand]
    private void AddLog()
    {
        Statements.Add(new LogBlock { Template = NewLogTemplate });
        Recompile();
    }

    [RelayCommand]
    private void AddVariable()
    {
        Statements.Add(new SetVariableBlock
        {
            VariableName = NewVariableName,
            FromParameter = NewParameterName,
            Type = SelectedParameterType,
        });
        Recompile();
    }

    [RelayCommand]
    private void AddDelay()
    {
        Statements.Add(new DelayBlock { Milliseconds = NewDelayMs });
        Recompile();
    }

    [RelayCommand]
    private void AddSuccess()
    {
        Statements.Add(new ReturnResultBlock { Outcome = "success" });
        Recompile();
    }

    [RelayCommand]
    private void AddFailure()
    {
        Statements.Add(new ReturnResultBlock
        {
            Outcome = "failed",
            ErrorCode = "InvalidParameter",
            Message = "The action could not run.",
        });
        Recompile();
    }

    [RelayCommand]
    private void AddHttp()
    {
        Statements.Add(new HttpRequestBlock { Url = NewUrl, IntoVariable = "response" });
        Recompile();
    }

    [RelayCommand]
    private void AddNotify()
    {
        Statements.Add(new NotifyBlock { Title = NewNotifyTitle, Message = NewNotifyMessage });
        Recompile();
    }

    [RelayCommand]
    private void RemoveSelected(object? block)
    {
        if (block is BlockStatement statement)
        {
            Statements.Remove(statement);
            Recompile();
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Statements.Clear();
        Recompile();
    }

    private void Recompile() =>
        CompiledPreview = BlockCompiler.Compile(new BlockProgram
        {
            TargetActionId = "generated",
            Statements = [.. Statements],
        });

    /// <summary>
    /// Writes the compiled region into the example action file inside the markers,
    /// leaving everything outside untouched (the agreed blocks + escape hatch model).
    /// </summary>
    [RelayCommand]
    private void SaveIntoAction()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a workspace first.";
            return;
        }
        var actionFile = Path.Combine(ws.PluginProjectDirectory, "LogMessageAction.cs");
        if (!File.Exists(actionFile))
        {
            StatusText = "LogMessageAction.cs not found (rename/replace the example action first).";
            return;
        }

        var source = File.ReadAllText(actionFile);
        var compiled = BlockCompiler.Compile(new BlockProgram { TargetActionId = "log-message", Statements = [.. Statements] });

        string updated;
        if (BlockCompiler.ExtractRegion(source) is not null)
        {
            var begin = source.IndexOf(BlockCompiler.BeginMarker, StringComparison.Ordinal);
            var end = source.IndexOf(BlockCompiler.EndMarker, StringComparison.Ordinal)
                      + BlockCompiler.EndMarker.Length;
            updated = source[..begin] + compiled + source[end..];
        }
        else
        {
            // First generation: insert the region at the top of ExecuteAsync's body.
            var anchor = "public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)";
            var idx = source.IndexOf(anchor, StringComparison.Ordinal);
            var openBrace = source.IndexOf('{', idx);
            updated = source[..(openBrace + 1)] + "\n        " + compiled + source[(openBrace + 1)..];
        }

        File.WriteAllText(actionFile, updated);
        StatusText = $"Blocks written into {actionFile}";
    }
}
