using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Blocks;
using DeckForge.Core.Code;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>
/// The block canvas: compose a program from blocks, see the C# it compiles to, and write it into
/// the target action.
/// </summary>
public partial class BlockActionViewModel : ObservableObject
{
    /// <summary>
    /// The action the canvas edits.
    /// </summary>
    /// <remarks>
    /// It used to be written <c>generated</c> when previewing and <c>log-message</c> when saving.
    /// The id reaches the generated code - it is the action's log label - so the preview a user
    /// read was not the code they got. One constant, used by both.
    /// </remarks>
    private const string DefaultTargetActionId = "log-message";

    private readonly WorkspaceManager _workspaces;

    public BlockActionViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    /// <summary>
    /// The id of the action the canvas writes into.
    /// </summary>
    /// <remarks>
    /// This was a <c>const</c>, which is why the canvas could only ever write into
    /// <c>LogMessageAction.cs</c>: the target file was never derived from a choice, because there was
    /// no choice to make. It is a property now so the target is real state, restored from the saved
    /// program and usable by the target picker that still has to be added to the page.
    /// </remarks>
    [ObservableProperty]
    private string _targetActionId = DefaultTargetActionId;

    /// <summary>
    /// The file the canvas writes into, named from <see cref="TargetActionId"/> by the same rule
    /// the generator and the Actions page use.
    /// </summary>
    public string TargetFileName => CSharpCode.TypeName(CSharpCode.ToPascal(TargetActionId), "Action") + ".cs";

    partial void OnTargetActionIdChanged(string value) => OnPropertyChanged(nameof(TargetFileName));

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _compiledPreview = "// Blocks appear here once you add statements.";

    public ObservableCollection<BlockStatement> Statements { get; } = [];

    /// <summary>
    /// Whether the canvas has any statements, so the page can show a real empty state instead of
    /// an unexplained blank list.
    /// </summary>
    public bool HasStatements
    {
        get => Statements.Count > 0;
        set
        {
            if (!value)
            {
                Statements.Clear();
            }
        }
    }

    /// <summary>Statement count, shown in the program header.</summary>
    public int StatementCount => Statements.Count;

    /// <summary>
    /// The workspace the statements currently on the canvas belong to, or null when there is none.
    /// </summary>
    /// <remarks>
    /// This is what tells a plain navigation apart from a real workspace switch. Without it the
    /// canvas could not tell "the user went to another page and came back" from "the user opened a
    /// different plugin", and had to reload for both - which is what threw the work away.
    /// </remarks>
    private string? _canvasWorkspace;

    // New-statement form state.
    [ObservableProperty]
    private string _newLogTemplate = "Hello from my plugin";

    [ObservableProperty]
    private string _newVariableName = "message";

    [ObservableProperty]
    private string _newParameterName = "message";

    [ObservableProperty]
    private string _selectedParameterType = "string";

    public string[] ParameterTypes { get; } = BlockCompiler.VariableTypes.ToArray();

    [ObservableProperty]
    private int _newDelayMs = 500;

    [ObservableProperty]
    private string _newUrl = "https://example.com/api";

    [ObservableProperty]
    private string _newNotifyTitle = "Done";

    [ObservableProperty]
    private string _newNotifyMessage = "The action finished.";

    // Host navigation. The ids are whatever the host reports; Macro Deck's folder and profile ids
    // are user-chosen strings, so these are free text rather than a closed list.
    [ObservableProperty]
    private string _newFolderId = "";

    [ObservableProperty]
    private string _newProfileId = "";

    [ObservableProperty]
    private string _newTargetFolder = "Parent";

    [ObservableProperty]
    private string _newTargetProfile = "Profile 1";

    // Conditional.
    [ObservableProperty]
    private string _newIfLeftVariable = "message";

    [ObservableProperty]
    private string _newIfRightLiteral = "b";

    [ObservableProperty]
    private string _selectedIfOperator = "contains";

    public string[] IfOperators { get; } = BlockCompiler.ComparisonOperators.ToArray();

    // Script, event, host variable.
    [ObservableProperty]
    private string _newScriptId = "";

    [ObservableProperty]
    private string _newScriptInputs = "";

    [ObservableProperty]
    private string _newEventId = "";

    [ObservableProperty]
    private string _newEventPayload = "";

    [ObservableProperty]
    private string _newHostVariable = "my-variable";

    [ObservableProperty]
    private string _newHostValue = "";

    [ObservableProperty]
    private string _newIconActionId = DefaultTargetActionId;

    [ObservableProperty]
    private string _newModalViewId = "my-plugin.view";

    [ObservableProperty]
    private string _newModalTitle = "Something happened";

    [ObservableProperty]
    private string _newModalData = "";

    [ObservableProperty]
    private string _newThrowMessage = "The action could not run.";

    public string[] ErrorCodes { get; } = BlockCompiler.ErrorCodes.ToArray();

    public void Load()
    {
        var ws = _workspaces.Current;
        HasWorkspace = ws is not null;

        // Cleared even with no workspace open. The canvas belonged to the workspace that just
        // closed, so leaving it on screen let the user save one workspace's blocks into another,
        // or press Save with no workspace and read a confusing message.
        Statements.Clear();
        _canvasWorkspace = ws?.PluginProjectDirectory;
        StatusText = "";
        if (ws is null)
        {
            CompiledPreview = "Open a workspace to compose its blocks.";
            OnPropertyChanged(nameof(HasStatements));
            OnPropertyChanged(nameof(StatementCount));
            return;
        }

        // Read once: this parses the sidecar off disk, and it was being read twice - once for the
        // statements and again for the target - so every load paid for two reads of the same file.
        var persisted = ReadPersisted();

        foreach (var statement in persisted?.Statements ?? [])
        {
            Statements.Add(statement);
        }

        // The target is restored with the statements. It used to be dropped, so reopening a saved
        // program aimed the canvas back at the example action instead of the action it was written
        // for, and the next save quietly moved the blocks somewhere else.
        if (persisted?.TargetActionId is { Length: > 0 } savedTarget)
        {
            TargetActionId = savedTarget;
        }

        Recompile();
    }

    /// <summary>Shell hook.</summary>
    /// <remarks>
    /// Navigating to another page and coming back used to call <see cref="Load"/>, which clears the
    /// canvas and refills it from the sidecar on disk. A canvas that has never been saved has no
    /// sidecar, so the refill found nothing and the work was gone: build a few statements, glance at
    /// another page, come back, and the canvas is empty, with no warning and no undo.
    ///
    /// So a plain navigation now only reloads when the workspace behind the canvas is not the one
    /// already on screen. A genuine workspace switch is still handled, because that is what raises
    /// <c>WorkspaceChanged</c>, which calls <see cref="Load"/> directly.
    /// </remarks>
    public void RefreshOnNavigate()
    {
        if (string.Equals(
                _canvasWorkspace,
                _workspaces.Current?.PluginProjectDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Load();
    }

    private BlockProgram Program() => new()
    {
        TargetActionId = TargetActionId,
        // Derived from the target id, by the same rule the Actions page and the generator use. It
        // used to be left unset, so BlockProgram's default of "LogMessageAction.cs" applied to every
        // program and the save always went to the example action, whatever the canvas was aimed at.
        TargetFile = TargetFileName,
        Statements = [.. Statements],
    };

    [RelayCommand]
    private void AddLog()
    {
        Statements.Add(new LogBlock { Template = NewLogTemplate });
        Recompile();
        NoteAdded("log block");
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
        NoteAdded("set-variable block");
    }

    [RelayCommand]
    private void AddDelay()
    {
        Statements.Add(new DelayBlock { Milliseconds = NewDelayMs });
        Recompile();
        NoteAdded("wait block");
    }

    [RelayCommand]
    private void AddSuccess()
    {
        Statements.Add(new ReturnResultBlock { Outcome = "success" });
        Recompile();
        NoteAdded("return-success block");
    }

    [RelayCommand]
    private void AddFailure()
    {
        Statements.Add(new ReturnResultBlock
        {
            Outcome = "failed",
            ErrorCode = BlockCompiler.ErrorCodes.Contains("InvalidParameter") ? "InvalidParameter" : BlockCompiler.ErrorCodes[0],
            Message = NewThrowMessage,
        });
        Recompile();
        NoteAdded("return-failed block");
    }

    [RelayCommand]
    private void AddHttp()
    {
        Statements.Add(new HttpRequestBlock { Url = NewUrl, IntoVariable = "response" });
        Recompile();
        NoteAdded("HTTP GET block");
    }

    [RelayCommand]
    private void AddNotify()
    {
        Statements.Add(new NotifyBlock { Title = NewNotifyTitle, Message = NewNotifyMessage });
        Recompile();
        NoteAdded("notify block");
    }

    /// <summary>
    /// Adds a conditional.
    /// </summary>
    /// <remarks>
    /// The branch bodies start empty. A branch with nothing in it compiles to an empty block, which
    /// is honest - and the canvas's statement list is flat, so there is nowhere yet to put the
    /// statements that belong inside a branch.
    /// </remarks>
    [RelayCommand]
    private void AddIf()
    {
        Statements.Add(new IfBlock
        {
            LeftVariable = NewIfLeftVariable,
            Operator = SelectedIfOperator,
            RightLiteral = SelectedIfOperator is "isEmpty" or "isNotEmpty" ? null : NewIfRightLiteral,
        });
        Recompile();
        NoteAdded("if block");
    }

    [RelayCommand]
    private void AddNavigate()
    {
        if (string.IsNullOrWhiteSpace(NewFolderId))
        {
            StatusText = "A folder id is required: the host navigates by id, not by name.";
            return;
        }

        Statements.Add(new NavigateBlock { FolderId = NewFolderId.Trim() });
        Recompile();
        NoteAdded("open-folder block");
    }

    [RelayCommand]
    private void AddGoToParent()
    {
        Statements.Add(new GoToParentBlock());
        Recompile();
        NoteAdded("parent-folder block");
    }

    [RelayCommand]
    private void AddGoBack()
    {
        Statements.Add(new GoBackBlock());
        Recompile();
        NoteAdded("back block");
    }

    [RelayCommand]
    private void AddChangeProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileId))
        {
            StatusText = "A profile id is required: the host switches by id, not by name.";
            return;
        }

        Statements.Add(new ChangeProfileBlock { ProfileId = NewProfileId.Trim() });
        Recompile();
        NoteAdded("switch-profile block");
    }

    [RelayCommand]
    private void AddRunScript()
    {
        if (string.IsNullOrWhiteSpace(NewScriptId))
        {
            StatusText = "A script id is required: the host runs scripts by id.";
            return;
        }

        Statements.Add(new RunScriptBlock { ScriptId = NewScriptId.Trim(), Inputs = NewScriptInputs });
        Recompile();
        NoteAdded("run-script block");
    }

    [RelayCommand]
    private void AddPublishEvent()
    {
        if (string.IsNullOrWhiteSpace(NewEventId))
        {
            StatusText = "An event id is required: the host publishes occurrences by id.";
            return;
        }

        Statements.Add(new PublishEventBlock { EventId = NewEventId.Trim(), Payload = NewEventPayload });
        Recompile();
        NoteAdded("publish-event block");
    }

    [RelayCommand]
    private void AddReadVariable()
    {
        if (string.IsNullOrWhiteSpace(NewHostVariable))
        {
            StatusText = "A variable name is required.";
            return;
        }

        Statements.Add(new ReadVariableBlock { VariableName = NewHostVariable.Trim(), IntoVariable = "hostValue" });
        Recompile();
        NoteAdded("read-variable block");
    }

    [RelayCommand]
    private void AddSetVariableValue()
    {
        if (string.IsNullOrWhiteSpace(NewHostVariable))
        {
            StatusText = "A variable name is required.";
            return;
        }

        Statements.Add(new SetVariableValueBlock { VariableName = NewHostVariable.Trim(), Value = NewHostValue });
        Recompile();
        NoteAdded("write-variable block");
    }

    [RelayCommand]
    private void AddShowModal()
    {
        if (string.IsNullOrWhiteSpace(NewModalViewId))
        {
            StatusText = "A view id is required: the plugin has to serve the view it asks for.";
            return;
        }

        Statements.Add(new ShowModalBlock
        {
            ViewId = NewModalViewId.Trim(),
            Title = NewModalTitle,
            Data = NewModalData,
        });
        Recompile();
        NoteAdded("show-modal block");
    }

    [RelayCommand]
    private void AddInvalidateIcon()
    {
        if (string.IsNullOrWhiteSpace(NewIconActionId))
        {
            StatusText = "An action id is required: that is whose icon is being invalidated.";
            return;
        }

        Statements.Add(new InvalidateIconBlock { ActionId = NewIconActionId.Trim() });
        Recompile();
        NoteAdded("invalidate-icon block");
    }

    [RelayCommand]
    private void AddThrow()
    {
        Statements.Add(new ThrowBlock { Message = NewThrowMessage });
        Recompile();
        NoteAdded("throw block");
    }

    [RelayCommand]
    private void RemoveSelected(object? block)
    {
        if (block is BlockStatement statement)
        {
            Statements.Remove(statement);
            Recompile();
            StatusText = $"Removed {statement.Description}. {StatementCount} statement{Plural(StatementCount)} left.";
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Statements.Clear();
        Recompile();
        StatusText = "Canvas cleared. Nothing is written until you save.";
    }

    /// <summary>
    /// Recompiles the preview and refreshes the properties the header and empty state bind to.
    /// </summary>
    /// <remarks>
    /// <see cref="HasStatements"/> and <see cref="StatementCount"/> are derived from the collection,
    /// so they are recomputed here rather than stored: any add, remove, clear, or load has to
    /// re-raise them, and the collection itself does not.
    /// </remarks>
    private void Recompile()
    {
        CompiledPreview = BlockCompiler.Compile(Program());
        OnPropertyChanged(nameof(HasStatements));
        OnPropertyChanged(nameof(StatementCount));
    }

    /// <summary>
    /// Records that a block was added, and drops any stale error left on the status line.
    /// </summary>
    /// <remarks>
    /// The status line used to only ever be written on failure or save. Once an error was shown it
    /// stayed there for the rest of the session, so a later successful add still read as failed.
    /// </remarks>
    private void NoteAdded(string blockName)
    {
        StatusText = $"Added {blockName}. {StatementCount} statement{Plural(StatementCount)}.";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    /// <summary>The sidecar the canvas is persisted to, alongside the action it edits.</summary>
    private string? SidecarPath()
    {
        var ws = _workspaces.Current;
        return ws is null ? null : Path.Combine(ws.PluginProjectDirectory, BlockProgramJson.FileNameFor(Program()));
    }

    /// <summary>
    /// The saved canvas, or null when there is none or it cannot be read.
    /// </summary>
    /// <remarks>
    /// A corrupt sidecar is a warning, not an error: the user may be looking at a canvas they want
    /// to keep, and refusing to open the page would hide it. Returning null leaves the canvas empty
    /// and the file untouched, so the next save is a deliberate overwrite rather than data loss.
    /// </remarks>
    private BlockProgram? ReadPersisted()
    {
        var path = SidecarPath();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        if (BlockProgramJson.TryDeserialize(File.ReadAllText(path), out var program))
        {
            return program;
        }

        StatusText = $"{Path.GetFileName(path)} could not be read; the canvas starts empty and the file is left alone.";
        return null;
    }

    /// <summary>
    /// Whether the plugin declares <c>IIntegrationContextAware</c>, which host-calling blocks need.
    /// </summary>
    /// <remarks>
    /// A plugin generated before that interface existed, or generated without the capability that
    /// declares it, has no such type. The writer has to know, because wiring an action for a host
    /// context names the interface in the class's base list - and against a plugin that does not
    /// declare it, that is a CS0246 on the action's own declaration. The page said "Blocks written"
    /// and the plugin would not build.
    /// </remarks>
    private static bool PluginDeclaresIntegrationContext(string pluginProjectDirectory)
    {
        if (!Directory.Exists(pluginProjectDirectory))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(pluginProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .Any(text => text.Contains("interface IIntegrationContextAware", StringComparison.Ordinal));
    }

    /// <summary>
    /// Writes the compiled region into the target action, and keeps the canvas itself so the
    /// blocks can be edited again.
    /// </summary>
    /// <remarks>
    /// The write used to be done here, by hand: it spliced the region in at a hardcoded eight-space
    /// indent, and never made the executor <c>async</c> or gave the action a host context. So a
    /// canvas with a delay or a navigation step - the two things a block program is for - wrote code
    /// that did not compile, and the page said it had been written. All three steps now live in
    /// <see cref="BlockProgramWriter"/>, where a test can reach them.
    /// </remarks>
    [RelayCommand]
    private void SaveIntoAction()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a workspace first.";
            return;
        }

        var program = Program();
        var actionFile = Path.Combine(ws.PluginProjectDirectory, program.TargetFile);
        if (!File.Exists(actionFile))
        {
            // Naming the file that is actually missing, and how to get it, instead of telling the
            // user to "set the target action on the Blocks page": there is no such control on the
            // page, so the instruction sent them looking for something that does not exist and left
            // them with no way to fix it.
            StatusText =
                $"{program.TargetFile} not found. This workspace has no action with the id " +
                $"'{TargetActionId}'. Create an action with that id on the Actions page, or point " +
                "the canvas at an action this workspace already has.";
            return;
        }

        try
        {
            var result = BlockProgramWriter.Write(
                File.ReadAllText(actionFile),
                program,
                integrationAvailable: PluginDeclaresIntegrationContext(ws.PluginProjectDirectory));
            if (!result.Success)
            {
                StatusText = result.Message;
                return;
            }

            File.WriteAllText(actionFile, result.Content);
            File.WriteAllText(SidecarPath()!, BlockProgramJson.Serialize(program));

            // The writer's own message, when it has more to say than "written" - a canvas that ends
            // in a return makes the action's own trailing return dead, and the user needs to hear that
            // here rather than find CS0162 in their own build.
            StatusText = result.Message.Length > "Blocks written.".Length
                ? result.Message
                : $"Blocks written into {program.TargetFile}.";
        }
        catch (IOException ex)
        {
            StatusText = $"Could not write the blocks: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = $"Could not write the blocks: {ex.Message}";
        }
    }
}
