using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>One row in the Events editor parameter designer (config or payload list).</summary>
public partial class EventParameterSpec : ObservableObject
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

    /// <summary>For Slider/Number: min;max;step.</summary>
    [ObservableProperty]
    private string _range = "0;100;1";

    /// <summary>For Choice/MultiSelect: one option per line "value=Label".</summary>
    [ObservableProperty]
    private string _options = "option1=First\noption2=Second";
}

public partial class EventsEditorViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly ResxMergerService _resx;

    public EventsEditorViewModel(WorkspaceManager workspaces, ResxMergerService resx)
    {
        _workspaces = workspaces;
        _resx = resx;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _eventId = "something-happened";

    [ObservableProperty]
    private string _eventName = "Something happened";

    [ObservableProperty]
    private string _eventDescription = "";

    [ObservableProperty]
    private string _eventCategory = "";

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<EventParameterSpec> ConfigurationParameters { get; } = [];

    public ObservableCollection<EventParameterSpec> PayloadParameters { get; } = [];

    public static IReadOnlyList<string> EditorTypes { get; } = ActionsEditorViewModel.EditorTypes;

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    /// <summary>Shell hook.</summary>
    public void RefreshOnNavigate() => Load();

    [RelayCommand]
    private void AddConfigurationParameter() => ConfigurationParameters.Add(NewParameter("config"));

    [RelayCommand]
    private void AddPayloadParameter() => PayloadParameters.Add(NewParameter("payload"));

    private EventParameterSpec NewParameter(string kind) => new()
    {
        Name = $"{kind}{ConfigurationParameters.Count + PayloadParameters.Count + 1}",
        Label = $"{char.ToUpperInvariant(kind[0]) + kind[1..]} parameter",
    };

    [RelayCommand]
    private void RemoveConfigurationParameter(EventParameterSpec? parameter)
    {
        if (parameter is not null)
        {
            ConfigurationParameters.Remove(parameter);
        }
    }

    [RelayCommand]
    private void RemovePayloadParameter(EventParameterSpec? parameter)
    {
        if (parameter is not null)
        {
            PayloadParameters.Remove(parameter);
        }
    }

    /// <summary>Generates the IEventProvider implementation + resx keys + registration.</summary>
    [RelayCommand]
    private void Generate()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        if (!Core.Utils.MacroDeckRules.IsValidLocalId(EventId))
        {
            StatusText = "Event id must be lowercase kebab-case (it is persisted in every trigger - choose carefully).";
            return;
        }

        try
        {
            // Events live on the integration itself (IEventProvider), matching the docs:
            // the provider declares EventDefinitions, and occurrences are published through
            // context.Events.Publish(id, payload).
            var integrationPath = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
            if (!File.Exists(integrationPath))
            {
                StatusText = "PluginIntegration.cs not found - is a plugin open?";
                return;
            }
            var integration = File.ReadAllText(integrationPath);
            if (integration.Contains("IEventProvider", StringComparison.Ordinal))
            {
                StatusText = "Events capability is already scaffolded - add further events by hand in PluginIntegration.cs (EventDefinitions).";
                return;
            }

            integration = AddUsing(integration, "using MacroDeck.Sdk.Events;");
            integration = AddUsing(integration, "using MacroDeck.Sdk.Actions;");
            integration = AddInterface(integration, "IEventProvider");
            integration = integration.Insert(integration.LastIndexOf('}'),
                "\n\n    // ----- Events capability (generated by DeckForge Events editor) -----\n\n"
                + RenderEventDefinitions() + "\n");

            File.WriteAllText(integrationPath, integration);

            // resx keys.
            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            var entries = new Dictionary<string, string>
            {
                [$"Events.{ToPascal(EventId)}"] = EventName,
            };
            if (!string.IsNullOrWhiteSpace(EventDescription))
            {
                entries[$"Events.{ToPascal(EventId)}.Description"] = EventDescription;
            }
            if (!string.IsNullOrWhiteSpace(EventCategory))
            {
                entries[$"Events.{ToPascal(EventId)}.Category"] = EventCategory;
            }
            foreach (var parameter in ConfigurationParameters.Concat(PayloadParameters))
            {
                entries[$"Events.{ToPascal(EventId)}.{ToPascal(parameter.Name)}.Label"] = parameter.Label;
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    entries[$"Events.{ToPascal(EventId)}.{ToPascal(parameter.Name)}.Description"] = parameter.Description;
                }
                if (!string.IsNullOrWhiteSpace(parameter.Placeholder))
                {
                    entries[$"Events.{ToPascal(EventId)}.{ToPascal(parameter.Name)}.Placeholder"] = parameter.Placeholder;
                }
            }
            _resx.AddKeys(stringsPath, entries);

            // Manifest permission for publishing events.
            AddPermission(ws, "events:publish");

            StatusText = $"Added event '{EventId}' to PluginIntegration.cs (IEventProvider), resx keys and events:publish permission. Publish it with context.Events.Publish(\"{EventId}\", payload).";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    private string RenderEventDefinitions()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("    public IReadOnlyList<EventDefinition> EventDefinitions { get; } =");
        sb.AppendLine("    [");
        sb.AppendLine("        new()");
        sb.AppendLine("        {");
        sb.AppendLine($"            Id = \"{EventId}\",");
        sb.AppendLine($"            Name = Strings.Events.{ToPascal(EventId)}(),");
        if (!string.IsNullOrWhiteSpace(EventDescription))
        {
            sb.AppendLine($"            Description = Strings.Events.{ToPascal(EventId)}.Description(),");
        }
        if (!string.IsNullOrWhiteSpace(EventCategory))
        {
            sb.AppendLine($"            Category = Strings.Events.{ToPascal(EventId)}.Category(),");
        }
        sb.Append(RenderParameterList("ConfigurationParameters", ConfigurationParameters));
        sb.Append(RenderParameterList("PayloadParameters", PayloadParameters));
        sb.AppendLine("        },");
        sb.AppendLine("    ];");
        return sb.ToString();
    }

    private string RenderParameterList(string propertyName, ObservableCollection<EventParameterSpec> parameters)
    {
        if (parameters.Count == 0)
        {
            return $"            {propertyName} = [],\n";
        }

        var sb = new System.Text.StringBuilder();
        var key = $"Events.{ToPascal(EventId)}";
        sb.AppendLine($"            {propertyName} =");
        sb.AppendLine("            [");
        foreach (var parameter in parameters)
        {
            var factoryArgs = new List<string> { $"\"{parameter.Name}\"" };
            if (parameter.EditorType is "Slider" or "Number")
            {
                var range = parameter.Range.Split(';');
                var min = range.Length > 0 ? range[0] : "0";
                var max = range.Length > 1 ? range[1] : "100";
                var step = range.Length > 2 ? range[2] : "1";
                factoryArgs.Add(min);
                factoryArgs.Add(max);
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
            factoryArgs.Add($"label: Strings.{key}.{ToPascal(parameter.Name)}.Label()");
            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                factoryArgs.Add($"description: Strings.{key}.{ToPascal(parameter.Name)}.Description()");
            }
            if (!string.IsNullOrWhiteSpace(parameter.Placeholder))
            {
                factoryArgs.Add($"placeholder: Strings.{key}.{ToPascal(parameter.Name)}.Placeholder()");
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
            sb.AppendLine($"                {factoryCall},");
        }
        sb.AppendLine("            ],");
        return sb.ToString();
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

    private static string AddInterface(string source, string interfaceName)
    {
        if (source.Contains(interfaceName, StringComparison.Ordinal))
        {
            return source;
        }
        var anchor = "public sealed class PluginIntegration : IPluginIntegration";
        if (!source.Contains(anchor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PluginIntegration.cs does not have the expected shape.");
        }
        return source.Replace(anchor, $"public sealed class PluginIntegration : IPluginIntegration, {interfaceName}");
    }

    private void AddPermission(WorkspaceContext ws, string permission)
    {
        var doc = Core.Plugins.ManifestDocument.Load(ws.ManifestPath);
        var current = doc.Permissions.ToList();
        if (!current.Contains(permission, StringComparer.Ordinal))
        {
            current.Add(permission);
            doc.SetPermissions([.. current]);
            doc.Save(ws.ManifestPath);
        }
    }

    private static string ToPascal(string kebab)
    {
        var parts = kebab.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
