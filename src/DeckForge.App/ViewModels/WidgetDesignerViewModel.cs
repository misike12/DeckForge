using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>One typed property in the JSON Schema builder.</summary>
public partial class SchemaProperty : ObservableObject
{
    [ObservableProperty]
    private string _name = "value";

    [ObservableProperty]
    private string _schemaType = "string";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private bool _required;

    [ObservableProperty]
    private string _defaultValue = "";

    public static IReadOnlyList<string> SchemaTypes { get; } =
        ["string", "number", "boolean", "array", "object"];
}

/// <summary>One button state (e.g. "on" / "off") offered to a ui.button node.</summary>
public partial class ButtonState : ObservableObject
{
    [ObservableProperty]
    private string _name = "on";

    [ObservableProperty]
    private string _background = "#4F8CFF";

    [ObservableProperty]
    private string _text = "On";
}

/// <summary>One event handler declared on a node (press, change, ...).</summary>
public partial class NodeEvent : ObservableObject
{
    [ObservableProperty]
    private string _eventName = "press";

    /// <summary>What the handler does: log, publish an event, or a TODO stub.</summary>
    [ObservableProperty]
    private string _handlerKind = "Log";

    [ObservableProperty]
    private string _detail = "";

    public static IReadOnlyList<string> EventNames { get; } =
        ["press", "long-press", "press-start", "press-end", "double-press", "change", "adjust", "tap"];

    public static IReadOnlyList<string> HandlerKinds { get; } = ["Log", "Publish event", "TODO"];
}

/// <summary>One node in the widget UI tree (v2: reorderable, events, states).</summary>
public partial class WidgetNode : ObservableObject
{
    [ObservableProperty]
    private string _nodeType = "ui.text";

    [ObservableProperty]
    private string _key = "node1";

    [ObservableProperty]
    private string _text = "Hello";

    /// <summary>For ui.text: relative size (0..1 of tile).</summary>
    [ObservableProperty]
    private double _size = 0.14;

    /// <summary>Background color for ui.button / container types.</summary>
    [ObservableProperty]
    private string _background = "#2C2C2E";

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Events this node declares (generated into the element's Events list).</summary>
    public ObservableCollection<NodeEvent> Events { get; } = [];

    /// <summary>For ui.button: named states; the generated view switches face by state.</summary>
    public ObservableCollection<ButtonState> States { get; } = [];

    public ObservableCollection<WidgetNode> Children { get; } = [];

    public static IReadOnlyList<string> NodeTypes { get; } =
    [
        "ui.text", "ui.button", "ui.stack", "ui.layer", "ui.icon", "ui.image",
        "ui.shape", "ui.slider", "ui.toggle", "ui.gauge", "ui.dial", "ui.chart",
        "ui.range-bar", "ui.segmented", "ui.grid", "ui.list", "ui.text-field",
        "macrodeck.dynamic-text", "macrodeck.progress-bar", "macrodeck.progress-text",
        "macrodeck.clock-dial",
    ];

    public string Describe => $"{NodeType.Split('.').Last()}  \"{Key}\"";
}

public partial class WidgetDesignerViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspaces;
    private readonly ResxMergerService _resx;

    public WidgetDesignerViewModel(WorkspaceManager workspaces, ResxMergerService resx)
    {
        _workspaces = workspaces;
        _resx = resx;
        Services.ShellMessenger.WorkspaceChanged += _ => Load();
    }

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private string _widgetTypeId = "gauge";

    [ObservableProperty]
    private string _widgetName = "Gauge";

    [ObservableProperty]
    private string _unit = "km/h";

    [ObservableProperty]
    private bool _hasConfiguration = true;

    [ObservableProperty]
    private bool _supportsFlows;

    [ObservableProperty]
    private WidgetNode? _selectedNode;

    [ObservableProperty]
    private SchemaProperty? _selectedSchemaProperty;

    [ObservableProperty]
    private string _previewHtml = "";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>False = widget surface region, true = config surface region.</summary>
    [ObservableProperty]
    private bool _editingConfigRegion;

    public ObservableCollection<WidgetNode> RootNodes { get; } = [];

    /// <summary>Widget-config region nodes (the configuration view's tree).</summary>
    public ObservableCollection<WidgetNode> ConfigNodes { get; } = [];

    public ObservableCollection<SchemaProperty> SchemaProperties { get; } = [];

    public static IReadOnlyList<string> RegionChoices { get; } = ["Widget", "Config"];

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    public void RefreshOnNavigate() => Load();

    private ObservableCollection<WidgetNode> ActiveRoots => EditingConfigRegion ? ConfigNodes : RootNodes;

    partial void OnEditingConfigRegionChanged(bool value) => UpdatePreview();

    [RelayCommand]
    private void AddNode(string nodeType)
    {
        var node = new WidgetNode
        {
            NodeType = nodeType,
            Key = $"{nodeType.Split('.').Last()}{ActiveRoots.Count + 1}",
            Text = nodeType.Contains("text", StringComparison.Ordinal) ? "Hello" : "",
        };
        if (SelectedNode is not null && SelectedNode.NodeType.Contains("stack", StringComparison.Ordinal))
        {
            SelectedNode.Children.Add(node);
        }
        else
        {
            ActiveRoots.Add(node);
        }
        SelectedNode = node;
        UpdatePreview();
    }

    [RelayCommand]
    private void RemoveNode(WidgetNode? node)
    {
        if (node is null)
        {
            return;
        }
        RemoveRecursive(RootNodes, node);
        RemoveRecursive(ConfigNodes, node);
        if (SelectedNode == node)
        {
            SelectedNode = null;
        }
        UpdatePreview();
    }

    private static void RemoveRecursive(ObservableCollection<WidgetNode> nodes, WidgetNode target)
    {
        if (nodes.Remove(target))
        {
            return;
        }
        foreach (var child in nodes)
        {
            RemoveRecursive(child.Children, target);
        }
    }

    [RelayCommand]
    private void MoveUp(WidgetNode? node) => Move(node, -1);

    [RelayCommand]
    private void MoveDown(WidgetNode? node) => Move(node, +1);

    private void Move(WidgetNode? node, int delta)
    {
        if (node is null)
        {
            return;
        }
        var list = FindList(RootNodes, node) ?? FindList(ConfigNodes, node);
        if (list is null)
        {
            return;
        }
        var index = list.IndexOf(node);
        var target = index + delta;
        if (target < 0 || target >= list.Count)
        {
            return;
        }
        list.Move(index, target);
        UpdatePreview();
    }

    private static ObservableCollection<WidgetNode>? FindList(ObservableCollection<WidgetNode> nodes, WidgetNode target)
    {
        if (nodes.Contains(target))
        {
            return nodes;
        }
        foreach (var child in nodes)
        {
            var found = FindList(child.Children, target);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    [RelayCommand]
    private void AddNodeEvent()
    {
        if (SelectedNode is null)
        {
            StatusText = "Select a node first, then add its events.";
            return;
        }
        SelectedNode.Events.Add(new NodeEvent
        {
            EventName = SelectedNode.NodeType == "ui.button" ? "press" : "change",
            Detail = $"{SelectedNode.Key} interacted",
        });
        UpdatePreview();
    }

    [RelayCommand]
    private void RemoveNodeEvent(NodeEvent? nodeEvent)
    {
        if (nodeEvent is not null && SelectedNode is not null)
        {
            SelectedNode.Events.Remove(nodeEvent);
        }
    }

    [RelayCommand]
    private void AddButtonState()
    {
        if (SelectedNode is null || SelectedNode.NodeType != "ui.button")
        {
            StatusText = "Button states apply to a selected ui.button node.";
            return;
        }
        SelectedNode.States.Add(new ButtonState
        {
            Name = $"state{SelectedNode.States.Count + 1}",
        });
    }

    [RelayCommand]
    private void RemoveButtonState(ButtonState? state)
    {
        if (state is not null && SelectedNode is not null)
        {
            SelectedNode.States.Remove(state);
        }
    }

    [RelayCommand]
    private void AddSchemaProperty()
    {
        var property = new SchemaProperty
        {
            Name = $"prop{SchemaProperties.Count + 1}",
        };
        SchemaProperties.Add(property);
        SelectedSchemaProperty = property;
    }

    [RelayCommand]
    private void RemoveSchemaProperty(SchemaProperty? property)
    {
        if (property is not null)
        {
            SchemaProperties.Remove(property);
        }
    }

    partial void OnSelectedNodeChanged(WidgetNode? value)
    {
        if (value is not null)
        {
            UpdatePreview();
        }
    }

    partial void OnUnitChanged(string value) => UpdatePreview();

    private void UpdatePreview()
    {
        var body = new StringBuilder();
        body.Append("<div style='display:flex;flex-direction:column;align-items:center;justify-content:center;height:100%;gap:6px;font-family:Segoe UI,sans-serif;color:#fff'>");
        var roots = EditingConfigRegion ? ConfigNodes : RootNodes;
        if (EditingConfigRegion && roots.Count == 0)
        {
            body.Append("<div style='opacity:.6;font-size:12px'>Config region - add nodes</div>");
        }
        foreach (var node in roots)
        {
            body.Append(RenderNodeHtml(node));
        }
        body.Append("</div>");
        PreviewHtml = Wrap(body.ToString());
    }

    private static string RenderNodeHtml(WidgetNode node) => node.NodeType switch
    {
        "ui.button" => $"<div style='background:{node.Background};border-radius:12px;padding:14px 22px;font-size:{node.Size * 700:.0f}%'>{System.Security.SecurityElement.Escape(node.Text)}</div>",
        "ui.text" => $"<div style='font-size:{node.Size * 700:.0f}%'>{System.Security.SecurityElement.Escape(node.Text)}</div>",
        "ui.shape" => "<div style='width:70%;height:6px;background:rgba(255,255,255,.35);border-radius:3px'></div>",
        "ui.gauge" => $"<div style='font-size:{node.Size * 500:.0f}%'>▶ 70</div>",
        "macrodeck.progress-bar" => "<div style='width:80%;height:8px;background:rgba(255,255,255,.2);border-radius:4px'><div style='width:60%;height:100%;background:#4F8CFF;border-radius:4px'></div></div>",
        _ => $"<div style='opacity:.8;font-size:12px'>{System.Security.SecurityElement.Escape(node.NodeType)}</div>",
    };

    /// <summary>Generates the widget-type provider file + resx keys.</summary>
    [RelayCommand]
    private void Generate()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }
        if (!Core.Utils.MacroDeckRules.IsValidLocalId(WidgetTypeId))
        {
            StatusText = "Widget type id must be lowercase kebab-case (it is persisted).";
            return;
        }

        try
        {
            var className = ToPascal(WidgetTypeId) + "WidgetProvider";
            var source = RenderProvider(ws.ProjectName, className);
            File.WriteAllText(Path.Combine(ws.PluginProjectDirectory, className + ".cs"), source);

            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            _resx.AddKeys(stringsPath, new Dictionary<string, string>
            {
                [$"Widgets.{ToPascal(WidgetTypeId)}.Name"] = WidgetName,
                [$"Widgets.{ToPascal(WidgetTypeId)}.Description"] = $"{WidgetName} widget generated by DeckForge.",
            });

            StatusText = $"Generated {className}.cs: IWidgetTypeProvider + IUiProvider, {CountNodes(RootNodes)} widget nodes, {CountNodes(ConfigNodes)} config nodes, {SchemaProperties.Count} schema properties. Build, then test the tile in Macro Deck's developer preview.";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    private static int CountNodes(ObservableCollection<WidgetNode> nodes) =>
        nodes.Count + nodes.Sum(n => CountNodes(n.Children));

    private string RenderProvider(string projectName, string className)
    {
        var schemaJson = RenderSchemaJson();

        // Non-interpolated raw template; tokens replaced below ($Name pattern).
        var template = """
            using System.Text.Json;
            using MacroDeck.Localization;
            using MacroDeck.Sdk;
            using MacroDeck.Sdk.Ui;
            using MacroDeck.Ui;
            using MacroDeck.Ui.Model;
            using Serilog;

            namespace $Namespace;

            /// <summary>
            /// $WidgetName widget - generated by DeckForge Widget Designer v2.
            /// Implements IWidgetTypeProvider (registration) and IUiProvider (drawing).
            /// </summary>
            public sealed class $ClassName : IWidgetTypeProvider, IUiProvider
            {
                private static readonly ILogger Logger = Log.ForContext<$ClassName>();
                private string? _widgetTypeId;

                public string ProviderName => "$WidgetName";

                public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
                {
                    var registration = context.RegisterWidgetTypeAsync(
                        new WidgetTypeDescriptor(
                            "$WidgetTypeId",
                            Strings.Widgets.$StringsKey.Name(),
                            Strings.Widgets.$StringsKey.Description(),
                            DefaultData: $DefaultData,
                            DataSchema: $DataSchema,
                            HasConfiguration: $HasConfiguration)
                        {
                            SupportsFlows = $SupportsFlows,
                        },
                        cancellationToken);
                    _widgetTypeId = registration.Result.WidgetTypeId;
                    return Task.CompletedTask;
                }

                public UiSurfaceDeclaration[] Surfaces { get; } =
                [
                    new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
                    new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
                    new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
                ];

                public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
                {
                    var surface = request.Surface;
                    UiElement? root = surface.Kind switch
                    {
                        UiSurfaceKinds.Widget or UiSurfaceKinds.Preview => BuildWidgetView(),
                        UiSurfaceKinds.Config => BuildConfigView(),
                        _ => null,
                    };
                    return Task.FromResult<IUiSession?>(root is null ? null : new ViewSession(new UiView(surface, root)));
                }

                /// <summary>Widget-surface tree, designed in DeckForge.</summary>
                private static UiElement? BuildWidgetView() => BuildTree(DesignerData.WidgetNodes);

                /// <summary>Config-surface tree (widget configuration view), designed in DeckForge.</summary>
                private static UiElement? BuildConfigView() => BuildTree(DesignerData.ConfigNodes);

                private static UiElement? BuildTree(IReadOnlyList<DesignedNode> designedNodes)
                {
                    if (designedNodes.Count == 0)
                    {
                        return null;
                    }

                    UiElement? root = null;
                    var byKey = new Dictionary<string, UiElement>();
                    foreach (var designed in designedNodes)
                    {
                        var element = ToElement(designed);
                        byKey[designed.Key] = element;
                        if (designed.ParentKey is null)
                        {
                            root = element;
                        }
                        else
                        {
                            switch (byKey[designed.ParentKey])
                            {
                                case UiButton button: button.Children.Add(element); break;
                                case UiStack stack: stack.Children.Add(element); break;
                                case UiLayer layer: layer.Children.Add(element); break;
                            }
                        }
                    }
                    return root;
                }

                private static UiElement ToElement(DesignedNode designed)
                {
                    UiElement element = designed.NodeType switch
                    {
                        "ui.button" => new UiButton { Key = designed.Key, Background = designed.Background ?? "#2C2C2E" },
                        "ui.text" => new UiTextRun { Key = designed.Key, Text = UiText.FromFixed(designed.Text), Size = designed.Size },
                        "ui.stack" => new UiStack { Key = designed.Key },
                        "ui.layer" => new UiLayer { Key = designed.Key },
                        _ => new UiTextRun { Key = designed.Key, Text = UiText.FromFixed(designed.Text) },
                    };

                    foreach (var handled in designed.Events)
                    {
                        AttachHandler(element, handled.EventName, handled.HandlerKind, designed.Key);
                    }
                    return element;
                }

                /// <summary>Declares one event; the lambda is a working stub - extend with real logic.</summary>
                private static void AttachHandler(UiElement element, string eventName, string handlerKind, string nodeKey)
                {
                    var name = eventName switch
                    {
                        "long-press" => UiComponentEvents.LongPress,
                        "press-start" => UiComponentEvents.PressStart,
                        "press-end" => UiComponentEvents.PressEnd,
                        "double-press" => UiComponentEvents.DoublePress,
                        "change" => UiComponentEvents.Change,
                        "adjust" => UiComponentEvents.Adjust,
                        "tap" => UiComponentEvents.Tap,
                        _ => UiComponentEvents.Press,
                    };

                    switch (handlerKind)
                    {
                        case "Log":
                            element.Events.Add(UiEventHandler.On(name, () =>
                                Logger.Information("{Key}: {Event}", nodeKey, eventName)));
                            break;
                        case "Publish event":
                            // TODO: replace the log with context.Events.Publish(eventName, payload) -
                            // keep a reference to IIntegrationContext.Events from InitializeAsync.
                            element.Events.Add(UiEventHandler.On(name, () =>
                                Logger.Information("{Key}: {Event} -> publish (wire up Events here)", nodeKey, eventName)));
                            break;
                        default:
                            // TODO: implement the real behavior for {nodeKey} / {eventName}.
                            element.Events.Add(UiEventHandler.On(name, () =>
                                Logger.Debug("{Key}: {Event} TODO", nodeKey, eventName)));
                            break;
                    }
                }
            }

            /// <summary>Adapter from UiView to the session contract.</summary>
            public sealed class ViewSession : IUiSession
            {
                private readonly UiView _view;

                public ViewSession(UiView view) => _view = view;

                public UiView View => _view;

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }

            /// <summary>Designer output: the flattened node list with events, serialized once.</summary>
            public static class DesignerData
            {
                public sealed record DesignedEvent(string EventName, string HandlerKind);

                public sealed record DesignedNode(
                    string Key,
                    string NodeType,
                    string Text,
                    double Size,
                    string? Background,
                    string? ParentKey,
                    DesignedEvent[] Events);

                public static readonly DesignedNode[] WidgetNodes =
                [
            $WidgetNodes
                ];

                public static readonly DesignedNode[] ConfigNodes =
                [
            $ConfigNodes
                ];
            }
            """;

        return template
            .Replace("$Namespace", projectName)
            .Replace("$ClassName", className)
            .Replace("$WidgetName", WidgetName)
            .Replace("$WidgetTypeId", WidgetTypeId)
            .Replace("$StringsKey", ToPascal(WidgetTypeId))
            .Replace("$DefaultData", Json(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["unit"] = Unit,
                ["value"] = 0,
            })))
            .Replace("$DataSchema", Json(schemaJson))
            .Replace("$HasConfiguration", HasConfiguration ? "true" : "false")
            .Replace("$SupportsFlows", SupportsFlows ? "true" : "false")
            .Replace("$WidgetNodes", RenderDesignedNodes(RootNodes))
            .Replace("$ConfigNodes", RenderDesignedNodes(ConfigNodes));
    }

    private string RenderDesignedNodes(ObservableCollection<WidgetNode> nodes, string? parentKey = null)
    {
        if (nodes.Count == 0)
        {
            return "                    // (none designed)";
        }
        var sb = new StringBuilder();
        foreach (var node in nodes)
        {
            var events = string.Join(", ", node.Events.Select(e =>
                $"new({CSharpString(e.EventName)}, {CSharpString(e.HandlerKind)})"));
            sb.AppendLine($"                    new({CSharpString(node.Key)}, {CSharpString(node.NodeType)}, {CSharpString(node.Text)}, {node.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {CSharpStringOrNull(node.Background)}, {CSharpStringOrNull(parentKey)}, [{events}]),");
            sb.Append(RenderDesignedNodes(node.Children, node.Key));
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private string RenderSchemaJson()
    {
        var properties = new Dictionary<string, object>();
        foreach (var property in SchemaProperties)
        {
            var entry = new Dictionary<string, object>
            {
                ["type"] = property.SchemaType,
            };
            if (!string.IsNullOrWhiteSpace(property.Title))
            {
                entry["title"] = property.Title;
            }
            if (!string.IsNullOrWhiteSpace(property.DefaultValue))
            {
                entry["default"] = property.SchemaType is "number" && double.TryParse(property.DefaultValue, out var number)
                    ? number
                    : property.DefaultValue;
            }
            properties[property.Name] = entry;
        }
        if (SupportsFlows && !properties.ContainsKey("flows"))
        {
            properties["flows"] = new Dictionary<string, object> { ["type"] = "array" };
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        var required = SchemaProperties.Where(p => p.Required).Select(p => p.Name).ToList();
        if (required.Count > 0)
        {
            schema["required"] = required;
        }
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CSharpString(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static string CSharpStringOrNull(string? value) => value is null ? "null" : CSharpString(value);

    private static string Json(string schema) => "@\"" + schema.Replace("\"", "\"\"") + "\"";

    private static string ToPascal(string kebab)
    {
        var parts = kebab.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string Wrap(string body) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><style>html,body{margin:0;height:100%;background:#1a1c22;overflow:hidden}</style></head><body>"
        + body + "</body></html>";
}
