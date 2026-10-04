using System.Globalization;
using System.Text.RegularExpressions;
using DeckForge.Core.Capabilities;

namespace DeckForge.Core.Visual.Providers;

/// <summary>
/// One thing a provider got wrong, or a guarantee its blocks would break.
/// </summary>
/// <param name="ProviderId">The provider it belongs to.</param>
/// <param name="Subject">
/// What it is about: a category, or a block kind. Named rather than left to the reader's guesswork, because a
/// finding that says "the label is blank" without saying which of forty contributed blocks is useless.
/// </param>
/// <param name="Message">
/// The finding, written so it can be shown to the person who wrote the provider rather than to the person
/// using it.
/// </param>
/// <param name="IsRefusal">
/// Whether the block is left out of the registry entirely. Only a collision can be a refusal: everything
/// else is a warning about something a user could work around, and refusing a whole provider's blocks over a
/// bad tooltip would be a worse answer than showing them.
/// </param>
/// <remarks>
/// <para>
/// A record rather than a <see cref="VisualDiagnostic"/>, and deliberately: a diagnostic belongs to a
/// document, and these belong to the palette. There is no block id to select and no document to fix them in,
/// so reusing the type would have meant either a null id everywhere or a second diagnostic shape in the same
/// namespace — which is how the product ended up with two vocabularies of codes before they were prefixed
/// into one.
/// </para>
/// </remarks>
public sealed record BlockProviderFinding(
    string ProviderId,
    string Subject,
    string Message,
    bool IsRefusal = false);

/// <summary>
/// The checks a contributed descriptor goes through, which are the checks the built-in rows go through.
/// </summary>
/// <remarks>
/// <para>
/// §23.4 and the task it states: a third party must not be able to introduce a block that fails a guarantee
/// the built-in ones have. Every rule below exists as a test in <c>BlockCatalogTests</c> — the ones a
/// provider cannot skip are the same ones the catalogue is held to, and the ones it can be warned about are
/// the ones a user would survive.
/// </para>
/// <para>
/// <strong>Why the rules live here rather than in the test project.</strong> A test proves the built-in rows
/// comply; it does nothing about rows that arrive at runtime from an extension assembly, and a check that
/// only the build runs is not a check. Moving the rules into Core is what makes "held to the same rules" a
/// property of the code rather than an aspiration in a document — and it is why the palette's accessibility,
/// label and slot-shape guarantees are the ones a contributed block inherits rather than the ones it opts
/// into.
/// </para>
/// <para>
/// The rules are pure functions over descriptors: no file, no WPF, no window. The one that needs the icon
/// library — is this glyph a real <c>SymbolRegular</c> member — is a predicate the caller supplies, because
/// Core must not reference WPF.UI and the alternative is dropping the check for providers while keeping it
/// for the catalogue, which is the exact asymmetry this class exists to remove.
/// </para>
/// </remarks>
public static partial class BlockProviderRules
{
    /// <summary>How short a block's summary may be before it is not a tooltip.</summary>
    private const int MinimumSummaryLength = 12;

    /// <summary>
    /// Checks one provider's declarations against everything the built-in catalogue is held to.
    /// </summary>
    /// <param name="providerId">The provider's id, used as the namespace its block ids must sit in.</param>
    /// <param name="categories">The categories it declares.</param>
    /// <param name="blocks">The blocks it declares.</param>
    /// <param name="takenKinds">
    /// Every kind already in use — the catalogue's and any other provider's. A contributed kind in here
    /// would make <see cref="BlockCatalog.Find"/> and the registry disagree about what a block is.
    /// </param>
    /// <param name="takenCategories">
    /// Every category already described. A provider may only describe one the built-in rail leaves free.
    /// </param>
    /// <param name="glyphExists">
    /// Whether a glyph name is one the icon library defines, or null when the caller cannot answer. A null
    /// skips the check with nothing said about it, which is the honest outcome for a Core consumer that has
    /// no icon library — and the App, which does have one, always supplies it.
    /// </param>
    /// <remarks>
    /// Ordered rather than short-circuited. A provider that declares six blocks and gets four of them wrong
    /// is a different conversation from one that gets one wrong, and a rule set that stopped at the first
    /// finding would make the second impossible to have.
    /// </remarks>
    public static IReadOnlyList<BlockProviderFinding> Check(
        string providerId,
        IReadOnlyList<BlockCategoryDescriptor> categories,
        IReadOnlyList<BlockDescriptor> blocks,
        IReadOnlySet<string>? takenKinds = null,
        IReadOnlySet<BlockCategory>? takenCategories = null,
        Func<string, bool>? glyphExists = null)
    {
        var findings = new List<BlockProviderFinding>();
        var taken = takenKinds is null
            ? new HashSet<string>(BlockCatalog.All.Select(row => row.Kind), StringComparer.Ordinal)
            : new HashSet<string>(takenKinds, StringComparer.Ordinal);

        var usedCategories = takenCategories is null
            ? BlockCatalog.Categories.Select(row => row.Category).ToHashSet()
            : [.. takenCategories];

        CheckCategories(providerId, categories, usedCategories, glyphExists, findings);

        foreach (var block in blocks)
        {
            findings.AddRange(CheckBlock(providerId, block, taken));
        }

        return findings;
    }

    private static void CheckCategories(
        string providerId,
        IReadOnlyList<BlockCategoryDescriptor> categories,
        HashSet<BlockCategory> usedCategories,
        Func<string, bool>? glyphExists,
        List<BlockProviderFinding> findings)
    {
        foreach (var category in categories)
        {
            var subject = category.Category.ToString();

            // `BlockCategory.Media` is the one member the built-in rail deliberately leaves without a
            // descriptor, and that is exactly why it is the slot a provider fills: the enum cannot grow a
            // thirteenth member without every switch over it and the rail having to know, while Media already
            // has a decision attached to it (Part 7.16 dropped the media blocks because the SDK's music
            // surface is provider-side). A provider that declares it is filling a hole the design left on
            // purpose, which is why the only category rule is "somebody else has not taken it".
            if (!usedCategories.Add(category.Category))
            {
                findings.Add(new BlockProviderFinding(
                    providerId,
                    subject,
                    "This category already has a palette row. A provider may only add one the built-in "
                    + "catalogue leaves free, or two rail headings would wear the same name.",
                    IsRefusal: true));
            }

            if (category.Name.Trim().Length == 0 || category.Summary.Trim().Length < MinimumSummaryLength)
            {
                findings.Add(new BlockProviderFinding(
                    providerId,
                    subject,
                    "A rail entry with nothing under it: the name or the summary is blank."));
            }

            if (!IsHue(category.Hue))
            {
                findings.Add(new BlockProviderFinding(
                    providerId,
                    subject,
                    $"\"{category.Hue}\" is not a #RRGGBB colour, so nothing can derive the block's fill, "
                    + "its outline or its contrast from it."));
            }
            else
            {
                // §28.2, measured rather than asserted: a label has to read against its own block at
                // 4.5:1 and the outline has to read as a boundary at 3:1, in both themes. Computed from the
                // provider's own hue through the same `BlockThemePalette` the built-ins use, so adding a
                // category with a hostile colour cannot reintroduce the failure Events' gold once had.
                foreach (var dark in (bool[])[true, false])
                {
                    var tokens = BlockThemePalette.For(category.Hue, dark);
                    var theme = dark ? "dark" : "light";

                    foreach (var (stop, fill) in new[] { ("top", tokens.FillTop), ("bottom", tokens.FillBottom) })
                    {
                        var ratio = ContrastRatio.Between(tokens.Ink, fill);
                        if (ratio < ContrastRatio.TextMinimum)
                        {
                            findings.Add(new BlockProviderFinding(
                                providerId,
                                subject,
                                $"Its {theme} label would be {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1 "
                                + $"on the {stop} of its own fill, and {ContrastRatio.TextMinimum.ToString(CultureInfo.InvariantCulture)}:1 "
                                + "is the minimum. Pick a hue that can carry white or black text."));
                        }
                    }

                    var lighter = ContrastRatio.Luminance(tokens.FillTop) > ContrastRatio.Luminance(tokens.FillBottom)
                        ? tokens.FillTop
                        : tokens.FillBottom;
                    var edge = ContrastRatio.Between(tokens.Stroke, lighter);

                    if (edge < ContrastRatio.NonTextMinimum)
                    {
                        findings.Add(new BlockProviderFinding(
                            providerId,
                            subject,
                            $"Its {theme} outline would be {edge.ToString("0.00", CultureInfo.InvariantCulture)}:1 "
                            + $"against its own fill, and {ContrastRatio.NonTextMinimum.ToString(CultureInfo.InvariantCulture)}:1 "
                            + "is the minimum for a boundary. A block's edge has to read as an edge."));
                    }
                }
            }

            // The rail binds this straight into a SymbolIcon, where an unknown name is a blank square and
            // not an error — the same trap XamlMarkupTests exists for, one layer earlier.
            if (glyphExists is not null && !glyphExists(category.Glyph))
            {
                findings.Add(new BlockProviderFinding(
                    providerId,
                    subject,
                    $"\"{category.Glyph}\" is not a glyph the icon library defines, so the rail draws an "
                    + "empty square for it."));
            }
        }
    }

    private static IEnumerable<BlockProviderFinding> CheckBlock(
        string providerId,
        BlockDescriptor block,
        HashSet<string> taken)
    {
        foreach (var finding in Identity(providerId, block, taken))
        {
            yield return finding;
        }

        foreach (var finding in Described(providerId, block))
        {
            yield return finding;
        }

        foreach (var finding in Shaped(providerId, block))
        {
            yield return finding;
        }
    }

    /// <summary>
    /// Who this block is: a legal, namespaced, unused kind.
    /// </summary>
    /// <remarks>
    /// The only rules here that cost a block its place. A collision means a document naming that id means
    /// two different blocks depending on what was loaded, which is the one failure in this file that cannot
    /// be shown to the user as anything but wrong — everything else is a warning about something they could
    /// work around, and refusing a whole provider's blocks over a bad tooltip would be the worse answer.
    /// </remarks>
    private static IEnumerable<BlockProviderFinding> Identity(
        string providerId,
        BlockDescriptor block,
        HashSet<string> taken)
    {
        if (block.Kind.Length == 0
            || block.Kind.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-'))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind.Length == 0 ? "(no kind)" : block.Kind,
                "A kind is what a saved document stores, so it has to be lowercase, dotted and hyphenated. "
                + "A capital or a space makes the sidecar ambiguous to read and the migration's alias table "
                + "impossible to grep.",
                IsRefusal: true);
        }
        else if (!block.Kind.StartsWith(providerId + ".", StringComparison.Ordinal))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                $"A contributed kind has to be namespaced by the provider that supplied it, so it reads "
                + $"\"{providerId}.something\". Without the prefix a third party could take an id the "
                + "catalogue already means by something else.",
                IsRefusal: true);
        }
        else if (!taken.Add(block.Kind))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                "This kind is already in use, so a document naming it would mean two different blocks "
                + "depending on which was loaded.",
                IsRefusal: true);
        }
    }

    /// <summary>
    /// What the block says: a label that is not an internal name, a summary worth a tooltip, and holes that
    /// name something the block declares.
    /// </summary>
    private static IEnumerable<BlockProviderFinding> Described(string providerId, BlockDescriptor block)
    {
        if (block.Label.Trim().Length == 0 || string.Equals(block.Label, block.Kind, StringComparison.Ordinal))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                "A block's label is blank or is just its id, so the palette row would read as an internal "
                + "name.");
        }

        if (block.Summary.Trim().Length < MinimumSummaryLength)
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                $"A summary of \"{block.Summary}\" is too short to be the tooltip a user reads before "
                + "dragging this block.");
        }

        // A hole naming something the row does not declare renders as the raw marker `{count}` on the
        // tile. It is the cheapest guard there is against a whole class of visible-but-subtle defect, and a
        // contributed row is exactly where it will be written.
        foreach (var marker in Markers(block.Label))
        {
            if (block.Slot(marker) is null && block.Menu(marker) is null)
            {
                yield return new BlockProviderFinding(
                    providerId,
                    block.Kind,
                    $"The label names \"{{{marker}}}\" and the block declares no slot or menu by that "
                    + "name, so the canvas draws the raw marker instead of a control.");
            }
        }
    }

    /// <summary>
    /// What the block is made of: a shape that agrees with its bodies, unique and legal keys, menus that
    /// offer what they default to, and slots that say where their value comes from.
    /// </summary>
    private static IEnumerable<BlockProviderFinding> Shaped(string providerId, BlockDescriptor block)
    {
        if (block.IsContainer != (block.Shape is BlockShape.C or BlockShape.CIf or BlockShape.CChain))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                $"A {block.Shape} block "
                + (block.IsContainer
                    ? "wraps a body, which would draw a notch that goes nowhere."
                    : "wraps nothing, so a block dropped into it would have nowhere to go."));
        }

        if (block.IsReporter && block.Bodies is { Count: > 0 })
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                "A reporter produces a value and cannot also wrap a body; the shape is the grammar of where "
                + "a block may go.");
        }

        foreach (var duplicate in Duplicates(block))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                $"\"{duplicate}\" is declared twice, so one of the two is unreachable.");
        }

        foreach (var key in (block.Slots ?? []).Select(slot => slot.Name)
            .Concat((block.Menus ?? []).Select(menu => menu.Name))
            .Concat((block.Bodies ?? []).Select(body => body.Name))
            .Where(name => name.Length == 0
                || name != name.Trim()
                || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')))
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                $"\"{key}\" is not a plain key. These names are JSON keys in a saved document, so "
                + "whitespace or casing shows up as an empty slot.");
        }

        foreach (var menu in block.Menus ?? [])
        {
            if (!menu.Options.Contains(menu.Default, StringComparer.Ordinal))
            {
                yield return new BlockProviderFinding(
                    providerId,
                    block.Kind,
                    $"Menu \"{menu.Name}\" starts on \"{menu.Default}\", which it does not offer, so the "
                    + "canvas shows a blank dropdown and the emitter falls through to its own default.");
            }

            if (menu.Options.Count < 2
                || menu.Options.Distinct(StringComparer.Ordinal).Count() != menu.Options.Count)
            {
                yield return new BlockProviderFinding(
                    providerId,
                    block.Kind,
                    $"Menu \"{menu.Name}\" offers {menu.Options.Count} option(s), with "
                    + $"{menu.Options.Distinct(StringComparer.Ordinal).Count()} distinct. A menu with one "
                    + "option is a label pretending to be a choice, and a repeated one is a value the user "
                    + "cannot distinguish.");
            }
        }

        foreach (var slot in block.Slots ?? [])
        {
            // `var` and `list` name something the document owns, so the type is what tells the canvas to
            // draw a dropdown of the right names. Tagged Text it renders an empty box the user has to spell,
            // and no diagnostic can tell a typo from an undeclared variable.
            if (slot.Name is "var" or "list"
                && slot.Type is SlotType.Any or SlotType.Text or SlotType.Number)
            {
                yield return new BlockProviderFinding(
                    providerId,
                    block.Kind,
                    $"Slot \"{slot.Name}\" names a document entity but is tagged {slot.Type}, so nothing "
                    + "offers the names that could go in it.");
            }

            // A default on a source slot is a name written into every fresh block, and it is a name that
            // probably does not exist.
            if (slot.DefaultText is not null && IsSource(slot.Type))
            {
                yield return new BlockProviderFinding(
                    providerId,
                    block.Kind,
                    $"Slot \"{slot.Name}\" starts on the name \"{slot.DefaultText}\", which a fresh block "
                    + "would reference as if it were declared. Leave it empty and let the user choose.");
            }
        }

        if (block.CapabilityId is { Length: > 0 } capability && CapabilityCatalog.Find(capability) is null)
        {
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                $"Capability \"{capability}\" is not in the gallery, so the warning this block would raise "
                + "would deep link to nothing.");
        }

        if (!block.IsVerified)
        {
            // A warning, not a refusal. §23.4 makes an unverified block unsaveable, which the document
            // validator already reports as `vis-unverified-block`; refusing to load it here would instead
            // make the provider invisible, and a provider that cannot show its work gets no feedback on it.
            yield return new BlockProviderFinding(
                providerId,
                block.Kind,
                "This block cites no SDK member, so it is unverified and unsaveable until it does. Read the "
                + "member it calls and put it in Sdk.VerifiedAgainst.");
        }

        yield break;
    }

    private static IEnumerable<string> Markers(string label) =>
        LabelMarker().Matches(label ?? string.Empty).Select(match => match.Groups[1].Value);

    private static IEnumerable<string> Duplicates(BlockDescriptor block)
    {
        foreach (var (what, names) in new[]
        {
            ("slot", (block.Slots ?? []).Select(slot => slot.Name)),
            ("menu", (block.Menus ?? []).Select(menu => menu.Name)),
            ("body", (block.Bodies ?? []).Select(body => body.Name)),
        })
        {
            foreach (var name in names.GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{what}[{group.Key}]"))
            {
                yield return name;
            }
        }
    }

    private static bool IsSource(SlotType type) => type
        is SlotType.List
        or SlotType.HostVariable
        or SlotType.UserVariable
        or SlotType.Parameter
        or SlotType.Event
        or SlotType.Action
        or SlotType.Script
        or SlotType.Widget
        or SlotType.ConfigEntry
        or SlotType.Variable
        or SlotType.Procedure;

    private static bool IsHue(string hue) =>
        hue.Length == 7 && hue[0] == '#' && hue[1..].All(Uri.IsHexDigit);

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex LabelMarker();
}