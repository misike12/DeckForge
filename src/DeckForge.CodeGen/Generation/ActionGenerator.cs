using System.Text;
using DeckForge.Core.Code;

namespace DeckForge.CodeGen.Generation;

/// <summary>Everything the Actions designer collects, in a form the generator can render.</summary>
public sealed record ActionDesign
{
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public string ActionDescription { get; init; } = "";
    public IReadOnlyList<ActionParameterSpec> Parameters { get; init; } = [];

    /// <summary>
    /// The integration class name, derived from the id.
    /// </summary>
    /// <remarks>
    /// Via <see cref="CSharpCode.TypeName"/> rather than <see cref="CSharpCode.ToPascal"/>: the
    /// latter prefixes a C# keyword with <c>@</c>, so the id <c>int</c> produced <c>@intAction</c> -
    /// and that is also the file name, where csc rejects it with CS2011 before compiling anything.
    /// </remarks>
    public string ClassName => CSharpCode.TypeName(CSharpCode.ToPascal(ActionId), "Action");

    /// <summary>The resx group every string for this action lives under.</summary>
    public string StringsRoot => ResxKeyBuilder.Build("Actions", CSharpCode.ToPascal(ActionId));

    /// <summary>Problems that would make the output not compile, or not work.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Core.Utils.MacroDeckRules.IsValidLocalId(ActionId))
        {
            problems.Add($"Action id '{ActionId}' must be lowercase kebab-case and at most 64 characters.");
        }

        if (Parameters.Count == 0)
        {
            problems.Add("An action needs at least one parameter, or it cannot be configured.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Parameters)
        {
            // The name is the wire identity: the host persists it and every existing button that
            // uses this action keeps referring to it. MDP1002 enforces the grammar at build time
            // too, but a clear message beats a squiggle.
            if (!Core.Utils.MacroDeckRules.IsValidLocalId(parameter.Name))
            {
                problems.Add($"Parameter name '{parameter.Name}' must be lowercase kebab-case; it is the persisted wire name.");
            }

            if (!seen.Add(parameter.Name))
            {
                problems.Add($"Parameter name '{parameter.Name}' is used twice.");
            }

            if (!ActionParameterTypes.IsKnown(parameter.EditorType))
            {
                problems.Add($"Unknown editor type '{parameter.EditorType}'.");
            }

            problems.AddRange(BraceProblem(parameter.Label, "label", parameter.Name));
            problems.AddRange(BraceProblem(parameter.Description, "description", parameter.Name));
            problems.AddRange(BraceProblem(parameter.Placeholder, "placeholder", parameter.Name));
        }

        return problems;
    }

    /// <summary>
    /// Refuses a string containing a brace, because the SDK's localization generator turns
    /// <c>{name}</c> in a resx value into a parameter on the generated method.
    /// </summary>
    /// <remarks>
    /// A value of <c>Description with {braces} here</c> produces
    /// <c>Description(LocalizedText braces)</c>, and the generated action calls
    /// <c>Description()</c> - a CS7036 in the user's plugin. DeckForge cannot fill a parameter whose
    /// name and count it does not control, and the SDK offers no escape for a literal brace that
    /// the editor could apply silently, so the string is refused with a message instead. The
    /// alternative - emitting the call and letting the build fail - tells the user less and later.
    /// </remarks>
    private static IEnumerable<string> BraceProblem(string? value, string what, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('{') && !value.Contains('}'))
        {
            yield break;
        }

        yield return $"The {what} of parameter '{parameterName}' contains a brace. "
            + "Macro Deck reads {name} in a string as a substitution and adds a parameter for it, "
            + "which the generated code cannot supply. Use parentheses or square brackets instead.";
    }
}

/// <summary>
/// Emits an <c>IActionDefinition</c> that compiles against the real
/// <c>MacroDeck.Sdk.Actions</c> surface.
/// </summary>
/// <remarks>
/// Every emitted argument after the parameter name is passed by name. C# binds named arguments
/// first and then fills the remainder positionally in declaration order, so a positional argument
/// for an optional parameter silently steals the slot of whatever follows it.
/// </remarks>
public static class ActionGenerator
{
    /// <summary>Renders the action class.</summary>
    public static string Render(ActionDesign design, string @namespace)
    {
        var problems = design.Validate();
        if (problems.Count > 0)
        {
            throw new GenerationException(string.Join(" ", problems));
        }

        var className = design.ClassName;
        var stem = CSharpCode.ToPascal(design.ActionId);
        var root = design.StringsRoot;
        var sb = new StringBuilder();

        sb.AppendLine("using MacroDeck.Localization;");
        sb.AppendLine("using MacroDeck.Sdk;");
        sb.AppendLine("using MacroDeck.Sdk.Actions;");
        sb.AppendLine("using Serilog;");
        sb.AppendLine();
        sb.AppendLine($"namespace {CSharpCode.Identifier(@namespace)};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// {CSharpCode.Xml(design.ActionName)} - generated by DeckForge's Actions editor.");
        sb.AppendLine("/// The parameter plumbing is complete; replace the body of ExecuteAsync with the real work.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public sealed class {className} : IActionDefinition, {ActionContextPatcher.InterfaceName}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly ILogger _logger;");
        sb.AppendLine("    private IIntegrationContext? _integration;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}(ILogger logger) => _logger = logger.ForContext<{className}>();");
        sb.AppendLine();
        sb.AppendLine("    public void SetIntegrationContext(IIntegrationContext context) => _integration = context;");
        sb.AppendLine();
        sb.AppendLine($"    public string Id => {CSharpCode.StringLiteral(design.ActionId)};");
        sb.AppendLine();
        sb.AppendLine($"    public LocalizedText Name => {ResxKeyBuilder.Accessor(root, "Name")};");
        sb.AppendLine();
        sb.AppendLine($"    public LocalizedText Description => {ResxKeyBuilder.Accessor(root, "Description")};");
        sb.AppendLine();
        sb.AppendLine("    public IReadOnlyList<ActionParameter> Parameters { get; } =");
        sb.AppendLine("    [");

        foreach (var parameter in design.Parameters)
        {
            // Parameters live under their own `Parameters` group, so a parameter called "Name" or
            // "Description" cannot be both a leaf and the group its siblings nest under, which
            // is the SDK's MDLOC008 and a CS0102 in the generated Strings class.
            var key = ResxKeyBuilder.Build(root, "Parameters", parameter.Name);
            var call = ActionParameterFactory.Emit(parameter, property => $"Strings.{key}.{property}()");

            if (!string.IsNullOrWhiteSpace(parameter.OnlyWhenParameter))
            {
                call += $".OnlyWhen({CSharpCode.StringLiteral(parameter.OnlyWhenParameter)}, {CSharpCode.StringLiteral(parameter.OnlyWhenValue)})";
            }

            sb.AppendLine($"        {call},");
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("    public MacroDeckPlatform Platforms => MacroDeckPlatform.All;");
        sb.AppendLine();
        sb.AppendLine($"    public IActionExecutor CreateExecutor() => new Executor(_logger, _integration);");
        sb.AppendLine();
        sb.AppendLine("    private sealed class Executor : IActionExecutor");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly ILogger _logger;");
        sb.AppendLine("        private readonly MacroDeck.Sdk.IIntegrationContext? _integration;");
        sb.AppendLine();
        sb.AppendLine("        public Executor(ILogger logger, MacroDeck.Sdk.IIntegrationContext? integration = null)");
        sb.AppendLine("        {");
        sb.AppendLine("            _logger = logger;");
        sb.AppendLine("            _integration = integration;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // async unconditionally, so a later Blocks save that injects an await - Task.Delay, an
        // HTTP call, a folder change - cannot land a bare await in a non-async method.
        sb.AppendLine("        public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)");
        sb.AppendLine("        {");
        sb.AppendLine("            // TODO: implement the real work. The configured values are:");
        foreach (var parameter in design.Parameters)
        {
            var local = CSharpCode.Identifier(parameter.Name);
            var key = CSharpCode.StringLiteral(parameter.Name);
            sb.AppendLine($"            //     {key} -> context.Parameters.TryGetValue({key}, out var {local});");
        }

        sb.AppendLine("            //");
        sb.AppendLine("            // Parameters is IReadOnlyDictionary<string, object>, so a value is never null.");
        sb.AppendLine("            // Narrow it yourself: `value as string`, Convert.ToDouble(value, ...), and so on.");
            sb.AppendLine("            //");
            sb.AppendLine("            // _integration is the IIntegrationContext the integration hands this action");
            sb.AppendLine("            // during InitializeAsync, once per session and again after a reconnect. Deck,");
            sb.AppendLine("            // Notifications, Widgets, Variables, Scripts, Events and Messages live there,");
            sb.AppendLine("            // not on ActionExecutionContext. It is null before initialization and in a");
            sb.AppendLine("            // unit test that constructs the action directly, so guard a host call:");
            sb.AppendLine("            //     if (_integration is not null) await _integration.Deck.GoBackAsync(...);");
            sb.AppendLine("            //");
        sb.AppendLine("            // context.CancellationToken is already cancelled when the flow is aborted, so");
        sb.AppendLine("            // forward it to anything that can wait (MDP3001).");
        sb.AppendLine("            await Task.CompletedTask;");
        sb.AppendLine();
        sb.AppendLine($"            _logger.Information(\"Action {CSharpCode.EscapeLiteralBody(design.ActionId)} executed\");");
        sb.AppendLine("            return ActionResult.Success();");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The resx entries the generated code references.</summary>
    public static IReadOnlyDictionary<string, string> BuildStrings(ActionDesign design)
    {
        var root = design.StringsRoot;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ResxKeyBuilder.Build(root, "Name")] = design.ActionName,
            [ResxKeyBuilder.Build(root, "Description")] = design.ActionDescription,
        };

        foreach (var parameter in design.Parameters)
        {
            var key = ResxKeyBuilder.Build(root, "Parameters", parameter.Name);
            entries[$"{key}.Label"] = parameter.Label;
            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                entries[$"{key}.Description"] = parameter.Description;
            }

            if (!string.IsNullOrWhiteSpace(parameter.Placeholder))
            {
                entries[$"{key}.Placeholder"] = parameter.Placeholder;
            }
        }

        return entries;
    }

    /// <summary>Renders the action and the strings it needs, as one unit.</summary>
    public static GeneratedFile Generate(ActionDesign design, string projectName)
    {
        return GeneratedFile.WithStrings(
            $"{design.ClassName}.cs",
            Render(design, projectName),
            BuildStrings(design));
    }
}
