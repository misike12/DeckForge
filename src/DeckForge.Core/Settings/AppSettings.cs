using System.Globalization;
using System.Text.Json.Serialization;

namespace DeckForge.Core.Settings;

/// <summary>Which of the three themes the shell draws with.</summary>
/// <remarks>
/// Moved here with <see cref="AppSettings"/> rather than left in the App project beside the service that
/// reads it. It is a value the settings file holds, it is named by the file's JSON, and a type that
/// decides what a JSON value means belongs next to the file's other value types - otherwise the
/// persisted contract is split across two assemblies and only one of them is tested.
/// </remarks>
public enum AppTheme
{
    /// <summary>Follow the operating system's light or dark preference.</summary>
    System,

    /// <summary>Light, whatever the system says.</summary>
    Light,

    /// <summary>Dark, whatever the system says.</summary>
    Dark,
}

/// <summary>
/// App-level settings persisted under %LOCALAPPDATA%/DeckForge/settings.json.
/// </summary>
/// <remarks>
/// <para>
/// Every property here is read by something. A setting that is written to disk and never read is
/// worse than no setting: it appears in the JSON, it invites a user to change it, and nothing
/// happens. There were five such properties; each is now wired to the feature it names.
/// </para>
/// <para>
/// Fourteen of the sixteen Visual keys added in Part 20 are, by that standard, also unread - and the
/// difference is that every one of them says so on the Settings page rather than pretending. They exist
/// because the document's table is a specification and the specification is right about what a user
/// should be able to choose; the page is what makes the difference between "specified" and "shipped"
/// visible instead of a matter of trust.
/// </para>
/// <para>
/// This file is window-free on purpose. It is the persisted contract of the whole application, and the
/// test project may not reference the WPF app - so a settings model that lived in the App project would
/// be the one part of the application with no test at all, which is the part everybody hand-edits.
/// </para>
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Which theme the shell draws with. Read at startup and re-read on every save.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>The accent name from <c>LiquidTheme.Accents</c>; an unknown name falls back to the first.</summary>
    public string Accent { get; set; } = "Deck Blue";

    /// <summary>Folder the New Plugin wizard offers first. Empty means Documents.</summary>
    public string? DefaultProjectsDirectory { get; set; }

    /// <summary>Ask the update source for a newer release when the app starts.</summary>
    public bool CheckForUpdatesOnStart { get; set; } = true;

    /// <summary>GitHub account the Publish page uses to name the owner of a release.</summary>
    public string? GitHubAccount { get; set; }

    /// <summary>
    /// Extension ids the user has switched off.
    /// </summary>
    /// <remarks>
    /// Default is off, not on. An extension is third-party code that runs in this process the
    /// moment it is discovered, so installing one is not consent to load it - the user has to say
    /// so. Everything found is listed either way, so a disabled extension is visible rather than
    /// invisible.
    /// </remarks>
    public List<string> DisabledExtensions { get; set; } = [];

    /// <summary>
    /// The macrodeck-plugin version DeckForge targets. The Environment page gates on it, because a
    /// wrong CLI version fails in ways that look like a DeckForge bug rather than a version skew.
    /// </summary>
    public string? MacroDeckCliVersion { get; set; }

    /// <summary>Velopack update source (https folder or releases URL); empty disables update checks.</summary>
    public string? VelopackUpdateUrl { get; set; }

    /// <summary>The most recently opened workspaces, newest first, capped at ten by the service.</summary>
    public List<string> RecentWorkspaces { get; set; } = [];

    // ---- Part 20's sixteen keys -------------------------------------------------------------------
    //
    // Every default below is the one SettingCatalog records for the same id, and SettingsTests asserts
    // that they still agree. That assertion is the point: these two lists are the third and fourth copy
    // of every default in the product, and a copy nobody compares is a copy that drifts.

    /// <summary>Whether a near drop snaps to a block. Stored; the resolver uses its own radius.</summary>
    /// <remarks>See <see cref="SettingCatalog"/> for the full statement, and for why nothing reads it yet.</remarks>
    public bool VisualSnapEnabled { get; set; } = true;

    /// <summary>How near a drop has to be before it snaps, in pixels at 100% zoom.</summary>
    /// <remarks>Range 16–96. See <see cref="SettingCatalog"/> for why nothing reads it yet.</remarks>
    public double VisualSnapRadius { get; set; } = 40;

    /// <summary>Whether free placement lines up with a grid. Stored; there is no free placement.</summary>
    public bool VisualSnapToGrid { get; set; }

    /// <summary>The spacing a grid would use. Range 4–64 px; read by nothing.</summary>
    public double VisualGridSize { get; set; } = 16;

    /// <summary>The zoom a canvas opens at, as a percentage. Range 25–200.</summary>
    public double VisualDefaultZoom { get; set; } = 100;

    /// <summary>Whether the canvas minimap is drawn. Stored; the minimap is always present.</summary>
    public bool VisualMinimapVisible { get; set; } = true;

    /// <summary>Whether the palette lists deprecated blocks. Stored; none are deprecated.</summary>
    public bool VisualPaletteShowDeprecated { get; set; }

    /// <summary>
    /// How often the sidecar canvas is written without being asked. 0 is off; the only other values are
    /// 15 to 600 seconds.
    /// </summary>
    /// <remarks>
    /// The gap is part of the contract, not an oversight, and <see cref="SettingDescriptor.Clamp"/> is
    /// what keeps a value inside it. See <see cref="SettingCatalog"/> for why no timer reads it yet - and
    /// note that when one does, it must write the sidecar only, because a background write into a user's
    /// source tree is not something this application does quietly.
    /// </remarks>
    public int VisualAutosaveSeconds { get; set; }

    /// <summary>
    /// Whether DeckForge animates. The default follows the operating system, which preserves today's
    /// behaviour for a user who has expressed no preference either way.
    /// </summary>
    /// <remarks>
    /// The one key of the sixteen that is read. <see cref="MotionPolicy"/> turns it into the answer the
    /// App acts on, and the App's motion controller applies that to page transitions, popup drop
    /// animations and the window's own window-manager transitions.
    /// </remarks>
    public MotionPreference VisualReduceMotion { get; set; } = MotionPreference.System;

    /// <summary>The font size of a block's label. Range 12–18 px; stored; the theme's scale is used.</summary>
    public double VisualBlockTextSize { get; set; } = 13.5;

    /// <summary>Whether the simulator's HTTP blocks may reach the network. Off, and stays off.</summary>
    public bool VisualStageAllowNetwork { get; set; }

    /// <summary>How long the simulator waits between interpreted steps. Range 0–1000 ms.</summary>
    public int VisualStageStepDelayMs { get; set; } = 120;

    /// <summary>
    /// Block ids that stop the interpreter. Stored rather than offered, because nothing writes a
    /// stage.json (Part 11.1).
    /// </summary>
    /// <remarks>
    /// It is a <see cref="List{T}"/> rather than a set because it is persisted as JSON and the order is
    /// the order the file was written in; deduplicating on load would make a save-after-load change the
    /// file for no reason a user could see.
    /// </remarks>
    public List<string> VisualBreakpoints { get; set; } = [];

    /// <summary>
    /// A url pattern to response table for the simulator's HTTP blocks. Stored rather than offered.
    /// </summary>
    /// <remarks>
    /// Ordinal comparison, deliberately: a url pattern is a literal string, and case-insensitive lookup
    /// would make two patterns differing only in case silently collide.
    /// </remarks>
    public Dictionary<string, string> VisualCannedResponses { get; set; } = [];

    /// <summary>Whether the inspector shows the C# one block compiles to. Stored; it is always shown.</summary>
    public bool VisualShowBlockCode { get; set; }

    /// <summary>Whether the empty-state walkthrough has been dismissed. Stored; there is none.</summary>
    public bool VisualOnboardingSeen { get; set; }

    /// <summary>
    /// Brings a settings file that was read into the shape this application can use, and says what it
    /// changed.
    /// </summary>
    /// <returns>
    /// One sentence per repair, empty when the file was already sound. The sentences exist so a caller
    /// can tell the user their file was adjusted rather than silently adjusting it - a magnet radius of
    /// 400 that quietly became 96 would otherwise look like a bug.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This used to be <c>RepairNulls</c>, which did half of it, and the half it did was already a fix for
    /// a crash rather than a nicety: an explicit <c>"recentWorkspaces": null</c> deserialises to a null
    /// property, and the next <c>Insert</c> on it is a NullReferenceException from opening a project -
    /// the one action the user cannot avoid. The property initialiser does not help, because the
    /// deserialiser overwrites it with the null it read.
    /// </para>
    /// <para>
    /// The numeric half is new and it is not about crashes. A value outside its documented range comes
    /// from a hand-edited file or a build that knew a wider range, and both are cases where the useful
    /// answer is the nearest value that works rather than a refusal: the file is the user's, and a
    /// settings page that cannot be opened is a worse outcome than a magnet radius of 96 instead of 400.
    /// The clamping rule itself lives in <see cref="SettingDescriptor.Clamp"/>, because "nearest allowed
    /// range" is a rule about a key rather than about a file format.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Normalise()
    {
        var repairs = new List<string>();

        RecentWorkspaces ??= [];
        DisabledExtensions ??= [];
        VisualBreakpoints ??= [];
        VisualCannedResponses ??= [];

        VisualSnapRadius = Clamp(nameof(VisualSnapRadius), VisualSnapRadius, repairs);
        VisualGridSize = Clamp(nameof(VisualGridSize), VisualGridSize, repairs);
        VisualDefaultZoom = Clamp(nameof(VisualDefaultZoom), VisualDefaultZoom, repairs);
        VisualBlockTextSize = Clamp(nameof(VisualBlockTextSize), VisualBlockTextSize, repairs);
        VisualAutosaveSeconds = Clamp(nameof(VisualAutosaveSeconds), VisualAutosaveSeconds, repairs);
        VisualStageStepDelayMs = Clamp(nameof(VisualStageStepDelayMs), VisualStageStepDelayMs, repairs);

        return repairs;
    }

    /// <summary>
    /// Brings one number into its key's range, recording the change.
    /// </summary>
    /// <param name="id">The key's stable name, which is how it is named in the repair sentence.</param>
    /// <param name="value">The value the file held.</param>
    /// <param name="repairs">Where the sentence is added.</param>
    /// <returns>The value if it was allowed, otherwise the nearest value that is.</returns>
    /// <remarks>
    /// No special case for a value that is not a finite number. <see cref="SettingDescriptor.Clamp"/>
    /// handles that, because it is a property of the key rather than of this file format, and a second
    /// copy of the rule in the caller is a second place for the two to disagree.
    /// </remarks>
    private double Clamp(string id, double value, List<string> repairs)
    {
        var descriptor = SettingCatalog.Find(id)
            ?? throw new InvalidOperationException($"'{id}' is not a known settings key.");

        var result = descriptor.IsInRange(value) ? value : descriptor.Clamp(value);

        if (!result.Equals(value))
        {
            repairs.Add($"{id} was {Show(value)}, outside {descriptor.RangeText}; it is now {Show(result)}.");
        }

        return result;
    }

    /// <summary>The integer form of <see cref="Clamp"/>, for the two second-based keys.</summary>
    /// <param name="id">The key's stable name.</param>
    /// <param name="value">The value the file held.</param>
    /// <param name="repairs">Where the sentence is added.</param>
    /// <returns>The value if it was allowed, otherwise the nearest value that is.</returns>
    private int Clamp(string id, int value, List<string> repairs) =>
        (int)Clamp(id, (double)value, repairs);

    /// <summary>A number as the file writes it, whatever this machine's regional settings are.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The number in invariant culture.</returns>
    private static string Show(double value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Where this application keeps its settings file.</summary>
    /// <remarks>
    /// A static path with no way to redirect it, which is why the store takes a path in its constructor
    /// instead: the file layer is the part most worth testing, and a test that could only ever read the
    /// real user's file would be a test nobody runs.
    /// </remarks>
    [JsonIgnore]
    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeckForge", "settings.json");
}