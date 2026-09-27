using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>One row in the parameter designer.</summary>
public partial class ParameterSpec : ObservableObject
{
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

    /// <summary>For Slider/Number: min;max;step.</summary>
    [ObservableProperty]
    private string _range = "0;100;1";

    /// <summary>For Choice/MultiSelect: one option per line "value=Label".</summary>
    [ObservableProperty]
    private string _options = "option1=First\noption2=Second";
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

    public static IReadOnlyList<string> EditorTypes { get; } =
    [
        "Text", "MultilineText", "Number", "Slider", "Toggle", "Password", "Secret",
        "Choice", "DynamicChoice", "Autocomplete", "MultiSelect", "Color", "File",
        "Folder", "Hotkey", "Duration", "DateTime", "Json", "Code", "KeyValue",
        "Object", "Array", "IpAddress", "Url", "Icon", "Image", "KeyboardSequence",
        "KeyboardCombo", "WidgetTarget",
    ];

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
            StatusText = "Action id must be lowercase kebab-case (it is persisted - choose carefully).";
            return;
        }

        try
        {
            var className = ToPascal(ActionId) + "Action";
            var source = RenderAction(ws.ProjectName, className);
            var path = Path.Combine(ws.PluginProjectDirectory, className + ".cs");
            File.WriteAllText(path, source);

            // resx keys for name/description/labels.
            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            var entries = new Dictionary<string, string>
            {
                [$"Actions.{ToPascal(ActionId)}.Name"] = ActionName,
                [$"Actions.{ToPascal(ActionId)}.Description"] = ActionDescription,
            };
            foreach (var parameter in Parameters)
            {
                entries[$"Actions.{ToPascal(ActionId)}.{ToPascal(parameter.Name)}.Label"] = parameter.Label;
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    entries[$"Actions.{ToPascal(ActionId)}.{ToPascal(parameter.Name)}.Description"] = parameter.Description;
                }
                if (!string.IsNullOrWhiteSpace(parameter.Placeholder))
                {
                    entries[$"Actions.{ToPascal(ActionId)}.{ToPascal(parameter.Name)}.Placeholder"] = parameter.Placeholder;
                }
            }
            _resx.AddKeys(stringsPath, entries);

            RegisterInIntegration(ws, className);

            StatusText = $"Generated {className}.cs, resx keys and registration. Build & Run to try it.";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    private string RenderAction(string projectName, string className)
    {
        var ns = projectName;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using MacroDeck.Localization;");
        sb.AppendLine("using MacroDeck.Sdk;");
        sb.AppendLine("using MacroDeck.Sdk.Actions;");
        sb.AppendLine("using Serilog;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {EscapeXml(ActionName)} - generated by DeckForge Actions editor.");
        sb.AppendLine("/// Extend the executor below with the real work; the parameter plumbing is done.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public sealed class {className} : IActionDefinition");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly ILogger _logger;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}(ILogger logger) => _logger = logger.ForContext<{className}>();");
        sb.AppendLine();
        sb.AppendLine($"    public string Id => \"{ActionId}\";");
        sb.AppendLine();
        sb.AppendLine($"    public LocalizedText Name => Strings.Actions.{ToPascal(ActionId)}.Name();");
        sb.AppendLine();
        sb.AppendLine($"    public LocalizedText Description => Strings.Actions.{ToPascal(ActionId)}.Description();");
        sb.AppendLine();
        sb.AppendLine("    public IReadOnlyList<ActionParameter> Parameters { get; } =");
        sb.AppendLine("    [");
        foreach (var parameter in Parameters)
        {
            var key = $"Actions.{ToPascal(ActionId)}.{ToPascal(parameter.Name)}";
            var args = new List<string>
            {
                $"\"{parameter.Name}\"",
            };
            var factoryArgs = new List<string>();
            if (parameter.EditorType is "Slider" or "Number")
            {
                var range = parameter.Range.Split(';');
                var min = range.Length > 0 ? range[0] : "0";
                var max = range.Length > 1 ? range[1] : "100";
                var step = range.Length > 2 ? range[2] : "1";
                factoryArgs.Add($"{min}");
                factoryArgs.Add($"{max}");
                if (parameter.EditorType == "Slider")
                {
                    factoryArgs.Add(step);
                }
            }
            if (parameter.EditorType is "Choice" or "MultiSelect")
            {
                var options = parameter.Options.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line =>
                    {
                        var parts = line.Split('=', 2);
                        var value = parts[0].Trim();
                        var label = parts.Length > 1 ? parts[1].Trim() : value;
                        return $"new ActionParameterOption {{ Value = \"{value}\", Label = \"{label}\" }}";
                    });
                factoryArgs.Add($"[{string.Join(", ", options)}]");
            }
            if (parameter.EditorType is "Duration")
            {
                factoryArgs.Add("defaultMilliseconds: 5000");
            }
            factoryArgs.Add($"label: Strings.{key}.Label()");
            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                factoryArgs.Add($"description: Strings.{key}.Description()");
            }
            if (!string.IsNullOrWhiteSpace(parameter.Placeholder))
            {
                factoryArgs.Add($"placeholder: Strings.{key}.Placeholder()");
            }
            if (parameter.Required)
            {
                factoryArgs.Add("required: true");
            }
            if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                var defaultValue = parameter.EditorType is "Number" or "Slider"
                    ? parameter.DefaultValue
                    : $"\"{parameter.DefaultValue}\"";
                factoryArgs.Add($"defaultValue: {defaultValue}");
            }

            var factoryCall = parameter.EditorType switch
            {
                "Slider" => $"ActionParameter.Slider({string.Join(", ", factoryArgs)})",
                "Number" => $"ActionParameter.Number({string.Join(", ", factoryArgs)})",
                "Toggle" => $"ActionParameter.Toggle({string.Join(", ", factoryArgs)})",
                "Choice" => $"ActionParameter.Choice({string.Join(", ", factoryArgs)})",
                "MultiSelect" => $"ActionParameter.MultiSelect({string.Join(", ", factoryArgs)})",
                "MultilineText" => $"ActionParameter.MultilineText({string.Join(", ", factoryArgs)})",
                "Duration" => $"ActionParameter.Duration({string.Join(", ", factoryArgs)})",
                "Url" => $"ActionParameter.Url({string.Join(", ", factoryArgs)})",
                "IpAddress" => $"ActionParameter.IpAddress({string.Join(", ", factoryArgs)})",
                "Json" => $"ActionParameter.Json({string.Join(", ", factoryArgs)})",
                "Code" => $"ActionParameter.Code({string.Join(", ", factoryArgs)})",
                "Color" => $"ActionParameter.Color({string.Join(", ", factoryArgs)})",
                _ => $"ActionParameter.{parameter.EditorType}({string.Join(", ", factoryArgs)})",
            };

            if (!string.IsNullOrWhiteSpace(parameter.OnlyWhenParameter))
            {
                sb.AppendLine($"        {factoryCall}.OnlyWhen(\"{parameter.OnlyWhenParameter}\", \"{parameter.OnlyWhenValue}\"),");
            }
            else
            {
                sb.AppendLine($"        {factoryCall},");
            }
        }
        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public MacroDeckPlatform Platforms => MacroDeckPlatform.All;");
        sb.AppendLine();
        sb.AppendLine("    public IActionExecutor CreateExecutor() => new Executor(_logger);");
        sb.AppendLine();
        sb.AppendLine("    private sealed class Executor : IActionExecutor");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly ILogger _logger;");
        sb.AppendLine();
        sb.AppendLine("        public Executor(ILogger logger) => _logger = logger;");
        sb.AppendLine();
        sb.AppendLine("        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)");
        sb.AppendLine("        {");
        sb.AppendLine("            // TODO: implement the real work. Read parameters:");
        foreach (var parameter in Parameters.Take(3))
        {
            sb.AppendLine($"            // context.Parameters.TryGetValue(\"{parameter.Name}\", out var {parameter.Name});");
        }
        sb.AppendLine("            _logger.Information(\"Action " + ActionId + " executed\");");
        sb.AppendLine("            return ActionResult.SucceededTask;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void RegisterInIntegration(WorkspaceContext ws, string className)
    {
        var path = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
        if (!File.Exists(path))
        {
            return;
        }
        var source = File.ReadAllText(path);
        if (source.Contains(className, StringComparison.Ordinal))
        {
            return;
        }
        var anchor = "Actions = [new LogMessageAction(logger)];";
        if (source.Contains(anchor, StringComparison.Ordinal))
        {
            source = source.Replace(anchor, $"Actions = [new LogMessageAction(logger), new {className}(logger)];");
        }
        else
        {
            var actionsAnchor = "Actions = [";
            var idx = source.IndexOf(actionsAnchor, StringComparison.Ordinal);
            var close = source.IndexOf(']', idx);
            source = source.Insert(close, $", new {className}(logger)");
        }
        File.WriteAllText(path, source);
    }

    private static string ToPascal(string kebab)
    {
        var parts = kebab.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string EscapeXml(string s) => System.Security.SecurityElement.Escape(s) ?? s;
}
