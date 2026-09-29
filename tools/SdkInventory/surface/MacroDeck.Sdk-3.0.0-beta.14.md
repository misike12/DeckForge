# MacroDeck.Sdk 3.0.0.0

Assembly: `C:\Users\Misu\.nuget\packages\macrodeck.sdk\3.0.0-beta.14\lib\net10.0\MacroDeck.Sdk.dll`
Exported types: 253

## MacroDeck.Sdk

### interface IIntegration

- property `IReadOnlyList<IActionDefinition> Actions { get; }`
- property `String Id { get; }`
- property `Boolean IsInitialized { get; }`
- property `LocalizedText Name { get; }`
- property `String Version { get; }`
- method `Task InitializeAsync(IIntegrationContext context)`
- method `Task ShutdownAsync()`

### interface IIntegrationContext

- property `IIntegrationConfig Config { get; }`
- property `IDeckNavigator Deck { get; }`
- property `IEventPublisher Events { get; }`
- property `IMessageChannel Messages { get; }`
- property `IUserNotifier Notifications { get; }`
- property `IScriptApi Scripts { get; }`
- property `IUiResourceRegistry UiResources { get; }`
- property `IUserVariableApi UserVariables { get; }`
- property `IVariableApi Variables { get; }`
- property `IWidgetApi Widgets { get; }`

### interface IIntegrationIconProvider

- property `String IconMimeType { get; }`
- method `Byte[] GetIcon()`

### interface IPluginIntegration

- property `IReadOnlyList<IActionDefinition> Actions { get; }`
- method `Task InitializeAsync(IIntegrationContext context)`
- method `Task ShutdownAsync()`

### interface ISampleIntegration


### interface ISystemIntegration

- property `Boolean IsActive { get; }`

### class MacroDeckIntegrationAttribute : Attribute

- property `MacroDeckPlatform Current { get; }`
- property `Boolean EnabledByDefault { get; set; }`
- property `MacroDeckPlatform Platforms { get; set; }`
- method `Boolean RunsHere()`

### enum MacroDeckPlatform : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Windows, MacOS, Linux, All

## MacroDeck.Sdk.Actions

### class ActionConfigurationRequest : IEquatable<ActionConfigurationRequest>

- property `IReadOnlyDictionary<String, JsonElement> Parameters { get; set; }`
- property `UiSessionRequest Session { get; set; }`
- method `ActionConfigurationRequest <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionConfigurationRequest other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionConfigurationRequest left, ActionConfigurationRequest right)`
- method `Boolean op_Inequality(ActionConfigurationRequest left, ActionConfigurationRequest right)`

### static class ActionDefinitionExtensions

- method `Boolean RunsHere(IActionDefinition action)`

### static class ActionErrorCodes

- field `String InvalidParameter = INVALID_PARAMETER`
- field `String NotConfigured = NOT_CONFIGURED`
- field `String NotConnected = NOT_CONNECTED`
- field `String NotFound = NOT_FOUND`
- field `String PermissionDenied = PERMISSION_DENIED`
- field `String ProviderError = PROVIDER_ERROR`
- field `String ProviderRejected = PROVIDER_REJECTED`
- field `String Timeout = TIMEOUT`
- field `String Unavailable = UNAVAILABLE`

### class ActionExecutionContext

- property `Int32 CallDepth { get; set; }`
- property `CancellationToken CancellationToken { get; set; }`
- property `IActionInteractions Interactions { get; set; }`
- property `String OriginClientId { get; set; }`
- property `String OwnerWidgetId { get; set; }`
- property `IReadOnlyDictionary<String, Object> Parameters { get; set; }`
- property `IUiInteractions Ui { get; set; }`

### class ActionIconContent : IEquatable<ActionIconContent>

- property `Byte[] Data { get; set; }`
- property `String MediaType { get; set; }`
- method `ActionIconContent <Clone>$()`
- method `Void Deconstruct(Byte[]& Data, String& MediaType)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionIconContent other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionIconContent left, ActionIconContent right)`
- method `Boolean op_Inequality(ActionIconContent left, ActionIconContent right)`

### class ActionIconReference : IEquatable<ActionIconReference>

- field `String IconPackType = icon-pack`
- field `String PluginIconType = plugin-icon`
- property `String Reference { get; set; }`
- property `String Type { get; set; }`
- method `ActionIconReference <Clone>$()`
- method `Void Deconstruct(String& Type, String& Reference)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionIconReference other)`
- method `Int32 GetHashCode()`
- method `ActionIconReference IconPack(String reference)`
- method `ActionIconReference PluginIcon(String key, String name)`
- method `String ToString()`
- method `Boolean op_Equality(ActionIconReference left, ActionIconReference right)`
- method `Boolean op_Inequality(ActionIconReference left, ActionIconReference right)`

### class ActionIconSnapshot : IEquatable<ActionIconSnapshot>

- property `String MediaType { get; set; }`
- property `Boolean NoIcon { get; set; }`
- property `ActionIconReference Reference { get; set; }`
- property `String Version { get; set; }`
- method `ActionIconSnapshot <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionIconSnapshot other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionIconSnapshot left, ActionIconSnapshot right)`
- method `Boolean op_Inequality(ActionIconSnapshot left, ActionIconSnapshot right)`

### class ActionParameter

- property `Boolean AllowSelf { get; set; }`
- property `Boolean AutoPrefixHttps { get; set; }`
- property `IReadOnlyList<ActionParameter> Children { get; set; }`
- property `Object DefaultValue { get; set; }`
- property `LocalizedText Description { get; set; }`
- property `Boolean DynamicOptions { get; set; }`
- property `IReadOnlyList<String> FileExtensions { get; set; }`
- property `ActionParameter ItemTemplate { get; set; }`
- property `LocalizedText Label { get; set; }`
- property `String Language { get; set; }`
- property `Boolean LiteralOnly { get; set; }`
- property `Nullable<Double> Max { get; set; }`
- property `Nullable<Int32> MaxLength { get; set; }`
- property `Nullable<Double> Min { get; set; }`
- property `Boolean Multiline { get; set; }`
- property `String Name { get; set; }`
- property `IReadOnlyList<ActionParameterOption> Options { get; set; }`
- property `String OptionsSourceId { get; set; }`
- property `LocalizedText Placeholder { get; set; }`
- property `Boolean Required { get; set; }`
- property `Boolean ShowSlider { get; set; }`
- property `Nullable<Double> Step { get; set; }`
- property `Boolean SupportsReset { get; set; }`
- property `ActionParameterType Type { get; set; }`
- property `String ValidationRegex { get; set; }`
- property `ParameterVisibility VisibleWhen { get; set; }`
- property `IReadOnlyList<String> WidgetTypes { get; set; }`
- method `ActionParameter Array(String name, ActionParameter itemTemplate, LocalizedText label, LocalizedText description)`
- method `ActionParameter Autocomplete(String name, LocalizedText label, LocalizedText description, IReadOnlyList<ActionParameterOption> options, String optionsSourceId, LocalizedText placeholder, Boolean required)`
- method `ActionParameter Choice(String name, IReadOnlyList<ActionParameterOption> options, LocalizedText label, LocalizedText description, String defaultValue, Boolean required)`
- method `ActionParameter Code(String name, String language, LocalizedText label, LocalizedText description, String defaultValue, Boolean required)`
- method `ActionParameter Color(String name, LocalizedText label, LocalizedText description, String defaultValue, Boolean supportsReset)`
- method `ActionParameter DateTime(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Duration(String name, LocalizedText label, LocalizedText description, Nullable<Double> min, Nullable<Double> max, Nullable<Double> defaultMilliseconds, Boolean required)`
- method `ActionParameter DynamicChoice(String name, LocalizedText label, LocalizedText description, String optionsSourceId, LocalizedText placeholder, Boolean required)`
- method `ActionParameter File(String name, LocalizedText label, LocalizedText description, IReadOnlyList<String> fileExtensions, Boolean required)`
- method `ActionParameter Folder(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Hotkey(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Icon(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Image(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter IpAddress(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Json(String name, LocalizedText label, LocalizedText description, String defaultValue, Boolean required)`
- method `ActionParameter KeyValue(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter KeyboardCombo(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter KeyboardSequence(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter MultiSelect(String name, IReadOnlyList<ActionParameterOption> options, LocalizedText label, LocalizedText description, String optionsSourceId, Boolean required)`
- method `ActionParameter MultilineText(String name, LocalizedText label, LocalizedText description, LocalizedText placeholder, String defaultValue, Boolean required, Nullable<Int32> maxLength)`
- method `ActionParameter Number(String name, LocalizedText label, LocalizedText description, Nullable<Double> min, Nullable<Double> max, Nullable<Double> step, Nullable<Double> defaultValue, Boolean required)`
- method `ActionParameter Object(String name, IReadOnlyList<ActionParameter> children, LocalizedText label, LocalizedText description)`
- method `ActionParameter OnlyWhen(String parameterName, String[] values)`
- method `ActionParameter Password(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Secret(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter Slider(String name, Double min, Double max, LocalizedText label, LocalizedText description, Nullable<Double> step, Nullable<Double> defaultValue)`
- method `ActionParameter Text(String name, LocalizedText label, LocalizedText description, LocalizedText placeholder, String defaultValue, Boolean required, String validationRegex, Nullable<Int32> maxLength)`
- method `ActionParameter Toggle(String name, LocalizedText label, LocalizedText description, Boolean defaultValue, Boolean literalOnly)`
- method `ActionParameter Url(String name, LocalizedText label, LocalizedText description, LocalizedText placeholder, Boolean required, Boolean autoPrefixHttps)`
- method `ActionParameter WidgetTarget(String name, LocalizedText label, LocalizedText description, Boolean required)`
- method `ActionParameter WidgetTarget(String name, WidgetTargetOptions options)`

### class ActionParameterOption

- property `LocalizedText Label { get; set; }`
- property `IReadOnlyDictionary<String, String> Metadata { get; set; }`
- property `String Value { get; set; }`

### enum ActionParameterType : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: String, Number, Boolean, Password, Secret, Choice, DynamicChoice, Autocomplete, MultiSelect, Color, File, Folder, Hotkey, Duration, DateTime, Json, Code, KeyValue, Object, Array, IpAddress, Url, Icon, Image, KeyboardSequence, KeyboardCombo, WidgetTarget

### class ActionResult

- property `String ErrorCode { get; set; }`
- property `LocalizedText ErrorMessage { get; set; }`
- property `String ExpectedStateId { get; set; }`
- property `LocalizedText Message { get; set; }`
- property `ActionResultStatus Status { get; set; }`
- property `Task<ActionResult> SucceededTask { get; }`
- method `ActionResult Accepted(LocalizedText message)`
- method `ActionResult Accepted(LocalizedText message, String expectedStateId)`
- method `ActionResult Failed(String code, LocalizedText message)`
- method `ActionResult Success()`
- method `ActionResult Success(String expectedStateId)`

### enum ActionResultStatus : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Succeeded, Accepted, Failed

### class ActionStateAppearance : IEquatable<ActionStateAppearance>

- property `String BackgroundColor { get; set; }`
- property `String IconId { get; set; }`
- property `String Label { get; set; }`
- property `String LabelColor { get; set; }`
- method `ActionStateAppearance <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionStateAppearance other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionStateAppearance left, ActionStateAppearance right)`
- method `Boolean op_Inequality(ActionStateAppearance left, ActionStateAppearance right)`

### class ActionStateDefinition : IEquatable<ActionStateDefinition>

- property `ActionStateAppearance DefaultAppearance { get; set; }`
- property `String Id { get; set; }`
- property `LocalizedText Label { get; set; }`
- method `ActionStateDefinition <Clone>$()`
- method `Void Deconstruct(String& Id, LocalizedText& Label)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionStateDefinition other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionStateDefinition left, ActionStateDefinition right)`
- method `Boolean op_Inequality(ActionStateDefinition left, ActionStateDefinition right)`

### class ActionStateSnapshot : IEquatable<ActionStateSnapshot>

- property `String ActiveStateId { get; set; }`
- property `IReadOnlyList<ActionStateDefinition> States { get; set; }`
- method `ActionStateSnapshot <Clone>$()`
- method `Void Deconstruct(IReadOnlyList`1& States, String& ActiveStateId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionStateSnapshot other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionStateSnapshot left, ActionStateSnapshot right)`
- method `Boolean op_Inequality(ActionStateSnapshot left, ActionStateSnapshot right)`

### class DynamicOptionsContext

- property `IReadOnlyDictionary<String, Object> CurrentParameters { get; set; }`
- property `String Filter { get; set; }`
- property `String ParameterName { get; set; }`

### class DynamicOptionsResult

- property `Boolean AllowsCustomValue { get; set; }`
- property `Nullable<Int32> CacheSeconds { get; set; }`
- property `LocalizedText Error { get; set; }`
- property `IReadOnlyList<ActionParameterOption> Options { get; set; }`

### interface IActionDefinition

- property `LocalizedText Description { get; }`
- property `String Id { get; }`
- property `LocalizedText Name { get; }`
- property `IReadOnlyList<ActionParameter> Parameters { get; }`
- property `MacroDeckPlatform Platforms { get; }`
- method `IActionExecutor CreateExecutor()`

### interface IActionExecutor

- method `Task<ActionResult> ExecuteAsync(ActionExecutionContext context)`

### interface IActionInteractions

- method `Void RequestDevicePicker(String originClientId, String instanceId, Boolean startPlayback, String prompt)`
- method `Void RequestItemPicker(String originClientId, String instanceId, MusicPlayerCatalogItemKind kind, String prompt)`

### interface IConfigurableActionDefinition : IActionDefinition

- property `String DescriptiveUiSchema { get; }`

### interface IDynamicOptionsActionDefinition : IActionDefinition

- method `Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)`

### interface IIconProviderActionDefinition

- property `TimeSpan IconPollInterval { get; }`
- method `Task<ActionIconSnapshot> GetActionIconAsync(IReadOnlyDictionary<String, Object> parameters, CancellationToken cancellationToken)`
- method `Task<ActionIconContent> GetActionIconContentAsync(IReadOnlyDictionary<String, Object> parameters, String version, CancellationToken cancellationToken)`

### interface IStateProviderActionDefinition

- property `TimeSpan StatePollInterval { get; }`
- method `Task<ActionStateSnapshot> GetActionStateAsync(IReadOnlyDictionary<String, Object> parameters, CancellationToken cancellationToken)`

### interface IUiConfigurableActionDefinition : IActionDefinition

- method `Task<IUiSession> CreateConfigurationSessionAsync(ActionConfigurationRequest request, CancellationToken cancellationToken)`

### class ParameterVisibility : IEquatable<ParameterVisibility>

- property `String ParameterName { get; set; }`
- property `IReadOnlyList<String> Values { get; set; }`
- method `ParameterVisibility <Clone>$()`
- method `Void Deconstruct(String& ParameterName, IReadOnlyList`1& Values)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ParameterVisibility other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ParameterVisibility left, ParameterVisibility right)`
- method `Boolean op_Inequality(ParameterVisibility left, ParameterVisibility right)`

### class WidgetTargetOptions : IEquatable<WidgetTargetOptions>

- property `Boolean AllowSelf { get; set; }`
- property `LocalizedText Description { get; set; }`
- property `LocalizedText Label { get; set; }`
- property `Boolean Required { get; set; }`
- property `IReadOnlyList<String> WidgetTypes { get; set; }`
- method `WidgetTargetOptions <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetTargetOptions other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WidgetTargetOptions left, WidgetTargetOptions right)`
- method `Boolean op_Inequality(WidgetTargetOptions left, WidgetTargetOptions right)`

## MacroDeck.Sdk.Android

### enum AndroidBatteryHealth : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, Good, Overheat, Dead, OverVoltage, Failure, Cold

### class AndroidBatteryState : IEquatable<AndroidBatteryState>

- property `AndroidBatteryHealth Health { get; set; }`
- property `Boolean IsCharging { get; set; }`
- property `Int32 Level { get; set; }`
- property `AndroidBatteryStatus Status { get; set; }`
- method `AndroidBatteryState <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(AndroidBatteryState other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(AndroidBatteryState left, AndroidBatteryState right)`
- method `Boolean op_Inequality(AndroidBatteryState left, AndroidBatteryState right)`

### enum AndroidBatteryStatus : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, Charging, Discharging, NotCharging, Full

### enum AndroidDeviceAccess : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unsupported, Available, AdbNotEnabled, AdbNotAllowed

### enum AndroidDeviceErrorCode : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, AdbNotEnabled, AdbNotAllowed, AdbUnavailable, DeviceNotFound, DeviceOffline, DeviceUnauthorized, Timeout, CommandFailed, InvalidArgument, HostUnavailable, Unsupported, HostLocked, RateLimited

### class AndroidDeviceEventArgs : EventArgs

- property `IAndroidDevice Device { get; }`
- property `Nullable<AndroidDeviceState> PreviousState { get; }`

### class AndroidDeviceException : Exception, ISerializable

- property `AndroidDeviceErrorCode ErrorCode { get; }`

### class AndroidDeviceInfo : IEquatable<AndroidDeviceInfo>

- property `String Manufacturer { get; set; }`
- property `String Model { get; set; }`
- property `String Product { get; set; }`
- method `AndroidDeviceInfo <Clone>$()`
- method `Void Deconstruct(String& Model, String& Manufacturer, String& Product)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(AndroidDeviceInfo other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(AndroidDeviceInfo left, AndroidDeviceInfo right)`
- method `Boolean op_Inequality(AndroidDeviceInfo left, AndroidDeviceInfo right)`

### enum AndroidDeviceState : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Offline, Connecting, Online, Unauthorized

### class AndroidShellResult : IEquatable<AndroidShellResult>

- property `Int32 ExitCode { get; set; }`
- property `String StandardError { get; set; }`
- property `String StandardOutput { get; set; }`
- property `Boolean Truncated { get; set; }`
- method `AndroidShellResult <Clone>$()`
- method `Void Deconstruct(Int32& ExitCode, String& StandardOutput, String& StandardError, Boolean& Truncated)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(AndroidShellResult other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(AndroidShellResult left, AndroidShellResult right)`
- method `Boolean op_Inequality(AndroidShellResult left, AndroidShellResult right)`

### interface IAndroidDevice

- property `AndroidDeviceInfo Info { get; }`
- property `String Serial { get; }`
- property `AndroidDeviceState State { get; }`
- method `Task<AndroidShellResult> ExecuteShellAsync(String command, CancellationToken cancellationToken)`
- method `Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken)`
- method `Task InstallApkAsync(String apkPath, CancellationToken cancellationToken)`
- method `Task<Boolean> IsPackageInstalledAsync(String packageName, CancellationToken cancellationToken)`
- method `Task PullFileAsync(String remotePath, String localPath, CancellationToken cancellationToken)`
- method `Task PushFileAsync(String localPath, String remotePath, CancellationToken cancellationToken)`
- method `Task UninstallPackageAsync(String packageName, CancellationToken cancellationToken)`

### interface IAndroidDeviceManager

- property `AndroidDeviceAccess Access { get; }`
- property `IReadOnlyCollection<IAndroidDevice> Devices { get; }`
- method `Task<IAndroidDevice> ConnectAsync(String address, CancellationToken cancellationToken)`
- method `IAndroidDevice FindDevice(String serial)`
- event `EventHandler AccessChanged`
- event `EventHandler<AndroidDeviceEventArgs> DeviceConnected`
- event `EventHandler<AndroidDeviceEventArgs> DeviceDisconnected`
- event `EventHandler<AndroidDeviceEventArgs> DeviceStateChanged`

## MacroDeck.Sdk.ConfigFlow

### class ConfigEntrySnapshot : IEquatable<ConfigEntrySnapshot>

- property `Guid Id { get; set; }`
- property `String Title { get; set; }`
- method `ConfigEntrySnapshot <Clone>$()`
- method `Void Deconstruct(Guid& Id, String& Title)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ConfigEntrySnapshot other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ConfigEntrySnapshot left, ConfigEntrySnapshot right)`
- method `Boolean op_Inequality(ConfigEntrySnapshot left, ConfigEntrySnapshot right)`

### class ConfigFlowCopyValue

- property `LocalizedText Label { get; set; }`
- property `String Value { get; set; }`

### class ConfigFlowInstruction

- property `LocalizedText Text { get; set; }`
- property `IReadOnlyList<ConfigFlowCopyValue> Values { get; set; }`

### class ConfigFlowLink

- property `LocalizedText Label { get; set; }`
- property `String Url { get; set; }`

### class ConfigFlowResult

- property `String EntryTitle { get; set; }`
- property `LocalizedText ErrorMessage { get; set; }`
- property `String ExternalUrl { get; set; }`
- property `IReadOnlyDictionary<String, LocalizedText> FieldErrors { get; set; }`
- property `ConfigFlowResultKind Kind { get; }`
- property `ConfigFlowStep NextStep { get; set; }`
- property `String ResumeStepId { get; set; }`
- property `IReadOnlyDictionary<String, ConfigFlowValue> Values { get; set; }`
- method `ConfigFlowResult Complete(String title, IReadOnlyDictionary<String, ConfigFlowValue> values)`
- method `ConfigFlowResult Error(ConfigFlowStep step, LocalizedText message, IReadOnlyDictionary<String, LocalizedText> fieldErrors)`
- method `ConfigFlowResult External(String url, String resumeStepId)`
- method `ConfigFlowResult Step(ConfigFlowStep step)`

### enum ConfigFlowResultKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Step, Error, Complete, External

### class ConfigFlowStep

- property `IReadOnlyList<ActionParameter> AdvancedFields { get; set; }`
- property `LocalizedText Description { get; set; }`
- property `IReadOnlyList<ActionParameter> Fields { get; set; }`
- property `IReadOnlyList<ConfigFlowInstruction> Instructions { get; set; }`
- property `IReadOnlyList<ConfigFlowLink> Links { get; set; }`
- property `String StepId { get; set; }`
- property `LocalizedText Title { get; set; }`
- property `IReadOnlyList<ConfigFlowCopyValue> Values { get; set; }`

### class ConfigFlowValue : IEquatable<ConfigFlowValue>

- property `Boolean IsSecret { get; }`
- property `String Value { get; }`
- method `ConfigFlowValue <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ConfigFlowValue other)`
- method `Int32 GetHashCode()`
- method `ConfigFlowValue Plain(String value)`
- method `ConfigFlowValue Secret(String value)`
- method `String ToString()`
- method `Boolean op_Equality(ConfigFlowValue left, ConfigFlowValue right)`
- method `Boolean op_Inequality(ConfigFlowValue left, ConfigFlowValue right)`

### interface IConfigFlow

- method `Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)`
- method `Task<ConfigFlowResult> SubmitAsync(String stepId, IReadOnlyDictionary<String, Object> input, IConfigFlowContext context, CancellationToken cancellationToken)`

### interface IConfigFlowContext

- property `IOAuthSession OAuth { get; }`

### interface IConfigFlowEntryContext : IConfigFlowContext

- property `String EntryTitle { get; }`

### interface IConfigFlowProvider

- property `Boolean AllowsMultipleConfigurations { get; }`
- method `IConfigFlow CreateConfigFlow()`

### interface IIntegrationConfig

- method `Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken)`
- method `Task<String> GetSecretAsync(Guid entryId, String key, CancellationToken cancellationToken)`
- method `Task<String> GetStringAsync(Guid entryId, String key, CancellationToken cancellationToken)`
- method `Task SetSecretAsync(Guid entryId, String key, String value, CancellationToken cancellationToken)`
- method `Task SetStringAsync(Guid entryId, String key, String value, CancellationToken cancellationToken)`

### interface IOAuthSession

- property `String AuthorizationCode { get; }`
- property `String RedirectUri { get; }`
- property `String State { get; }`

### interface IUiConfigFlow : IConfigFlow

- method `Task<IUiSession> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)`

### interface IUiConfigFlowProvider : IConfigFlowProvider

- property `Boolean ServesConfigUiTree { get; }`

## MacroDeck.Sdk.Decks

### class DeckClient

- property `String ClientId { get; set; }`
- property `String DeviceId { get; set; }`
- property `String FolderId { get; set; }`
- property `String ProfileId { get; set; }`

### class DeckClientChangedEventArgs : EventArgs

- property `DeckClient Client { get; }`
- property `String PreviousFolderId { get; }`
- property `String PreviousProfileId { get; }`

### class DeckFolder

- property `String Id { get; set; }`
- property `String Label { get; set; }`

### class DeckProfile

- property `String Id { get; set; }`
- property `String Label { get; set; }`

### interface IDeckNavigator

- method `Task ChangeFolderAsync(String folderId, String originClientId, CancellationToken cancellationToken)`
- method `Task ChangeProfileAsync(String profileId, String originClientId, CancellationToken cancellationToken)`
- method `IReadOnlyList<DeckClient> GetClients()`
- method `IReadOnlyList<DeckFolder> GetFolders()`
- method `IReadOnlyList<DeckProfile> GetProfiles()`
- method `Task GoBackAsync(String originClientId, CancellationToken cancellationToken)`
- method `Task GoToParentAsync(String originClientId, CancellationToken cancellationToken)`
- event `EventHandler<DeckClientChangedEventArgs> ClientChanged`

## MacroDeck.Sdk.Deprecation

### class MacroDeckDeprecatedAttribute : Attribute

- property `String DeprecatedIn { get; }`
- property `String Guidance { get; }`
- property `String MigrationUrl { get; set; }`
- property `String RemovedIn { get; }`
- property `String Replacement { get; set; }`

### class MacroDeckSdkUsageAttribute : Attribute

- property `String[] DeprecatedApis { get; }`
- property `String SdkVersion { get; }`
- property `Boolean Truncated { get; set; }`

### class SdkDeprecation : IEquatable<SdkDeprecation>

- property `String ApiId { get; set; }`
- property `String DeprecatedIn { get; set; }`
- property `String DisplayName { get; set; }`
- property `String Guidance { get; set; }`
- property `String MigrationUrl { get; set; }`
- property `String RemovedIn { get; set; }`
- property `String Replacement { get; set; }`
- method `SdkDeprecation <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(SdkDeprecation other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(SdkDeprecation left, SdkDeprecation right)`
- method `Boolean op_Inequality(SdkDeprecation left, SdkDeprecation right)`

### static class SdkDeprecations

- property `IReadOnlyList<SdkDeprecation> Active { get; }`
- property `IReadOnlyList<SdkDeprecation> All { get; }`
- property `IReadOnlyList<SdkDeprecation> Removed { get; }`
- method `Boolean TryGet(String apiId, SdkDeprecation& deprecation)`

## MacroDeck.Sdk.Devices

### class DeviceCapabilities : IEquatable<DeviceCapabilities>

- field `DeviceCapabilities None (readonly)`
- property `Int32 DialCount { get; set; }`
- property `Int32 DisplayCount { get; set; }`
- property `IReadOnlyDictionary<String, String> Extra { get; set; }`
- property `Int32 KeyCount { get; set; }`
- property `Boolean SupportsImages { get; set; }`
- property `Boolean SupportsText { get; set; }`
- method `DeviceCapabilities <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceCapabilities other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceCapabilities left, DeviceCapabilities right)`
- method `Boolean op_Inequality(DeviceCapabilities left, DeviceCapabilities right)`

### class DeviceDescriptor : IEquatable<DeviceDescriptor>

- property `DeviceCapabilities Capabilities { get; set; }`
- property `String Id { get; set; }`
- property `String LayoutReference { get; set; }`
- property `String Manufacturer { get; set; }`
- property `IReadOnlyDictionary<String, String> Metadata { get; set; }`
- property `String Model { get; set; }`
- property `String Name { get; set; }`
- property `DevicePresence Presence { get; set; }`
- method `DeviceDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name, String& Model, String& Manufacturer, String& LayoutReference, DeviceCapabilities& Capabilities, DevicePresence& Presence, IReadOnlyDictionary`2& Metadata)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceDescriptor left, DeviceDescriptor right)`
- method `Boolean op_Inequality(DeviceDescriptor left, DeviceDescriptor right)`

### class DeviceIconImage : IEquatable<DeviceIconImage>

- property `ReadOnlyMemory<Byte> Content { get; set; }`
- property `String ContentType { get; set; }`
- property `String ETag { get; set; }`
- property `String IconId { get; set; }`
- property `Boolean NotModified { get; set; }`
- method `DeviceIconImage <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceIconImage other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceIconImage left, DeviceIconImage right)`
- method `Boolean op_Inequality(DeviceIconImage left, DeviceIconImage right)`

### class DeviceInteraction : IEquatable<DeviceInteraction>

- property `IReadOnlyDictionary<String, String> Data { get; set; }`
- property `DeviceInteractionKind Kind { get; set; }`
- property `Nullable<Int64> SurfaceRevision { get; set; }`
- property `DeviceInteractionTarget Target { get; set; }`
- property `Nullable<Double> Value { get; set; }`
- method `DeviceInteraction <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceInteraction other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceInteraction left, DeviceInteraction right)`
- method `Boolean op_Inequality(DeviceInteraction left, DeviceInteraction right)`

### enum DeviceInteractionKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, Press, Release, ShortPress, LongPress, EncoderTurn, EncoderPress, EncoderRelease, TouchStart, TouchMove, TouchEnd, Analog

### class DeviceInteractionResult : IEquatable<DeviceInteractionResult>

- field `DeviceInteractionResult Accepted (readonly)`
- field `DeviceInteractionResult NotSupported (readonly)`
- property `Boolean IsAccepted { get; }`
- property `String ReasonCode { get; set; }`
- property `DeviceInteractionStatus Status { get; set; }`
- method `DeviceInteractionResult <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceInteractionResult other)`
- method `Int32 GetHashCode()`
- method `DeviceInteractionResult Rejected(String reasonCode)`
- method `String ToString()`
- method `Boolean op_Equality(DeviceInteractionResult left, DeviceInteractionResult right)`
- method `Boolean op_Inequality(DeviceInteractionResult left, DeviceInteractionResult right)`

### enum DeviceInteractionStatus : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Accepted, Rejected, NotSupported

### class DeviceInteractionTarget : IEquatable<DeviceInteractionTarget>

- property `Nullable<Int32> ControlIndex { get; set; }`
- property `String WidgetId { get; set; }`
- method `DeviceInteractionTarget <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceInteractionTarget other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceInteractionTarget left, DeviceInteractionTarget right)`
- method `Boolean op_Inequality(DeviceInteractionTarget left, DeviceInteractionTarget right)`

### enum DevicePresence : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, Online, Offline

### class DeviceRegistration : IEquatable<DeviceRegistration>

- property `String DeviceId { get; set; }`
- property `String ProviderDeviceId { get; set; }`
- method `DeviceRegistration <Clone>$()`
- method `Void Deconstruct(String& DeviceId, String& ProviderDeviceId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceRegistration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceRegistration left, DeviceRegistration right)`
- method `Boolean op_Inequality(DeviceRegistration left, DeviceRegistration right)`

### class DeviceSessionClosedEventArgs : EventArgs

- property `String Reason { get; }`

### class DeviceSessionException : Exception, ISerializable

- property `String ReasonCode { get; }`

### static class DeviceSessionReasons

- field `String HostLocked = HOST_LOCKED`
- field `String IconTooLarge = DEVICE_ICON_TOO_LARGE`
- field `String SessionNotFound = DEVICE_SESSION_NOT_FOUND`
- field `String TriggerFailed = DEVICE_TRIGGER_FAILED`
- field `String WidgetNotOnSurface = DEVICE_WIDGET_NOT_ON_SURFACE`

### class DeviceSurface : IEquatable<DeviceSurface>

- field `DeviceSurface Empty (readonly)`
- property `DeviceSurfaceFolder Folder { get; set; }`
- property `DeviceSurfaceLayout Layout { get; set; }`
- property `DeviceSurfaceProfile Profile { get; set; }`
- property `Int64 Revision { get; set; }`
- property `IReadOnlyList<DeviceSurfaceWidget> Widgets { get; set; }`
- method `DeviceSurface <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceSurface other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceSurface left, DeviceSurface right)`
- method `Boolean op_Inequality(DeviceSurface left, DeviceSurface right)`

### class DeviceSurfaceAppearance : IEquatable<DeviceSurfaceAppearance>

- property `String BackgroundColor { get; set; }`
- property `IReadOnlyDictionary<String, String> Extra { get; set; }`
- property `Nullable<Int32> FontSize { get; set; }`
- property `Boolean HasProviderIcon { get; set; }`
- property `String IconFit { get; set; }`
- property `String IconId { get; set; }`
- property `Nullable<Double> IconOffsetX { get; set; }`
- property `Nullable<Double> IconOffsetY { get; set; }`
- property `String IconVersion { get; set; }`
- property `Nullable<Double> IconZoom { get; set; }`
- property `String Label { get; set; }`
- property `String LabelColor { get; set; }`
- property `String LabelPosition { get; set; }`
- property `String TextAlign { get; set; }`
- method `DeviceSurfaceAppearance <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceSurfaceAppearance other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceSurfaceAppearance left, DeviceSurfaceAppearance right)`
- method `Boolean op_Inequality(DeviceSurfaceAppearance left, DeviceSurfaceAppearance right)`

### class DeviceSurfaceChangedEventArgs : EventArgs

- property `DeviceSurface Surface { get; }`

### class DeviceSurfaceFolder : IEquatable<DeviceSurfaceFolder>

- property `String Id { get; set; }`
- property `Boolean IsRoot { get; set; }`
- property `String Name { get; set; }`
- property `String ParentId { get; set; }`
- method `DeviceSurfaceFolder <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name, String& ParentId, Boolean& IsRoot)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceSurfaceFolder other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceSurfaceFolder left, DeviceSurfaceFolder right)`
- method `Boolean op_Inequality(DeviceSurfaceFolder left, DeviceSurfaceFolder right)`

### class DeviceSurfaceLayout : IEquatable<DeviceSurfaceLayout>

- property `String BackgroundColor { get; set; }`
- property `Int32 Columns { get; set; }`
- property `String LayoutReference { get; set; }`
- property `Int32 Rows { get; set; }`
- property `Int32 WidgetBorderRadius { get; set; }`
- property `Int32 WidgetSpacing { get; set; }`
- method `DeviceSurfaceLayout <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceSurfaceLayout other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceSurfaceLayout left, DeviceSurfaceLayout right)`
- method `Boolean op_Inequality(DeviceSurfaceLayout left, DeviceSurfaceLayout right)`

### class DeviceSurfaceProfile : IEquatable<DeviceSurfaceProfile>

- property `String Id { get; set; }`
- property `String Name { get; set; }`
- method `DeviceSurfaceProfile <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceSurfaceProfile other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceSurfaceProfile left, DeviceSurfaceProfile right)`
- method `Boolean op_Inequality(DeviceSurfaceProfile left, DeviceSurfaceProfile right)`

### class DeviceSurfaceWidget : IEquatable<DeviceSurfaceWidget>

- property `DeviceSurfaceAppearance Appearance { get; set; }`
- property `Int32 Height { get; set; }`
- property `String Id { get; set; }`
- property `Boolean IsPinned { get; set; }`
- property `Int32 PositionX { get; set; }`
- property `Int32 PositionY { get; set; }`
- property `String StateId { get; set; }`
- property `String StateLabel { get; set; }`
- property `IReadOnlyList<DeviceInteractionKind> SupportedInteractions { get; set; }`
- property `String Type { get; set; }`
- property `Int32 Width { get; set; }`
- method `DeviceSurfaceWidget <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceSurfaceWidget other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceSurfaceWidget left, DeviceSurfaceWidget right)`
- method `Boolean op_Inequality(DeviceSurfaceWidget left, DeviceSurfaceWidget right)`

### class DeviceWidgetIconImage : IEquatable<DeviceWidgetIconImage>

- property `ReadOnlyMemory<Byte> Content { get; set; }`
- property `String ContentType { get; set; }`
- property `String ETag { get; set; }`
- property `Boolean NotModified { get; set; }`
- property `String WidgetId { get; set; }`
- method `DeviceWidgetIconImage <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(DeviceWidgetIconImage other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(DeviceWidgetIconImage left, DeviceWidgetIconImage right)`
- method `Boolean op_Inequality(DeviceWidgetIconImage left, DeviceWidgetIconImage right)`

### interface IDeviceProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<DeviceDescriptor> GetDevices()`
- method `Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken)`
- method `Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken)`
- method `Task ShutdownAsync(CancellationToken cancellationToken)`

### interface IDeviceProviderContext

- method `Task<DeviceRegistration> RegisterDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken)`
- method `Task SetDevicePresenceAsync(String deviceId, DevicePresence presence, CancellationToken cancellationToken)`
- method `Task UnregisterDeviceAsync(String deviceId, CancellationToken cancellationToken)`
- method `Task UpdateDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken)`

### interface IDeviceSession : IAsyncDisposable

- property `DeviceSurface CurrentSurface { get; }`
- property `String DeviceId { get; }`
- property `String ProviderDeviceId { get; }`
- method `Task<DeviceIconImage> GetIconAsync(String iconId, Nullable<Int32> size, String knownETag, CancellationToken cancellationToken)`
- method `Task<DeviceWidgetIconImage> GetWidgetIconAsync(String widgetId, String knownETag, CancellationToken cancellationToken)`
- method `Task<DeviceInteractionResult> SendInteractionAsync(DeviceInteraction interaction, CancellationToken cancellationToken)`
- event `EventHandler<DeviceSessionClosedEventArgs> Closed`
- event `EventHandler<DeviceSurfaceChangedEventArgs> SurfaceChanged`

## MacroDeck.Sdk.Events

### class EventBinding

- property `String EventId { get; set; }`
- property `IReadOnlyDictionary<String, EventBindingValue> Parameters { get; set; }`

### class EventBindingValue : IEquatable<EventBindingValue>

- property `String Operator { get; set; }`
- property `Nullable<JsonElement> Value { get; set; }`
- method `EventBindingValue <Clone>$()`
- method `Void Deconstruct(Nullable`1& Value, String& Operator)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(EventBindingValue other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(EventBindingValue left, EventBindingValue right)`
- method `Boolean op_Inequality(EventBindingValue left, EventBindingValue right)`

### class EventDefinition

- property `LocalizedText Category { get; set; }`
- property `IReadOnlyList<ActionParameter> ConfigurationParameters { get; set; }`
- property `EventDeliveryKind DeliveryKind { get; set; }`
- property `LocalizedText Description { get; set; }`
- property `String IconName { get; set; }`
- property `String Id { get; set; }`
- property `LocalizedText Name { get; set; }`
- property `IReadOnlyList<ActionParameter> PayloadParameters { get; set; }`

### enum EventDeliveryKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Push, Scheduled

### class EventOptionsContext

- property `IReadOnlyDictionary<String, Object> CurrentParameters { get; set; }`
- property `String EventId { get; set; }`
- property `String Filter { get; set; }`
- property `String ParameterName { get; set; }`

### interface IDynamicEventOptionsProvider

- method `Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context, CancellationToken cancellationToken)`

### interface IEventProvider

- property `IReadOnlyList<EventDefinition> EventDefinitions { get; }`
- property `String ProviderName { get; }`

### interface IEventPublisher

- method `IReadOnlyList<EventBinding> GetBindings()`
- method `Void Publish(String eventId, IReadOnlyDictionary<String, Object> parameters)`
- event `Action BindingsChanged`

## MacroDeck.Sdk.FolderViews

### class FolderViewDescriptor : IEquatable<FolderViewDescriptor>

- property `Nullable<LocalizedText> Description { get; set; }`
- property `Boolean HasConfiguration { get; set; }`
- property `String Id { get; set; }`
- property `IReadOnlyDictionary<String, String> Metadata { get; set; }`
- property `LocalizedText Name { get; set; }`
- property `String Navigation { get; set; }`
- property `String ResolvedNavigation { get; }`
- method `FolderViewDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, LocalizedText& Name, Nullable`1& Description, String& Navigation, Boolean& HasConfiguration, IReadOnlyDictionary`2& Metadata)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(FolderViewDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(FolderViewDescriptor left, FolderViewDescriptor right)`
- method `Boolean op_Inequality(FolderViewDescriptor left, FolderViewDescriptor right)`

### static class FolderViewNavigation

- field `String Default = default`
- field `String Hidden = hidden`
- field `IReadOnlyList<String> WellKnown (readonly)`

### class FolderViewRegistration : IEquatable<FolderViewRegistration>

- property `String FolderViewId { get; set; }`
- property `String ProviderId { get; set; }`
- method `FolderViewRegistration <Clone>$()`
- method `Void Deconstruct(String& FolderViewId, String& ProviderId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(FolderViewRegistration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(FolderViewRegistration left, FolderViewRegistration right)`
- method `Boolean op_Inequality(FolderViewRegistration left, FolderViewRegistration right)`

### interface IFolderViewProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<FolderViewDescriptor> GetFolderViews()`
- method `Task InitializeAsync(IFolderViewProviderContext context, CancellationToken cancellationToken)`

### interface IFolderViewProviderContext

- method `Task<FolderViewRegistration> RegisterFolderViewAsync(FolderViewDescriptor folderView, CancellationToken cancellationToken)`
- method `Task UnregisterFolderViewAsync(String folderViewId, CancellationToken cancellationToken)`

## MacroDeck.Sdk.Identity

### class CapabilityIdConflict : IEquatable<CapabilityIdConflict>

- property `String CapabilityType { get; set; }`
- property `String LocalId { get; set; }`
- property `String OwnerId { get; set; }`
- property `String Reason { get; set; }`
- method `CapabilityIdConflict <Clone>$()`
- method `Void Deconstruct(String& OwnerId, String& CapabilityType, String& LocalId, String& Reason)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(CapabilityIdConflict other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(CapabilityIdConflict left, CapabilityIdConflict right)`
- method `Boolean op_Inequality(CapabilityIdConflict left, CapabilityIdConflict right)`

### static class DeclaredIdValidator

- method `Void Validate(ICollection<CapabilityIdConflict> conflicts, String ownerId, String capabilityType, IEnumerable<String> localIds)`
- method `CapabilityIdConflict ValidateOwner(String ownerId, OwnerIdKind kind, String capabilityType)`

### enum LocalIdKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Declared, Resource

### static class MacroDeckId

- field `Int32 MaxDeclaredLocalIdLength = 64`
- field `Int32 MaxOwnerIdLength = 128`
- field `Int32 MaxResourceLocalIdLength = 256`
- method `Boolean IsValidLocalId(String localId, LocalIdKind kind)`
- method `Boolean IsValidOwnerId(String ownerId, OwnerIdKind kind)`
- method `Boolean IsValidOwnerId(String ownerId)`
- method `Boolean TryValidateLocalId(String localId, LocalIdKind kind, String& error)`
- method `Boolean TryValidateOwnerId(String ownerId, OwnerIdKind kind, String& error)`

### class MacroDeckIdException : Exception, ISerializable

- property `String CapabilityType { get; }`
- property `String LocalId { get; }`
- property `String OwnerId { get; }`
- property `String Reason { get; }`

### enum OwnerIdKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Package, HostProvider

### struct QualifiedId : ValueType, IEquatable<QualifiedId>

- field `String Separator = ::`
- property `Boolean IsEmpty { get; }`
- property `String LocalId { get; }`
- property `String OwnerId { get; }`
- method `QualifiedId Create(String ownerId, String localId, OwnerIdKind ownerKind, LocalIdKind localKind, String capabilityType)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(QualifiedId other)`
- method `Int32 GetHashCode()`
- method `Boolean IsQualified(String value)`
- method `QualifiedId Parse(String value)`
- method `String ToString()`
- method `Boolean TryCreate(String ownerId, String localId, QualifiedId& id)`
- method `Boolean TryCreate(String ownerId, String localId, LocalIdKind localKind, QualifiedId& id)`
- method `Boolean TryCreate(String ownerId, String localId, OwnerIdKind ownerKind, LocalIdKind localKind, QualifiedId& id)`
- method `Boolean TryParse(String value, QualifiedId& id)`
- method `Boolean op_Equality(QualifiedId left, QualifiedId right)`
- method `Boolean op_Inequality(QualifiedId left, QualifiedId right)`

## MacroDeck.Sdk.Issues

### interface IIntegrationIssueProvider

- method `Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken)`
- method `Task<IssueResolution> ResolveIssueAsync(String issueId, CancellationToken cancellationToken)`

### class IntegrationIssue

- property `LocalizedText ActionLabel { get; set; }`
- property `LocalizedText Description { get; set; }`
- property `String Id { get; set; }`
- property `IntegrationIssueSeverity Severity { get; set; }`
- property `LocalizedText Title { get; set; }`

### enum IntegrationIssueSeverity : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Info, Warning, Error

### class IssueResolution

- property `IssueResolutionFollowUp FollowUp { get; set; }`
- property `LocalizedText Message { get; set; }`
- property `Boolean Success { get; set; }`
- method `IssueResolution Failed(LocalizedText message)`
- method `IssueResolution Ok(LocalizedText message, IssueResolutionFollowUp followUp)`

### enum IssueResolutionFollowUp : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: None, StartConfigFlow

## MacroDeck.Sdk.Layouts

### interface ILayoutProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<LayoutDescriptor> GetLayouts()`
- method `Task InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken)`

### interface ILayoutProviderContext

- method `Task<LayoutRegistration> RegisterLayoutAsync(LayoutDescriptor layout, CancellationToken cancellationToken)`
- method `Task UnregisterLayoutAsync(String layoutId, CancellationToken cancellationToken)`

### class LayoutCapabilities : IEquatable<LayoutCapabilities>

- field `LayoutCapabilities None (readonly)`
- property `IReadOnlyDictionary<String, String> Extra { get; set; }`
- property `LayoutVisualCapabilities Visuals { get; set; }`
- method `LayoutCapabilities <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutCapabilities other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutCapabilities left, LayoutCapabilities right)`
- method `Boolean op_Inequality(LayoutCapabilities left, LayoutCapabilities right)`

### class LayoutDescriptor : IEquatable<LayoutDescriptor>

- property `LayoutCapabilities Capabilities { get; set; }`
- property `String Id { get; set; }`
- property `IReadOnlyDictionary<String, String> Metadata { get; set; }`
- property `String Name { get; set; }`
- property `LayoutRegion PrimaryGrid { get; }`
- property `IReadOnlyList<LayoutRegion> Regions { get; set; }`
- method `LayoutDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name, IReadOnlyList`1& Regions, LayoutCapabilities& Capabilities, IReadOnlyDictionary`2& Metadata)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutDescriptor left, LayoutDescriptor right)`
- method `Boolean op_Inequality(LayoutDescriptor left, LayoutDescriptor right)`

### class LayoutGrid : IEquatable<LayoutGrid>

- property `Int32 Columns { get; set; }`
- property `Boolean ColumnsLocked { get; }`
- property `Boolean IsConfigurable { get; set; }`
- property `LayoutKeySize KeySize { get; set; }`
- property `Int32 MaxColumns { get; set; }`
- property `Int32 MaxRows { get; set; }`
- property `Int32 MinColumns { get; set; }`
- property `Int32 MinRows { get; set; }`
- property `Int32 Rows { get; set; }`
- property `Boolean RowsLocked { get; }`
- property `Boolean SupportsRuntimeResize { get; set; }`
- method `LayoutGrid <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutGrid other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutGrid left, LayoutGrid right)`
- method `Boolean op_Inequality(LayoutGrid left, LayoutGrid right)`

### class LayoutKeySize : IEquatable<LayoutKeySize>

- property `Int32 Height { get; set; }`
- property `Int32 Width { get; set; }`
- method `LayoutKeySize <Clone>$()`
- method `Void Deconstruct(Int32& Width, Int32& Height)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutKeySize other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutKeySize left, LayoutKeySize right)`
- method `Boolean op_Inequality(LayoutKeySize left, LayoutKeySize right)`

### class LayoutRegion : IEquatable<LayoutRegion>

- property `Int32 Count { get; set; }`
- property `IReadOnlyDictionary<String, String> Extra { get; set; }`
- property `LayoutGrid Grid { get; set; }`
- property `String Id { get; set; }`
- property `String Kind { get; set; }`
- property `String Name { get; set; }`
- property `LayoutVisualCapabilities Visuals { get; set; }`
- method `LayoutRegion <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutRegion other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutRegion left, LayoutRegion right)`
- method `Boolean op_Inequality(LayoutRegion left, LayoutRegion right)`

### static class LayoutRegionKinds

- field `IReadOnlyList<String> All (readonly)`
- field `String Button = button`
- field `String Encoder = encoder`
- field `String Grid = grid`
- field `String Pedal = pedal`
- field `String TouchStrip = touch-strip`
- method `Boolean IsKnown(String kind)`

### class LayoutRegistration : IEquatable<LayoutRegistration>

- property `String LayoutId { get; set; }`
- property `String ProviderId { get; set; }`
- method `LayoutRegistration <Clone>$()`
- method `Void Deconstruct(String& LayoutId, String& ProviderId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutRegistration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutRegistration left, LayoutRegistration right)`
- method `Boolean op_Inequality(LayoutRegistration left, LayoutRegistration right)`

### class LayoutVisualCapabilities : IEquatable<LayoutVisualCapabilities>

- field `LayoutVisualCapabilities Full (readonly)`
- property `Boolean AnimatedIcons { get; set; }`
- property `Boolean BackgroundColors { get; set; }`
- property `Boolean Borders { get; set; }`
- property `Boolean CornerRadius { get; set; }`
- property `Boolean CustomFolderViews { get; set; }`
- property `Nullable<Int32> MaxUpdatesPerSecond { get; set; }`
- property `Boolean StaticIcons { get; set; }`
- property `Boolean TextLabels { get; set; }`
- property `Boolean Transparency { get; set; }`
- property `Boolean WidgetSpacing { get; set; }`
- method `LayoutVisualCapabilities <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LayoutVisualCapabilities other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LayoutVisualCapabilities left, LayoutVisualCapabilities right)`
- method `Boolean op_Inequality(LayoutVisualCapabilities left, LayoutVisualCapabilities right)`

## MacroDeck.Sdk.Logging

### class FailureEpisodeEnd : IEquatable<FailureEpisodeEnd>

- property `TimeSpan Duration { get; set; }`
- property `Int32 Failures { get; set; }`
- property `DateTimeOffset StartedAt { get; set; }`
- method `FailureEpisodeEnd <Clone>$()`
- method `Void Deconstruct(DateTimeOffset& StartedAt, TimeSpan& Duration, Int32& Failures)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(FailureEpisodeEnd other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(FailureEpisodeEnd left, FailureEpisodeEnd right)`
- method `Boolean op_Inequality(FailureEpisodeEnd left, FailureEpisodeEnd right)`

### class FailureEpisodeSignal : IEquatable<FailureEpisodeSignal>

- property `Int32 ConsecutiveFailures { get; set; }`
- property `TimeSpan Duration { get; set; }`
- property `FailureEpisodeSignalKind Kind { get; set; }`
- property `String LastError { get; set; }`
- property `DateTimeOffset StartedAt { get; set; }`
- method `FailureEpisodeSignal <Clone>$()`
- method `Void Deconstruct(FailureEpisodeSignalKind& Kind, DateTimeOffset& StartedAt, TimeSpan& Duration, Int32& ConsecutiveFailures, String& LastError)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(FailureEpisodeSignal other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(FailureEpisodeSignal left, FailureEpisodeSignal right)`
- method `Boolean op_Inequality(FailureEpisodeSignal left, FailureEpisodeSignal right)`

### enum FailureEpisodeSignalKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Onset, SummaryDue, Quiet

### class FailureEpisodeTracker

- property `Boolean IsActive { get; }`
- property `Nullable<DateTimeOffset> StartedAt { get; }`
- method `FailureEpisodeSignal RecordFailure(String lastError)`
- method `FailureEpisodeEnd RecordSuccess()`

### static class IntegrationLog

- field `String IntegrationPropertyName = MacroDeckIntegrationId`
- method `ILogger For(String integrationId)`
- method `ILogger For(String integrationId)`
- method `ILogger For(String integrationId, Type source)`

## MacroDeck.Sdk.Messaging

### class ChannelMessage : IEquatable<ChannelMessage>

- property `ChannelMessageKind Kind { get; set; }`
- property `String MessageId { get; set; }`
- property `Nullable<JsonElement> Payload { get; set; }`
- property `String Sender { get; set; }`
- property `DateTimeOffset SentAt { get; set; }`
- property `String Topic { get; set; }`
- method `ChannelMessage <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ChannelMessage other)`
- method `Int32 GetHashCode()`
- method `T PayloadAs(JsonSerializerOptions options)`
- method `String ToString()`
- method `Boolean op_Equality(ChannelMessage left, ChannelMessage right)`
- method `Boolean op_Inequality(ChannelMessage left, ChannelMessage right)`

### enum ChannelMessageKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Event, Command, Request

### interface IMessageChannel

- method `Task<IAsyncDisposable> HandleCommandsAsync(String topic, Func<ChannelMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)`
- method `Task<IAsyncDisposable> HandleRequestsAsync(String topic, Func<ChannelMessage, CancellationToken, Task<Nullable<JsonElement>>> handler, CancellationToken cancellationToken)`
- method `Task PublishAsync(String topic, Nullable<JsonElement> payload, CancellationToken cancellationToken)`
- method `Task<Nullable<JsonElement>> RequestAsync(String topic, Nullable<JsonElement> payload, Nullable<TimeSpan> timeout, CancellationToken cancellationToken)`
- method `Task SendAsync(String topic, Nullable<JsonElement> payload, Nullable<TimeSpan> timeout, CancellationToken cancellationToken)`
- method `Task<IAsyncDisposable> SubscribeAsync(String topicPattern, Func<ChannelMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)`

### enum MessageChannelErrorCode : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, Unsupported, InvalidTopic, PayloadTooLarge, NoHandler, HandlerUnavailable, TopicAlreadyHandled, HandlerFailed, Timeout, RateLimited, NotConnected

### class MessageChannelException : Exception, ISerializable

- property `MessageChannelErrorCode ErrorCode { get; }`
- property `String HandlerOwner { get; }`
- property `String Topic { get; }`

### static class MessageChannelExtensions

- method `Task<IAsyncDisposable> HandleCommandsAsync(IMessageChannel channel, String topic, Func<T, ChannelMessage, CancellationToken, Task> handler, JsonSerializerOptions options, CancellationToken cancellationToken)`
- method `Task<IAsyncDisposable> HandleRequestsAsync(IMessageChannel channel, String topic, Func<TRequest, ChannelMessage, CancellationToken, Task<TResponse>> handler, JsonSerializerOptions options, CancellationToken cancellationToken)`
- method `Task PublishAsync(IMessageChannel channel, String topic, T payload, JsonSerializerOptions options, CancellationToken cancellationToken)`
- method `Task<TResponse> RequestAsync(IMessageChannel channel, String topic, TRequest request, JsonSerializerOptions options, Nullable<TimeSpan> timeout, CancellationToken cancellationToken)`
- method `Task SendAsync(IMessageChannel channel, String topic, T payload, JsonSerializerOptions options, Nullable<TimeSpan> timeout, CancellationToken cancellationToken)`
- method `Task<IAsyncDisposable> SubscribeAsync(IMessageChannel channel, String topicPattern, Func<T, ChannelMessage, CancellationToken, Task> handler, JsonSerializerOptions options, CancellationToken cancellationToken)`

### static class MessageTopic

- field `Int32 MaxLength = 128`
- field `Int32 MaxPayloadBytes = 65536`
- method `Boolean IsValidPattern(String pattern)`
- method `Boolean IsValidTopic(String topic)`
- method `Boolean Matches(String pattern, String topic)`

## MacroDeck.Sdk.Migration

### class ActionMigrationResult : IEquatable<ActionMigrationResult>

- property `String ActionId { get; set; }`
- property `String IntegrationId { get; set; }`
- property `String Label { get; set; }`
- property `IReadOnlyDictionary<String, JsonElement> Parameters { get; set; }`
- property `IReadOnlyList<LocalizedText> Warnings { get; set; }`
- method `ActionMigrationResult <Clone>$()`
- method `Void Deconstruct(String& IntegrationId, String& ActionId, String& Label, IReadOnlyDictionary`2& Parameters, IReadOnlyList`1& Warnings)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ActionMigrationResult other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ActionMigrationResult left, ActionMigrationResult right)`
- method `Boolean op_Inequality(ActionMigrationResult left, ActionMigrationResult right)`

### class ForeignAction : IEquatable<ForeignAction>

- property `String ActionSource { get; set; }`
- property `String Configuration { get; set; }`
- property `String ConfigurationSummary { get; set; }`
- property `String DisplayName { get; set; }`
- property `String TypeName { get; set; }`
- method `ForeignAction <Clone>$()`
- method `Void Deconstruct(String& TypeName, String& ActionSource, String& DisplayName, String& Configuration, String& ConfigurationSummary)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ForeignAction other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ForeignAction left, ForeignAction right)`
- method `Boolean op_Inequality(ForeignAction left, ForeignAction right)`

### class ForeignPluginSettings : IEquatable<ForeignPluginSettings>

- property `IReadOnlyList<ForeignAction> Actions { get; set; }`
- property `IReadOnlyList<IReadOnlyDictionary<String, String>> Credentials { get; set; }`
- property `IReadOnlyDictionary<String, String> Settings { get; set; }`
- property `String SettingsSource { get; set; }`
- method `ForeignPluginSettings <Clone>$()`
- method `Void Deconstruct(String& SettingsSource, IReadOnlyDictionary`2& Settings, IReadOnlyList`1& Credentials, IReadOnlyList`1& Actions)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ForeignPluginSettings other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ForeignPluginSettings left, ForeignPluginSettings right)`
- method `Boolean op_Inequality(ForeignPluginSettings left, ForeignPluginSettings right)`

### interface IIntegrationMigration

- property `IReadOnlyList<String> ClaimedActionSources { get; }`
- property `IReadOnlyList<String> ClaimedSettingsSources { get; }`
- property `MigrationSource Source { get; }`
- method `Task<ActionMigrationResult> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)`
- method `Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(ForeignPluginSettings settings, CancellationToken cancellationToken)`

### interface IMigrationProvider

- property `IReadOnlyList<IIntegrationMigration> Migrations { get; }`

### class MigratedConfiguration : IEquatable<MigratedConfiguration>

- property `String IntegrationId { get; set; }`
- property `IReadOnlyDictionary<String, MigratedSecret> Secrets { get; set; }`
- property `String Title { get; set; }`
- property `IReadOnlyDictionary<String, JsonElement> Values { get; set; }`
- method `MigratedConfiguration <Clone>$()`
- method `Void Deconstruct(String& IntegrationId, String& Title, IReadOnlyDictionary`2& Values, IReadOnlyDictionary`2& Secrets)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MigratedConfiguration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(MigratedConfiguration left, MigratedConfiguration right)`
- method `Boolean op_Inequality(MigratedConfiguration left, MigratedConfiguration right)`

### class MigratedSecret : IEquatable<MigratedSecret>

- property `MigratedSecretKind Kind { get; set; }`
- property `String Value { get; set; }`
- method `MigratedSecret <Clone>$()`
- method `Void Deconstruct(String& Value, MigratedSecretKind& Kind)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MigratedSecret other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(MigratedSecret left, MigratedSecret right)`
- method `Boolean op_Inequality(MigratedSecret left, MigratedSecret right)`

### enum MigratedSecretKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Password, Secret

### enum MigrationSource : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: MacroDeck2, TouchPortal, Deckboard

## MacroDeck.Sdk.MusicPlayer

### interface ICatalogMusicPlayer : IMusicPlayer, IMusicPlayerCatalogProvider

- method `Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken)`

### interface IMusicPlayer

- method `Task<MusicPlayerArtwork> GetArtworkAsync(String artworkId, CancellationToken cancellationToken)`
- method `Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken)`
- method `Task NextAsync(CancellationToken cancellationToken)`
- method `Task PauseAsync(CancellationToken cancellationToken)`
- method `Task PlayAsync(CancellationToken cancellationToken)`
- method `Task PreviousAsync(CancellationToken cancellationToken)`
- method `Task SeekAsync(TimeSpan position, CancellationToken cancellationToken)`
- method `Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken)`
- method `Task SetShuffleAsync(Boolean enabled, CancellationToken cancellationToken)`
- method `Task SetVolumeAsync(Int32 volumePercent, CancellationToken cancellationToken)`
- method `Task TogglePlayPauseAsync(CancellationToken cancellationToken)`

### interface IMusicPlayerCatalogProvider

- method `Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(String instanceId, MusicPlayerCatalogItemKind kind, String filter, CancellationToken cancellationToken)`

### interface IMusicPlayerDeviceProvider

- method `Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)`
- method `Task TransferPlaybackAsync(String deviceId, Boolean startPlayback, CancellationToken cancellationToken)`

### interface IMusicPlayerProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<MusicPlayerInstance> GetInstances()`
- method `IMusicPlayer GetPlayer(String instanceId)`

### class MusicPlayerArtwork : IEquatable<MusicPlayerArtwork>

- property `Byte[] Data { get; set; }`
- property `String MimeType { get; set; }`
- method `MusicPlayerArtwork <Clone>$()`
- method `Void Deconstruct(Byte[]& Data, String& MimeType)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MusicPlayerArtwork other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(MusicPlayerArtwork left, MusicPlayerArtwork right)`
- method `Boolean op_Inequality(MusicPlayerArtwork left, MusicPlayerArtwork right)`

### class MusicPlayerCatalogItem : IEquatable<MusicPlayerCatalogItem>

- property `String ArtworkId { get; set; }`
- property `Nullable<TimeSpan> Duration { get; set; }`
- property `String Id { get; set; }`
- property `MusicPlayerCatalogItemKind Kind { get; set; }`
- property `String Subtitle { get; set; }`
- property `String Title { get; set; }`
- method `MusicPlayerCatalogItem <Clone>$()`
- method `Void Deconstruct(String& Id, String& Title, MusicPlayerCatalogItemKind& Kind, String& Subtitle, String& ArtworkId, Nullable`1& Duration)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MusicPlayerCatalogItem other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(MusicPlayerCatalogItem left, MusicPlayerCatalogItem right)`
- method `Boolean op_Inequality(MusicPlayerCatalogItem left, MusicPlayerCatalogItem right)`

### enum MusicPlayerCatalogItemKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Track, Playlist

### class MusicPlayerDevice : IEquatable<MusicPlayerDevice>

- property `String Id { get; set; }`
- property `Boolean IsActive { get; set; }`
- property `String Name { get; set; }`
- property `String Type { get; set; }`
- property `Nullable<Int32> VolumePercent { get; set; }`
- method `MusicPlayerDevice <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name, String& Type, Boolean& IsActive, Nullable`1& VolumePercent)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MusicPlayerDevice other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(MusicPlayerDevice left, MusicPlayerDevice right)`
- method `Boolean op_Inequality(MusicPlayerDevice left, MusicPlayerDevice right)`

### class MusicPlayerInstance : IEquatable<MusicPlayerInstance>

- property `String DisplayName { get; set; }`
- property `String Id { get; set; }`
- method `MusicPlayerInstance <Clone>$()`
- method `Void Deconstruct(String& Id, String& DisplayName)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MusicPlayerInstance other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(MusicPlayerInstance left, MusicPlayerInstance right)`
- method `Boolean op_Inequality(MusicPlayerInstance left, MusicPlayerInstance right)`

### class MusicPlayerState : IEquatable<MusicPlayerState>

- property `String AlbumName { get; set; }`
- property `IReadOnlyList<String> Artists { get; set; }`
- property `String ArtworkId { get; set; }`
- property `String DeviceName { get; set; }`
- property `String DeviceType { get; set; }`
- property `MusicPlayerState Disconnected { get; }`
- property `Nullable<TimeSpan> Duration { get; set; }`
- property `Boolean IsConnected { get; set; }`
- property `Boolean IsUnavailable { get; set; }`
- property `PlaybackState PlaybackState { get; set; }`
- property `Nullable<TimeSpan> Position { get; set; }`
- property `RepeatMode RepeatMode { get; set; }`
- property `Boolean ShuffleEnabled { get; set; }`
- property `String StatusMessage { get; set; }`
- property `String TrackName { get; set; }`
- property `Nullable<Int32> VolumePercent { get; set; }`
- method `MusicPlayerState <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(MusicPlayerState other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `MusicPlayerState Unavailable(String statusMessage)`
- method `Boolean op_Equality(MusicPlayerState left, MusicPlayerState right)`
- method `Boolean op_Inequality(MusicPlayerState left, MusicPlayerState right)`

### static class MusicPlayerVariableWrites

- field `VariableWriteCapability Position (readonly)`
- field `VariableWriteCapability Volume (readonly)`
- method `ValueTask<VariableWriteResult> SeekAsync(IMusicPlayer player, Object value, CancellationToken cancellationToken)`
- method `ValueTask<VariableWriteResult> SetVolumeAsync(IMusicPlayer player, Object value, CancellationToken cancellationToken)`
- method `Boolean TryReadNumber(Object value, Double& number)`

### enum PlaybackState : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Stopped, Playing, Paused

### enum RepeatMode : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Off, Track, Context

## MacroDeck.Sdk.MusicPlayer.Actions

### class MusicPlayerActionDefinition : IActionDefinition, IDynamicOptionsActionDefinition

- property `LocalizedText Description { get; }`
- property `String Id { get; }`
- property `LocalizedText Name { get; }`
- property `IReadOnlyList<ActionParameter> Parameters { get; }`
- method `IActionExecutor CreateExecutor()`
- method `Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)`

### static class MusicPlayerActions

- field `String DeviceParameterName = device`
- field `String InstanceParameterName = instance`
- method `IReadOnlyList<IActionDefinition> Common(MusicPlayerResolver resolver, Func<IReadOnlyList<MusicPlayerInstance>> getInstances)`
- method `IActionDefinition PlayOnDevice(String integrationId, MusicPlayerResolver resolver, Func<IReadOnlyList<MusicPlayerInstance>> getInstances)`
- method `IActionDefinition PlayPlaylist(String integrationId, MusicPlayerResolver resolver, Func<IReadOnlyList<MusicPlayerInstance>> getInstances)`
- method `IActionDefinition PlayTrack(String integrationId, MusicPlayerResolver resolver, Func<IReadOnlyList<MusicPlayerInstance>> getInstances)`
- method `IActionDefinition TransferPlayback(String integrationId, MusicPlayerResolver resolver, Func<IReadOnlyList<MusicPlayerInstance>> getInstances)`

### class MusicPlayerCommand : MulticastDelegate, ICloneable, ISerializable

- method `IAsyncResult BeginInvoke(IMusicPlayer player, IReadOnlyDictionary<String, Object> values, IActionInteractions interactions, CancellationToken cancellationToken, AsyncCallback callback, Object object)`
- method `Task EndInvoke(IAsyncResult result)`
- method `Task Invoke(IMusicPlayer player, IReadOnlyDictionary<String, Object> values, IActionInteractions interactions, CancellationToken cancellationToken)`

### class MusicPlayerDeviceActionDefinition : MusicPlayerActionDefinition, IActionDefinition, IDynamicOptionsActionDefinition

- method `IActionExecutor CreateExecutor()`
- method `Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)`

### class MusicPlayerItemActionDefinition : MusicPlayerActionDefinition, IActionDefinition, IDynamicOptionsActionDefinition

- method `IActionExecutor CreateExecutor()`
- method `Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)`

### class MusicPlayerResolver : MulticastDelegate, ICloneable, ISerializable

- method `IAsyncResult BeginInvoke(String instanceId, AsyncCallback callback, Object object)`
- method `IMusicPlayer EndInvoke(IAsyncResult result)`
- method `IMusicPlayer Invoke(String instanceId)`

### class MusicPlayerStateActionDefinition : MusicPlayerActionDefinition, IActionDefinition, IDynamicOptionsActionDefinition, IStateProviderActionDefinition

- method `IActionExecutor CreateExecutor()`
- method `Task<ActionStateSnapshot> GetActionStateAsync(IReadOnlyDictionary<String, Object> parameters, CancellationToken cancellationToken)`

## MacroDeck.Sdk.Notifications

### interface IUserNotifier

- method `Void Dismiss(String key)`
- method `Void Notify(UserNotificationRequest notification)`

### enum UserNotificationLevel : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Info, Warning, Error

### class UserNotificationRequest

- property `String Key { get; set; }`
- property `UserNotificationLevel Level { get; set; }`
- property `String Message { get; set; }`
- property `String Title { get; set; }`

## MacroDeck.Sdk.Profiles

### interface IProfileProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<VirtualProfileDescriptor> GetProfiles()`
- method `Task HandleWidgetInteractionAsync(String profileId, String folderId, String widgetId, WidgetInteraction interaction)`

### enum LayoutKind : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Grid

### class ProfileLayout : IEquatable<ProfileLayout>

- property `Int32 Columns { get; set; }`
- property `Boolean ColumnsLocked { get; set; }`
- property `LayoutKind Kind { get; set; }`
- property `Int32 Rows { get; set; }`
- property `Boolean RowsLocked { get; set; }`
- method `ProfileLayout <Clone>$()`
- method `Void Deconstruct(LayoutKind& Kind, Int32& Rows, Int32& Columns, Boolean& RowsLocked, Boolean& ColumnsLocked)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ProfileLayout other)`
- method `Int32 GetHashCode()`
- method `ProfileLayout Grid(Int32 rows, Int32 columns, Boolean locked)`
- method `String ToString()`
- method `Boolean op_Equality(ProfileLayout left, ProfileLayout right)`
- method `Boolean op_Inequality(ProfileLayout left, ProfileLayout right)`

### class VirtualFolderDescriptor : IEquatable<VirtualFolderDescriptor>

- property `String Id { get; set; }`
- property `String Name { get; set; }`
- property `Int32 Order { get; set; }`
- property `String ParentId { get; set; }`
- property `IReadOnlyList<VirtualWidgetDescriptor> Widgets { get; set; }`
- method `VirtualFolderDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name, IReadOnlyList`1& Widgets, String& ParentId, Int32& Order)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VirtualFolderDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VirtualFolderDescriptor left, VirtualFolderDescriptor right)`
- method `Boolean op_Inequality(VirtualFolderDescriptor left, VirtualFolderDescriptor right)`

### class VirtualProfileDescriptor : IEquatable<VirtualProfileDescriptor>

- property `IReadOnlyList<VirtualFolderDescriptor> Folders { get; set; }`
- property `String Id { get; set; }`
- property `ProfileLayout Layout { get; set; }`
- property `String Name { get; set; }`
- method `VirtualProfileDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, String& Name, ProfileLayout& Layout, IReadOnlyList`1& Folders)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VirtualProfileDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VirtualProfileDescriptor left, VirtualProfileDescriptor right)`
- method `Boolean op_Inequality(VirtualProfileDescriptor left, VirtualProfileDescriptor right)`

### class VirtualWidgetDescriptor : IEquatable<VirtualWidgetDescriptor>

- property `String Data { get; set; }`
- property `Int32 Height { get; set; }`
- property `String Id { get; set; }`
- property `Int32 PositionX { get; set; }`
- property `Int32 PositionY { get; set; }`
- property `String Type { get; set; }`
- property `Int32 Width { get; set; }`
- method `VirtualWidgetDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, String& Type, Int32& PositionX, Int32& PositionY, Int32& Width, Int32& Height, String& Data)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VirtualWidgetDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VirtualWidgetDescriptor left, VirtualWidgetDescriptor right)`
- method `Boolean op_Inequality(VirtualWidgetDescriptor left, VirtualWidgetDescriptor right)`

### class WidgetInteraction : IEquatable<WidgetInteraction>

- property `String TriggerType { get; set; }`
- method `WidgetInteraction <Clone>$()`
- method `Void Deconstruct(String& TriggerType)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetInteraction other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WidgetInteraction left, WidgetInteraction right)`
- method `Boolean op_Inequality(WidgetInteraction left, WidgetInteraction right)`

## MacroDeck.Sdk.ScreenSavers

### interface IScreenSaverProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<ScreenSaverDescriptor> GetScreenSavers()`
- method `Task InitializeAsync(IScreenSaverProviderContext context, CancellationToken cancellationToken)`

### interface IScreenSaverProviderContext

- method `Task<ScreenSaverRegistration> RegisterScreenSaverAsync(ScreenSaverDescriptor screenSaver, CancellationToken cancellationToken)`
- method `Task UnregisterScreenSaverAsync(String screenSaverId, CancellationToken cancellationToken)`

### class ScreenSaverDescriptor : IEquatable<ScreenSaverDescriptor>

- property `Nullable<LocalizedText> Description { get; set; }`
- property `Boolean HasConfiguration { get; set; }`
- property `String Id { get; set; }`
- property `Boolean Interactive { get; set; }`
- property `IReadOnlyDictionary<String, String> Metadata { get; set; }`
- property `LocalizedText Name { get; set; }`
- method `ScreenSaverDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, LocalizedText& Name, Nullable`1& Description, Boolean& HasConfiguration, Boolean& Interactive, IReadOnlyDictionary`2& Metadata)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ScreenSaverDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ScreenSaverDescriptor left, ScreenSaverDescriptor right)`
- method `Boolean op_Inequality(ScreenSaverDescriptor left, ScreenSaverDescriptor right)`

### class ScreenSaverRegistration : IEquatable<ScreenSaverRegistration>

- property `String ProviderId { get; set; }`
- property `String ScreenSaverId { get; set; }`
- method `ScreenSaverRegistration <Clone>$()`
- method `Void Deconstruct(String& ScreenSaverId, String& ProviderId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ScreenSaverRegistration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ScreenSaverRegistration left, ScreenSaverRegistration right)`
- method `Boolean op_Inequality(ScreenSaverRegistration left, ScreenSaverRegistration right)`

## MacroDeck.Sdk.Scripts

### interface IScriptApi

- method `IReadOnlyList<Script> GetScripts()`
- method `Task<ActionResult> RunAsync(String scriptId, IReadOnlyDictionary<String, Object> inputs, String originClientId, String ownerWidgetId, CancellationToken cancellationToken)`

### class Script

- property `String Description { get; set; }`
- property `String Id { get; set; }`
- property `IReadOnlyList<ScriptInput> Inputs { get; set; }`
- property `String Name { get; set; }`
- property `Boolean RunsOnWidget { get; set; }`

### class ScriptInput

- property `String DefaultValue { get; set; }`
- property `String Description { get; set; }`
- property `String Label { get; set; }`
- property `String Name { get; set; }`
- property `Boolean Required { get; set; }`
- property `ScriptInputType Type { get; set; }`

### enum ScriptInputType : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Text, Numeric, Boolean

## MacroDeck.Sdk.Ui

### interface IUiInteractions

- method `Task<Boolean> ShowModalAsync(String originClientId, ModalDefinition modal, CancellationToken cancellationToken)`
- method `Task<ModalResult<T>> ShowModalAsync(String originClientId, ModalDefinition modal, CancellationToken cancellationToken)`

### interface IUiProvider

- property `IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; }`
- method `Task<IUiSession> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)`

### interface IUiResourceRegistry

- method `Task<UiResource> GetPluginIconAsync(String key, String name, CancellationToken cancellationToken)`
- method `Task<UiResource> RegisterAsync(String name, ReadOnlyMemory<Byte> content, String mediaType, CancellationToken cancellationToken)`
- method `Task RemoveAsync(String name, CancellationToken cancellationToken)`

### interface IUiSession : IAsyncDisposable

- method `UiTree BuildTree()`
- method `Void Dispatch(UiEvent uiEvent)`
- method `IReadOnlyList<UiPatch> DrainPatches()`
- event `EventHandler Changed`
- event `EventHandler<UiSessionFaultedEventArgs> Faulted`

### class ModalDefinition : IEquatable<ModalDefinition>

- property `IReadOnlyDictionary<String, JsonElement> Data { get; set; }`
- property `Nullable<LocalizedText> Title { get; set; }`
- property `String ViewId { get; set; }`
- method `ModalDefinition <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ModalDefinition other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ModalDefinition left, ModalDefinition right)`
- method `Boolean op_Inequality(ModalDefinition left, ModalDefinition right)`

### static class ModalResult

- method `ModalResult<T> FromCancellation()`
- method `ModalResult<T> FromValue(T value)`

### class ModalResult`1 : IEquatable<ModalResult<T>>

- property `Boolean Cancelled { get; set; }`
- property `T Value { get; set; }`
- method `ModalResult<T> <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(ModalResult<T> other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(ModalResult<T> left, ModalResult<T> right)`
- method `Boolean op_Inequality(ModalResult<T> left, ModalResult<T> right)`

### enum UiResourceErrorCode : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Failed, Unsupported, QuotaExceeded, RateLimited, PluginIconNotFound

### class UiResourceException : Exception, ISerializable

- property `UiResourceErrorCode ErrorCode { get; }`

### class UiSessionFaultedEventArgs : EventArgs

- property `Exception Exception { get; }`
- property `String Reason { get; }`

### class UiSessionRequest : IEquatable<UiSessionRequest>

- property `UiSurface Surface { get; set; }`
- property `Int32 UiModelVersion { get; set; }`
- method `UiSessionRequest <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(UiSessionRequest other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(UiSessionRequest left, UiSessionRequest right)`
- method `Boolean op_Inequality(UiSessionRequest left, UiSessionRequest right)`

### class UiSurfaceDeclaration : IEquatable<UiSurfaceDeclaration>

- property `String Kind { get; set; }`
- property `String SessionMode { get; set; }`
- method `UiSurfaceDeclaration <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(UiSurfaceDeclaration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(UiSurfaceDeclaration left, UiSurfaceDeclaration right)`
- method `Boolean op_Inequality(UiSurfaceDeclaration left, UiSurfaceDeclaration right)`

## MacroDeck.Sdk.Variables

### interface IUserVariableApi

- method `Task<UserVariableWriteResult> ApplyAsync(String name, String ownerWidgetId, UserVariableOperation operation, String value, CancellationToken cancellationToken)`
- method `Task<UserVariableCreateResult> CreateAsync(String name, String ownerWidgetId, VariableType type, String initialValue, Nullable<Int32> decimalPlaces, CancellationToken cancellationToken)`

### interface IVariableApi

- method `Task<VariableHandle> CreateAsync(String name, VariableType type, Object initialValue, Nullable<Int32> decimalPlaces, String definitionId)`
- method `Task<VariableHandle> CreateAsync(VariableDefinition declaration, Object initialValue)`
- method `Task DeleteAsync(Guid variableId)`
- method `Task<IReadOnlyList<VariableHandle>> GetAllAsync()`
- method `Task<VariableHandle> GetByNameAsync(String name)`
- method `Task SetValueAsync(Guid variableId, Object value)`

### interface IVariableProvider

- property `Nullable<Int32> CatalogEntryCount { get; }`
- property `String CatalogName { get; }`
- property `IReadOnlyList<VariableDefinition> DeclaredVariables { get; }`
- property `Boolean SupportsCatalog { get; }`
- property `Boolean SupportsPush { get; }`
- property `Boolean SupportsSearch { get; }`
- property `IReadOnlyList<VariableDefinition> Variables { get; }`
- property `Boolean VariablesDependOnConfiguration { get; }`
- method `ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken)`
- method `Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken)`
- method `ValueTask<VariableReading> ReadAsync(String localId, CancellationToken cancellationToken)`
- method `ValueTask<VariableDefinition> ResolveAsync(String localId, CancellationToken cancellationToken)`
- method `ValueTask<VariableWriteResult> SetValueAsync(String localId, Object value, CancellationToken cancellationToken)`
- method `ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(IReadOnlyCollection<String> localIds, CancellationToken cancellationToken)`

### interface IVariableSink

- method `Task InvalidateCatalogAsync(CancellationToken cancellationToken)`
- method `Task PublishAsync(IReadOnlyCollection<VariableValue> values, CancellationToken cancellationToken)`
- method `Task PublishAsync(VariableValue value, CancellationToken cancellationToken)`

### class UserVariableCreateResult : IEquatable<UserVariableCreateResult>

- property `String Message { get; set; }`
- property `UserVariableCreateStatus Status { get; set; }`
- method `UserVariableCreateResult <Clone>$()`
- method `UserVariableCreateResult Created()`
- method `Void Deconstruct(UserVariableCreateStatus& Status, String& Message)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(UserVariableCreateResult other)`
- method `UserVariableCreateResult Failed(UserVariableCreateStatus status, String message)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(UserVariableCreateResult left, UserVariableCreateResult right)`
- method `Boolean op_Inequality(UserVariableCreateResult left, UserVariableCreateResult right)`

### enum UserVariableCreateStatus : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Created, AlreadyExists, InvalidName, InvalidValue, UnknownWidget, NotSupported, Unavailable

### enum UserVariableOperation : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Set, Add, Toggle, Append

### class UserVariableWriteResult : IEquatable<UserVariableWriteResult>

- property `String Message { get; set; }`
- property `UserVariableWriteStatus Status { get; set; }`
- method `UserVariableWriteResult <Clone>$()`
- method `UserVariableWriteResult Applied()`
- method `Void Deconstruct(UserVariableWriteStatus& Status, String& Message)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(UserVariableWriteResult other)`
- method `UserVariableWriteResult Failed(UserVariableWriteStatus status, String message)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(UserVariableWriteResult left, UserVariableWriteResult right)`
- method `Boolean op_Inequality(UserVariableWriteResult left, UserVariableWriteResult right)`

### enum UserVariableWriteStatus : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Applied, NotFound, NotEditable, InvalidValue, Unavailable

### class VariableCatalogPage : IEquatable<VariableCatalogPage>

- field `VariableCatalogPage Empty (readonly)`
- property `String ContinuationToken { get; set; }`
- property `IReadOnlyList<VariableDefinition> Items { get; set; }`
- method `VariableCatalogPage <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableCatalogPage other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VariableCatalogPage left, VariableCatalogPage right)`
- method `Boolean op_Inequality(VariableCatalogPage left, VariableCatalogPage right)`

### class VariableCatalogQuery : IEquatable<VariableCatalogQuery>

- property `String ContinuationToken { get; set; }`
- property `Int32 PageSize { get; set; }`
- property `String ParentId { get; set; }`
- property `String Search { get; set; }`
- method `VariableCatalogQuery <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableCatalogQuery other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VariableCatalogQuery left, VariableCatalogQuery right)`
- method `Boolean op_Inequality(VariableCatalogQuery left, VariableCatalogQuery right)`

### class VariableConfiguration : IEquatable<VariableConfiguration>

- property `String Key { get; set; }`
- property `LocalizedText Name { get; set; }`
- method `VariableConfiguration <Clone>$()`
- method `Void Deconstruct(String& Key, LocalizedText& Name)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableConfiguration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VariableConfiguration left, VariableConfiguration right)`
- method `Boolean op_Inequality(VariableConfiguration left, VariableConfiguration right)`

### class VariableDefinition : IEquatable<VariableDefinition>

- property `IReadOnlyDictionary<String, String> Attributes { get; set; }`
- property `Boolean CanWrite { get; }`
- property `VariableConfiguration Configuration { get; set; }`
- property `Nullable<Int32> DecimalPlaces { get; set; }`
- property `LocalizedText Description { get; set; }`
- property `LocalizedText DisplayName { get; set; }`
- property `String Icon { get; set; }`
- property `String Id { get; set; }`
- property `Boolean IsBindable { get; set; }`
- property `Boolean IsContainer { get; set; }`
- property `VariableMaterialization Materialization { get; set; }`
- property `String Name { get; set; }`
- property `String ParentId { get; set; }`
- property `Nullable<TimeSpan> RefreshInterval { get; set; }`
- property `String ResolvedId { get; }`
- property `String SemanticKind { get; set; }`
- property `VariableType Type { get; set; }`
- property `String Unit { get; set; }`
- property `VariableWriteCapability Write { get; set; }`
- method `VariableDefinition <Clone>$()`
- method `VariableDefinition Eager(String name, VariableType type, Nullable<Int32> decimalPlaces, Nullable<TimeSpan> refreshInterval)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableDefinition other)`
- method `Int32 GetHashCode()`
- method `VariableDefinition OnDemand(String id, VariableType type)`
- method `String ToString()`
- method `Boolean op_Equality(VariableDefinition left, VariableDefinition right)`
- method `Boolean op_Inequality(VariableDefinition left, VariableDefinition right)`

### static class VariableDefinitionId

- method `String FromName(String canonicalName)`

### class VariableHandle : IEquatable<VariableHandle>

- property `Nullable<Int32> DecimalPlaces { get; set; }`
- property `String DefinitionId { get; set; }`
- property `Guid Id { get; set; }`
- property `String Name { get; set; }`
- property `VariableType Type { get; set; }`
- property `Object Value { get; set; }`
- method `VariableHandle <Clone>$()`
- method `Void Deconstruct(Guid& Id, String& Name, VariableType& Type, Object& Value, Nullable`1& DecimalPlaces)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableHandle other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VariableHandle left, VariableHandle right)`
- method `Boolean op_Inequality(VariableHandle left, VariableHandle right)`

### static class VariableLimits

- field `Int32 MaxAttributeEntries = 32`
- field `Int32 MaxAttributeValueLength = 256`
- field `Int32 MaxEagerVariablesPerProvider = 256`

### enum VariableMaterialization : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Eager, OnDemand

### static class VariableNameTemplate

- field `String PlaceholderEnd = >`
- field `String PlaceholderStart = <`
- method `Boolean IsTemplate(String name)`
- method `String Placeholder(String label)`

### class VariableReading : IEquatable<VariableReading>

- field `VariableReading Unavailable (readonly)`
- property `Nullable<Double> Max { get; set; }`
- property `Nullable<Double> Min { get; set; }`
- property `Nullable<Double> Step { get; set; }`
- property `Object Value { get; set; }`
- method `VariableReading <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableReading other)`
- method `Int32 GetHashCode()`
- method `VariableReading Of(Object value)`
- method `VariableReading Of(Object value, Nullable<Double> min, Nullable<Double> max, Nullable<Double> step)`
- method `String ToString()`
- method `Boolean op_Equality(VariableReading left, VariableReading right)`
- method `Boolean op_Inequality(VariableReading left, VariableReading right)`

### static class VariableSemanticKinds

- field `String Bytes = bytes`
- field `String Duration = duration`
- field `String None = none`
- field `String Percentage = percentage`

### enum VariableType : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Text, Numeric, Boolean

### class VariableValue : IEquatable<VariableValue>

- property `String Id { get; set; }`
- property `VariableReading Reading { get; set; }`
- method `VariableValue <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableValue other)`
- method `Int32 GetHashCode()`
- method `VariableValue Of(String id, Object value)`
- method `VariableValue Of(String id, VariableReading reading)`
- method `String ToString()`
- method `VariableValue Unavailable(String id)`
- method `Boolean op_Equality(VariableValue left, VariableValue right)`
- method `Boolean op_Inequality(VariableValue left, VariableValue right)`

### class VariableWriteCapability : IEquatable<VariableWriteCapability>

- property `Boolean CommitOnRelease { get; set; }`
- method `VariableWriteCapability <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableWriteCapability other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(VariableWriteCapability left, VariableWriteCapability right)`
- method `Boolean op_Inequality(VariableWriteCapability left, VariableWriteCapability right)`

### class VariableWriteResult : IEquatable<VariableWriteResult>

- property `LocalizedText Message { get; set; }`
- property `VariableWriteStatus Status { get; set; }`
- method `VariableWriteResult <Clone>$()`
- method `VariableWriteResult Applied()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(VariableWriteResult other)`
- method `VariableWriteResult Failed(LocalizedText message)`
- method `Int32 GetHashCode()`
- method `VariableWriteResult InvalidValue(LocalizedText message)`
- method `VariableWriteResult NotFound(LocalizedText message)`
- method `VariableWriteResult NotWritable(LocalizedText message)`
- method `String ToString()`
- method `VariableWriteResult Unavailable(LocalizedText message)`
- method `Boolean op_Equality(VariableWriteResult left, VariableWriteResult right)`
- method `Boolean op_Inequality(VariableWriteResult left, VariableWriteResult right)`

### enum VariableWriteStatus : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Applied, NotWritable, NotFound, Unavailable, InvalidValue, Failed

## MacroDeck.Sdk.Weather

### interface IWeatherProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<WeatherStationInstance> GetInstances()`
- method `IWeatherStation GetStation(String instanceId)`

### interface IWeatherStation

- method `Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct)`

### enum TemperatureUnit : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Celsius, Fahrenheit

### enum WeatherCondition : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Unknown, Clear, MainlyClear, PartlyCloudy, Overcast, Fog, Drizzle, Rain, FreezingRain, Snow, SnowGrains, RainShowers, SnowShowers, Thunderstorm

### class WeatherForecastDay : IEquatable<WeatherForecastDay>

- property `WeatherCondition Condition { get; set; }`
- property `DateOnly Date { get; set; }`
- property `Double Max { get; set; }`
- property `Double Min { get; set; }`
- method `WeatherForecastDay <Clone>$()`
- method `Void Deconstruct(DateOnly& Date, WeatherCondition& Condition, Double& Min, Double& Max)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WeatherForecastDay other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WeatherForecastDay left, WeatherForecastDay right)`
- method `Boolean op_Inequality(WeatherForecastDay left, WeatherForecastDay right)`

### class WeatherHour : IEquatable<WeatherHour>

- property `WeatherCondition Condition { get; set; }`
- property `Nullable<Double> PrecipitationProbability { get; set; }`
- property `Double Temperature { get; set; }`
- property `DateTimeOffset Time { get; set; }`
- method `WeatherHour <Clone>$()`
- method `Void Deconstruct(DateTimeOffset& Time, WeatherCondition& Condition, Double& Temperature, Nullable`1& PrecipitationProbability)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WeatherHour other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WeatherHour left, WeatherHour right)`
- method `Boolean op_Inequality(WeatherHour left, WeatherHour right)`

### class WeatherSnapshot : IEquatable<WeatherSnapshot>

- property `Nullable<Double> ApparentTemperature { get; set; }`
- property `WeatherCondition Condition { get; set; }`
- property `IReadOnlyList<WeatherForecastDay> Days { get; set; }`
- property `IReadOnlyList<WeatherHour> Hours { get; set; }`
- property `Nullable<Double> Humidity { get; set; }`
- property `Boolean IsAvailable { get; set; }`
- property `Boolean IsDay { get; set; }`
- property `String LocationName { get; set; }`
- property `Nullable<Double> Precipitation { get; set; }`
- property `Nullable<DateTimeOffset> Sunrise { get; set; }`
- property `Nullable<DateTimeOffset> Sunset { get; set; }`
- property `Nullable<Double> Temperature { get; set; }`
- property `TemperatureUnit Unit { get; set; }`
- property `Nullable<Double> WindDirection { get; set; }`
- property `Nullable<Double> WindSpeed { get; set; }`
- method `WeatherSnapshot <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WeatherSnapshot other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `WeatherSnapshot Unavailable(String locationName)`
- method `Boolean op_Equality(WeatherSnapshot left, WeatherSnapshot right)`
- method `Boolean op_Inequality(WeatherSnapshot left, WeatherSnapshot right)`

### class WeatherStationInstance : IEquatable<WeatherStationInstance>

- property `String DisplayName { get; set; }`
- property `String Id { get; set; }`
- method `WeatherStationInstance <Clone>$()`
- method `Void Deconstruct(String& Id, String& DisplayName)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WeatherStationInstance other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WeatherStationInstance left, WeatherStationInstance right)`
- method `Boolean op_Inequality(WeatherStationInstance left, WeatherStationInstance right)`

## MacroDeck.Sdk.Widgets

### interface IWidgetApi

- method `Task<WidgetStateWriteResult> AdvanceStateAsync(String widgetId, CancellationToken cancellationToken)`
- method `Task<Boolean> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken)`
- method `Boolean Exists(String widgetId)`
- method `IReadOnlyList<WidgetTargetInfo> GetWidgets()`
- method `Task InvalidateIconAsync(String actionId, CancellationToken cancellationToken)`
- method `Task<WidgetStateWriteResult> SetStateAsync(String widgetId, String stateId, CancellationToken cancellationToken)`

### interface IWidgetTypeProvider

- property `String ProviderName { get; }`
- method `IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()`
- method `Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)`

### interface IWidgetTypeProviderContext

- method `Task<WidgetTypeRegistration> RegisterWidgetTypeAsync(WidgetTypeDescriptor widgetType, CancellationToken cancellationToken)`
- method `Task UnregisterWidgetTypeAsync(String widgetTypeId, CancellationToken cancellationToken)`

### class WidgetAppearancePatch : IEquatable<WidgetAppearancePatch>

- property `String AccentColor { get; set; }`
- property `String BackgroundColor { get; set; }`
- property `String BorderColor { get; set; }`
- property `String BorderStyle { get; set; }`
- property `String FontFaceId { get; set; }`
- property `Nullable<Double> FontSize { get; set; }`
- property `String IconColor { get; set; }`
- property `String IconFit { get; set; }`
- property `String IconId { get; set; }`
- property `Nullable<Double> IconOffsetX { get; set; }`
- property `Nullable<Double> IconOffsetY { get; set; }`
- property `Nullable<Double> IconOpacity { get; set; }`
- property `Nullable<Double> IconZoom { get; set; }`
- property `Boolean IsEmpty { get; }`
- property `String Label { get; set; }`
- property `String LabelColor { get; set; }`
- property `String LabelPosition { get; set; }`
- property `String TextAlign { get; set; }`
- method `WidgetAppearancePatch <Clone>$()`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetAppearancePatch other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WidgetAppearancePatch left, WidgetAppearancePatch right)`
- method `Boolean op_Inequality(WidgetAppearancePatch left, WidgetAppearancePatch right)`

### enum WidgetAppearanceProperty : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: BackgroundColor, Label, LabelColor, Icon, Font, Border, BorderColor, IconDisplay, AccentColor, IconColor

### class WidgetAppearanceRequest

- property `IReadOnlyCollection<WidgetAppearanceProperty> ClearProperties { get; set; }`
- property `WidgetAppearancePatch Patch { get; set; }`
- property `WidgetStateSelector State { get; set; }`
- property `IReadOnlyCollection<String> StateIds { get; set; }`
- property `String WidgetId { get; set; }`
- method `IReadOnlyCollection<String> ResolveStateIds()`

### static class WidgetAppearanceValues

- field `String Reset = $reset`
- method `Boolean IsReset(String value)`

### static class WidgetOptionsSources

- field `String Fonts = macrodeck.fonts`
- field `String Widgets = macrodeck.widgets`

### class WidgetStateInfo : IEquatable<WidgetStateInfo>

- property `String Id { get; set; }`
- property `String Label { get; set; }`
- method `WidgetStateInfo <Clone>$()`
- method `Void Deconstruct(String& Id, String& Label)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetStateInfo other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WidgetStateInfo left, WidgetStateInfo right)`
- method `Boolean op_Inequality(WidgetStateInfo left, WidgetStateInfo right)`

### enum WidgetStateSelector : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: Current, On, Off, Both

### enum WidgetStateWriteError : IComparable, IConvertible, IFormattable, ISpanFormattable

Values: NotFound, ProviderActive, MappingActive, UnknownState

### class WidgetStateWriteResult : IEquatable<WidgetStateWriteResult>

- property `Nullable<WidgetStateWriteError> Error { get; set; }`
- property `String StateId { get; set; }`
- property `Boolean Success { get; set; }`
- method `WidgetStateWriteResult <Clone>$()`
- method `Void Deconstruct(Boolean& Success, Nullable`1& Error, String& StateId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetStateWriteResult other)`
- method `WidgetStateWriteResult Failed(WidgetStateWriteError error)`
- method `Int32 GetHashCode()`
- method `WidgetStateWriteResult Succeeded(String stateId)`
- method `String ToString()`
- method `Boolean op_Equality(WidgetStateWriteResult left, WidgetStateWriteResult right)`
- method `Boolean op_Inequality(WidgetStateWriteResult left, WidgetStateWriteResult right)`

### static class WidgetStates

- field `String All = $all`
- field `String Current = $current`
- method `Boolean IsAll(String value)`
- method `Boolean IsCurrent(String value)`

### class WidgetTargetInfo

- property `IReadOnlyCollection<WidgetAppearanceProperty> AppearanceProperties { get; set; }`
- property `String CurrentStateId { get; set; }`
- property `Boolean HasActiveIconProvider { get; set; }`
- property `Boolean HasOnOffStates { get; set; }`
- property `String Id { get; set; }`
- property `String Label { get; set; }`
- property `String Location { get; set; }`
- property `IReadOnlyList<WidgetStateInfo> States { get; set; }`
- property `String Type { get; set; }`

### static class WidgetTargets

- field `String Self = $self`
- method `Boolean IsSelf(String value)`

### class WidgetTypeDescriptor : IEquatable<WidgetTypeDescriptor>

- property `IReadOnlyList<WidgetAppearanceProperty> AppearanceProperties { get; set; }`
- property `String DataSchema { get; set; }`
- property `String DefaultData { get; set; }`
- property `Nullable<LocalizedText> Description { get; set; }`
- property `Boolean HasConfiguration { get; set; }`
- property `String Id { get; set; }`
- property `IReadOnlyDictionary<String, String> Metadata { get; set; }`
- property `LocalizedText Name { get; set; }`
- property `Boolean SupportsFlows { get; set; }`
- method `WidgetTypeDescriptor <Clone>$()`
- method `Void Deconstruct(String& Id, LocalizedText& Name, Nullable`1& Description, String& DefaultData, String& DataSchema, Boolean& HasConfiguration, IReadOnlyDictionary`2& Metadata)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetTypeDescriptor other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WidgetTypeDescriptor left, WidgetTypeDescriptor right)`
- method `Boolean op_Inequality(WidgetTypeDescriptor left, WidgetTypeDescriptor right)`

### class WidgetTypeRegistration : IEquatable<WidgetTypeRegistration>

- property `String ProviderId { get; set; }`
- property `String WidgetTypeId { get; set; }`
- method `WidgetTypeRegistration <Clone>$()`
- method `Void Deconstruct(String& WidgetTypeId, String& ProviderId)`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(WidgetTypeRegistration other)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(WidgetTypeRegistration left, WidgetTypeRegistration right)`
- method `Boolean op_Inequality(WidgetTypeRegistration left, WidgetTypeRegistration right)`

