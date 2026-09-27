using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.App.Services;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Code;
using DeckForge.Core.Widgets;
using DeckForge.Core.Workspace;

namespace DeckForge.App.ViewModels;

/// <summary>One property of a widget type's stored data.</summary>
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

    /// <summary>The five JSON Schema types a widget payload can use.</summary>
    public static IReadOnlyList<string> SchemaTypes { get; } = ["string", "number", "boolean", "array", "object"];

    public DesignedSchemaProperty ToModel() => new()
    {
        Name = Name.Trim(),
        SchemaType = SchemaTypes.Contains(SchemaType, StringComparer.OrdinalIgnoreCase)
            ? SchemaType.ToLowerInvariant()
            : "string",
        Title = Title,
        Required = Required,
        DefaultValue = DefaultValue,
    };
}

/// <summary>One named appearance a widget type offers.</summary>
public partial class ButtonState : ObservableObject
{
    [ObservableProperty]
    private string _name = "on";

    [ObservableProperty]
    private string _background = "#4F8CFF";

    [ObservableProperty]
    private string _text = "On";
}

/// <summary>One event handler on a designed node.</summary>
public partial class NodeEvent : ObservableObject
{
    [ObservableProperty]
    private string _eventName = "press";

    /// <summary>
    /// The handler body, as the author wants it. It is emitted verbatim into the generated
    /// session, so it is code, not a choice from a list.
    /// </summary>
    [ObservableProperty]
    private string _detail = "_logger.Information(\"handled\");";

    [ObservableProperty]
    private bool _isAsync;

    /// <summary>Every event name the component profile declares.</summary>
    public static IReadOnlyList<string> EventNames => UiNodeCatalog.AllEvents;
}

/// <summary>One node in the designed tree.</summary>
public partial class WidgetNode : ObservableObject
{
    [ObservableProperty]
    private string _nodeType = "ui.text";

    [ObservableProperty]
    private string _key = "node1";

    [ObservableProperty]
    private string _text = "Hello";

    [ObservableProperty]
    private double _size = 0.14;

    [ObservableProperty]
    private string _background = "#2C2C2E";

    /// <summary>Property values keyed by the catalog's property name.</summary>
    public ObservableCollection<PropertyValue> Properties { get; } = [];

    public ObservableCollection<NodeEvent> Events { get; } = [];

    public ObservableCollection<WidgetNode> Children { get; } = [];

    public ObservableCollection<ButtonState> States { get; } = [];

    /// <summary>The node this one hangs from, or null when it is a root.</summary>
    public WidgetNode? Parent { get; internal set; }

    /// <summary>The catalog entry for the selected type.</summary>
    public UiNodeTypeInfo Info => UiNodeCatalog.Find(NodeType) ?? UiNodeCatalog.All[1];

    /// <summary>True when the selected type accepts children.</summary>
    public bool IsContainer => Info.IsContainer;

    /// <summary>True when the selected type is one the reader derives from a reference.</summary>
    public bool IsReaderDerived => NodeType.StartsWith("macrodeck.", StringComparison.Ordinal);

    /// <summary>Every node type, all twenty-four.</summary>
    public static IReadOnlyList<UiNodeTypeInfo> NodeTypes => UiNodeCatalog.All;

    /// <summary>The events the selected type advertises, so the designer can offer them.</summary>
    public IReadOnlyList<string> OfferableEvents => Info.OfferableEvents;

    /// <summary>The property rows for the selected type, rebuilt when the type changes.</summary>
    public IReadOnlyList<UiPropertyDescriptor> AvailableProperties => Info.Properties;

    public string Describe => $"{Info.DisplayName}  \"{Key}\"";

    partial void OnNodeTypeChanged(string value)
    {
        OnPropertyChanged(nameof(Info));
        OnPropertyChanged(nameof(IsContainer));
        OnPropertyChanged(nameof(IsReaderDerived));
        OnPropertyChanged(nameof(OfferableEvents));
        OnPropertyChanged(nameof(AvailableProperties));
        SyncProperties();
    }

    partial void OnKeyChanged(string value) => OnPropertyChanged(nameof(Describe));

    /// <summary>Rebuilds the property rows so they always match the selected type.</summary>
    public void SyncProperties()
    {
        var wanted = Info.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var existing in Properties.Where(p => !wanted.Contains(p.PropertyName)).ToList())
        {
            Properties.Remove(existing);
        }

        foreach (var descriptor in Info.Properties)
        {
            if (Properties.All(p => p.PropertyName != descriptor.Name))
            {
                Properties.Add(new PropertyValue { PropertyName = descriptor.Name, Value = DefaultFor(descriptor) });
            }
        }

        OnPropertyChanged(nameof(AvailableProperties));
    }

    private static string DefaultFor(UiPropertyDescriptor descriptor) => descriptor.Kind switch
    {
        UiPropertyKind.Flag => "false",
        UiPropertyKind.Number or UiPropertyKind.ValueNumber => "0",
        _ => string.Empty,
    };

    public DesignedNode ToModel(string? parentKey) => new()
    {
        Key = Key.Trim(),
        NodeType = NodeType,
        ParentKey = parentKey,
        Text = Text,
        Size = Size,
        Background = Background,
        Properties = Properties
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.PropertyName, p => p.Value, StringComparer.Ordinal),
        Events = Events.Select(e => new DesignedEvent
        {
            Name = e.EventName,
            Body = e.Detail,
            IsAsync = e.IsAsync,
        }).ToList(),
        Children = Children.Select(c => c.ToModel(Key.Trim())).ToList(),
    };
}

/// <summary>One property row: a name from the catalog and the value the designer gave it.</summary>
public partial class PropertyValue : ObservableObject
{
    [ObservableProperty]
    private string _propertyName = "";

    [ObservableProperty]
    private string _value = "";
}

/// <summary>
/// The widget designer. Collects a design and hands it to <see cref="WidgetGenerator"/>.
/// </summary>
/// <remarks>
/// The rendering used to live here, which is why none of it could be tested: the project it
/// generates for is a WPF application and this project's test project is not. Six defects
/// survived in it for exactly that reason - five wrong namespaces, a call to a method that does
/// not exist, two <c>Add</c> calls on init-only properties, a property typed as an array where
/// the interface demands a list, and a session adapter implementing none of its interface.
/// </remarks>
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
    private string _widgetDescription = "";

    [ObservableProperty]
    private string _providerName = "";

    [ObservableProperty]
    private string _unit = "km/h";

    [ObservableProperty]
    private bool _hasConfiguration = true;

    [ObservableProperty]
    private bool _supportsFlows;

    [ObservableProperty]
    private WidgetNode? _selectedNode;

    [ObservableProperty]
    private string _previewHtml = "";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Whether the config surface is being edited rather than the widget face.</summary>
    [ObservableProperty]
    private bool _editingConfigRegion;

    public ObservableCollection<WidgetNode> RootNodes { get; } = [];

    public ObservableCollection<SchemaProperty> SchemaProperties { get; } = [];

    public ObservableCollection<ButtonState> States { get; } = [];

    /// <summary>Every node type, for the palette.</summary>
    public static IReadOnlyList<UiNodeTypeInfo> NodeTypes => UiNodeCatalog.All;

    /// <summary>Every event name, for the handler editor.</summary>
    public static IReadOnlyList<string> EventNames => UiNodeCatalog.AllEvents;

    /// <summary>The JSON Schema types a payload can use.</summary>
    public static IReadOnlyList<string> SchemaTypes => SchemaProperty.SchemaTypes;

    /// <summary>Which region the surface is being edited in.</summary>
    public static IReadOnlyList<string> RegionChoices { get; } = ["Widget", "Config"];

    // Instance mirrors of the statics above. XAML binds these through
    // {Binding DataContext.X, RelativeSource={RelativeSource AncestorType=Page}}, and a binding
    // path resolves against an instance - a static property is invisible to it, which is why the
    // three ComboBoxes that used them were permanently empty.

    /// <summary>
    /// The events the selected node advertises. A handler for an event the node does not
    /// declare is never dispatched, so the picker must offer only its own.
    /// </summary>
    public IReadOnlyList<string> OfferableEvents => SelectedNode?.OfferableEvents ?? UiNodeCatalog.AllEvents;

    /// <summary>Alias for <see cref="RootNodes"/>, which is what the tree binds to.</summary>
    public ObservableCollection<WidgetNode> ConfigNodes => RootNodes;

    /// <summary>Properties of the selected node's type, for the property grid.</summary>
    public IReadOnlyList<UiPropertyDescriptor> AvailableProperties =>
        SelectedNode?.AvailableProperties ?? [];

    /// <summary>The selected node's property rows.</summary>
    public ObservableCollection<PropertyValue> SelectedNodeProperties =>
        SelectedNode?.Properties ?? [];

    partial void OnSelectedNodeChanged(WidgetNode? value)
    {
        value?.SyncProperties();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(OfferableEvents));
        OnPropertyChanged(nameof(AvailableProperties));
        OnPropertyChanged(nameof(SelectedNodeProperties));
        UpdatePreview();
    }

    public void Load() => HasWorkspace = _workspaces.Current is not null;

    public void RefreshOnNavigate() => Load();

    partial void OnHasConfigurationChanged(bool value) => OnPropertyChanged(nameof(ShowsSchemaEditor));

    /// <summary>True when the schema editor is meaningful, which is whenever configuration is on.</summary>
    public bool ShowsSchemaEditor => HasConfiguration;

    // ------------------------------------------------------------------ tree editing

    [RelayCommand]
    private void AddNode(string? nodeType)
    {
        var type = UiNodeCatalog.Find(nodeType) ?? UiNodeCatalog.All[1];
        var node = new WidgetNode
        {
            NodeType = type.WireType,
            Key = NextKey(type.WireType),
        };

        node.SyncProperties();

        // A node nests under the selection when the selection can hold it. Previously the rule was
        // "the type name contains stack", which made ui.layer children impossible even though the
        // generated tree handled layer parenting.
        var parent = SelectedNode;
        if (parent is not null && parent.IsContainer)
        {
            parent.Children.Add(node);
            node.Parent = parent;
        }
        else
        {
            RootNodes.Add(node);
        }

        SelectedNode = node;
        UpdatePreview();
    }

    private string NextKey(string wireType)
    {
        var baseName = wireType.Split('.').Last();
        var candidate = baseName;
        var index = 1;
        while (AllNodes().Any(n => n.Key == candidate))
        {
            candidate = baseName + (++index);
        }

        return candidate;
    }

    private IEnumerable<WidgetNode> AllNodes()
    {
        foreach (var node in RootNodes)
        {
            yield return node;
            foreach (var child in Descendants(node))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<WidgetNode> Descendants(WidgetNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    [RelayCommand]
    private void RemoveNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var target = SelectedNode;
        if (target.Parent is { } parent)
        {
            parent.Children.Remove(target);
            target.Parent = null;
        }
        else
        {
            RootNodes.Remove(target);
        }

        SelectedNode = target.Parent;
        UpdatePreview();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var siblings = SelectedNode.Parent?.Children ?? RootNodes;
        var index = siblings.IndexOf(SelectedNode);
        if (index > 0)
        {
            siblings.Move(index, index - 1);
            UpdatePreview();
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var siblings = SelectedNode.Parent?.Children ?? RootNodes;
        var index = siblings.IndexOf(SelectedNode);
        if (index >= 0 && index < siblings.Count - 1)
        {
            siblings.Move(index, index + 1);
            UpdatePreview();
        }
    }

    [RelayCommand]
    private void AddNodeEvent()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var names = SelectedNode.OfferableEvents;
        if (names.Count == 0)
        {
            StatusText = $"{SelectedNode.Info.DisplayName} advertises no events.";
            return;
        }

        SelectedNode.Events.Add(new NodeEvent { EventName = names[0] });
    }

    [RelayCommand]
    private void RemoveNodeEvent(NodeEvent? handler)
    {
        if (handler is not null && SelectedNode is not null)
        {
            SelectedNode.Events.Remove(handler);
        }
    }

    [RelayCommand]
    private void AddButtonState() => States.Add(new ButtonState());

    [RelayCommand]
    private void RemoveButtonState(ButtonState? state)
    {
        if (state is not null)
        {
            States.Remove(state);
        }
    }

    [RelayCommand]
    private void AddSchemaProperty() => SchemaProperties.Add(new SchemaProperty());

    [RelayCommand]
    private void RemoveSchemaProperty(SchemaProperty? property)
    {
        if (property is not null)
        {
            SchemaProperties.Remove(property);
        }
    }

    // ------------------------------------------------------------------ preview

    private void UpdatePreview()
    {
        var design = BuildDesign();
        var nodes = design.Nodes.Count == 0
            ? "<p class='empty'>No nodes yet - add one from the palette.</p>"
            : string.Join(string.Empty, design.Nodes.Select(RenderPreview));

        var unit = string.IsNullOrWhiteSpace(Unit) ? string.Empty : $"<span class='unit'>{WebEscape(Unit)}</span>";
        PreviewHtml = $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><style>
              body { margin:0; display:grid; place-items:center; height:144px; background:#1b1d24; color:#f2f4f8;
                     font:12px/1.3 "Segoe UI", system-ui, sans-serif; }
              .node { display:inline-block; padding:2px 4px; border-radius:3px; }
              .unit { opacity:.6; font-size:10px; margin-left:3px; }
              .empty { opacity:.45; }
            </style></head>
            <body>{{unit}}{{nodes}}</body></html>
            """;
    }

    private string RenderPreview(DesignedNode node)
    {
        var info = UiNodeCatalog.Find(node.NodeType);
        var label = info?.DisplayName ?? node.NodeType;
        var text = WebEscape(node.Text);
        return $"<span class='node' title='{WebEscape(node.NodeType)}' style='background:{WebEscape(node.Background)}'>{label}{(text.Length > 0 ? ": " + text : string.Empty)}</span>";
    }

    private static string WebEscape(string? value) => (value ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);



    partial void OnUnitChanged(string value) => UpdatePreview();

    /// <summary>True when a node is selected, so the property panel can be shown.</summary>
    public bool HasSelection => SelectedNode is not null;

    // ------------------------------------------------------------------ generation

    /// <summary>Projects the designer state onto the generator's model.</summary>
    public WidgetDesign BuildDesign() => new()
    {
        WidgetTypeId = WidgetTypeId.Trim(),
        WidgetName = WidgetName,
        WidgetDescription = WidgetDescription,
        ProviderName = ProviderName,
        HasConfiguration = HasConfiguration,
        SupportsFlows = SupportsFlows,
        Unit = Unit,
        Surfaces = HasConfiguration ? ["widget", "config"] : ["widget"],
        Nodes = [.. RootNodes.Select(r => r.ToModel(null))],
        States = [.. States.Select(s => new DesignedState { Name = s.Name, Background = s.Background, Text = s.Text })],
        SchemaProperties = [.. SchemaProperties.Select(p => p.ToModel())],
    };

    [RelayCommand]
    private void Generate()
    {
        var ws = _workspaces.Current;
        if (ws is null)
        {
            StatusText = "Open a plugin first.";
            return;
        }

        var design = BuildDesign();
        var problems = design.Validate();
        if (problems.Count > 0)
        {
            StatusText = string.Join(" ", problems);
            return;
        }

        var path = Path.Combine(ws.PluginProjectDirectory, design.ClassName + ".cs");
        if (File.Exists(path))
        {
            StatusText = $"{design.ClassName}.cs already exists - delete it first or change the widget type id.";
            return;
        }

        try
        {
            File.WriteAllText(path, WidgetGenerator.Render(design, ws.ProjectName), new UTF8Encoding(false));

            var stringsPath = Path.Combine(ws.LocalizationDirectory, "Strings.resx");
            _resx.AddKeys(stringsPath, WidgetGenerator.BuildStrings(design));

            StatusText = $"Generated {design.ClassName}.cs with {CountNodes(design)} node(s). "
                + "Register it on PluginIntegration by implementing IWidgetTypeProvider there.";
        }
        catch (GenerationException ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Generation failed: {ex.Message}";
        }
    }

    private static int CountNodes(WidgetDesign design)
    {
        var total = 0;
        var pending = new Stack<DesignedNode>(design.Nodes);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            total++;
            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return total;
    }
}

/// <summary>ObservableCollection index-aware move, which the tree editor needs and the BCL lacks.</summary>
public static class ObservableCollectionExtensions
{
    public static void Move<T>(this ObservableCollection<T> collection, int from, int to)
    {
        if (from == to || from < 0 || from >= collection.Count)
        {
            return;
        }

        var item = collection[from];
        collection.RemoveAt(from);
        collection.Insert(Math.Clamp(to, 0, collection.Count), item);
    }
}
