# MacroDeck.Localization 3.0.0.0

Assembly: `C:\Users\Misu\.nuget\packages\macrodeck.localization\3.0.0-beta.14\lib\net10.0\MacroDeck.Localization.dll`
Exported types: 23

## MacroDeck.Localization

### static class Appearance

- method `LocalizedString AccentColor()`
- method `LocalizedString BackgroundColor()`
- method `LocalizedString Border()`
- method `LocalizedString BorderBlink()`
- method `LocalizedString BorderBreathing()`
- method `LocalizedString BorderColor()`
- method `LocalizedString BorderComet()`
- method `LocalizedString BorderHeartbeat()`
- method `LocalizedString BorderHueShift()`
- method `LocalizedString BorderMarchingAnts()`
- method `LocalizedString BorderOff()`
- method `LocalizedString BorderRgb()`
- method `LocalizedString BorderStatic()`
- method `LocalizedString BorderStyle()`
- method `LocalizedString Font()`
- method `LocalizedString FontSize()`
- method `LocalizedString Heading()`
- method `LocalizedString Label()`
- method `LocalizedString LabelColor()`
- method `LocalizedString LabelPosition()`
- method `LocalizedString LabelPositionBottom()`
- method `LocalizedString LabelPositionCenter()`
- method `LocalizedString LabelPositionTop()`
- method `LocalizedString TextAlign()`
- method `LocalizedString TextAlignCenter()`
- method `LocalizedString TextAlignLeft()`
- method `LocalizedString TextAlignRight()`

### static class Common

- method `LocalizedString Add()`
- method `LocalizedString Back()`
- method `LocalizedString Cancel()`
- method `LocalizedString Close()`
- method `LocalizedString Confirm()`
- method `LocalizedString Continue()`
- method `LocalizedString Copy()`
- method `LocalizedString Create()`
- method `LocalizedString Delete()`
- method `LocalizedString Disabled()`
- method `LocalizedString Done()`
- method `LocalizedString Edit()`
- method `LocalizedString Enabled()`
- method `LocalizedString Export()`
- method `LocalizedString Import()`
- method `LocalizedString Loading()`
- method `LocalizedString Next()`
- method `LocalizedString No()`
- method `LocalizedString None()`
- method `LocalizedString Refresh()`
- method `LocalizedString Remove()`
- method `LocalizedString Rename()`
- method `LocalizedString Retry()`
- method `LocalizedString Save()`
- method `LocalizedString Select()`
- method `LocalizedString Yes()`

### static class ConfigFlow

- method `LocalizedString SetUpIntegration()`

### static class Connection

- method `LocalizedString Connected()`
- method `LocalizedString Connecting()`
- method `LocalizedString Disconnected()`
- method `LocalizedString Reconnecting()`

### interface ILocalizationCatalog

- property `IReadOnlyList<String> Cultures { get; }`
- property `String DefaultCulture { get; }`
- property `String Scope { get; }`
- method `IReadOnlyCollection<String> KeysOf(String culture)`
- method `Boolean TryGetTemplate(String culture, String key, String& text)`

### interface ILocalizationCatalogRegistry

- property `IReadOnlyList<String> Scopes { get; }`
- method `ILocalizationCatalog Find(String scope)`
- method `Void Register(ILocalizationCatalog catalog)`
- method `Boolean Unregister(String scope)`

### interface ILocalizationResolver

- method `String Resolve(LocalizedString value, String culture)`
- method `String Resolve(LocalizedText text, String culture)`

### class LocalizationCatalog : ILocalizationCatalog

- property `IReadOnlyList<String> Cultures { get; }`
- property `String DefaultCulture { get; }`
- property `String Scope { get; }`
- method `IReadOnlyCollection<String> KeysOf(String culture)`
- method `Boolean TryGetTemplate(String culture, String key, String& text)`

### class LocalizationCatalogRegistry : ILocalizationCatalogRegistry

- property `IReadOnlyList<String> Scopes { get; }`
- method `ILocalizationCatalog Find(String scope)`
- method `Void Register(ILocalizationCatalog catalog)`
- method `Boolean Unregister(String scope)`

### static class LocalizationCultureChain

- method `IReadOnlyList<String> For(String requested, String catalogDefaultCulture)`

### static class LocalizationDefaults

- field `String Culture = en`
- method `String MissingText(LocalizationKey key)`

### struct LocalizationKey : ValueType, IEquatable<LocalizationKey>

- property `String Name { get; }`
- property `String Scope { get; }`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LocalizationKey other)`
- method `Int32 GetHashCode()`
- method `LocalizationKey MacroDeck(String name)`
- method `LocalizationKey Plugin(String pluginId, String name)`
- method `String ToString()`
- method `Boolean TryParse(String text, LocalizationKey& key)`
- method `Boolean op_Equality(LocalizationKey left, LocalizationKey right)`
- method `Boolean op_Inequality(LocalizationKey left, LocalizationKey right)`

### class LocalizationResolver : ILocalizationResolver

- method `String Resolve(LocalizedString value, String culture)`
- method `String Resolve(LocalizedText text, String culture)`

### static class LocalizationScope

- field `String MacroDeck = macrodeck`
- field `String PluginPrefix = plugin:`
- method `String ForPlugin(String pluginId)`
- method `Boolean IsApplication(String scope)`
- method `Boolean IsPlugin(String scope)`
- method `Boolean IsValid(String scope)`
- method `String PluginIdOf(String scope)`

### struct LocalizedString : ValueType, IEquatable<LocalizedString>

- property `IReadOnlyDictionary<String, Object> Arguments { get; }`
- property `LocalizationKey Key { get; }`
- method `Boolean Equals(LocalizedString other)`
- method `Boolean Equals(Object obj)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LocalizedString left, LocalizedString right)`
- method `Boolean op_Inequality(LocalizedString left, LocalizedString right)`

### struct LocalizedText : ValueType, IEquatable<LocalizedText>

- property `Boolean IsEmpty { get; }`
- property `Boolean IsLocalized { get; }`
- property `String Literal { get; }`
- property `Nullable<LocalizedString> Localized { get; }`
- method `Boolean Equals(Object obj)`
- method `Boolean Equals(LocalizedText other)`
- method `LocalizedText FromLiteral(String literal)`
- method `LocalizedText FromLocalized(LocalizedString localized)`
- method `Int32 GetHashCode()`
- method `String ToString()`
- method `Boolean op_Equality(LocalizedText left, LocalizedText right)`
- method `LocalizedText op_Implicit(String literal)`
- method `LocalizedText op_Implicit(LocalizedString localized)`
- method `Boolean op_Inequality(LocalizedText left, LocalizedText right)`

### class MacroDeckLocalizationRemovedAttribute : Attribute

- property `String Guidance { get; }`

### static class MacroDeckStrings

- field `String LocalizationScope = macrodeck`
- property `ILocalizationCatalog LocalizationCatalog { get; }`

### static class Settings

- method `LocalizedString About()`
- method `LocalizedString Adb()`
- method `LocalizedString Advanced()`
- method `LocalizedString Appearance()`
- method `LocalizedString BackupAndData()`
- method `LocalizedString Backups()`
- method `LocalizedString Connectivity()`
- method `LocalizedString DataAndPrivacy()`
- method `LocalizedString Developer()`
- method `LocalizedString Devices()`
- method `LocalizedString General()`
- method `LocalizedString Language()`
- method `LocalizedString Logging()`
- method `LocalizedString Network()`
- method `LocalizedString PrivacyAndSecurity()`
- method `LocalizedString Security()`
- method `LocalizedString Startup()`
- method `LocalizedString Title()`

### static class States

- method `LocalizedString Active()`
- method `LocalizedString Hidden()`
- method `LocalizedString InPlaylist()`
- method `LocalizedString Inactive()`
- method `LocalizedString Liked()`
- method `LocalizedString Monitoring()`
- method `LocalizedString Muted()`
- method `LocalizedString NotInPlaylist()`
- method `LocalizedString NotLiked()`
- method `LocalizedString NotMonitoring()`
- method `LocalizedString NotRecording()`
- method `LocalizedString NotStreaming()`
- method `LocalizedString Off()`
- method `LocalizedString On()`
- method `LocalizedString Paused()`
- method `LocalizedString Playing()`
- method `LocalizedString PushToTalk()`
- method `LocalizedString Recording()`
- method `LocalizedString RecordingPaused()`
- method `LocalizedString RepeatContext()`
- method `LocalizedString RepeatOff()`
- method `LocalizedString RepeatTrack()`
- method `LocalizedString Stopped()`
- method `LocalizedString Streaming()`
- method `LocalizedString Unavailable()`
- method `LocalizedString Unmuted()`
- method `LocalizedString Visible()`
- method `LocalizedString VoiceActivity()`

### static class Validation

- method `LocalizedString AtLeast(LocalizedText field, Double minimum)`
- method `LocalizedString AtMost(LocalizedText field, Double maximum)`
- method `LocalizedString InvalidIpAddress(LocalizedText field)`
- method `LocalizedString InvalidJson(LocalizedText field)`
- method `LocalizedString InvalidUrl(LocalizedText field)`
- method `LocalizedString InvalidValue(LocalizedText field)`
- method `LocalizedString PatternMismatch(LocalizedText field)`
- method `LocalizedString Required(LocalizedText field)`
- method `LocalizedString TooLong(LocalizedText field, Int32 limit)`

### static class Widgets


## MacroDeck.Localization.Serialization

### class LocalizedTextJsonConverter : JsonConverter<LocalizedText>

- field `String Marker = $localized`
- method `LocalizedText Read(Utf8JsonReader& reader, Type typeToConvert, JsonSerializerOptions options)`
- method `Void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)`

