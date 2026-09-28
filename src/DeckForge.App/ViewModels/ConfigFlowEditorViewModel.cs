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

/// <summary>
/// One form field on a config-flow step.
/// </summary>
/// <remarks>
/// The step's fields are the same <c>ActionParameter</c> schema an action uses, so this is the
/// shared spec plus a projection. The previous version was a third divergent copy with fourteen
/// editor types and a factory switch that emitted a call with a missing required argument.
/// </remarks>
public partial class ConfigFlowField : ParameterSpecBase
{
    protected override string EditorTypeValue => EditorType;

    [ObservableProperty]
    private string _name = "server_url";

    [ObservableProperty]
    private string _editorType = "Text";

    [ObservableProperty]
    private string _label = "Server URL";

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private string _defaultValue = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _placeholder = "";

    /// <summary>Show this field only when another field of the step holds one of these values (blank = always).</summary>
    [ObservableProperty]
    private string _onlyWhenParameter = "";

    [ObservableProperty]
    private string _onlyWhenValue = "";

    [ObservableProperty]
    private string _minimum = "";

    [ObservableProperty]
    private string _maximum = "";

    [ObservableProperty]
    private string _step = "1";

    /// <summary>For Choice/MultiSelect/Autocomplete: one option per line "value=Label".</summary>
    [ObservableProperty]
    private string _options = "option1=First\noption2=Second";

    [ObservableProperty]
    private string _optionsSourceId = "";

    [ObservableProperty]
    private string _fileExtensions = "";

    [ObservableProperty]
    private string _language = "csharp";

    [ObservableProperty]
    private string _validationRegex = "";

    [ObservableProperty]
    private string _maxLength = "";

    [ObservableProperty]
    private bool _autoPrefixHttps;

    [ObservableProperty]
    private bool _supportsReset;

    [ObservableProperty]
    private bool _literalOnly;

    [ObservableProperty]
    private bool _allowSelf = true;

    [ObservableProperty]
    private string _widgetTypes = "";

    [ObservableProperty]
    private string _children = "";

    [ObservableProperty]
    private string _itemTemplate = "";

    partial void OnEditorTypeChanged(string value) => RefreshCapabilities();

    /// <summary>Projects onto the shared emitter model.</summary>
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

/// <summary>One step of the config flow.</summary>
public partial class ConfigFlowStepSpec : ObservableObject
{
    [ObservableProperty]
    private string _stepId = "connection";

    [ObservableProperty]
    private string _title = "Connection";

    [ObservableProperty]
    private string _description = "";

    /// <summary>Instruction lines shown as a numbered list above the form.</summary>
    [ObservableProperty]
    private string _instructions = "";

    /// <summary>Links shown next to the instructions, one per line "Label=https://...".</summary>
    [ObservableProperty]
    private string _links = "";

    /// <summary>
    /// Validate credentials or connectivity on submit. Generates a real
    /// <c>ValidateAsync</c> hook rather than a call to a method that was never emitted.
    /// </summary>
    [ObservableProperty]
    private bool _validateOnSubmit;

    public ObservableCollection<ConfigFlowField> Fields { get; } = [];
}

public partial class ConfigFlowEditorViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly ResxMergerService _resx;

    public ConfigFlowEditorViewModel(WorkspaceManager workspaces, ResxMergerService resx)
    {
        _workspaces = workspaces;
        _resx = resx;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _flowName = "My Setup";

    /// <summary>Flow class name (derived, read-only display).</summary>
    public string FlowClassName
    {
        get
        {
            var stem = CSharpCode.ToPascal(FlowName).TrimStart('@');
            return stem.Length > 0 ? stem + "ConfigFlow" : "MyConfigFlow";
        }
    }

    /// <summary>When set, Complete() stores a secret the user never typed.</summary>
    [ObservableProperty]
    private bool _issueSecretOnComplete;

    [ObservableProperty]
    private string _secretValueName = "access_token";

    [ObservableProperty]
    private ConfigFlowStepSpec? _selectedStep;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<ConfigFlowStepSpec> Steps { get; } = [];

    /// <summary>Every editor type the SDK declares. A config-flow field is an ActionParameter.</summary>
    public static IReadOnlyList<string> EditorTypes => ActionParameterTypes.Names;

    /// <summary>Field names across every step, for the OnlyWhen picker.</summary>
    public IReadOnlyList<string> FieldNames =>
        [.. Steps.SelectMany(s => s.Fields).Select(f => f.Name).Distinct(StringComparer.Ordinal)];

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => Load();

    [RelayCommand]
    private void AddStep()
    {
        var step = new ConfigFlowStepSpec
        {
            StepId = $"step{Steps.Count + 1}",
            Title = $"Step {Steps.Count + 1}",
        };
        Steps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void RemoveStep(ConfigFlowStepSpec? step)
    {
        if (step is not null)
        {
            Steps.Remove(step);
            if (SelectedStep == step)
            {
                SelectedStep = Steps.FirstOrDefault();
            }
        }
    }

    [RelayCommand]
    private void AddField(ConfigFlowStepSpec? step)
    {
        step ??= SelectedStep;
        if (step is null)
        {
            return;
        }

        step.Fields.Add(new ConfigFlowField
        {
            Name = $"field{step.Fields.Count + 1}",
            Label = $"Field {step.Fields.Count + 1}",
        });
    }

    [RelayCommand]
    private void RemoveField(ConfigFlowField? field)
    {
        if (field is null)
        {
            return;
        }

        foreach (var step in Steps)
        {
            if (step.Fields.Remove(field))
            {
                return;
            }
        }
    }

    partial void OnFlowNameChanged(string value) => OnPropertyChanged(nameof(FlowClassName));

    /// <summary>The resx group every string for this flow lives under.</summary>
    private string StringsRoot => ResxKeyBuilder.Build("Setup", "Flows", CSharpCode.ToPascal(FlowName));

    /// <summary>Generates the config flow class, resx keys and the IConfigFlowProvider registration.</summary>
    [RelayCommand]
    private void Generate()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }

        if (Steps.Count == 0)
        {
            StatusText = "Add at least one step.";
            return;
        }

        foreach (var step in Steps)
        {
            if (!Core.Utils.MacroDeckRules.IsValidLocalId(step.StepId))
            {
                StatusText = $"Step id '{step.StepId}' must be lowercase kebab-case.";
                return;
            }
        }

        // Two steps with one id would dispatch to the same switch arm, and the second would be
        // unreachable - the compiler says CS8120 and the flow would silently lose a step.
        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in Steps)
        {
            if (!stepIds.Add(step.StepId))
            {
                StatusText = $"Step id '{step.StepId}' is used twice.";
                return;
            }
        }

        foreach (var step in Steps)
        {
            foreach (var field in step.Fields)
            {
                if (!ActionParameterTypes.IsKnown(field.EditorType))
                {
                    StatusText = $"Unknown editor type '{field.EditorType}'.";
                    return;
                }

                if (field.Name.Trim().Length == 0)
                {
                    StatusText = $"A field on step '{step.StepId}' has no name.";
                    return;
                }
            }
        }

        if (Steps.Any(s => s.Fields.Any(f => f.OnlyWhenParameter.Trim().Length > 0)))
        {
            // OnlyWhen names another field on the same step; a dangling name would never match.
            foreach (var step in Steps)
            {
                foreach (var field in step.Fields.Where(f => f.OnlyWhenParameter.Trim().Length > 0))
                {
                    if (!step.Fields.Any(f => f.Name.Trim() == field.OnlyWhenParameter.Trim()))
                    {
                        StatusText = $"'{field.Name}' on step '{step.StepId}' is only shown when '{field.OnlyWhenParameter}' is set, but no such field exists on that step.";
                        return;
                    }
                }
            }
        }

        var className = FlowClassName;
        var path = Path.Combine(ws.PluginProjectDirectory, className + ".cs");
        if (File.Exists(path))
        {
            StatusText = $"{className}.cs already exists - delete it first or change the flow name.";
            return;
        }

        try
        {
            var source = RenderFlow(ws.ProjectName, className);
            File.WriteAllText(path, source);

            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            var entries = BuildStringEntries();
            _resx.AddKeys(stringsPath, entries);

            var registered = RegisterInIntegration(ws, className);

            var permissionAdded = EnsureConfigPermission(ws);

            var notes = new List<string> { $"{entries.Count} resx keys" };
            notes.Add(registered ? "registered in PluginIntegration.cs" : "NOT registered - add the IConfigFlowProvider members by hand");
            if (permissionAdded)
            {
                notes.Add("host:config permission added");
            }

            StatusText = $"Generated {className}.cs: {Steps.Count} step(s), " + string.Join(", ", notes) + ".";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    private Dictionary<string, string> BuildStringEntries()
    {
        var root = StringsRoot;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ResxKeyBuilder.Build(root, "Title")] = FlowName,
        };

        foreach (var step in Steps)
        {
            // Steps and Fields get their own groups, so a step called "Title" or a field called
            // "Description" cannot collide with the group's own leaves.
            var stepKey = ResxKeyBuilder.Build(root, "Steps", step.StepId);
            entries[$"{stepKey}.Title"] = step.Title;
            if (!string.IsNullOrWhiteSpace(step.Description))
            {
                entries[$"{stepKey}.Description"] = step.Description;
            }

            var instructions = SplitLines(step.Instructions);
            for (var i = 0; i < instructions.Count; i++)
            {
                entries[$"{stepKey}.Instruction{i + 1}"] = instructions[i];
            }

            foreach (var link in SplitLinks(step.Links))
            {
                entries[$"{stepKey}.Links.{CSharpCode.ResxSegment(link.Key)}"] = link.Value;
            }

            foreach (var field in step.Fields)
            {
                var fieldKey = ResxKeyBuilder.Build(stepKey, "Fields", field.Name);
                entries[$"{fieldKey}.Label"] = field.Label;
                if (!string.IsNullOrWhiteSpace(field.Description))
                {
                    entries[$"{fieldKey}.Description"] = field.Description;
                }

                if (!string.IsNullOrWhiteSpace(field.Placeholder))
                {
                    entries[$"{fieldKey}.Placeholder"] = field.Placeholder;
                }
            }
        }

        return entries;
    }

    private static List<string> SplitLines(string? text) =>
        text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [];

    private static List<string> SplitList(string? text) =>
        text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [];

    private static Dictionary<string, string> SplitLinks(string? text)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(text))
        {
            var parts = line.Split('=', 2);
            var label = parts[0].Trim();
            var url = parts.Length > 1 ? parts[1].Trim() : label;
            if (label.Length > 0)
            {
                links[label] = url;
            }
        }

        return links;
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static IReadOnlyList<ActionParameterSpec> ParseNested(string? text) =>
        SplitLines(text).Select(line =>
        {
            var parts = line.Split('|');
            return new ActionParameterSpec
            {
                Name = parts[0].Trim(),
                EditorType = parts.Length > 1 && ActionParameterTypes.IsKnown(parts[1].Trim()) ? parts[1].Trim() : "Text",
                Label = parts.Length > 2 ? parts[2].Trim() : CSharpCode.ToPascal(parts[0]),
            };
        }).ToList();

    private static ActionParameterSpec? ParseNestedItem(string? text)
    {
        var all = ParseNested(text);
        return all.Count == 0 ? null : all[0];
    }

    private string RenderFlow(string projectName, string className)
    {
        var ns = CSharpCode.Identifier(CSharpCode.ToPascal(projectName));
        var root = StringsRoot;
        var anyValidation = Steps.Any(s => s.ValidateOnSubmit);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using MacroDeck.Localization;");
        sb.AppendLine("using MacroDeck.Sdk;");
        sb.AppendLine("using MacroDeck.Sdk.Actions;");
        sb.AppendLine("using MacroDeck.Sdk.ConfigFlow;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {CSharpCode.Xml(FlowName)} - generated by DeckForge's Setup Flow editor.");
        sb.AppendLine("/// The host creates one IConfigFlow per setup session.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public sealed class {className} : IConfigFlow");
        sb.AppendLine("{");
        sb.AppendLine("    public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)");
        sb.AppendLine($"        => Task.FromResult(ConfigFlowResult.Step({StepMethod(Steps[0])}()));");
        sb.AppendLine();
        sb.AppendLine("    // stepId is a plain string and the user can go back, so an earlier step than the one");
        sb.AppendLine("    // returned last can arrive here, and input holds only the values collected up to it.");
        sb.AppendLine("    // Dispatch on stepId and rebuild that step's state rather than tracking a position.");
        sb.AppendLine("    public async Task<ConfigFlowResult> SubmitAsync(");
        sb.AppendLine("        string stepId,");
        sb.AppendLine("        IReadOnlyDictionary<string, object?> input,");
        sb.AppendLine("        IConfigFlowContext context,");
        sb.AppendLine("        CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (stepId)");
        sb.AppendLine("        {");

        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            var method = StepMethod(step);
            sb.AppendLine($"            case {CSharpCode.StringLiteral(step.StepId)}:");
            sb.AppendLine("            {");

            if (step.ValidateOnSubmit)
            {
                sb.AppendLine($"                if (!await Validate{CSharpCode.ToPascal(step.StepId)}Async(input, cancellationToken))");
                sb.AppendLine("                {");
                sb.AppendLine($"                    return ConfigFlowResult.Error({method}(), {ResxKeyBuilder.Accessor(root, "Steps", step.StepId, "Error")});");
                sb.AppendLine("                }");
                sb.AppendLine();
            }

            var next = i + 1 < Steps.Count ? StepMethod(Steps[i + 1]) : null;
            sb.AppendLine(next is null
                ? $"                return {RenderCompletion(root)};"
                : $"                return ConfigFlowResult.Step({next}());");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("            default:");
        sb.AppendLine($"                return ConfigFlowResult.Error({StepMethod(Steps[0])}(), {ResxKeyBuilder.Accessor(root, "UnknownStep")});");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // One validation hook per step that asked for one, so nothing is ever called that does
        // not exist.
        foreach (var step in Steps.Where(s => s.ValidateOnSubmit))
        {
            var stepId = CSharpCode.ToPascal(step.StepId);
            var required = step.Fields.Where(f => f.Required).ToList();
            sb.AppendLine($"    // TODO: validate step '{step.StepId}'. Secret values arrive here already decrypted,");
            sb.AppendLine("    // so a connectivity check can use them directly. Return false to redisplay the");
            sb.AppendLine("    // step with the error below; that beats surfacing an integration issue later.");
            sb.AppendLine($"    private async Task<bool> Validate{stepId}Async(IReadOnlyDictionary<string, object?> input, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            foreach (var field in required)
            {
                sb.AppendLine($"        // required: {CSharpCode.StringLiteral(field.Name)} -> input[{CSharpCode.StringLiteral(field.Name)}]");
            }

            sb.AppendLine("        await Task.CompletedTask;");
            sb.AppendLine("        return true;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        foreach (var step in Steps)
        {
            AppendStepMethod(sb, step, root);
        }

        sb.AppendLine($"    private static ConfigFlowStep {StepMethod(Steps[0])}() => {StepMethod(Steps[0])}();");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string StepMethod(ConfigFlowStepSpec step) => $"Step{CSharpCode.ToPascal(step.StepId)}";

    private void AppendStepMethod(System.Text.StringBuilder sb, ConfigFlowStepSpec step, string root)
    {
        var stepId = CSharpCode.ToPascal(step.StepId);
        var stepKey = ResxKeyBuilder.Build(root, "Steps", step.StepId);

        sb.AppendLine($"    private static ConfigFlowStep Step{stepId}() => new()");
        sb.AppendLine("    {");
        sb.AppendLine($"        StepId = {CSharpCode.StringLiteral(step.StepId)},");
        sb.AppendLine($"        Title = {ResxKeyBuilder.Accessor(stepKey, "Title")},");
        if (!string.IsNullOrWhiteSpace(step.Description))
        {
            sb.AppendLine($"        Description = {ResxKeyBuilder.Accessor(stepKey, "Description")},");
        }

        var instructions = SplitLines(step.Instructions);
        if (instructions.Count > 0)
        {
            sb.AppendLine("        Instructions =");
            sb.AppendLine("        [");
            for (var i = 0; i < instructions.Count; i++)
            {
                sb.AppendLine($"            new() {{ Text = {ResxKeyBuilder.Accessor(stepKey, $"Instruction{i + 1}")} }},");
            }

            sb.AppendLine("        ],");
        }

        var links = SplitLinks(step.Links);
        if (links.Count > 0)
        {
            sb.AppendLine("        Links =");
            sb.AppendLine("        [");
            foreach (var link in links)
            {
                sb.AppendLine($"            new() {{ Label = {CSharpCode.StringLiteral(link.Key)}, Url = {CSharpCode.StringLiteral(link.Value)} }},");
            }

            sb.AppendLine("        ],");
        }

        sb.AppendLine("        Fields =");
        sb.AppendLine("        [");
        foreach (var field in step.Fields)
        {
            var spec = field.ToSpec();
            var fieldKey = ResxKeyBuilder.Build(stepKey, "Fields", field.Name);
            var call = ActionParameterFactory.Emit(spec, property => $"Strings.{fieldKey}.{property}()");

            if (!string.IsNullOrWhiteSpace(spec.OnlyWhenParameter))
            {
                call += $".OnlyWhen({CSharpCode.StringLiteral(spec.OnlyWhenParameter)}, {CSharpCode.StringLiteral(spec.OnlyWhenValue)})";
            }

            sb.AppendLine($"            {call},");
        }

        sb.AppendLine("        ],");
        sb.AppendLine("    };");
        sb.AppendLine();
    }

    private string RenderCompletion(string root)
    {
        // ConfigFlowResult.Complete takes a plain string title, not a LocalizedText, so the
        // display name is a literal here. The step titles above are LocalizedText and are.
        var title = CSharpCode.StringLiteral(FlowName);

        if (!IssueSecretOnComplete)
        {
            return $"ConfigFlowResult.Complete({title})";
        }

        var name = CSharpCode.StringLiteral(SecretValueName);
        return "ConfigFlowResult.Complete(" + "\n            " + title + ",\n            "
             + "new Dictionary<string, ConfigFlowValue>\n            {\n"
             + $"                [{name}] = ConfigFlowValue.Secret(input.GetValueOrDefault({name}) as string ?? string.Empty),\n"
             + "            })";
    }

    private static bool EnsureConfigPermission(WorkspaceContext ws)
    {
        var doc = Core.Plugins.ManifestDocument.Load(ws.ManifestPath);
        var current = doc.Permissions.ToList();
        if (current.Contains("host:config", StringComparer.Ordinal))
        {
            return false;
        }

        current.Add("host:config");
        doc.SetPermissions([.. current]);
        doc.Save(ws.ManifestPath);
        return true;
    }

    /// <summary>Adds the IConfigFlowProvider members. Returns false when the file could not be patched.</summary>
    /// <remarks>
    /// This had its own copies of all three patch steps, and the same faults: the using directive
    /// anchored on <c>IndexOf("using ")</c>, which matches inside the stock file's XML doc comment,
    /// and the interface demanded the exact stock class declaration, so a plugin that had already
    /// opted into another capability could not have a config flow added at all. It now uses
    /// <see cref="IntegrationPatcher"/> - the one implementation the test suite covers - and a
    /// patch that cannot be applied is reported rather than assumed.
    /// </remarks>
    private bool RegisterInIntegration(WorkspaceContext ws, string className)
    {
        var path = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
        if (!File.Exists(path))
        {
            return false;
        }

        var source = File.ReadAllText(path);

        var contract = IntegrationPatcher.AddInterface(source, "IConfigFlowProvider");
        if (contract.Outcome == PatchOutcome.AnchorMissing)
        {
            return false;
        }

        source = contract.Content;

        var directive = IntegrationPatcher.AddUsing(source, "MacroDeck.Sdk.ConfigFlow");
        if (directive.Outcome == PatchOutcome.AnchorMissing)
        {
            return false;
        }

        source = directive.Content;

        if (!source.Contains($"new {className}()", StringComparison.Ordinal))
        {
            var member = IntegrationPatcher.AddMember(
                source,
                $"public IConfigFlow CreateConfigFlow() => new {className}();");

            if (member.Outcome == PatchOutcome.AnchorMissing)
            {
                return false;
            }

            source = member.Content;
        }

        File.WriteAllText(path, source, new System.Text.UTF8Encoding(false));
        return true;
    }
}

/// <summary>
/// Shared base for the designer rows that project onto an <see cref="ActionParameterSpec"/>.
/// </summary>
/// <remarks>
/// Actions and Setup Flow fields expose the same capability surface, and they previously each
/// carried their own copy and drifted - one bound five of its eleven fields, the other none of
/// its range and options fields. The projection is the only part that differs between them.
/// </remarks>
public abstract class ParameterSpecBase : ObservableObject
{
    /// <summary>
    /// The selected editor type. Derived classes expose this as an observable property and
    /// forward it here, so the capability flags below follow a type change.
    /// </summary>
    protected abstract string EditorTypeValue { get; }

    public ActionParameterTypeInfo Info => ActionParameterTypes.Find(EditorTypeValue) ?? ActionParameterTypes.All[0];

    public bool ShowsRange => Info.SupportsRange;
    public bool ShowsOptions => Info.SupportsOptions;
    public bool ShowsOptionsSource => Info.SupportsOptionsSourceId;
    public bool ShowsFileExtensions => Info.SupportsFileExtensions;
    public bool ShowsLanguage => Info.SupportsLanguage;
    public bool ShowsValidation => Info.SupportsValidationRegex;
    public bool ShowsMaxLength => Info.SupportsMaxLength;
    public bool ShowsAutoPrefixHttps => Info.SupportsAutoPrefixHttps;
    public bool ShowsReset => Info.SupportsSupportsReset;
    public bool ShowsLiteralOnly => Info.SupportsLiteralOnly;
    public bool ShowsRequired => Info.SupportsRequired;
    public bool ShowsPlaceholder => Info.SupportsPlaceholder;
    public bool ShowsDescription => Info.SupportsDescription;
    public bool ShowsDefaultValue => Info.SupportsDefaultValue;
    public bool ShowsWidgetTargetOptions => Info.SupportsWidgetTargetOptions;
    public bool ShowsChildren => Info.SupportsChildren;
    public bool ShowsItemTemplate => Info.SupportsItemTemplate;

    /// <summary>Re-raises the capability flags after the editor type changes.</summary>
    protected void RefreshCapabilities()
    {
        foreach (var name in CapabilityNames)
        {
            OnPropertyChanged(name);
        }
    }

    private static readonly string[] CapabilityNames =
    [
        nameof(Info), nameof(ShowsRange), nameof(ShowsOptions), nameof(ShowsOptionsSource),
        nameof(ShowsFileExtensions), nameof(ShowsLanguage), nameof(ShowsValidation),
        nameof(ShowsMaxLength), nameof(ShowsAutoPrefixHttps), nameof(ShowsReset),
        nameof(ShowsLiteralOnly), nameof(ShowsRequired), nameof(ShowsPlaceholder),
        nameof(ShowsDescription), nameof(ShowsDefaultValue), nameof(ShowsWidgetTargetOptions),
        nameof(ShowsChildren), nameof(ShowsItemTemplate),
    ];

    protected static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    protected static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    protected static List<string> SplitLines(string? text) =>
        text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [];

    protected static List<string> SplitList(string? text) =>
        text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [];

    protected static IReadOnlyList<ActionParameterSpec> ParseNested(string? text) =>
        SplitLines(text).Select(line =>
        {
            var parts = line.Split('|');
            return new ActionParameterSpec
            {
                Name = parts[0].Trim(),
                EditorType = parts.Length > 1 && ActionParameterTypes.IsKnown(parts[1].Trim()) ? parts[1].Trim() : "Text",
                Label = parts.Length > 2 ? parts[2].Trim() : CSharpCode.ToPascal(parts[0]),
            };
        }).ToList();

    protected static ActionParameterSpec? ParseNestedItem(string? text)
    {
        var all = ParseNested(text);
        return all.Count == 0 ? null : all[0];
    }
}
