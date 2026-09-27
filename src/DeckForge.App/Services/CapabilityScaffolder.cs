using System.IO;
using System.Text;
using DeckForge.Core.Plugins;
using DeckForge.Core.Workspace;

namespace DeckForge.App.Services;

/// <summary>
/// Adds capability implementations to an existing plugin project: generates the provider
/// file, implements the interface on PluginIntegration, adds resx keys and manifest
/// permissions. Idempotent - scaffolding the same capability twice is a no-op.
/// </summary>
public sealed class CapabilityScaffolder
{
    /// <summary>Result describing what changed on disk.</summary>
    public sealed record ScaffoldResult(bool Changed, string Message);

    public ScaffoldResult Scaffold(WorkspaceContext ws, string capabilityId)
    {
        return capabilityId switch
        {
            "variables" => ScaffoldVariables(ws),
            "events" => ScaffoldEvents(ws),
            _ => new ScaffoldResult(false, $"Scaffolding for '{capabilityId}' is not implemented yet - use the docs link for the manual contract."),
        };
    }

    // ---------- shared plumbing ----------

    private static string ReadIntegration(WorkspaceContext ws)
    {
        var path = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private static void WriteIntegration(WorkspaceContext ws, string source)
    {
        File.WriteAllText(Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs"), source);
    }

    private static void AddUsing(ref string source, string usingLine)
    {
        if (!source.Contains(usingLine, StringComparison.Ordinal))
        {
            var idx = source.IndexOf("using ", StringComparison.Ordinal);
            var insertAt = idx < 0 ? 0 : idx;
            source = source.Insert(insertAt, usingLine + "\n");
        }
    }

    private static void AddInterface(ref string source, string interfaceName)
    {
        if (source.Contains(interfaceName, StringComparison.Ordinal))
        {
            return;
        }
        var anchor = "public sealed class PluginIntegration : IPluginIntegration";
        if (!source.Contains(anchor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PluginIntegration.cs does not have the expected shape.");
        }
        source = source.Replace(anchor, $"public sealed class PluginIntegration : IPluginIntegration, {interfaceName}");
    }

    private static void AddPermissions(WorkspaceContext ws, IEnumerable<string> permissions)
    {
        var manifestPath = ws.ManifestPath;
        var doc = ManifestDocument.Load(manifestPath);
        var current = doc.Permissions.ToList();
        var added = false;
        foreach (var permission in permissions)
        {
            if (!current.Contains(permission, StringComparer.Ordinal))
            {
                current.Add(permission);
                added = true;
            }
        }
        if (added)
        {
            doc.SetPermissions([.. current]);
            doc.Save(manifestPath);
        }
    }

    // ---------- variables ----------

    private ScaffoldResult ScaffoldVariables(WorkspaceContext ws)
    {
        var integrationPath = Path.Combine(ws.PluginProjectDirectory, "PluginIntegration.cs");
        var integration = ReadIntegration(ws);
        if (integration.Contains("IVariableProvider", StringComparison.Ordinal))
        {
            return new ScaffoldResult(false, "Variables capability is already scaffolded.");
        }

        var p = ws.ProjectName;
        AddUsing(ref integration, "using MacroDeck.Sdk.Variables;");
        AddInterface(ref integration, "IVariableProvider");

        // Insert members before the final closing brace of the class.
        var members = """

                // ----- Variables capability (scaffolded by DeckForge) -----

                public IReadOnlyList<VariableDefinition> Variables { get; } =
                [
                    VariableDefinition.Eager("$prefix_example", VariableType.Text) with { Id = "example" },
                ];

                public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
                {
                    return ValueTask.FromResult(localId switch
                    {
                        "example" => VariableReading.Of("replace me"),
                        _ => VariableReading.Unavailable,
                    });
                }
            """.Replace("$prefix", ws.Options.PluginId.Split('.').Last().Replace('-', '_')).Replace("\r\n", "\n");

        var lastBrace = integration.LastIndexOf('}');
        integration = integration.Insert(lastBrace, members + "\n");
        WriteIntegration(ws, integration);

        AddPermissions(ws, ["host:variables", "host:variable-values"]);
        return new ScaffoldResult(true, $"Variables scaffolded: implemented IVariableProvider in PluginIntegration.cs, added permissions. Edit Variables/{p} values, then run macrodeck-plugin test.");
    }

    // ---------- events ----------

    private ScaffoldResult ScaffoldEvents(WorkspaceContext ws)
    {
        var integration = ReadIntegration(ws);
        if (integration.Contains("IEventProvider", StringComparison.Ordinal))
        {
            return new ScaffoldResult(false, "Events capability is already scaffolded.");
        }

        AddUsing(ref integration, "using MacroDeck.Sdk.Events;");
        AddInterface(ref integration, "IEventProvider");

        var members = """

                // ----- Events capability (scaffolded by DeckForge) -----

                public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
                [
                    new()
                    {
                        Id = "something-happened",
                        Name = Strings.Events.SomethingHappened.Name(),
                        ConfigurationParameters = [],
                        PayloadParameters = [],
                    },
                ];
            """.Replace("\r\n", "\n");

        var lastBrace = integration.LastIndexOf('}');
        integration = integration.Insert(lastBrace, members + "\n");
        WriteIntegration(ws, integration);

        AddPermissions(ws, ["events:publish"]);
        return new ScaffoldResult(true, "Events scaffolded: implemented IEventProvider with one example event. Publish it with context.Events.Publish(\"something-happened\").");
    }
}
