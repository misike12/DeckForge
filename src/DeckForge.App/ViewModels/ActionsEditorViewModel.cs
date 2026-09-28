using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Code;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>One row in the parameter designer.</summary>
/// <remarks>
/// Every field here is reachable from the designer. The previous version carried eleven fields
/// of which the XAML bound five, so <c>Range</c>, <c>Options</c>, <c>Description</c>,
/// <c>Placeholder</c> and both <c>OnlyWhen</c> fields could be set in the generator but never in
/// the UI.
/// </remarks>
public partial class ParameterSpec : ParameterSpecBase
{
    protected override string EditorTypeValue => EditorType;

    [ObservableProperty]
    private string _name = "value";

    [ObservableProperty]
    private string _editorType = "Text";

    [ObservableProperty]
    private string _label = "Value";

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private string _defaultValue = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _placeholder = "";

    /// <summary>OnlyWhen: show only when this parameter equals a value (empty = always).</summary>
    [ObservableProperty]
    private string _onlyWhenParameter = "";

    [ObservableProperty]
    private string _onlyWhenValue = "";

    /// <summary>For Slider/Number/Duration: minimum.</summary>
    [ObservableProperty]
    private string _minimum = "";

    /// <summary>For Slider/Number/Duration: maximum.</summary>
    [ObservableProperty]
    private string _maximum = "100";

    /// <summary>For Slider/Number: step.</summary>
    [ObservableProperty]
    private string _step = "1";

    /// <summary>For Choice/MultiSelect/Autocomplete: one option per line "value=Label".</summary>
    [ObservableProperty]
    private string _options = "option1=First\noption2=Second";

    /// <summary>For DynamicChoice/Autocomplete/MultiSelect: the named source to resolve from.</summary>
    [ObservableProperty]
    private string _optionsSourceId = "";

    /// <summary>For File: comma-separated extension filter, e.g. <c>.png,.jpg</c>.</summary>
    [ObservableProperty]
    private string _fileExtensions = "";

    /// <summary>For Code: the language identifier.</summary>
    [ObservableProperty]
    private string _language = "csharp";

    /// <summary>For Text: a validation pattern.</summary>
    [ObservableProperty]
    private string _validationRegex = "";

    /// <summary>For Text/MultilineText: a maximum length.</summary>
    [ObservableProperty]
    private string _maxLength = "";

    [ObservableProperty]
    private bool _autoPrefixHttps;

    [ObservableProperty]
    private bool _supportsReset;

    [ObservableProperty]
    private bool _literalOnly;

    /// <summary>For WidgetTarget: whether the widget may target itself.</summary>
    [ObservableProperty]
    private bool _allowSelf = true;

    /// <summary>For WidgetTarget: comma-separated widget type ids to narrow the picker.</summary>
    [ObservableProperty]
    private string _widgetTypes = "";

    /// <summary>For Object: child parameters, one per line as <c>name|Type|Label</c>.</summary>
    [ObservableProperty]
    private string _children = "";

    /// <summary>For Array: the item template as <c>name|Type|Label</c>.</summary>
    [ObservableProperty]
    private string _itemTemplate = "";

    partial void OnEditorTypeChanged(string value) => RefreshCapabilities();

    /// <summary>Projects the designer row onto the emitter's model.</summary>
    public ActionParameterSpec ToSpec() => new()
    {
        Name = Name.Trim(),
        EditorType = EditorType,
        Label = Label,
        Description = Description,
        Placeholder = Placeholder,
        Required = Required,
        DefaultValue = DefaultValue,
        Options = SplitLines(Options).Select(ParameterOption.Parse).Where(o => o.Value.Length > 0).ToList(),
        Min = Number(Minimum),
        Max = Number(Maximum),
        Step = Number(Step),
        OptionsSourceId = OptionsSourceId.Trim(),
        FileExtensions = SplitList(FileExtensions),
        Language = Language,
        ValidationRegex = ValidationRegex,
        MaxLength = Int(MaxLength),
        AutoPrefixHttps = AutoPrefixHttps,
        SupportsReset = SupportsReset,
        LiteralOnly = LiteralOnly,
        AllowSelf = AllowSelf,
        WidgetTypes = SplitList(WidgetTypes),
        OnlyWhenParameter = OnlyWhenParameter.Trim(),
        OnlyWhenValue = OnlyWhenValue,
        Children = ParseNested(Children),
        ItemTemplate = ParseNestedItem(ItemTemplate),
    };
}

public partial class ActionsEditorViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly ResxMergerService _resx;

    public ActionsEditorViewModel(WorkspaceManager workspaces, ResxMergerService resx)
    {
        _workspaces = workspaces;
        _resx = resx;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _actionId = "my-action";

    [ObservableProperty]
    private string _actionName = "My action";

    [ObservableProperty]
    private string _actionDescription = "Does something useful.";

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<ParameterSpec> Parameters { get; } = [];

    /// <summary>Every editor type the SDK declares, in SDK order.</summary>
    public static IReadOnlyList<string> EditorTypes => ActionParameterTypes.Names;

    /// <summary>The capability table, for the designer's help text.</summary>
    public static IReadOnlyList<ActionParameterTypeInfo> EditorTypeDetails => ActionParameterTypes.All;

    /// <summary>The parameter names in use, so the designer can offer a OnlyWhen picker.</summary>
    public IReadOnlyList<string> ParameterNames => [.. Parameters.Select(p => p.Name)];

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => Load();

    [RelayCommand]
    private void AddParameter() => Parameters.Add(new ParameterSpec
    {
        Name = $"param{Parameters.Count + 1}",
        Label = $"Parameter {Parameters.Count + 1}",
    });

    [RelayCommand]
    private void RemoveParameter(ParameterSpec? parameter)
    {
        if (parameter is not null)
        {
            Parameters.Remove(parameter);
            OnPropertyChanged(nameof(ParameterNames));
        }
    }

    /// <summary>Generates the action class + resx keys + integration registration.</summary>
    [RelayCommand]
    private void Generate()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }

        if (!Core.Utils.MacroDeckRules.IsValidLocalId(ActionId))
        {
            StatusText = "Action id must be lowercase kebab-case and at most 64 characters (it is persisted - choose carefully).";
            return;
        }

        if (Parameters.Count == 0)
        {
            StatusText = "Add at least one parameter, or the action cannot be configured.";
            return;
        }

        // A parameter name is the wire identity of a configured value: the host persists it and
        // every existing button that uses this action keeps referring to it. Duplicates, blanks
        // and illegal characters are refused up front rather than emitting code that will not bind.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Parameters)
        {
            var name = parameter.Name.Trim();
            if (name.Length == 0)
            {
                StatusText = "Every parameter needs a name.";
                return;
            }

            if (!Core.Utils.MacroDeckRules.IsValidLocalId(name))
            {
                StatusText = $"Parameter name '{name}' must be lowercase kebab-case (it is the persisted wire name).";
                return;
            }

            if (!seen.Add(name))
            {
                StatusText = $"Parameter name '{name}' is used twice.";
                return;
            }
        }

        foreach (var parameter in Parameters)
        {
            if (!ActionParameterTypes.IsKnown(parameter.EditorType))
            {
                StatusText = $"Unknown editor type '{parameter.EditorType}'.";
                return;
            }
        }

        // The same name the generator will use, so the file on disk and the registration agree. The
        // path matters: an id like `int` produced `@intAction.cs`, and csc rejects that with CS2011
        // before it compiles anything.
        var className = CSharpCode.TypeName(CSharpCode.ToPascal(ActionId), "Action");
        var path = Path.Combine(ws.PluginProjectDirectory, className + ".cs");
        if (File.Exists(path))
        {
            StatusText = $"{className}.cs already exists - delete it first or change the action id.";
            return;
        }

        try
        {
            // The shared generator, not a second copy of it. This page used to keep its own
            // renderer, and the two had drifted: the copy put the class in a namespace derived from
            // the action id rather than the project's, so the interface the template declares, the
            // generated Strings class, and the registration in PluginIntegration were all in three
            // different namespaces. Every action the page produced failed to build, and the copy
            // was the one nothing tested.
            var design = new ActionDesign
            {
                ActionId = ActionId,
                ActionName = ActionName,
                ActionDescription = ActionDescription,
                Parameters = [.. Parameters.Select(p => p.ToSpec())],
            };

            var problems = design.Validate();
            if (problems.Count > 0)
            {
                StatusText = string.Join(" ", problems);
                return;
            }

            var file = ActionGenerator.Generate(design, ws.RootNamespace);
            File.WriteAllText(path, file.Content);

            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            _resx.AddKeys(stringsPath, file.ResxEntries);

            var registered = RegisterInIntegration(ws, design.ClassName);

            StatusText = registered
                ? $"Generated {file.Path} with {Parameters.Count} parameter(s), {file.ResxEntries.Count} resx keys, and registered it in PluginIntegration.cs."
                : $"Generated {file.Path} with {Parameters.Count} parameter(s) and {file.ResxEntries.Count} resx keys, but PluginIntegration.cs could not be patched - add `new {design.ClassName}(logger)` to the Actions list by hand.";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    /// <summary>Appends the new action to the integration's Actions list. Returns false when it could not.</summary>
    private bool RegisterInIntegration(WorkspaceContext ws, string className)
    {
        var path = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
        if (!File.Exists(path))
        {
            return false;
        }

        var source = File.ReadAllText(path);
        if (source.Contains(className, StringComparison.Ordinal))
        {
            return true;
        }

        // The stock template's anchor, used verbatim.
        var anchor = "Actions = [new LogMessageAction(logger)];";
        if (source.Contains(anchor, StringComparison.Ordinal))
        {
            source = source.Replace(
                anchor,
                $"Actions = [new LogMessageAction(logger), new {className}(logger)];",
                StringComparison.Ordinal);
            File.WriteAllText(path, source);
            return true;
        }

        // Otherwise splice into the existing collection expression by bracket matching, so a
        // renamed or hand-extended list still works. IndexOf(']', -1) used to throw here.
        var open = source.IndexOf("Actions = [", StringComparison.Ordinal);
        if (open < 0)
        {
            return false;
        }

        var depth = 0;
        var close = -1;
        for (var i = source.IndexOf('[', open); i >= 0 && i < source.Length; i++)
        {
            if (source[i] == '[')
            {
                depth++;
            }
            else if (source[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        if (close < 0)
        {
            return false;
        }

        var before = source[..close].TrimEnd();
        var needsComma = before.EndsWith(']') ? false : !before.EndsWith('[');
        source = source.Insert(close, $"{(needsComma ? ", " : string.Empty)}new {className}(logger)");
        File.WriteAllText(path, source);
        return true;
    }
}
