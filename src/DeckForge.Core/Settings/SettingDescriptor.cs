using System.Globalization;

namespace DeckForge.Core.Settings;

/// <summary>Which page section a setting belongs in, and therefore which one renders it.</summary>
/// <remarks>
/// The sections are the ones Part 20's table names in its last column. They are not decoration: the
/// Settings page builds its rows from the catalogue grouped by this, so a key cannot end up on the page
/// without a section and a key in the wrong section is a one-line fix rather than an argument.
/// </remarks>
public enum SettingSection
{
    /// <summary>The block canvas: snapping, zoom, the minimap, tile text.</summary>
    Visual,

    /// <summary>Motion and anything else a user needs because of how they read a screen.</summary>
    Accessibility,

    /// <summary>The block simulator: its network permission and its step delay.</summary>
    Simulator,

    /// <summary>
    /// State that belongs to a document rather than to a person, so it is stored but not offered.
    /// </summary>
    /// <remarks>
    /// Kept as a first-class section rather than a bool on the descriptor because "do not show this"
    /// and "this has no editor" are different reasons, and the second one changes when a feature lands.
    /// </remarks>
    Internal,
}

/// <summary>What kind of editor a setting needs, which is also what its default parses as.</summary>
public enum SettingKind
{
    /// <summary>A bool. The default is <c>true</c> or <c>false</c>.</summary>
    Toggle,

    /// <summary>A number with a range. The default is written in invariant culture.</summary>
    Number,

    /// <summary>One of a fixed set, as an enum member. The default is the member's name.</summary>
    Choice,

    /// <summary>Free text. The default is the text itself.</summary>
    Text,

    /// <summary>A list of values.</summary>
    Table,

    /// <summary>A set of named values.</summary>
    Map,
}

/// <summary>
/// One inclusive range a numeric setting may take, or one of several when the documented values are not
/// contiguous.
/// </summary>
/// <param name="Minimum">The lowest allowed value, inclusive.</param>
/// <param name="Maximum">The highest allowed value, inclusive.</param>
/// <remarks>
/// A record rather than a min/max pair on the descriptor because the autosave interval is genuinely two
/// ranges - off, or 15 seconds to 10 minutes - and Part 20 says so. Modelling that as
/// <c>Minimum: 0, Maximum: 600</c> would accept 1, and one second of autosave is a value nobody asked
/// for and cannot survive: the first write wins the race against the next one every time.
/// </remarks>
public sealed record SettingRange(double Minimum, double Maximum)
{
    /// <summary>A single range, which is what all but one of the numeric keys has.</summary>
    /// <param name="minimum">The lowest allowed value.</param>
    /// <param name="maximum">The highest allowed value.</param>
    public static SettingRange Between(double minimum, double maximum) => new(minimum, maximum);

    /// <summary>Whether a value falls inside this range, inclusive at both ends.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True when the value is one this range allows.</returns>
    /// <remarks>
    /// Inclusive because every documented bound is a value somebody typed deliberately - a magnet radius
    /// of exactly 96px is not a near miss of 96, it is a magnet radius. An exclusive bound would make the
    /// two ends of every slider unselectable and nobody would notice until a user tried.
    /// </remarks>
    public bool Contains(double value) => value >= Minimum && value <= Maximum;
}

/// <summary>
/// Everything the product knows about one settings key: what it is called, what it does, what it
/// defaults to, and what values it will accept.
/// </summary>
/// <param name="Id">
/// The stable name. It is the property name on <see cref="AppSettings"/> and, camel-cased, the key in
/// <c>settings.json</c>.
/// </param>
/// <param name="Name">The label the Settings page shows.</param>
/// <param name="Description">
/// What the setting is for, in one or two sentences, written for somebody reading the page rather than
/// for somebody implementing it.
/// </param>
/// <param name="Section">Which page section renders it.</param>
/// <param name="Kind">What kind of editor it needs.</param>
/// <param name="Default">The default value, as text in invariant culture.</param>
/// <param name="Ranges">
/// The allowed ranges, for a number. Empty for every other kind, and the reason is that a toggle cannot
/// be out of range while a number can.
/// </param>
/// <param name="Unit">A unit shown beside the value, or null for one.</param>
/// <remarks>
/// <para>
/// The catalogue this record belongs to exists because the alternative was a list in XAML. A list in
/// XAML gives every key its default once - in the control - while the property on
/// <see cref="AppSettings"/> has it a second time, and a file on disk has it a third time. The three
/// copies disagree within a release, and the copy nobody looks at is the one a hand-edited file
/// disagrees with.
/// </para>
/// <para>
/// One record per key means the page can be generated from the same table that validates the file, so a
/// key that exists is a key that is described, defaulted, ranged and displayed - and a test can assert
/// all four at once instead of trusting that whoever added the fifth row did all four.
/// </para>
/// </remarks>
public sealed record SettingDescriptor(
    string Id,
    string Name,
    string Description,
    SettingSection Section,
    SettingKind Kind,
    string Default,
    IReadOnlyList<SettingRange> Ranges,
    string? Unit)
{
    /// <summary>A boolean setting.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="value">The default.</param>
    /// <param name="section">Which section renders it.</param>
    /// <returns>A descriptor whose default parses as a bool.</returns>
    public static SettingDescriptor Toggle(
        string id,
        string name,
        string description,
        bool value,
        SettingSection section) =>
        new(id, name, description, section, SettingKind.Toggle, value ? "true" : "false", [], null);

    /// <summary>A number inside one range.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="value">The default.</param>
    /// <param name="minimum">The lowest allowed value.</param>
    /// <param name="maximum">The highest allowed value.</param>
    /// <param name="section">Which section renders it.</param>
    /// <param name="unit">A unit shown beside the value.</param>
    /// <returns>A descriptor with a single allowed range.</returns>
    public static SettingDescriptor Number(
        string id,
        string name,
        string description,
        double value,
        double minimum,
        double maximum,
        SettingSection section,
        string? unit = null) =>
        new(id, name, description, section, SettingKind.Number,
            value.ToString(CultureInfo.InvariantCulture),
            [SettingRange.Between(minimum, maximum)],
            unit);

    /// <summary>A number whose allowed values are not contiguous.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for, including what the gap means.</param>
    /// <param name="value">The default, which must be inside one of the ranges.</param>
    /// <param name="ranges">Every allowed range.</param>
    /// <param name="section">Which section renders it.</param>
    /// <param name="unit">A unit shown beside the value.</param>
    /// <returns>A descriptor that validates against all of <paramref name="ranges"/>.</returns>
    public static SettingDescriptor Number(
        string id,
        string name,
        string description,
        double value,
        IReadOnlyList<SettingRange> ranges,
        SettingSection section,
        string? unit = null) =>
        new(id, name, description, section, SettingKind.Number,
            value.ToString(CultureInfo.InvariantCulture),
            ranges,
            unit);

    /// <summary>A setting that is one of a fixed set, named by an enum member.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="defaultMember">The default, as the enum member's name.</param>
    /// <param name="section">Which section renders it.</param>
    /// <returns>A descriptor whose default is text.</returns>
    public static SettingDescriptor Choice(
        string id,
        string name,
        string description,
        string defaultMember,
        SettingSection section) =>
        new(id, name, description, section, SettingKind.Choice, defaultMember, [], null);

    /// <summary>Free text.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="defaultText">The default.</param>
    /// <param name="section">Which section renders it.</param>
    /// <returns>A descriptor whose default is text.</returns>
    public static SettingDescriptor Text(
        string id,
        string name,
        string description,
        string defaultText,
        SettingSection section) =>
        new(id, name, description, section, SettingKind.Text, defaultText, [], null);

    /// <summary>A list of values, rendered but not editable.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="section">Which section owns it.</param>
    /// <returns>A descriptor with no text default, because the empty list is the default.</returns>
    public static SettingDescriptor Table(
        string id,
        string name,
        string description,
        SettingSection section) =>
        new(id, name, description, section, SettingKind.Table, "[]", [], null);

    /// <summary>A map of named values, rendered but not editable.</summary>
    /// <param name="id">The stable name.</param>
    /// <param name="name">The page label.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="section">Which section owns it.</param>
    /// <returns>A descriptor with no text default, because the empty map is the default.</returns>
    public static SettingDescriptor Map(
        string id,
        string name,
        string description,
        SettingSection section) =>
        new(id, name, description, section, SettingKind.Map, "{}", [], null);

    /// <summary>The default for a toggle, parsed.</summary>
    /// <remarks>
    /// Parsed rather than stored as a bool so that the descriptor stays a plain record with one
    /// <c>Default</c> string, and so that a test can compare a descriptor against a model's default
    /// property without the model knowing that the catalogue exists.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The descriptor is not a toggle.</exception>
    public bool DefaultToggle => bool.Parse(Default);

    /// <summary>The default for a number, parsed in invariant culture.</summary>
    /// <remarks>
    /// Invariant rather than current culture on purpose: the value came from a literal written with a
    /// dot, and a machine whose regional settings use a comma would otherwise parse 13.5 as thirteen and
    /// a half hundredths of a pixel, or fail.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The descriptor is not a number.</exception>
    public double DefaultNumber => double.Parse(Default, CultureInfo.InvariantCulture);

    /// <summary>Whether a number is inside one of the allowed ranges.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True when no clamping is needed.</returns>
    public bool IsInRange(double value) =>
        Ranges.Any(range => range.Contains(value));

    /// <summary>
    /// Brings a number inside the allowed ranges.
    /// </summary>
    /// <param name="value">The value read from the file or typed into the page.</param>
    /// <returns>
    /// The value itself when it is allowed, otherwise the nearest edge of the nearest allowed range - or,
    /// for a value that is not a number at all, the key's own default.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Nearest range rather than nearest endpoint, because the one key with two ranges needs it: an
    /// autosave interval of 7 seconds is 7 away from "off" and 8 away from 15 seconds, so it becomes
    /// off, and an interval of 12 is closer to 15 and becomes 15. Both outcomes are the ones a user
    /// would have picked, and neither is a value that would lose a document.
    /// </para>
    /// <para>
    /// Clamping rather than rejecting to the default, so that a hand-edited 500px magnet radius becomes
    /// the largest radius that still works instead of silently reverting to a number the user had
    /// already decided against. A setting is not a form: the reader has no way to ask, and a clamp is
    /// the answer that leaves the file usable.
    /// </para>
    /// <para>
    /// The non-finite case returns the default rather than being clamped, and it is here rather than in
    /// the caller because <see cref="Math.Clamp(double, double, double)"/> hands a NaN straight back - so
    /// without this arm a value that never went through a file at all, such as one a numeric box produced,
    /// would pass every range check in the product and be written out as <c>NaN</c>, which is not even a
    /// JSON number.
    /// </para>
    /// </remarks>
    public double Clamp(double value)
    {
        if (!double.IsFinite(value))
        {
            return DefaultNumber;
        }

        if (Ranges.Count == 0)
        {
            return value;
        }

        var nearest = Ranges[0];
        var nearestDistance = Distance(nearest, value);

        foreach (var range in Ranges.Skip(1))
        {
            var distance = Distance(range, value);
            if (distance < nearestDistance)
            {
                nearest = range;
                nearestDistance = distance;
            }
        }

        return Math.Clamp(value, nearest.Minimum, nearest.Maximum);
    }

    /// <summary>How far outside a range a value is; zero when it is inside.</summary>
    private static double Distance(SettingRange range, double value) =>
        value < range.Minimum ? range.Minimum - value
        : value > range.Maximum ? value - range.Maximum
        : 0;

    /// <summary>
    /// A short "what values are allowed" hint for the Settings page, derived from the ranges rather
    /// than written out.
    /// </summary>
    /// <remarks>
    /// Derived, because a range typed twice - once in the machine-readable <see cref="Ranges"/> and once
    /// in a sentence beside the control - is a range that will eventually disagree with itself, and the
    /// sentence is the copy nobody reviews.
    /// </remarks>
    public string RangeText => Kind switch
    {
        SettingKind.Toggle => "on or off",
        SettingKind.Number => string.Join(
            " or ",
            Ranges.Select(range => range.Minimum == range.Maximum
                ? range.Minimum.ToString(CultureInfo.InvariantCulture)
                : $"{range.Minimum.ToString(CultureInfo.InvariantCulture)}–{range.Maximum.ToString(CultureInfo.InvariantCulture)}"))
            + (Unit is null ? string.Empty : $" {Unit}"),
        SettingKind.Choice => "one of the listed choices",
        SettingKind.Text => "any text",
        _ => "not editable here",
    };

    /// <summary>
    /// Whether the value shown on the page is the value the file will hold, as opposed to what a
    /// selection means.
    /// </summary>
    /// <param name="value">The text as it would be written.</param>
    /// <returns>True when <paramref name="value"/> is the stored default.</returns>
    public bool IsDefault(string value) => string.Equals(value, Default, StringComparison.Ordinal);

    /// <summary>Whether the Settings page offers an editor for this key.</summary>
    /// <remarks>
    /// False for <see cref="SettingKind.Table"/> and <see cref="SettingKind.Map"/>: they are document
    /// state with no sensible text editor, and a text box over a list of block ids would be a control
    /// that accepts anything and means nothing. They are stored, ranged and described, and the page says
    /// where they live.
    /// </remarks>
    public bool HasEditor => Kind is SettingKind.Toggle or SettingKind.Number or SettingKind.Choice or SettingKind.Text;

    /// <summary>
    /// The four things a settings row needs to draw itself, flattened into one value.
    /// </summary>
    /// <returns>A record the page binds three text properties to.</returns>
    /// <remarks>
    /// Returned by a method rather than exposed as four more properties on the descriptor so the page has
    /// one binding per row rather than three, and so that adding a fifth thing a row has to show is a
    /// change in one type instead of one in every row template.
    /// </remarks>
    public SettingInfo ToInfo() => new(Id, Name, Description, RangeText);
}

/// <summary>One settings row's text, ready to bind.</summary>
/// <param name="Id">The key's stable name.</param>
/// <param name="Name">The label.</param>
/// <param name="Description">What the key is for, and whether anything reads it yet.</param>
/// <param name="Range">The allowed values as a short hint.</param>
/// <remarks>
/// Exists so the page's three text bindings have something typed to point at. The alternative is three
/// lookups per row through the catalogue by string key, which compiles whatever the key says.
/// </remarks>
public sealed record SettingInfo(string Id, string Name, string Description, string Range);