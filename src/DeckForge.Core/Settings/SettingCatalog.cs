namespace DeckForge.Core.Settings;

/// <summary>
/// The sixteen settings keys Part 20 names, in the order it lists them, with everything said about each
/// one.
/// </summary>
/// <remarks>
/// <para>
/// Part 20.1 records that none of these keys shipped, and the honest reason: the document was written
/// as a design and read as a description of the product, so a contributor wrote
/// <c>settings.VisualSnapRadius</c> and discovered it did not compile. This catalogue is the correction
/// - every row is a real property on <see cref="AppSettings"/>, in the real settings file, validated on
/// the way in.
/// </para>
/// <para>
/// Fourteen of the sixteen are stored and validated but read by nothing, and each description says so in
/// the words a user would read on the Settings page rather than in a footnote. That is not a temporary
/// embarrassment to be hidden: it is the state of the product, and a settings page that showed a live
/// control for a feature that does not exist would be a control that lies. Each description names the
/// thing that does not exist yet, so the page and this file cannot drift apart.
/// </para>
/// <para>
/// The two keys that do something - <see cref="MotionPreference"/>'s own setting, and nothing else - are
/// wired on the App side by the motion controller, which is the only WPF-dependent half.
/// </para>
/// </remarks>
public static class SettingCatalog
{
    /// <summary>
    /// Every key, in Part 20's order. The order is the page's order and the documentation's order, so a
    /// reader comparing this with the table in Part 20 reads down one column.
    /// </summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } =
    [
        SettingDescriptor.Toggle(
            "VisualSnapEnabled",
            "Magnetic snapping",
            "Whether a script dropped close to another one snaps to it rather than landing where the "
            + "pointer was released. Stored and validated; nothing reads it yet, because a script drops "
            + "onto a hat slot or into a stack and both of those are already unambiguous.",
            value: true,
            SettingSection.Visual),

        SettingDescriptor.Number(
            "VisualSnapRadius",
            "Snap radius",
            "How near a drop has to be, in pixels at 100% zoom, before it snaps. Stored and validated; "
            + "the resolver's radius is a design number (Part 9.5) and is not read from here.",
            value: 40,
            minimum: 16,
            maximum: 96,
            SettingSection.Visual,
            unit: "px"),

        SettingDescriptor.Toggle(
            "VisualSnapToGrid",
            "Snap to grid",
            "Whether free placement on the canvas lines up with a grid. Stored and validated; nothing "
            + "reads it yet, because a script drops onto a hat slot or a stack rather than at an "
            + "arbitrary point, so there is no free placement to align.",
            value: false,
            SettingSection.Visual),

        SettingDescriptor.Number(
            "VisualGridSize",
            "Grid size",
            "The spacing the setting above would use. Stored and validated, and read by nothing until "
            + "the setting above is.",
            value: 16,
            minimum: 4,
            maximum: 64,
            SettingSection.Visual,
            unit: "px"),

        SettingDescriptor.Number(
            "VisualDefaultZoom",
            "Default zoom",
            "The zoom a canvas opens at. Stored and validated; the canvas opens at 100%.",
            value: 100,
            minimum: 25,
            maximum: 200,
            SettingSection.Visual,
            unit: "%"),

        SettingDescriptor.Toggle(
            "VisualMinimapVisible",
            "Show the minimap",
            "Whether the canvas minimap is drawn. Stored and validated; the minimap is always present.",
            value: true,
            SettingSection.Visual),

        SettingDescriptor.Toggle(
            "VisualPaletteShowDeprecated",
            "Show deprecated blocks",
            "Whether the palette lists blocks a later DeckForge has deprecated. Stored and validated; no "
            + "block in the catalogue is deprecated, so there is nothing for this to reveal.",
            value: false,
            SettingSection.Visual),

        SettingDescriptor.Number(
            "VisualAutosaveSeconds",
            "Autosave the canvas",
            "How often the sidecar canvas is written without being asked. 0 turns autosave off, and the "
            + "gap between 0 and 15 is deliberate: an interval that short rewrites the file faster than a "
            + "person can stop dragging, and a canvas that changes under the pointer is worse than one "
            + "that waits. Stored and validated; nothing reads it yet - a canvas is written when the user "
            + "says so, and no timer writes it.",
            value: 0,
            ranges: [SettingRange.Between(0, 0), SettingRange.Between(15, 600)],
            SettingSection.Visual,
            unit: "s"),

        SettingDescriptor.Choice(
            "VisualReduceMotion",
            "Reduced motion",
            "Whether DeckForge animates. Following the system reads Windows' own animation preference; "
            + "the other two overrule it. It applies today to the slide and fade between sidebar pages, to "
            + "the drop animation of menus, combo boxes and tooltips, and to the window's own open, close "
            + "and snap animations. It does not shorten anything inside the block canvas, because the "
            + "canvas does not animate.",
            defaultMember: nameof(MotionPreference.System),
            SettingSection.Accessibility),

        SettingDescriptor.Number(
            "VisualBlockTextSize",
            "Tile label size",
            "The font size of the text drawn on a block. Stored and validated; tile text follows the "
            + "theme's own type scale, which is the one thing that changes every label on every theme.",
            value: 13.5,
            minimum: 12,
            maximum: 18,
            SettingSection.Visual,
            unit: "px"),

        SettingDescriptor.Toggle(
            "VisualStageAllowNetwork",
            "Let the simulator use the network",
            "Whether the simulator's HTTP blocks may reach the network. Off by default and it stays off: "
            + "the simulator has no network implementation for this to switch on (Part 27.1), and a "
            + "permission with nothing behind it is worse than no permission.",
            value: false,
            SettingSection.Simulator),

        SettingDescriptor.Number(
            "VisualStageStepDelayMs",
            "Step delay",
            "How long the simulator waits between two interpreted steps. Stored and validated; the stage "
            + "runs on its speed slider, which is a session value and is not this setting.",
            value: 120,
            minimum: 0,
            maximum: 1000,
            SettingSection.Simulator,
            unit: "ms"),

        SettingDescriptor.Table(
            "VisualBreakpoints",
            "Breakpoints",
            "Block ids that stop the interpreter. Stored here rather than in a stage.json because nothing "
            + "writes stage.json (Part 11.1), and not offered on this page because a breakpoint belongs to "
            + "the document in front of the user rather than to whoever happened to run it last.",
            SettingSection.Internal),

        SettingDescriptor.Map(
            "VisualCannedResponses",
            "Canned HTTP responses",
            "A url pattern to response table for the simulator's HTTP blocks to answer from. Stored for "
            + "the same reason as the breakpoints, and for the same reason not on this page: there is "
            + "nothing to fill it in with.",
            SettingSection.Internal),

        SettingDescriptor.Toggle(
            "VisualShowBlockCode",
            "Show the block's code",
            "Whether the inspector shows the C# that one block compiles to. Stored and validated; the "
            + "preview is always shown, because it is the only place the canvas and the generated file "
            + "can be compared.",
            value: false,
            SettingSection.Visual),

        SettingDescriptor.Toggle(
            "VisualOnboardingSeen",
            "Walkthrough seen",
            "Whether the empty-state walkthrough has been dismissed. Stored and validated; there is no "
            + "walkthrough.",
            value: false,
            SettingSection.Visual),
    ];

    /// <summary>The sections the Settings page renders, in the order it renders them.</summary>
    /// <remarks>
    /// <see cref="SettingSection.Internal"/> is excluded because those keys are document state with no
    /// editor, and <see cref="SettingDescriptor.HasEditor"/> already says which kinds those are. The
    /// exclusion lives here rather than in XAML so that the page and the catalogue cannot disagree about
    /// how many keys a user is shown.
    /// </remarks>
    public static IReadOnlyList<SettingSection> RenderedSections { get; } =
    [
        SettingSection.Visual,
        SettingSection.Accessibility,
        SettingSection.Simulator,
    ];

    /// <summary>The keys one section renders, in catalogue order.</summary>
    /// <param name="section">The section to list.</param>
    /// <returns>
    /// The keys with an editor. Empty for <see cref="SettingSection.Internal"/>, which is the point of
    /// asking through here rather than reading <see cref="All"/>.
    /// </returns>
    public static IReadOnlyList<SettingDescriptor> ForSection(SettingSection section) =>
        All.Where(descriptor => descriptor.Section == section && descriptor.HasEditor).ToList();

    /// <summary>Every key's stable id, which is also its property name and its JSON key.</summary>
    public static IReadOnlyList<string> Ids { get; } =
        All.Select(descriptor => descriptor.Id).ToList();

    /// <summary>The descriptor for a key, or null when the id is not one of ours.</summary>
    /// <param name="id">The stable name to look up.</param>
    /// <returns>The descriptor, or null.</returns>
    /// <remarks>
    /// Returning null rather than throwing is deliberate. A lookup by id is also how a file written by a
    /// newer DeckForge is inspected, and a settings file from the future is a file to read and describe,
    /// not a reason to refuse to start.
    /// </remarks>
    public static SettingDescriptor? Find(string id) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.Id, id, StringComparison.Ordinal));

    /// <summary>The keys stored but not offered on the page, for the page to name in one sentence.</summary>
    /// <remarks>
    /// A list rather than a count so the page can name them. "Two further settings are stored" is a
    /// statement a user cannot act on and cannot check; naming them is one word more and answers the
    /// question the count raises.
    /// </remarks>
    public static IReadOnlyList<SettingDescriptor> StoredOnlyKeys =>
        All.Where(descriptor => !RenderedSections.Contains(descriptor.Section)).ToList();

    /// <summary>
    /// Everything one settings row needs to draw itself, looked up by key.
    /// </summary>
    /// <param name="id">The key's stable name.</param>
    /// <returns>
    /// The row's text. For an id that is not in the catalogue, a record that says so rather than null or
    /// an exception: a row that renders an empty label is a mystery, and a row that takes the window down
    /// is worse.
    /// </returns>
    /// <remarks>
    /// The fallback names the id it did not find, because the failure this is for is a view model whose
    /// property name and the catalogue's id have drifted apart - and the one piece of information that
    /// diagnoses that is the string that did not match.
    /// </remarks>
    public static SettingInfo InfoFor(string id)
    {
        var descriptor = Find(id);

        return descriptor is null
            ? new SettingInfo(id, id, $"'{id}' is not in the settings catalogue.", "")
            : descriptor.ToInfo();
    }
}