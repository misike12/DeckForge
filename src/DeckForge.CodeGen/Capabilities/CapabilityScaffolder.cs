using DeckForge.CodeGen.Generation;
using DeckForge.Core.Code;
using DeckForge.Core.Plugins;
using DeckForge.Core.Workspace;

namespace DeckForge.CodeGen.Capabilities;

/// <summary>The outcome of scaffolding one capability into a plugin.</summary>
public sealed record CapabilityScaffoldResult(bool Changed, string Message, PatchOutcome Outcome = PatchOutcome.Patched)
{
    public bool Failed => Outcome == PatchOutcome.AnchorMissing;
}

/// <summary>
/// Adds one capability to an already-generated plugin: the interfaces and members on
/// <c>PluginIntegration.cs</c>, the resx keys, and the manifest permissions.
/// </summary>
/// <remarks>
/// The previous implementation was a 22-case switch with two arms. Worse, its events arm emitted
/// <c>Strings.Events.SomethingHappened.Name()</c> without ever creating that resx key, so the
/// project it produced could not compile - while the gallery's "Add to plugin" button was enabled
/// for all twenty-two and the status line reported success either way.
/// </remarks>
public static class CapabilityScaffolder
{
    /// <summary>Adds <paramref name="capabilityId"/> to the project at <paramref name="workspace"/>.</summary>
    public static CapabilityScaffoldResult Scaffold(WorkspaceContext workspace, string capabilityId)
    {
        var definition = CapabilityDefinitions.Find(capabilityId);
        if (definition is null)
        {
            return new CapabilityScaffoldResult(false,
                $"'{capabilityId}' is not a known capability.",
                PatchOutcome.AnchorMissing);
        }

        var integrationPath = Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs");
        if (!File.Exists(integrationPath))
        {
            return new CapabilityScaffoldResult(false,
                "PluginIntegration.cs not found - open the project the generator produced.",
                PatchOutcome.AnchorMissing);
        }

        var notes = new List<string>();
        var failed = false;
        var changed = false;

        try
        {
            var source = File.ReadAllText(integrationPath);

            foreach (var ns in definition.Usings)
            {
                var patch = IntegrationPatcher.AddUsing(source, ns);
                source = patch.Content;
                failed |= patch.Outcome == PatchOutcome.AnchorMissing;
                changed |= patch.Outcome == PatchOutcome.Patched;
            }

            foreach (var contract in definition.Interfaces)
            {
                var patch = IntegrationPatcher.AddInterface(source, contract);
                source = patch.Content;
                failed |= patch.Outcome == PatchOutcome.AnchorMissing;
                changed |= patch.Outcome == PatchOutcome.Patched;
            }

            // The members go in only once the interfaces are declared, so a contributor that
            // implements a capability twice is a no-op rather than a duplicate member.
            var marker = CapabilityMarker(definition);
            if (!source.Contains(marker, StringComparison.Ordinal))
            {
                var member = IntegrationPatcher.AddMember(source, MemberBody(definition, marker));
                source = member.Content;
                failed |= member.Outcome == PatchOutcome.AnchorMissing;
                changed |= member.Outcome == PatchOutcome.Patched;
                notes.Add(member.Outcome == PatchOutcome.AlreadyPresent ? "members already present" : "members added");
            }
            else
            {
                notes.Add("members already present");
            }

            File.WriteAllText(integrationPath, source, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CapabilityScaffoldResult(false, $"Could not patch PluginIntegration.cs: {ex.Message}",
                PatchOutcome.AnchorMissing);
        }

        if (definition.NeedsStrings)
        {
            var stringsPath = Path.Combine(workspace.LocalizationDirectory, "Strings.resx");
            var entries = definition.Strings
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var resx = ResxMerger.AddKeys(stringsPath, entries);
            changed |= resx.Count > 0;
            notes.Add(resx.Count == 0 ? "resx keys already present" : $"{resx.Count} resx key(s) added");
        }

        var permissionNote = AddPermissions(workspace, definition);
        if (permissionNote is not null)
        {
            notes.Add(permissionNote);
            changed = true;
        }

        var summary = notes.Count == 0
            ? "nothing to change - the capability is already there."
            : string.Join(", ", notes) + ".";

        var outcome = failed
            ? PatchOutcome.AnchorMissing
            : changed ? PatchOutcome.Patched : PatchOutcome.AlreadyPresent;
        var message = failed
            ? $"Added what it could for '{definition.Id}', but PluginIntegration.cs does not have the expected shape - "
              + "add the members by hand. " + summary
            : $"'{definition.Id}': {summary} Next: {definition.NextStep}";

        return new CapabilityScaffoldResult(changed && !failed, message, outcome);
    }

    /// <summary>
    /// A comment line that identifies the generated block, so a second run recognises it and a
    /// human can see which parts of the class a generator owns.
    /// </summary>
    private static string CapabilityMarker(CapabilityDefinition definition) =>
        $"// <deckforge:capability id=\"{definition.Id}\" />";

    private static string MemberBody(CapabilityDefinition definition, string marker) =>
        string.Join(
            Environment.NewLine,
            marker,
            definition.MemberTemplate.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", Environment.NewLine, StringComparison.Ordinal));

    /// <summary>Adds the capability's permissions, returning a note or null when nothing changed.</summary>
    private static string? AddPermissions(WorkspaceContext workspace, CapabilityDefinition definition)
    {
        if (definition.Permissions.Count == 0)
        {
            return null;
        }

        var document = ManifestDocument.Load(workspace.ManifestPath);
        var current = document.Permissions.ToList();

        // The missing permissions are the ones the capability wants that the manifest does not
        // already declare. This used to be written the other way round - filtering the manifest's
        // own permissions - so it re-added what was there and never added what was missing, and
        // every capability silently reported success while declaring nothing.
        var added = definition.Permissions.Where(p => !current.Contains(p, StringComparer.Ordinal)).ToList();
        if (added.Count == 0)
        {
            return null;
        }

        current.AddRange(added);
        document.SetPermissions([.. current]);
        document.Save(workspace.ManifestPath);
        return $"{added.Count} permission(s) added";
    }

    /// <summary>
    /// The capabilities a project already has, read from the markers in
    /// <c>PluginIntegration.cs</c>.
    /// </summary>
    public static IReadOnlyList<string> ScaffoldsPresent(WorkspaceContext workspace)
    {
        var path = Path.Combine(workspace.PluginProjectDirectory, "PluginIntegration.cs");
        if (!File.Exists(path))
        {
            return [];
        }

        var source = File.ReadAllText(path);
        return
        [
            .. CapabilityDefinitions.All
                .Where(c => source.Contains(CapabilityMarker(c), StringComparison.Ordinal))
                .Select(c => c.Id),
        ];
    }
}
