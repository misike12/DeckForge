namespace DeckForge.Core.Plugins;

/// <summary>One known permission with its group and a one-line explanation.</summary>
public sealed record PermissionInfo(string Name, PermissionGroup Group, string Explanation, string DocsPath = "reference/manifest");

/// <summary>Permission groups, matching the manifest reference's grouping.</summary>
public enum PermissionGroup
{
    /// <summary>host:* - Macro Deck host APIs.</summary>
    Host,
    /// <summary>events:*/assets:* - content the plugin publishes or uploads.</summary>
    Publishing,
    /// <summary>net:/fs:/process:/device: - outward-reaching capabilities.</summary>
    System,
}

/// <summary>
/// The known permission vocabulary from the manifest reference, grouped and explained.
/// Unknown strings are legal (warning only), so the editor also accepts custom entries.
/// </summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionInfo> All { get; } =
    [
        // ----- Host -----
        new("host:variables", PermissionGroup.Host, "Read and write shared variables ({{ vars.* }} templates other plugins and widgets can read)."),
        new("host:user-variables", PermissionGroup.Host, "Access the user's own variables that are not scoped to your plugin."),
        new("host:variable-values", PermissionGroup.Host, "Read variable values at runtime (required to resolve {{ vars.* }} yourself)."),
        new("host:config", PermissionGroup.Host, "Store setup-flow entries and read them back (config flows, GetEntriesAsync/GetSecretAsync)."),
        new("host:deck", PermissionGroup.Host, "Navigate decks: open folders/profiles, ask which folder a client has open."),
        new("host:scripts", PermissionGroup.Host, "Run or manage host scripts."),
        new("host:widgets", PermissionGroup.Host, "Interact with widgets placed on decks (state, appearance writes)."),
        new("host:notifications", PermissionGroup.Host, "Show user notifications in Macro Deck."),
        new("host:action-interactions", PermissionGroup.Host, "Trigger action interactions (run flows, press action buttons programmatically)."),
        new("host:devices", PermissionGroup.Host, "Register and manage devices (hardware or custom clients)."),
        new("host:layouts", PermissionGroup.Host, "Provide device layouts (regions and geometry)."),
        new("host:folder-views", PermissionGroup.Host, "Replace a folder's button grid with your own rendering."),
        new("host:widget-types", PermissionGroup.Host, "Register custom widget types beside Macro Deck's built-ins."),
        new("host:event-bindings", PermissionGroup.Host, "Read which triggers are bound to your events (GetBindings/BindingsChanged)."),
        new("host:screensavers", PermissionGroup.Host, "Draw screensavers on idle devices."),
        new("host:adb", PermissionGroup.Host, "Use Macro Deck's ADB bridge. The one permission the host actually enforces."),
        new("host:messaging", PermissionGroup.Host, "Publish and receive messages on the plugin-to-plugin message channel."),

        // ----- Publishing -----
        new("events:publish", PermissionGroup.Publishing, "Publish event occurrences users can bind triggers and widget flows to."),
        new("assets:upload", PermissionGroup.Publishing, "Upload icons and other resources to the host's asset pipeline."),

        // ----- System -----
        new("net:outbound", PermissionGroup.Publishing, "Make outbound network requests (HTTP APIs, websockets). Purely declarative today."),
        new("fs:user-files", PermissionGroup.System, "Read and write files inside the user-visible scope."),
        new("process:spawn", PermissionGroup.System, "Start external processes."),
        new("device:usb", PermissionGroup.System, "Talk to USB hardware directly."),
    ];

    public static PermissionGroup GroupOf(string permission) =>
        All.FirstOrDefault(p => p.Name == permission)?.Group ?? GroupForUnknown(permission);

    private static PermissionGroup GroupForUnknown(string permission) =>
        permission.StartsWith("host:", StringComparison.Ordinal) ? PermissionGroup.Host
        : permission.StartsWith("events:", StringComparison.Ordinal) || permission.StartsWith("assets:", StringComparison.Ordinal)
            ? PermissionGroup.Publishing
            : PermissionGroup.System;

    public static string? ExplanationOf(string permission) =>
        All.FirstOrDefault(p => p.Name == permission)?.Explanation;

    /// <summary>All permissions of one group, in catalog order.</summary>
    public static IEnumerable<PermissionInfo> OfGroup(PermissionGroup group) =>
        All.Where(p => p.Group == group);

    public static string Title(PermissionGroup group) => group switch
    {
        PermissionGroup.Host => "Host APIs (host:*)",
        PermissionGroup.Publishing => "Publishing (events / assets / net)",
        _ => "System access (fs / process / device)",
    };

    public static string GroupSummary(PermissionGroup group) => group switch
    {
        PermissionGroup.Host => "What your plugin may call on the Macro Deck host. Declarative except host:adb, which is enforced.",
        PermissionGroup.Publishing => "Content your plugin pushes into the host or out to the network.",
        _ => "Outward-reaching access beyond the host: files, processes, hardware.",
    };
}
