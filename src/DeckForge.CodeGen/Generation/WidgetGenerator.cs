using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeckForge.Core.Code;
using DeckForge.Core.Widgets;

namespace DeckForge.CodeGen.Generation;

/// <summary>One event on a designed node.</summary>
public sealed class DesignedEvent
{
    public string Name { get; set; } = "press";

    /// <summary>
    /// What the handler does. Kept as free text on purpose: the body is the author's own code, and
    /// a generator that guessed would produce a handler that silently does nothing.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>True for an async handler, which must not block the dispatch.</summary>
    public bool IsAsync { get; set; }
}

/// <summary>One node in the designed tree.</summary>
public sealed class DesignedNode
{
    public string Key { get; set; } = "node";

    public string NodeType { get; set; } = "ui.text";

    /// <summary>The parent node's key, or null for a root.</summary>
    public string? ParentKey { get; set; }

    public string Text { get; set; } = "Hello";

    public double Size { get; set; } = 0.14;

    public string Background { get; set; } = "#2C2C2E";

    /// <summary>Property values the designer set, keyed by the property name in the catalog.</summary>
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);

    public List<DesignedEvent> Events { get; set; } = [];

    public List<DesignedNode> Children { get; set; } = [];

    public string Describe() => $"{Key} ({NodeType})";
}

/// <summary>One named state a widget type offers.</summary>
public sealed class DesignedState
{
    public string Name { get; set; } = "on";
    public string Background { get; set; } = "#4F8CFF";
    public string Text { get; set; } = "On";
}

/// <summary>One property of a widget type's stored data.</summary>
public sealed class DesignedSchemaProperty
{
    public string Name { get; set; } = "value";
    public string SchemaType { get; set; } = "string";
    public string Title { get; set; } = "Value";
    public bool Required { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
}

/// <summary>Everything the widget designer collects.</summary>
public sealed class WidgetDesign
{
    public string WidgetTypeId { get; set; } = "gauge";
    public string WidgetName { get; set; } = "Gauge";
    public string WidgetDescription { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>True when the widget type offers a configuration surface.</summary>
    public bool HasConfiguration { get; set; } = true;

    /// <summary>True when the widget type declares that it runs flows.</summary>
    public bool SupportsFlows { get; set; }

    /// <summary>The unit shown next to a reading, e.g. km/h.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>The two surfaces a widget type may serve.</summary>
    public string[] Surfaces { get; set; } = ["widget"];

    public List<DesignedNode> Nodes { get; set; } = [];

    public List<DesignedState> States { get; set; } = [];

    public List<DesignedSchemaProperty> SchemaProperties { get; set; } = [];

    /// <summary>The provider class name.</summary>
    public string ClassName => CSharpCode.ToPascal(WidgetTypeId) + "WidgetProvider";

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Core.Utils.MacroDeckRules.IsValidLocalId(WidgetTypeId))
        {
            problems.Add($"Widget type id '{WidgetTypeId}' must be lowercase kebab-case. It is persisted on every placed widget, so renaming it later strands them.");
        }

        if (string.IsNullOrWhiteSpace(WidgetName))
        {
            problems.Add("The widget type needs a display name.");
        }

        foreach (var node in Nodes)
        {
            if (!UiNodeCatalog.IsKnown(node.NodeType))
            {
                problems.Add($"Unknown node type '{node.NodeType}'.");
            }

            if (node.Key.Trim().Length == 0)
            {
                problems.Add($"Node '{node.NodeType}' has no key. A key is how the tree identifies the node.");
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (!keys.Add(node.Key))
            {
                problems.Add($"Node key '{node.Key}' is used twice.");
            }
        }

        foreach (var node in Nodes.Where(n => n.ParentKey is not null))
        {
            var parent = Nodes.FirstOrDefault(n => n.Key == node.ParentKey);
            if (parent is null)
            {
                problems.Add($"Node '{node.Key}' names parent '{node.ParentKey}', which does not exist.");
            }
            else if (!UiNodeCatalog.CanContain(parent.NodeType, node.NodeType))
            {
                problems.Add($"'{parent.NodeType}' cannot contain '{node.NodeType}'. A widget view's root must be a container.");
            }
        }

        // A schema is mandatory once a configuration surface is offered, and the host refuses the
        // registration otherwise: a tree writing into an unvalidated payload is how a widget's
        // stored data becomes unreadable with nothing reporting an error.
        if (HasConfiguration && SchemaProperties.Count == 0)
        {
            problems.Add("A widget type with a configuration surface needs at least one data property; the host refuses a configuration surface with no schema.");
        }

        return problems;
    }
}

/// <summary>
/// Emits an <c>IWidgetTypeProvider</c> and <c>IUiProvider</c> that compile against the real
/// <c>MacroDeck.Ui</c> component DSL.
/// </summary>
/// <remarks>
/// <para>
/// Six defects in the previous version were only visible to the compiler: five wrong or missing
/// <c>using</c> directives (<c>MacroDeck.Ui</c> is not a namespace - the DSL is
/// <c>MacroDeck.Ui.Dsl</c> and the runtime is <c>MacroDeck.Ui.Runtime</c>), a call to
/// <c>UiText.FromFixed</c> which does not exist, <c>Add</c> on two init-only
/// <c>IReadOnlyList</c> properties, a <c>Surfaces</c> property typed as an array where the
/// interface demands <c>IReadOnlyList</c> (no covariant returns), and a session adapter
/// implementing none of <c>IUiSession</c>.
/// </para>
/// <para>
/// One more was silent rather than loud: the node renderer trimmed its trailing newline, so a
/// childless node's <c>// (none designed)</c> placeholder swallowed every following sibling into
/// a comment. The tree compiled and rendered with nodes missing.
/// </para>
/// </remarks>
public static class WidgetGenerator
{
    /// <summary>The resx group every string for a widget type lives under.</summary>
    public static string StringsRoot(string widgetTypeId) =>
        ResxKeyBuilder.Build("Widgets", CSharpCode.ToPascal(widgetTypeId));

    /// <summary>Renders the provider class.</summary>
    public static string Render(WidgetDesign design, string @namespace)
    {
        var problems = design.Validate();
        if (problems.Count > 0)
        {
            throw new GenerationException(string.Join(" ", problems));
        }

        var root = StringsRoot(design.WidgetTypeId);
        var className = design.ClassName;
        var sb = new StringBuilder();

        // The exact namespaces the real types live in. MacroDeck.Ui is not one of them.
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using MacroDeck.Localization;");
        sb.AppendLine("using MacroDeck.Sdk;");
        sb.AppendLine("using MacroDeck.Sdk.Ui;");
        sb.AppendLine("using MacroDeck.Sdk.Widgets;");
        sb.AppendLine("using MacroDeck.Ui.Components;");
        sb.AppendLine("using MacroDeck.Ui.Dsl;");
        sb.AppendLine("using MacroDeck.Ui.Model.Nodes;");
        sb.AppendLine("using MacroDeck.Ui.Model.Patches;");
        sb.AppendLine("using MacroDeck.Ui.Model.Surfaces;");
        sb.AppendLine("using MacroDeck.Ui.Runtime;");
        sb.AppendLine("using Serilog;");
        sb.AppendLine();
        sb.AppendLine($"namespace {CSharpCode.Identifier(@namespace)};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {CSharpCode.Xml(design.WidgetName)} - generated by DeckForge's Widget designer.");
        sb.AppendLine("/// A widget type declares which types it offers; the view itself is built by IUiProvider,");
        sb.AppendLine("/// the same path a folder view or a configuration flow is served through.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public sealed class {className} : IWidgetTypeProvider, IUiProvider");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly ILogger _logger;");
        sb.AppendLine("    private IWidgetTypeProviderContext? _types;");
        sb.AppendLine("    private IUiResourceRegistry? _resources;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}(ILogger logger) => _logger = logger.ForContext<{className}>();");
        sb.AppendLine();

        // ProviderName and Surfaces are default interface members, so they are only emitted when set.
        if (!string.IsNullOrWhiteSpace(design.ProviderName))
        {
            sb.AppendLine($"    public string ProviderName => {CSharpCode.StringLiteral(design.ProviderName)};");
            sb.AppendLine();
        }

        sb.AppendLine("    // The interface requires IReadOnlyList<UiSurfaceDeclaration>; an array does not");
        sb.AppendLine("    // satisfy it, because C# has no covariant returns.");
        sb.AppendLine("    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =");
        sb.AppendLine("    [");
        foreach (var surface in design.Surfaces)
        {
            sb.AppendLine($"        new() {{ Kind = {CSharpCode.StringLiteral(surface)}, SessionMode = UiSessionModes.Shared }},");
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        _types = context;");
        sb.AppendLine("        _resources ??= context as IUiResourceRegistry;");
        sb.AppendLine();
        sb.AppendLine("        // The type id is stable across releases: renaming it strands every widget already");
        sb.AppendLine("        // placed with it. The host qualifies it as your.plugin.id::your-type.");
        sb.AppendLine("        var descriptor = new WidgetTypeDescriptor(");
        sb.AppendLine($"            {CSharpCode.StringLiteral(design.WidgetTypeId)},");
        sb.AppendLine($"            {ResxKeyBuilder.Accessor(root, "Name")},");
        if (!string.IsNullOrWhiteSpace(design.WidgetDescription))
        {
            sb.AppendLine($"            {ResxKeyBuilder.Accessor(root, "Description")},");
        }

        sb.AppendLine($"            DefaultData: {CSharpCode.StringLiteral(RenderDefaultData(design))},");
        sb.AppendLine($"            DataSchema: {CSharpCode.StringLiteral(RenderSchema(design))},");
        sb.AppendLine($"            HasConfiguration: {(design.HasConfiguration ? "true" : "false")})");
        sb.AppendLine("        {");
        sb.AppendLine($"            SupportsFlows = {(design.SupportsFlows ? "true" : "false")},");
        if (design.States.Count > 0)
        {
            // Appearance properties are what make a placed widget's look configurable, and a
            // property is only offered when the data schema accepts a sample under its key.
            var appearances = design.States.Select(s =>
                $"new WidgetAppearanceProperty({CSharpCode.StringLiteral(s.Name)}, {CSharpCode.StringLiteral(s.Background)})");
            sb.AppendLine($"            AppearanceProperties = [{string.Join(", ", appearances)}],");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        _logger.Information(\"Registering widget type {TypeId}.\", design.TypeId);");
        sb.AppendLine("        await context.RegisterWidgetTypeAsync(descriptor, cancellationToken);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    // Declared so the host can recover the catalogue after a reconnect, which is what");
        sb.AppendLine("    // the default empty list would silently prevent.");
        sb.AppendLine("    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() =>");
        sb.AppendLine("    [");
        sb.AppendLine("        new()");
        sb.AppendLine("        {");
        sb.AppendLine($"            Id = {CSharpCode.StringLiteral(design.WidgetTypeId)},");
        sb.AppendLine($"            Name = {ResxKeyBuilder.Accessor(root, "Name")},");
        if (!string.IsNullOrWhiteSpace(design.WidgetDescription))
        {
            sb.AppendLine($"            Description = {ResxKeyBuilder.Accessor(root, "Description")},");
        }

        sb.AppendLine("        },");
        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        // Returning null declines a surface, which is the correct answer for one this");
        sb.AppendLine("        // provider does not serve - not an error.");
        sb.AppendLine("        if (request.Surface.Kind != UiSurfaceKinds.Widget)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Task.FromResult<IUiSession?>(null);");
        sb.AppendLine("        }");
        sb.AppendLine();
        AppendViewConstruction(sb, design, root);
        sb.AppendLine("        return Task.FromResult<IUiSession?>(new WidgetSession(view));");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // The session adapter. The previous one implemented a View property and none of IUiSession.
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Adapts the view to IUiSession. Disposing it is what lets a closed session go: a");
        sb.AppendLine("/// session that skips disposing its view stays in memory for as long as the plugin");
        sb.AppendLine("/// runs, and so does every patch queued for it.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class WidgetSession : IUiSession");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly UiView _view;");
        sb.AppendLine("    private int _disposed;");
        sb.AppendLine();
        sb.AppendLine("    public WidgetSession(UiView view)");
        sb.AppendLine("    {");
        sb.AppendLine("        _view = view;");
        sb.AppendLine("        _view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);");
        sb.AppendLine("        _view.HandlerFaulted += (_, fault) => Faulted?.Invoke(this,");
        sb.AppendLine("            new UiSessionFaultedEventArgs(fault.Reason, fault.Exception));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public event EventHandler? Changed;");
        sb.AppendLine();
        sb.AppendLine("    public event EventHandler<UiSessionFaultedEventArgs>? Faulted;");
        sb.AppendLine();
        sb.AppendLine("    public UiTree BuildTree() => _view.Tree;");
        sb.AppendLine();
        sb.AppendLine("    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();");
        sb.AppendLine();
        sb.AppendLine("    // Dispatch stays synchronous: a client's event must be accepted or rejected before");
        sb.AppendLine("    // the work it triggers can finish. A handler that throws faults the session rather");
        sb.AppendLine("    // than escaping into the caller.");
        sb.AppendLine("    public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);");
        sb.AppendLine();
        sb.AppendLine("    public ValueTask DisposeAsync()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (Interlocked.Exchange(ref _disposed, 1) == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            _view.Dispose();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return ValueTask.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        _ = System.Text.Json.JsonSerializer.Serialize(new { }); // keep System.Text.Json referenced for DataSchema literals
        return sb.ToString();
    }

    /// <summary>The resx entries the generated code references.</summary>
    public static IReadOnlyDictionary<string, string> BuildStrings(WidgetDesign design)
    {
        var root = StringsRoot(design.WidgetTypeId);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ResxKeyBuilder.Build(root, "Name")] = design.WidgetName,
            [ResxKeyBuilder.Build(root, "Description")] = design.WidgetDescription,
        };
    }

    private static void AppendViewConstruction(StringBuilder sb, WidgetDesign design, string root)
    {
        var roots = design.Nodes.Where(n => n.ParentKey is null).ToList();
        if (roots.Count == 0)
        {
            sb.AppendLine("        // A widget view's root must be a container, so an empty design still renders");
            sb.AppendLine("        // something rather than failing to build.");
            sb.AppendLine("        var root = new UiStack { Key = \"root\" };");
            sb.AppendLine("        var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }, root);");
            return;
        }

        if (roots.Count > 1)
        {
            // Several roots is the shape the previous BuildTree silently collapsed: it assigned
            // `root = element` per parentless node and kept only the last one.
            sb.AppendLine("        // Several top-level nodes are wrapped in a stack: a view has exactly one root,");
            sb.AppendLine("        // so they cannot be siblings at the top level.");
            sb.AppendLine("        var rootChildren = new List<UiElement>");
            sb.AppendLine("        {");
            foreach (var node in roots)
            {
                sb.AppendLine($"            {RenderNode(node, design, root, 3)},");
            }

            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        var root = new UiStack { Key = \"root\", Children = rootChildren };");
        }
        else
        {
            sb.AppendLine($"        var root = {RenderNode(roots[0], design, root, 2)};");
        }

        sb.AppendLine();
        sb.AppendLine("        var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }, root);");
    }

    /// <summary>Renders one node, and its children, as a C# expression.</summary>
    private static string RenderNode(DesignedNode node, WidgetDesign design, string root, int indent)
    {
        var info = UiNodeCatalog.Find(node.NodeType) ?? UiNodeCatalog.All[0];
        var members = new List<string>
        {
            $"Key = {CSharpCode.StringLiteral(node.Key)}",
        };

        if (node.Events.Count > 0)
        {
            var handlers = node.Events.Select(e => RenderHandler(e, indent + 1));
            members.Add($"Events = [{string.Join(", ", handlers)}]");
        }

        AppendCommonMembers(members, node, info);

        foreach (var (name, value) in node.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var descriptor = info.Properties.FirstOrDefault(p => p.Name == name);
            if (descriptor is null)
            {
                // A property the type does not have would not compile, and silently dropping it
                // would hide the mistake, so it is reported rather than ignored.
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            members.Add(RenderProperty(descriptor, value, design, root, node));
        }

        // Children is init-only, so a childless node must not be given the property at all: an
        // empty list is a different statement from an absent one to a renderer.
        if (info.IsContainer && node.Children.Count > 0)
        {
            var children = node.Children.Select(c => RenderNode(c, design, root, indent + 1));
            members.Add($"Children = [{string.Join(", ", children)}]");
        }

        return $"new {info.ClassName} {{ {string.Join(", ", members)} }}";
    }

    private static void AppendCommonMembers(List<string> members, DesignedNode node, UiNodeTypeInfo info)
    {
        if (info.Has("text") && !string.IsNullOrWhiteSpace(node.Text) && !node.Properties.ContainsKey("text"))
        {
            // UiText has an implicit conversion from string, so the literal assigns directly. There
            // is no FromFixed method: the previous version called one and did not compile.
            members.Add($"Text = {CSharpCode.StringLiteral(node.Text)}");
        }

        if (info.Has("size") && node.Size > 0 && !node.Properties.ContainsKey("size"))
        {
            members.Add($"Size = {CSharpCode.NumberLiteral(node.Size)}");
        }

        if (info.Has("background") && !string.IsNullOrWhiteSpace(node.Background)
            && !node.Properties.ContainsKey("background"))
        {
            members.Add($"Background = {CSharpCode.StringLiteral(node.Background)}");
        }
    }

    private static string RenderProperty(UiPropertyDescriptor descriptor, string value, WidgetDesign design, string root, DesignedNode node) => descriptor.Kind switch
    {
        UiPropertyKind.Text => $"{descriptor.Name} = {RenderText(descriptor.Name, value, node)}",
        UiPropertyKind.Number => $"{descriptor.Name} = {CSharpCode.NumberLiteral(value)}",
        UiPropertyKind.Flag => $"{descriptor.Name} = {CSharpCode.BoolLiteral(value)}",
        UiPropertyKind.ValueNumber => $"{descriptor.Name} = {CSharpCode.NumberLiteral(value)}",
        _ => $"{descriptor.Name} = {CSharpCode.StringLiteral(value)}",
    };
    private static string RenderText(string property, string value, DesignedNode node)
    {
        // The two reference-carrying families need their real reference type, not a string.
        if (property == "value" && node.NodeType.StartsWith("macrodeck.", StringComparison.Ordinal))
        {
            return "UiTimeReference.Now()";
        }

        if (property == "points")
        {
            var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CSharpCode.NumberLiteral);
            return $"[{string.Join(", ", values)}]";
        }

        return CSharpCode.StringLiteral(value);
    }

    private static string RenderHandler(DesignedEvent handler, int indent)
    {
        var name = CSharpCode.StringLiteral(handler.Name);
        var body = string.IsNullOrWhiteSpace(handler.Body)
            ? "_ = 0; // TODO: react to the event."
            : handler.Body.Trim();

        if (handler.IsAsync)
        {
            return $"UiEventHandler.OnAsync({name}, _ => {{ {body} return Task.CompletedTask; }})";
        }

        return $"UiEventHandler.On({name}, () => {{ {body} }})";
    }

    private static string RenderDefaultData(WidgetDesign design)
    {
        if (!design.HasConfiguration || design.SchemaProperties.Count == 0)
        {
            return "{}";
        }

        var entries = design.SchemaProperties
            .Where(p => !string.IsNullOrEmpty(p.DefaultValue))
            .Select(p => $"{CSharpCode.StringLiteral(p.Name)}: {CSharpCode.StringLiteral(p.DefaultValue)}");
        return $"{{{string.Join(", ", entries)}}}";
    }

    private static string RenderSchema(WidgetDesign design)
    {
        if (design.SchemaProperties.Count == 0)
        {
            // A configuration surface with no schema is refused by the host, so this is only
            // reachable when the type declares no configuration surface.
            return "{}";
        }

        var properties = design.SchemaProperties.Select(p =>
        {
            var required = p.Required ? ", \"required\"" : string.Empty;
            return $"\"{p.Name}\": {{ \"type\": {CSharpCode.StringLiteral(p.SchemaType)}, \"title\": {CSharpCode.StringLiteral(p.Title)}{required} }}";
        });

        var requiredList = design.SchemaProperties.Where(p => p.Required).Select(p => CSharpCode.StringLiteral(p.Name));
        return $"{{ \"type\": \"object\", \"properties\": {{{string.Join(", ", properties)}}}, \"required\": [{string.Join(", ", requiredList)}] }}";
    }
}
