using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// The marker pair around the block the Events editor owns.
    /// </summary>
    /// <remarks>
    /// The editor used to bail out with "add further events by hand" as soon as
    /// <c>IEventProvider</c> appeared, so a second event was impossible and the parameters the
    /// user had just designed were silently thrown away. Owning a delimited region means the list
    /// can be re-rendered: the entry for the event being edited is replaced and a new one is
    /// appended, and every other entry - including hand-written ones - is left byte-identical.
    /// </remarks>
    private const string RegionStart = "// <deckforge:events>";

    private const string RegionEnd = "// </deckforge:events>";

    /// <summary>Generates or updates this event inside the IEventProvider implementation.</summary>
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

            // Idempotent patches, not a bail-out: the interface and usings may already be there
            // from a previous event or from the capability scaffolder.
            integration = AddUsing(integration, "MacroDeck.Sdk.Events", out var eventsUsing);
            integration = AddUsing(integration, "MacroDeck.Sdk.Actions", out var actionsUsing);
            integration = AddInterface(integration, "IEventProvider", out var interfaceAdded);

            if (!eventsUsing || !actionsUsing || !interfaceAdded)
            {
                StatusText =
                    "PluginIntegration.cs does not have the shape this editor patches (it needs a "
                    + "using block and a PluginIntegration class). Nothing was written; add the event "
                    + "by hand in PluginIntegration.cs under EventDefinitions.";
                return;
            }

            var region = RenderRegion();
            var outcome = ReplaceOrAppendRegion(integration, region, EventId);
            if (outcome is null)
            {
                StatusText =
                    "The generated events block is not in the shape this editor writes, so it was left "
                    + "alone rather than overwritten. Add the event by hand in "
                    + "PluginIntegration.cs (EventDefinitions).";
                return;
            }

            integration = outcome;
            File.WriteAllText(integrationPath, integration);

            // resx keys.
            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            var entries = new Dictionary<string, string>
            {
                [$"Events.{ToPascal(EventId)}.Name"] = EventName,
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logged as well as shown. The bare catch this replaced reduced every failure - a bad input, a
            // full disk, a bug in the generator - to one status line with no stack and no record, so a
            // product defect and a user's typo were indistinguishable from the outside and neither could be
            // chased afterwards.
            App.Logger.LogError(ex, "Generating the design failed.");
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Replaces this event's entry inside the owned region, or appends one.
    /// </summary>
    /// <returns>The patched source, or null when the region is not in the shape this editor writes.</returns>
    private string? ReplaceOrAppendRegion(string source, string region, string eventId)
    {
        var start = source.IndexOf(RegionStart, StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf(RegionEnd, start, StringComparison.Ordinal);

        if (start < 0 || end < 0)
        {
            // No region yet. Insert before the class's closing brace, which is the last one in the
            // file. A generated integration ends with "}\n" so this lands inside the class.
            var close = source.LastIndexOf("}", StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            return source.Insert(
                close,
                "\n\n    " + RegionStart + "\n" + region + RegionEnd + "\n");
        }

        var existing = source[(start + RegionStart.Length)..end];
        var entries = SplitEntries(existing);
        if (entries is null)
        {
            return null;
        }

        var rendered = RenderEntry(eventId);
        var replaced = false;
        var rebuilt = new System.Text.StringBuilder();
        foreach (var entry in entries)
        {
            if (!replaced && IdOf(entry) == eventId)
            {
                rebuilt.Append(rendered);
                replaced = true;
                continue;
            }

            rebuilt.Append(entry);
        }

        if (!replaced)
        {
            rebuilt.Append(rendered);
        }

        return source[..(start + RegionStart.Length)] + rebuilt + source[end..];
    }

    /// <summary>
    /// Splits the generated list into its <c>new() { ... }</c> entries.
    /// </summary>
    /// <returns>Null when the region has been hand-edited out of the shape this editor writes.</returns>
    private static List<string>? SplitEntries(string region)
    {
        var listStart = region.IndexOf('[');
        var listEnd = region.LastIndexOf(']');
        if (listStart < 0 || listEnd < listStart)
        {
            return null;
        }

        // A generated entry starts at "new()" and runs to its matching close brace. Anything else
        // in the list means it has been hand-edited into a shape this editor does not own, and
        // rewriting it would destroy work.
        var text = region[(listStart + 1)..listEnd];
        var entries = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var next = text.IndexOf("new()", index, StringComparison.Ordinal);
            if (next < 0)
            {
                break;
            }

            var open = text.IndexOf('{', next);
            if (open < 0)
            {
                return null;
            }

            var close = MatchingBrace(text, open);
            if (close < 0)
            {
                return null;
            }

            entries.Add(text[next..(close + 1)]);
            index = close + 1;
        }

        return entries.Count == 0 ? null : entries;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>The event id an entry declares, or null when it has none we recognise.</summary>
    private static string? IdOf(string entry)
    {
        const string marker = "Id = \"";
        var start = entry.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = entry.IndexOf('"', start);
        return end < 0 ? null : entry[start..end];
    }

    private string RenderRegion()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("    IReadOnlyList<EventDefinition> IEventProvider.EventDefinitions { get; } =");
        sb.AppendLine("    [");
        sb.Append(RenderEntry(EventId));
        sb.AppendLine("    ];");
        return sb.ToString();
    }

    private string RenderEntry(string eventId)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("        new()");
        sb.AppendLine("        {");
        sb.AppendLine($"            Id = \"{eventId}\",");
        sb.AppendLine($"            Name = Strings.Events.{ToPascal(eventId)}.Name(),");
        if (!string.IsNullOrWhiteSpace(EventDescription))
        {
            sb.AppendLine($"            Description = Strings.Events.{ToPascal(eventId)}.Description(),");
        }

        if (!string.IsNullOrWhiteSpace(EventCategory))
        {
            sb.AppendLine($"            Category = Strings.Events.{ToPascal(eventId)}.Category(),");
        }

        sb.Append(RenderParameterList("ConfigurationParameters", ConfigurationParameters, eventId));
        sb.Append(RenderParameterList("PayloadParameters", PayloadParameters, eventId));
        sb.AppendLine("        },");
        return sb.ToString();
    }

    private string RenderParameterList(
        string propertyName,
        ObservableCollection<EventParameterSpec> parameters,
        string eventId)
    {
        if (parameters.Count == 0)
        {
            return $"            {propertyName} = [],\n";
        }

        var sb = new System.Text.StringBuilder();

        // The event's own strings go under a .Name leaf, not on Events.<Pascal> directly.
        // Events.<Pascal> is a *group* once a parameter adds Events.<Pascal>.<Param>.Label, and a
        // resx cannot have a key and a group at the same path - which is exactly what the
        // generator's own MDLOC008 rule reports. Actions already uses the .Name form.
        var parameterKey = $"Events.{ToPascal(eventId)}";

        sb.AppendLine($"            {propertyName} =");
        sb.AppendLine("            [");
        foreach (var parameter in parameters)
        {
            var factoryArgs = new List<string> { $"\"{parameter.Name}\"" };

            // Duration takes a min and max in milliseconds, like Slider and Number. Leaving it out
            // meant a Duration parameter silently lost whatever range the user had set.
            if (parameter.EditorType is "Slider" or "Number" or "Duration")
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

            factoryArgs.Add($"label: Strings.{parameterKey}.{ToPascal(parameter.Name)}.Label()");
            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                factoryArgs.Add($"description: Strings.{parameterKey}.{ToPascal(parameter.Name)}.Description()");
            }

            if (!string.IsNullOrWhiteSpace(parameter.Placeholder))
            {
                factoryArgs.Add($"placeholder: Strings.{parameterKey}.{ToPascal(parameter.Name)}.Placeholder()");
            }

            if (parameter.Required)
            {
                factoryArgs.Add("required: true");
            }

            if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                // A duration's default is milliseconds, and the SDK parses it as a number. Quoting
                // it produced a default the host could not read.
                var defaultValue = parameter.EditorType is "Number" or "Slider" or "Duration"
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

    /// <summary>
    /// Adds a using directive, or reports why it could not.
    /// </summary>
    /// <remarks>
    /// This was a third copy of IntegrationPatcher, and the worst of the three: it anchored on
    /// <c>IndexOf("using ")</c>, which matches inside the template's XML doc comment - so on a
    /// file whose comment mentioned a using directive, the new import landed inside a comment and
    /// the code did not compile. It is now the one implementation, and its failures are reported
    /// instead of being assumed away.
    /// </remarks>
    private static string AddUsing(string source, string @namespace, out bool ok)
    {
        var patch = IntegrationPatcher.AddUsing(source, @namespace);
        ok = patch.Outcome != PatchOutcome.AnchorMissing;
        return patch.Content;
    }

    private static string AddInterface(string source, string interfaceName, out bool ok)
    {
        // The local version demanded the exact stock class declaration and threw otherwise, so
        // adding an event to a plugin that had already opted into a capability failed outright -
        // the base list was longer by then.
        var patch = IntegrationPatcher.AddInterface(source, interfaceName);
        ok = patch.Outcome != PatchOutcome.AnchorMissing;
        return patch.Content;
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
