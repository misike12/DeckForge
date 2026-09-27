using DeckForge.CodeGen.Generation;
using DeckForge.Core.Plugins;

namespace DeckForge.CodeGen.Capabilities;

/// <summary>
/// Turns the capability presets chosen in the new-project wizard into code.
/// </summary>
/// <remarks>
/// The extension point was documented and wired - the generator resolved contributors from DI and
/// ran them in order - but nothing was ever registered, so the pipeline was a no-op and a preset
/// selected in the wizard changed nothing about the project it produced. This is the first
/// contributor, and it is the same <see cref="CapabilityDefinitions"/> the Capabilities page and
/// <see cref="CapabilityScaffolder"/> use, so a preset and a later "add to plugin" produce the same
/// code rather than two things that can drift.
/// <para>
/// It fills <see cref="ProjectContentBuilder.ExtraIntegrationUsings"/>,
/// <c>ExtraIntegrationInterfaces</c> and <c>ExtraIntegrationMembers</c>, which the template
/// factory consumes when it writes PluginIntegration.cs. Those three lists were declared,
/// documented and read by nothing until now.
/// </para>
/// </remarks>
public sealed class CapabilityPresetContributor : IProjectContentContributor
{
    /// <inheritdoc />
    public string Id => "capability-presets";

    /// <summary>
    /// Runs after the stock files exist, which is what lets it patch PluginIntegration.cs, and
    /// before any extension that wants to adjust the code it produced.
    /// </summary>
    public int Order => 10;

    /// <summary>
    /// The capability ids this contributor implements.
    /// </summary>
    /// <remarks>
    /// Derived from the definitions rather than written out by hand, so adding a capability
    /// definition makes it selectable in the wizard without touching this class. The test compares
    /// it against the definitions so the wizard and the Capabilities page cannot drift apart.
    /// </remarks>
    public static IReadOnlyList<string> PresetIds { get; } = [.. CapabilityDefinitions.Ids];

    IReadOnlyList<string> IProjectContentContributor.PresetIds => PresetIds;

    /// <inheritdoc />
    public void Contribute(ProjectContentBuilder builder, NewProjectOptions options)
    {
        foreach (var id in options.CapabilityPresets)
        {
            var definition = CapabilityDefinitions.Find(id);
            if (definition is null)
            {
                continue;
            }

            foreach (var @namespace in definition.Usings)
            {
                if (!builder.ExtraIntegrationUsings.Contains(@namespace, StringComparer.Ordinal))
                {
                    builder.ExtraIntegrationUsings.Add(@namespace);
                }
            }

            foreach (var contract in definition.Interfaces)
            {
                if (!builder.ExtraIntegrationInterfaces.Contains(contract, StringComparer.Ordinal))
                {
                    builder.ExtraIntegrationInterfaces.Add(contract);
                }
            }

            foreach (var permission in definition.Permissions)
            {
                builder.ExtraPermissions.Add(permission);
            }

            foreach (var (key, value) in definition.Strings)
            {
                builder.AddStringKey(key, value);
            }

            // Members are appended with their marker so the same block the scaffolder writes is
            // recognised later and not added twice.
            var body = string.Join(
                Environment.NewLine,
                CapabilityMarker(definition),
                definition.MemberTemplate
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\n", Environment.NewLine, StringComparison.Ordinal));

            if (!builder.ExtraIntegrationMembers.Any(m => m.Contains(Marker(definition), StringComparison.Ordinal)))
            {
                builder.ExtraIntegrationMembers.Add(body);
            }
        }
    }

    /// <summary>The same marker <see cref="CapabilityScaffolder"/> uses, so the two agree.</summary>
    internal static string CapabilityMarker(CapabilityDefinition definition) => Marker(definition);

    private static string Marker(CapabilityDefinition definition) =>
        $"// <deckforge:capability id=\"{definition.Id}\" />";
}
