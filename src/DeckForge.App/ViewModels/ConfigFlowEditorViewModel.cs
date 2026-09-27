using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>One form field on a config-flow step.</summary>
public partial class ConfigFlowField : ObservableObject
{
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

    /// <summary>For Choice: one option per line "value=Label".</summary>
    [ObservableProperty]
    private string _options = "option1=First\noption2=Second";
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

    /// <summary>Validate credentials/connectivity on submit (generates the TODO hook).</summary>
    [ObservableProperty]
    private bool _validateOnSubmit;

    /// <summary>On failure, redisplay this step with a localized error.</summary>
    [ObservableProperty]
    private bool _supportsBack;

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
    private string _flowName = "Setup";

    /// <summary>Flow class name (derived, read-only display).</summary>
    public string FlowClassName => ToPascal(FlowName).Trim() is { Length: > 0 } stem ? stem + "ConfigFlow" : "MyConfigFlow";

    /// <summary>When set, Complete() adds an OAuth-style secret the user never typed.</summary>
    [ObservableProperty]
    private bool _issueSecretOnComplete;

    [ObservableProperty]
    private string _secretValueName = "access_token";

    [ObservableProperty]
    private ConfigFlowStepSpec? _selectedStep;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<ConfigFlowStepSpec> Steps { get; } = [];

    public static IReadOnlyList<string> EditorTypes { get; } =
    [
        "Text", "MultilineText", "Number", "Toggle", "Password", "Secret",
        "Choice", "DynamicChoice", "Url", "IpAddress", "Json", "Code", "Color",
        "WidgetTarget",
    ];

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

    /// <summary>Generates the config flow class + resx keys + IConfigFlowProvider registration.</summary>
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
        if (Steps.Any(s => !Core.Utils.MacroDeckRules.IsValidLocalId(s.StepId)))
        {
            StatusText = "Every step id must be lowercase kebab-case.";
            return;
        }

        try
        {
            var className = FlowClassName;
            var source = RenderFlow(ws.ProjectName, className);
            File.WriteAllText(Path.Combine(ws.PluginProjectDirectory, className + ".cs"), source);

            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            var entries = new Dictionary<string, string>
            {
                [$"Setup.{ToPascal(FlowName)}.Title"] = FlowName,
            };
            foreach (var step in Steps)
            {
                var key = $"Setup.{ToPascal(FlowName)}.{ToPascal(step.StepId)}";
                entries[$"{key}.Title"] = step.Title;
                if (!string.IsNullOrWhiteSpace(step.Description))
                {
                    entries[$"{key}.Description"] = step.Description;
                }
                var instructionLines = step.Instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < instructionLines.Length; i++)
                {
                    entries[$"{key}.Instruction{i + 1}"] = instructionLines[i].Trim();
                }
                foreach (var field in step.Fields)
                {
                    entries[$"{key}.{ToPascal(field.Name)}.Label"] = field.Label;
                    if (!string.IsNullOrWhiteSpace(field.Description))
                    {
                        entries[$"{key}.{ToPascal(field.Name)}.Description"] = field.Description;
                    }
                    if (!string.IsNullOrWhiteSpace(field.Placeholder))
                    {
                        entries[$"{key}.{ToPascal(field.Name)}.Placeholder"] = field.Placeholder;
                    }
                }
            }
            entries[$"Setup.{ToPascal(FlowName)}.CannotConnect"] = "Could not connect - check the values and try again.";
            entries[$"Setup.{ToPascal(FlowName)}.EntryTitle"] = FlowName;
            _resx.AddKeys(stringsPath, entries);

            RegisterInIntegration(ws, className);

            // Config-flow permission.
            var doc = Core.Plugins.ManifestDocument.Load(ws.ManifestPath);
            var current = doc.Permissions.ToList();
            if (!current.Contains("host:config", StringComparer.Ordinal))
            {
                current.Add("host:config");
                doc.SetPermissions([.. current]);
                doc.Save(ws.ManifestPath);
            }

            StatusText = $"Generated {className}.cs (IConfigFlow), resx keys, host:config permission and registration. Test it from the integration page in Macro Deck or the stub host.";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    private string RenderFlow(string projectName, string className)
    {
        var stem = ToPascal(FlowName);
        var template = """
            using MacroDeck.Localization;
            using MacroDeck.Sdk;
            using MacroDeck.Sdk.Actions;
            using MacroDeck.Sdk.ConfigFlow;

            namespace $Namespace;

            /// <summary>
            /// $FlowName setup flow - generated by DeckForge Config Flow editor.
            /// One IConfigFlow per setup session; the host calls CreateConfigFlow() each time.
            /// </summary>
            public sealed class $ClassName : IConfigFlow
            {
                public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
                    => Task.FromResult(ConfigFlowResult.Step($FirstStepMethod()));

                public async Task<ConfigFlowResult> SubmitAsync(
                    string stepId,
                    IReadOnlyDictionary<string, object?> input,
                    IConfigFlowContext context,
                    CancellationToken cancellationToken)
                {
            $SwitchBody
                    return ConfigFlowResult.Error(FirstStep(), Strings.Setup.$StringsStem.CannotConnect());
                }

            $StepMethods
                private static ConfigFlowStep FirstStep() => $FirstStepMethod();
            }
            """;

        // Per-step methods and the submit switch.
        var stepMethods = new System.Text.StringBuilder();
        var switchBody = new System.Text.StringBuilder();
        var first = Steps[0];

        foreach (var step in Steps)
        {
            var method = $"Step{ToPascal(step.StepId)}";
            stepMethods.AppendLine($"    private static ConfigFlowStep {method}() => new()");
            stepMethods.AppendLine("    {");
            stepMethods.AppendLine($"        StepId = \"{step.StepId}\",");
            stepMethods.AppendLine($"        Title = Strings.Setup.{stem}.{ToPascal(step.StepId)}.Title(),");
            if (!string.IsNullOrWhiteSpace(step.Description))
            {
                stepMethods.AppendLine($"        Description = Strings.Setup.{stem}.{ToPascal(step.StepId)}.Description(),");
            }
            var instructionLines = step.Instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (instructionLines.Length > 0)
            {
                stepMethods.AppendLine("        Instructions =");
                stepMethods.AppendLine("        [");
                for (var i = 0; i < instructionLines.Length; i++)
                {
                    stepMethods.AppendLine($"            new ConfigFlowInstruction {{ Text = Strings.Setup.{stem}.{ToPascal(step.StepId)}.Instruction{i + 1}() }},");
                }
                stepMethods.AppendLine("        ],");
            }
            var links = step.Links.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var parts = line.Split('=', 2);
                    var label = parts[0].Trim();
                    var url = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();
                    return $"new ConfigFlowLink {{ Label = \"{label}\", Url = \"{url}\" }}";
                })
                .ToList();
            if (links.Count > 0)
            {
                stepMethods.AppendLine("        Links =");
                stepMethods.AppendLine("        [");
                foreach (var link in links)
                {
                    stepMethods.AppendLine($"            {link},");
                }
                stepMethods.AppendLine("        ],");
            }
            stepMethods.AppendLine("        Fields =");
            stepMethods.AppendLine("        [");
            foreach (var field in step.Fields)
            {
                var key = $"Strings.Setup.{stem}.{ToPascal(step.StepId)}.{ToPascal(field.Name)}";
                var factoryArgs = new List<string> { $"\"{field.Name}\"" };
                if (field.EditorType is "Choice" or "DynamicChoice")
                {
                    var options = field.Options.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line =>
                        {
                            var parts = line.Split('=', 2);
                            var value = parts[0].Trim();
                            var label = parts.Length > 1 ? parts[1].Trim() : value;
                            return $"new ActionParameterOption {{ Value = \"{value}\", Label = \"{label}\" }}";
                        });
                    factoryArgs.Add($"[{string.Join(", ", options)}]");
                }
                factoryArgs.Add($"label: {key}.Label()");
                if (!string.IsNullOrWhiteSpace(field.Description))
                {
                    factoryArgs.Add($"description: {key}.Description()");
                }
                if (!string.IsNullOrWhiteSpace(field.Placeholder))
                {
                    factoryArgs.Add($"placeholder: {key}.Placeholder()");
                }
                if (field.Required)
                {
                    factoryArgs.Add("required: true");
                }
                if (!string.IsNullOrWhiteSpace(field.DefaultValue))
                {
                    factoryArgs.Add($"defaultValue: \"{field.DefaultValue}\"");
                }

                var factoryCall = field.EditorType switch
                {
                    "Toggle" => $"ActionParameter.Toggle({string.Join(", ", factoryArgs)})",
                    "Choice" => $"ActionParameter.Choice({string.Join(", ", factoryArgs)})",
                    "DynamicChoice" => $"ActionParameter.DynamicChoice({string.Join(", ", factoryArgs)})",
                    "MultilineText" => $"ActionParameter.MultilineText({string.Join(", ", factoryArgs)})",
                    "Url" => $"ActionParameter.Url({string.Join(", ", factoryArgs)})",
                    "IpAddress" => $"ActionParameter.IpAddress({string.Join(", ", factoryArgs)})",
                    "Json" => $"ActionParameter.Json({string.Join(", ", factoryArgs)})",
                    "Code" => $"ActionParameter.Code({string.Join(", ", factoryArgs)})",
                    "Color" => $"ActionParameter.Color({string.Join(", ", factoryArgs)})",
                    "Number" => $"ActionParameter.Number({string.Join(", ", factoryArgs)})",
                    "Secret" => $"ActionParameter.Secret({string.Join(", ", factoryArgs)})",
                    "Password" => $"ActionParameter.Password({string.Join(", ", factoryArgs)})",
                    "WidgetTarget" => $"ActionParameter.WidgetTarget({string.Join(", ", factoryArgs)})",
                    _ => $"ActionParameter.Text({string.Join(", ", factoryArgs)})",
                };

                if (!string.IsNullOrWhiteSpace(field.OnlyWhenParameter))
                {
                    stepMethods.AppendLine($"                {factoryCall}.OnlyWhen(\"{field.OnlyWhenParameter}\", \"{field.OnlyWhenValue}\"),");
                }
                else
                {
                    stepMethods.AppendLine($"                {factoryCall},");
                }
            }
            stepMethods.AppendLine("        };");
            stepMethods.AppendLine();

            var validation = step.ValidateOnSubmit
                ? """
                          // TODO: validate the values of step "$StepId" (input["<field>"] as string).
                          // Expected failures are Error, not exceptions - a field error now beats an
                          // integration issue later. Values shown here are already decrypted for secrets.
                          if (!await ValidateAsync(input, cancellationToken))
                          {
                              return ConfigFlowResult.Error($Method(), Strings.Setup.$Stem.CannotConnect());
                          }

                  """.Replace("$StepId", step.StepId).Replace("$Method", method).Replace("$Stem", stem)
                : "";
            var nextStep = Steps.IndexOf(step) + 1 < Steps.Count
                ? $"Step{ToPascal(Steps[Steps.IndexOf(step) + 1].StepId)}()"
                : "";
            var completion = nextStep.Length > 0
                ? $"return ConfigFlowResult.Step({nextStep});"
                : RenderCompletion();
            switchBody.AppendLine($"        \"{step.StepId}\" =>");
            switchBody.AppendLine("        {");
            switchBody.Append(validation);
            switchBody.AppendLine($"            {completion}");
            switchBody.AppendLine("        }");
            switchBody.AppendLine();
        }

        return template
            .Replace("$Namespace", projectName)
            .Replace("$ClassName", className)
            .Replace("$FlowName", FlowName)
            .Replace("$StringsStem", stem)
            .Replace("$FirstStepMethod()", $"Step{ToPascal(first.StepId)}()")
            .Replace("$SwitchBody", switchBody.ToString())
            .Replace("$StepMethods", stepMethods.ToString());
    }

    private string RenderCompletion()
    {
        if (!IssueSecretOnComplete)
        {
            return $"return ConfigFlowResult.Complete(Strings.Setup.{ToPascal(FlowName)}.EntryTitle);";
        }
        return """
                        return ConfigFlowResult.Complete(
                            Strings.Setup.$Stem.EntryTitle,
                            new Dictionary<string, ConfigFlowValue>
                            {
                                ["$Secret"] = ConfigFlowValue.Secret(
                                    input.GetValueOrDefault("$Secret") as string ?? string.Empty),
                            });
                """.Replace("$Stem", ToPascal(FlowName)).Replace("$Secret", SecretValueName);
    }

    private void RegisterInIntegration(WorkspaceContext ws, string className)
    {
        var path = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
        if (!File.Exists(path))
        {
            return;
        }
        var source = File.ReadAllText(path);

        if (!source.Contains("IConfigFlowProvider", StringComparison.Ordinal))
        {
            var anchor = "public sealed class PluginIntegration : IPluginIntegration";
            if (source.Contains(anchor, StringComparison.Ordinal))
            {
                source = source.Replace(anchor, "public sealed class PluginIntegration : IPluginIntegration, IConfigFlowProvider");
                source = AddUsing(source, "using MacroDeck.Sdk.ConfigFlow;");
            }
        }

        if (!source.Contains(className, StringComparison.Ordinal))
        {
            var anchor = "public sealed class PluginIntegration : IPluginIntegration";
            if (!source.Contains(anchor, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PluginIntegration.cs does not have the expected shape.");
            }
            // Add the CreateConfigFlow member right after the class opening.
            var idx = source.IndexOf(anchor, StringComparison.Ordinal);
            var brace = source.IndexOf('{', idx);
            source = source.Insert(brace + 1,
                $"\n\n    // ----- Setup flow (generated by DeckForge Config Flow editor) -----\n\n    public IConfigFlow CreateConfigFlow() => new {className}();\n");
        }

        File.WriteAllText(path, source);
    }

    private static string AddUsing(string source, string usingLine)
    {
        if (source.Contains(usingLine, StringComparison.Ordinal))
        {
            return source;
        }
        var idx = source.IndexOf("using ", StringComparison.Ordinal);
        var insertAt = idx < 0 ? 0 : idx;
        return source.Insert(insertAt, usingLine + "\n");
    }

    private static string ToPascal(string kebab)
    {
        var parts = kebab.Split('-', ' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
