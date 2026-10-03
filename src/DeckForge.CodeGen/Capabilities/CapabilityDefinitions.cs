namespace DeckForge.CodeGen.Capabilities;

/// <summary>What a capability needs in order to be scaffolded into a plugin.</summary>
/// <remarks>
/// This is the whole capability model in one record. It exists because the previous implementation
/// carried a 22-case switch with two arms and a "not implemented yet" string for the other
/// twenty, behind a UI that enabled the button for all of them anyway.
/// </remarks>
public sealed record CapabilityDefinition
{
    /// <summary>The stable machine id, matching <c>CapabilityCatalog</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The interfaces the integration declares, in the order they are added.</summary>
    public required IReadOnlyList<string> Interfaces { get; init; }

    /// <summary>The namespaces to import, which is not derivable from the interface name.</summary>
    public required IReadOnlyList<string> Usings { get; init; }

    /// <summary>Host permissions the capability needs, or an empty list.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>
    /// The C# members to add to the integration class body. <c>$Name</c>, <c>$Namespace</c> and
    /// <c>$Logger</c> are substituted.
    /// </summary>
    public required string MemberTemplate { get; init; }

    /// <summary>resx keys to add, relative to a <c>$Name</c> group.</summary>
    public IReadOnlyList<(string Key, string Value)> Strings { get; init; } = [];

    /// <summary>True when the capability needs a user-facing string, so resx keys are worth adding.</summary>
    public bool NeedsStrings => Strings.Count > 0;

    /// <summary>One line for the gallery card, saying what the author still has to do.</summary>
    public required string NextStep { get; init; }

    /// <summary>
    /// Whether this capability is ticked in the new-project wizard.
    /// </summary>
    /// <remarks>
    /// The selection lives on the shared definition rather than in a view model, because the wizard
    /// and the Capabilities page both show the same list. Two copies of "is this ticked" drift
    /// within a session, and the wizard's copy was the one that was never read.
    /// </remarks>
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            SelectedChanged?.Invoke(this, value);
        }
    }

    private bool _selected;

    /// <summary>Raised when <see cref="IsSelected"/> changes, so a view model can track the ids.</summary>
    public event EventHandler<bool>? SelectedChanged;
}

/// <summary>
/// The twenty-three capabilities, each with the real interface, namespace, permission and member
/// template the SDK expects.
/// </summary>
/// <remarks>
/// Five of them - devices, layouts, folder views, screensavers and widget types - share one shape:
/// a provider with a <c>ProviderName</c>, an <c>InitializeAsync</c> that takes a context, and a
/// <c>Get…()</c> that restates its catalogue so the host can recover after a reconnect. They share
/// <see cref="Provider"/> rather than repeating the same template five times.
/// <para>
/// Every signature in these templates was read out of the SDK rather than assumed. Each member is
/// either abstract in the interface or carries a default implementation, so the generated class
/// compiles while still leaving the author something obvious to replace. The compile test that
/// scaffolds all twenty-three and builds the result is the check that keeps it that way.
/// </para>
/// </remarks>
public static class CapabilityDefinitions
{
    /// <summary>Builds a provider-shaped capability from its descriptor type and context type.</summary>
    /// <remarks>
    /// Every member is an explicit interface implementation, and that is not a style choice. One
    /// integration class routinely implements five or six provider interfaces at once, and they
    /// all declare <c>ProviderName</c> - nine of the twenty-three capabilities do. Implicit
    /// implementations collide the moment the second is added, and explicit ones do not. It also
    /// lets each provider report its own name, which a single shared property could not.
    /// </remarks>
    private static CapabilityDefinition Provider(
        string id,
        string @interface,
        string ns,
        string contextType,
        string descriptorType,
        string getMethod,
        string providerName,
        IReadOnlyList<string> permissions,
        string nextStep,
        bool needsShutdown = false)
    {
        var shutdown = needsShutdown
            ? """

                // Only IDeviceProvider requires a shutdown member, and it is abstract. Everything
                // else the provider acquires belongs in the integration's own ShutdownAsync, so
                // there is one place that tears the plugin down rather than one per interface.
                Task IDeviceProvider.ShutdownAsync(CancellationToken cancellationToken = default)
                {
                    _logger.Information("The devices provider is shutting down.");
                    // TODO: stop discovery and release whatever InitializeAsync acquired.
                    return Task.CompletedTask;
                }
                """
            : string.Empty;

        return new CapabilityDefinition
        {
            Id = id,
            Interfaces = [@interface],
            Usings = [ns],
            Permissions = permissions,
            MemberTemplate = $$"""
                string {{@interface}}.ProviderName => {{CSharpLiteral(providerName)}};

                // Registers whatever is already available, then keeps the host's view in step as the
                // hardware or configuration changes.
                Task {{@interface}}.InitializeAsync({{contextType}} context, CancellationToken cancellationToken = default)
                {
                    _logger.Information("The {{providerName}} provider is initializing.");
                    // TODO: register whatever is already available, then keep registering,
                    // updating and unregistering as it changes.
                    return Task.CompletedTask;
                }
                {{shutdown}}

                // Restated so the host can recover its view after a reconnect. The interface already
                // defaults this to an empty list, so it is only here to show the shape.
                IReadOnlyList<{{descriptorType}}> {{@interface}}.{{getMethod}}() =>
                [
                    // TODO: describe each {{descriptorType}} this provider offers.
                ];
                """,
            NextStep = nextStep,
        };
    }

    /// <summary>A quoted C# string literal.</summary>
    private static string CSharpLiteral(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public static IReadOnlyList<CapabilityDefinition> All { get; } =
    [
        // ------------------------------------------------------------ actions
        new()
        {
            Id = "actions",
            Interfaces = [],
            Usings = [],
            Permissions = [],
            MemberTemplate = """
                // Actions are added from the Actions editor, which appends to the collection below.
                // The stock example action is the template for the shape: an IActionDefinition
                // whose Name and Description are LocalizedText, and an IActionExecutor that returns
                // an ActionResult rather than throwing.
                """,
            NextStep = "Add actions from the Actions editor.",
        },

        new()
        {
            Id = "button-states",
            Interfaces = [],
            Usings = ["MacroDeck.Sdk.Actions"],
            Permissions = [],
            MemberTemplate = """
                // IStateProviderActionDefinition is a free-standing companion to IActionDefinition:
                // implement both on the same class. It reports the states a widget on this action can
                // show, and the named ones are what protocol major 2 identifies a state by.
                // TODO: implement GetActionStateAsync on an action that has states.
                """,
            NextStep = "Implement IStateProviderActionDefinition on an action that has states.",
        },

        new()
        {
            Id = "button-icons",
            Interfaces = [],
            Usings = ["MacroDeck.Sdk.Actions"],
            Permissions = ["assets:upload"],
            MemberTemplate = """
                // IIconProviderActionDefinition is the other free-standing companion. Return an
                // ActionIconSnapshot referencing a bundled icon pack by key and name; the host
                // resolves it to bytes through the assets pipeline, so no image data crosses the
                // descriptor.
                // TODO: implement GetActionIconAsync on an action that has an icon.
                """,
            NextStep = "Implement IIconProviderActionDefinition and declare bundled icon packs.",
        },

        // ------------------------------------------------------------ data
        new()
        {
            Id = "variables",
            Interfaces = ["IVariableProvider"],
            Usings = ["MacroDeck.Sdk.Variables"],
            Permissions = ["host:variables", "host:variable-values"],
            MemberTemplate = """
                // Only Variables and ReadAsync are abstract; everything else has a default, so a
                // read-only provider implements nothing more. Type and Materialization are both
                // required, which is why a definition is a factory call rather than an initializer.
                public IReadOnlyList<VariableDefinition> Variables { get; } =
                [
                    VariableDefinition.Eager("example-reading", VariableType.Text) with { Id = "example" },
                ];

                public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
                {
                    // A disconnected session answers Unavailable rather than a stale value.
                    return ValueTask.FromResult(localId switch
                    {
                        "example" => VariableReading.Of("replace me"),
                        _ => VariableReading.Unavailable,
                    });
                }
                """,
            Strings = [("Variables.Example.Name", "Example")],
            NextStep = "Add the variables your plugin publishes, then call SetValueAsync to change one.",
        },

        new()
        {
            Id = "events",
            Interfaces = ["IEventProvider"],
            Usings = ["MacroDeck.Sdk.Events"],
            Permissions = ["events:publish"],
            MemberTemplate = """
                // Explicit because every provider interface declares ProviderName; see the note
                // on CapabilityDefinitions.Provider.
                string IEventProvider.ProviderName => "TODO: event provider";

                // EventDefinitions is the only abstract member. An EventDefinition has no builder:
                // Id and Name are required and object-initializer syntax is the only construction
                // path. ConfigurationParameters are what the user authors on the trigger;
                // PayloadParameters describe an occurrence and are never rendered as an input.
                IReadOnlyList<EventDefinition> IEventProvider.EventDefinitions { get; } =
                [
                    new()
                    {
                        Id = "something-happened",
                        Name = Strings.Events.SomethingHappened.Name(),
                        ConfigurationParameters = [],
                        PayloadParameters = [],
                    },
                ];
                """,
            Strings =
            [
                ("Events.SomethingHappened.Name", "Something happened"),
                ("Events.SomethingHappened.Description", "Raised when something happens."),
            ],
            NextStep = "Add events, then publish one with context.Events.Publish(id, payload).",
        },

        new()
        {
            Id = "messaging",
            Interfaces = [],
            Usings = ["MacroDeck.Sdk.Messaging"],
            Permissions = ["host:messaging"],
            MemberTemplate = """
                // The message channel is reached through context.Messages, not by implementing an
                // interface. A registration returns an IAsyncDisposable, and disposing it
                // unregisters - so keep the handle and release it in the integration's ShutdownAsync.
                // TODO: in InitializeAsync:
                //   _subscription = await context.Messages.SubscribeAsync("topic", (message, ct) => { ... });
                """,
            NextStep = "Subscribe to topics in InitializeAsync and hold the registration.",
        },

        // ------------------------------------------------------------ deck and clients
        new()
        {
            Id = "deck",
            Interfaces = [],
            Usings = [],
            Permissions = ["host:deck"],
            MemberTemplate = """
                // Deck navigation is reached through context.Deck, which is non-nullable but null
                // until the session is established - so check rather than dereference. It offers
                // ChangeFolderAsync, ChangeProfileAsync, GoToParentAsync and GoBackAsync; there is
                // no OpenFolder, and GetFolders/GetProfiles/GetClients are served from the last
                // host state push rather than a round trip.
                // TODO: in an action executor:
                //   await context.Deck.ChangeFolderAsync(folderId, context.OriginClientId, context.CancellationToken);
                """,
            NextStep = "Use context.Deck from an action to move the pressing client.",
        },

        new()
        {
            Id = "music-players",
            Interfaces = ["IMusicPlayerProvider"],
            Usings = ["MacroDeck.Sdk.MusicPlayer"],
            Permissions = [],
            MemberTemplate = """
                // Both members are abstract. They are explicit implementations because
                // IWeatherProvider.GetInstances has the same signature as
                // IMusicPlayerProvider.GetInstances and a different return type - a single class
                // cannot implement both implicitly, and adding weather after a music player would
                // otherwise have produced a duplicate-member error.
                // GetPlayer returning null is the correct answer for an instance the provider no
                // longer has: the host falls back to a disconnect message rather than treating it
                // as an error, because a music service disappearing is ordinary, not exceptional.
                string IMusicPlayerProvider.ProviderName => "TODO: music player provider";

                IReadOnlyList<MusicPlayerInstance> IMusicPlayerProvider.GetInstances() =>
                [
                    // TODO: one entry per player the user has connected.
                    new("player-1", "TODO: player name"),
                ];

                IMusicPlayer? IMusicPlayerProvider.GetPlayer(string instanceId)
                {
                    // TODO: resolve instanceId against your own state and return the player.
                    _logger.Debug("No music player is registered for instance {InstanceId}.", instanceId);
                    return null;
                }
                """,
            Strings = [("MusicPlayers.Player1.Name", "TODO: player name")],
            NextStep = "Implement IMusicPlayerProvider and register a player instance.",
        },

        new()
        {
            Id = "weather",
            Interfaces = ["IWeatherProvider"],
            Usings = ["MacroDeck.Sdk.Weather"],
            Permissions = [],
            MemberTemplate = """
                // Explicit for the same reason as the music player above: GetInstances and
                // GetStation are the same signatures with different return types. A station is
                // what the host polls; it derives WeatherSnapshot values and the host normalises
                // them. Returning null is a decline, the same answer as for a music player.
                string IWeatherProvider.ProviderName => "TODO: weather provider";

                IReadOnlyList<WeatherStationInstance> IWeatherProvider.GetInstances() =>
                [
                    // TODO: one entry per station the user has added.
                    new("station-1", "TODO: station name"),
                ];

                IWeatherStation? IWeatherProvider.GetStation(string instanceId)
                {
                    // TODO: resolve instanceId against your own state and return the station.
                    _logger.Debug("No weather station is registered for instance {InstanceId}.", instanceId);
                    return null;
                }
                """,
            Strings = [("Weather.Station1.Name", "TODO: station name")],
            NextStep = "Implement IWeatherProvider and register a station.",
        },

        new()
        {
            Id = "virtual-profiles",
            Interfaces = ["IProfileProvider"],
            Usings = ["MacroDeck.Sdk.Profiles"],
            Permissions = ["host:deck", "host:widgets"],
            MemberTemplate = """
                // A virtual profile is a folder tree the provider owns. The host asks for it and
                // renders it; the provider never draws anything itself.
                string IProfileProvider.ProviderName => "TODO: virtual profile provider";

                IReadOnlyList<VirtualProfileDescriptor> IProfileProvider.GetProfiles() =>
                [
                    // TODO: describe the folder tree this provider offers.
                ];
                """,
            NextStep = "Implement IProfileProvider and describe the profile.",
        },

        // ------------------------------------------------------------ setup and maintenance
        new()
        {
            Id = "config-flows",
            Interfaces = ["IConfigFlowProvider"],
            Usings = ["MacroDeck.Sdk.ConfigFlow"],
            Permissions = ["host:config"],
            MemberTemplate = """
                // One IConfigFlow per setup session; the host calls CreateConfigFlow() each time.
                // SubmitAsync's stepId is a plain string and the user can go back, so dispatch on it
                // rather than tracking a position. The flow below completes immediately, which is
                // a valid answer - it is what a capability-only plugin should return until a real
                // setup flow is added from the Setup Flow editor.
                //
                // Block bodies rather than expression bodies, and that is for the Visual editor: a
                // config-flow target is spliced into the braces of the method its anchor names, and an
                // expression-bodied method has none. The stock member was rewritten instead of
                // special-cased for it, because a second splicing path for one target kind is
                // exactly what Part 8.5 asks not to happen.
                public IConfigFlow CreateConfigFlow() => new SetupFlow();

                private sealed class SetupFlow : IConfigFlow
                {
                    public Task<ConfigFlowResult> StartAsync(
                        IConfigFlowContext context,
                        CancellationToken cancellationToken)
                    {
                        return Task.FromResult(ConfigFlowResult.Complete("Setup complete."));
                    }

                    public Task<ConfigFlowResult> SubmitAsync(
                        string stepId,
                        IReadOnlyDictionary<string, object?> input,
                        IConfigFlowContext context,
                        CancellationToken cancellationToken)
                    {
                        return Task.FromResult(ConfigFlowResult.Complete("Setup complete."));
                    }
                }
                """,NextStep = "Add a setup flow from the Setup Flow editor, then return it here.",
        },

        new()
        {
            Id = "integration-issues",
            Interfaces = ["IIntegrationIssueProvider"],
            Usings = ["MacroDeck.Sdk.Issues"],
            Permissions = [],
            MemberTemplate = """
                // GetIssuesAsync is polled to render badges, so it must be cheap and non-blocking:
                // return cached state and do the real work in ResolveIssueAsync. A standing
                // condition - a missing permission, invalid credentials, a disconnected service -
                // belongs here rather than in a notification, which is session-scoped and does not
                // survive a host restart.
                // Answering Failed is honest: there is nothing to resolve until a real condition
                // is reported, and claiming success would make the badge disappear on click.
                Task<IReadOnlyList<IntegrationIssue>> IIntegrationIssueProvider.GetIssuesAsync(
                    CancellationToken cancellationToken = default)
                    => Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);

                Task<IssueResolution> IIntegrationIssueProvider.ResolveIssueAsync(
                    string issueId,
                    CancellationToken cancellationToken = default)
                    => Task.FromResult(IssueResolution.Failed("There is nothing to resolve yet."));
                """,
            NextStep = "Report a standing condition and what the user should do about it.",
        },

        new()
        {
            Id = "settings-migrations",
            Interfaces = ["IMigrationProvider"],
            Usings = ["MacroDeck.Sdk.Migration"],
            Permissions = [],
            MemberTemplate = """
                // The capability is the list of sources rather than the translation itself: one
                // integration commonly reads several applications, and each names its own source.
                // Answering null from MigrateActionAsync is the correct answer when there is no
                // equivalent - the host then keeps the original as a placeholder, which is better
                // than an action that quietly does something else.
                public IReadOnlyList<IIntegrationMigration> Migrations { get; } =
                [
                    // TODO: one IIntegrationMigration per application this plugin can take over from.
                ];
                """,
            NextStep = "Describe the applications this plugin can take a setup over from.",
        },

        new()
        {
            Id = "localization",
            Interfaces = [],
            Usings = [],
            Permissions = [],
            MemberTemplate = """
                // Localization is already wired: Program.cs calls UseLocalization(Strings.
                // LocalizationCatalog), and the catalog's scope is composed from manifest.json's id.
                // Add a language by adding Localization/Strings.<culture>.resx beside Strings.resx;
                // build and pack recompute manifest.json's languages from the files it finds.
                // The fallback chain is requested culture, then neutral, then the catalog default.
                """,
            NextStep = "Add Languages/Strings.<culture>.resx files, then use Strings.* everywhere.",
        },

        new()
        {
            Id = "testing",
            Interfaces = [],
            Usings = [],
            Permissions = [],
            MemberTemplate = """
                // Testing is already wired: the test project references MacroDeck.Plugin.Testing
                // and builds a PluginTestHarness over this integration. Add cases alongside the
                // stock ones and run macrodeck-plugin test --project . to run the conformance
                // suite as well.
                """,
            NextStep = "Add cases to the test project, then run the conformance suite.",
        },

        new()
        {
            Id = "logging",
            Interfaces = [],
            Usings = [],
            Permissions = [],
            MemberTemplate = """
                // Logging is already wired: Program.cs calls UseMacroDeckLogging(), which attaches
                // the host's log sink. The ILogger the integration takes in its constructor is
                // Serilog's, so structured properties and message templates work as usual and
                // reach Macro Deck's log rather than only the console.
                // A standing condition belongs in IIntegrationIssueProvider, not in a log line.
                """,
            NextStep = "Nothing to do; log through the injected Serilog ILogger.",
        },

        // ------------------------------------------------------------ hardware and surfaces
        Provider(
            "devices", "IDeviceProvider", "MacroDeck.Sdk.Devices", "IDeviceProviderContext",
            "DeviceDescriptor",
            "GetDevices", "My devices",
            ["host:devices"],
            "Describe the hardware or custom clients this plugin brings into Macro Deck.",
            needsShutdown: true),

        Provider(
            "layouts", "ILayoutProvider", "MacroDeck.Sdk.Layouts", "ILayoutProviderContext",
            "LayoutDescriptor",
            "GetLayouts", "My layouts",
            ["host:layouts"],
            "Describe the surface of a device. A layout says what a surface is, never what is on it."),

        Provider(
            "folder-views", "IFolderViewProvider", "MacroDeck.Sdk.FolderViews", "IFolderViewProviderContext",
            "FolderViewDescriptor",
            "GetFolderViews", "My folder views",
            ["host:folder-views"],
            "Offer complete renderings of a folder, served through IUiProvider."),

        Provider(
            "screensavers", "IScreenSaverProvider", "MacroDeck.Sdk.ScreenSavers", "IScreenSaverProviderContext",
            "ScreenSaverDescriptor",
            "GetScreenSavers", "My screensavers",
            ["host:screensavers"],
            "Offer screensavers, served through IUiProvider when a device opens a screensaver surface."),

        Provider(
            "widget-types", "IWidgetTypeProvider", "MacroDeck.Sdk.Widgets", "IWidgetTypeProviderContext",
            "WidgetTypeDescriptor",
            "GetWidgetTypes", "My widget types",
            ["host:widget-types"],
            "Offer custom widget types. A widget type declares which types it offers; IUiProvider builds the view."),

        new()
        {
            Id = "android-devices",
            Interfaces = ["IAndroidDeviceManager"],
            Usings = ["MacroDeck.Sdk.Android"],
            Permissions = ["host:adb"],
            MemberTemplate = """
                // Not a provider: the host owns the ADB connection and hands the manager whatever
                // devices it can already see. Access is the honest gate to read first - AdbNotAllowed
                // means the manifest does not declare host:adb, or plugin access is switched off in
                // Macro Deck, and no amount of retrying will change that.
                // Events are raised by the host as devices appear, vanish and change state.
                public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected;

                public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected;

                public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged;

                public event EventHandler? AccessChanged;

                public AndroidDeviceAccess Access { get; private set; } = AndroidDeviceAccess.Unsupported;

                public IReadOnlyCollection<IAndroidDevice> Devices { get; private set; } = [];

                public IAndroidDevice? FindDevice(string serial)
                {
                    // TODO: replace with your own lookup if the host's collection is not enough.
                    return Devices.FirstOrDefault(
                        device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
                }

                public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
                {
                    // TODO: hand the address to the host and return the device it connects.
                    throw new AndroidDeviceException(
                        AndroidDeviceErrorCode.InvalidArgument,
                        "ConnectAsync is not implemented yet.");
                }
                """,
            NextStep = "Read Access, then implement ConnectAsync for the addresses your plugin supports.",
        },

        new()
        {
            Id = "macrodeck-ui",
            Interfaces = ["IUiProvider"],
            Usings = ["MacroDeck.Sdk.Ui"],
            Permissions = ["assets:upload"],
            MemberTemplate = """
                // Surfaces is read before anything is initialized, so it must be side-effect free
                // and must not need a live connection. CreateSessionAsync returning null is a
                // decline, which is the correct answer for a surface kind this plugin does not
                // serve - not an error.
                public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
                [
                    new() { Kind = "widget", SessionMode = "shared" },
                ];

                public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
                    => Task.FromResult<IUiSession?>(null);
                """,
            NextStep = "Build a session per surface kind, and dispose the view in DisposeAsync.",
        },
    ];

    private static readonly Dictionary<string, CapabilityDefinition> ById =
        All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public static CapabilityDefinition? Find(string? id) =>
        id is not null && ById.TryGetValue(id, out var definition) ? definition : null;

    public static bool IsKnown(string? id) => Find(id) is not null;

    public static IReadOnlyList<string> Ids { get; } = [.. All.Select(c => c.Id)];
}
