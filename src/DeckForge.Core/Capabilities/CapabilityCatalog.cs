namespace DeckForge.Core.Capabilities;

/// <summary>
/// Catalog of every plugin capability Macro Deck 3 documents, with the metadata the UI
/// needs to render editor cards, categories and documentation links.
/// New capabilities are added by appending a <see cref="CapabilityDescriptor"/> - no other
/// file changes.
/// </summary>
public static class CapabilityCatalog
{
    public static readonly CapabilityDescriptor Actions = new()
    {
        Id = "actions",
        Name = "Actions",
        Summary = "Do something when a button is pressed or a flow runs.",
        Category = CapabilityCategory.Buttons,
        Interface = "IActionDefinition",
        DocsPath = "features/actions",
        IsInDefaultPresets = true,
        Glyph = "PlayCircle24",
    };

    public static readonly CapabilityDescriptor ButtonStates = new()
    {
        Id = "button-states",
        Name = "Button states",
        Summary = "Show on/off, muted, recording and similar states on a button.",
        Category = CapabilityCategory.Buttons,
        Interface = "IStateProviderActionDefinition",
        DocsPath = "features/button-states",
        Glyph = "ToggleMultiple24",
    };

    public static readonly CapabilityDescriptor ButtonIcons = new()
    {
        Id = "button-icons",
        Name = "Button icons",
        Summary = "Draw album art, avatars or weather imagery on a button.",
        Category = CapabilityCategory.Buttons,
        Interface = "IIconProviderActionDefinition",
        DocsPath = "features/button-icons",
        Glyph = "DrawImage24",
    };

    public static readonly CapabilityDescriptor Variables = new()
    {
        Id = "variables",
        Name = "Variables",
        Summary = "Expose live values such as {{ vars.music_track }}.",
        Category = CapabilityCategory.Data,
        Interface = "IVariableProvider",
        Permissions = ["host:variables", "host:variable-values"],
        DocsPath = "features/variables",
        IsInDefaultPresets = true,
        Glyph = "DataUsage24",
    };

    public static readonly CapabilityDescriptor Events = new()
    {
        Id = "events",
        Name = "Events",
        Summary = "Let users trigger automation when something happens.",
        Category = CapabilityCategory.Data,
        Interface = "IEventProvider",
        Permissions = ["events:publish", "host:event-bindings"],
        DocsPath = "features/events",
        Glyph = "Megaphone24",
    };

    public static readonly CapabilityDescriptor Messaging = new()
    {
        Id = "messaging",
        Name = "Messaging",
        Summary = "Talk to other plugins and integrations by topic.",
        Category = CapabilityCategory.Data,
        Interface = "IIntegrationContext.Messages",
        Permissions = ["host:messaging"],
        DocsPath = "features/messaging",
        Glyph = "MailInbox24",
    };

    public static readonly CapabilityDescriptor DeckNavigation = new()
    {
        Id = "deck",
        Name = "Deck navigation",
        Summary = "Navigate folders and profiles, find which folder each client has open.",
        Category = CapabilityCategory.DeckAndClients,
        Interface = "IIntegrationContext.Deck",
        Permissions = ["host:deck"],
        DocsPath = "features/deck",
        Glyph = "WindowApps24",
    };

    public static readonly CapabilityDescriptor MusicPlayers = new()
    {
        Id = "music-players",
        Name = "Music players",
        Summary = "Drive the Music Player widget and reuse ready-made music actions.",
        Category = CapabilityCategory.DeckAndClients,
        Interface = "IMusicPlayerProvider",
        DocsPath = "features/music-players",
        Glyph = "MusicNote124",
    };

    public static readonly CapabilityDescriptor Weather = new()
    {
        Id = "weather",
        Name = "Weather",
        Summary = "Supply weather stations to Macro Deck's weather features.",
        Category = CapabilityCategory.DeckAndClients,
        Interface = "IWeatherProvider",
        DocsPath = "features/weather",
        Glyph = "WeatherSunny24",
    };

    public static readonly CapabilityDescriptor VirtualProfiles = new()
    {
        Id = "virtual-profiles",
        Name = "Virtual profiles",
        Summary = "Offer profiles the plugin generates.",
        Category = CapabilityCategory.DeckAndClients,
        Interface = "IProfileProvider",
        DocsPath = "features/virtual-profiles",
        Glyph = "PersonAccounts24",
    };

    public static readonly CapabilityDescriptor ConfigFlows = new()
    {
        Id = "config-flows",
        Name = "Setup flows",
        Summary = "Ask for connection details, API keys or an OAuth login.",
        Category = CapabilityCategory.SetupAndMaintenance,
        Interface = "IConfigFlowProvider",
        Permissions = ["host:config"],
        DocsPath = "features/setup-flows",
        Glyph = "Flowchart24",
    };

    public static readonly CapabilityDescriptor IntegrationIssues = new()
    {
        Id = "integration-issues",
        Name = "Integration issues",
        Summary = "Tell the user what is wrong and how to fix it.",
        Category = CapabilityCategory.SetupAndMaintenance,
        Interface = "IIntegrationIssueProvider",
        DocsPath = "features/integration-issues",
        Glyph = "Warning24",
    };

    public static readonly CapabilityDescriptor SettingsMigrations = new()
    {
        Id = "settings-migrations",
        Name = "Settings migrations",
        Summary = "Take over a user's setup from Macro Deck 2.",
        Category = CapabilityCategory.SetupAndMaintenance,
        Interface = "IMigrationProvider",
        DocsPath = "features/settings-migrations",
        Glyph = "ArrowSync24",
    };

    public static readonly CapabilityDescriptor Localization = new()
    {
        Id = "localization",
        Name = "Localization",
        Summary = "Ship every user-facing string in every language.",
        Category = CapabilityCategory.SetupAndMaintenance,
        DocsPath = "features/localization",
        IsInDefaultPresets = true,
        Glyph = "Translate24",
    };

    public static readonly CapabilityDescriptor Testing = new()
    {
        Id = "testing",
        Name = "Testing",
        Summary = "Test your integration without a running Macro Deck.",
        Category = CapabilityCategory.SetupAndMaintenance,
        DocsPath = "features/testing",
        IsInDefaultPresets = true,
        Glyph = "Beaker24",
    };

    public static readonly CapabilityDescriptor Logging = new()
    {
        Id = "logging",
        Name = "Logging",
        Summary = "Write to the Macro Deck log, and report a standing condition as a resolvable issue.",
        Category = CapabilityCategory.SetupAndMaintenance,
        Interface = "MacroDeck.Sdk.Logging.IntegrationLog",
        DocsPath = "features/logging",
        Glyph = "TextBulletListSquare24",
    };

    public static readonly CapabilityDescriptor Devices = new()
    {
        Id = "devices",
        Name = "Devices",
        Summary = "Connect hardware or custom clients as Macro Deck devices.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "IDeviceProvider",
        Permissions = ["host:devices", "device:usb"],
        DocsPath = "features/devices",
        Glyph = "Board24",
    };

    public static readonly CapabilityDescriptor Layouts = new()
    {
        Id = "layouts",
        Name = "Layouts",
        Summary = "Describe a device's regions and geometry.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "ILayoutProvider",
        Permissions = ["host:layouts"],
        DocsPath = "features/layouts",
        Glyph = "Grid24",
    };

    public static readonly CapabilityDescriptor AndroidDevices = new()
    {
        Id = "android-devices",
        Name = "Android devices",
        Summary = "Run shell commands and install apps via Macro Deck's ADB.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "IAndroidDeviceManager",
        Permissions = ["host:adb"],
        DocsPath = "features/android-devices",
        Glyph = "Phone24",
    };

    public static readonly CapabilityDescriptor MacroDeckUi = new()
    {
        Id = "macrodeck-ui",
        Name = "Macro Deck UI",
        Summary = "Draw configuration views, widgets and folder views.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "IUiProvider",
        Permissions = ["assets:upload"],
        DocsPath = "ui/index",
        Glyph = "Window24",
    };

    public static readonly CapabilityDescriptor WidgetTypes = new()
    {
        Id = "widget-types",
        Name = "Widget types",
        Summary = "Add deck widgets beside Macro Deck's own.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "IWidgetTypeProvider",
        DocsPath = "ui/views/widget-types",
        Glyph = "PuzzlePiece24",
    };

    public static readonly CapabilityDescriptor FolderViews = new()
    {
        Id = "folder-views",
        Name = "Folder views",
        Summary = "Replace a folder's button grid with your own rendering.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "IFolderViewProvider",
        Permissions = ["host:folder-views"],
        DocsPath = "ui/views/folder-views",
        Glyph = "Folder24",
    };

    public static readonly CapabilityDescriptor Screensavers = new()
    {
        Id = "screensavers",
        Name = "Screensavers",
        Summary = "Show something of your own on an idle device.",
        Category = CapabilityCategory.HardwareAndSurfaces,
        Interface = "IScreenSaverProvider",
        Permissions = ["host:screensavers"],
        DocsPath = "ui/views/screensavers",
        Glyph = "Screenshot24",
    };

    /// <summary>All descriptors in documentation order.</summary>
    public static IReadOnlyList<CapabilityDescriptor> All { get; } =
    [
        Actions, ButtonStates, ButtonIcons,
        Variables, Events, Messaging,
        DeckNavigation, MusicPlayers, Weather, VirtualProfiles,
        ConfigFlows, IntegrationIssues, SettingsMigrations, Localization, Testing, Logging,
        Devices, Layouts, AndroidDevices, MacroDeckUi, WidgetTypes, FolderViews, Screensavers,
    ];

    public static IReadOnlyList<CapabilityDescriptor> DefaultPresets { get; } =
        [.. All.Where(c => c.IsInDefaultPresets)];

    public static CapabilityDescriptor? Find(string id) =>
        All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}
