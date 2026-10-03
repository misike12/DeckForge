using DeckForge.Core.Blocks;
using DeckForge.Core.Capabilities;
using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Migrations;
using DeckForge.CodeGen.Generation;
using NUnit.Framework;
using Wpf.Ui.Controls;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The block catalog, held to the rules Part 7.1 of <c>visual.md</c> states.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is the single source of truth for what a document may contain, so these tests are the
/// place where "the emitter, the canvas and the validator all agree" is actually enforced. A hundred
/// and fifty rows of hand-written data drift: two rows end up sharing a kind, a menu's default stops
/// being one of its options, an <c>_integration</c> member is misspelled, and none of it is a compile
/// error. Every one of those has a test here instead of a bug report.
/// </para>
/// <para>
/// The two tests worth reading first are <see cref="Every_host_access_cites_a_member_that_exists"/>
/// and <see cref="Every_host_guard_is_derived_from_the_expression"/>: between them they check every
/// row's emitted expression against the SDK surface inventory committed under
/// <c>tools/SdkInventory/surface/</c>, which is the evidence Appendix A is built from. A block whose
/// expression reaches for a member <c>IIntegrationContext</c> does not have is caught here rather than
/// by a plugin that will not compile.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BlockCatalogTests
{
    /// <summary>
    /// The ten members of <c>IIntegrationContext</c>, from Appendix A.1.
    /// </summary>
    /// <remarks>
    /// Transcribed, not reflected, because the test project deliberately does not reference the SDK:
    /// the point of the check is that the catalog's expressions match the *committed inventory*, and
    /// referencing the assembly would make this test pass whether or not the inventory was ever read.
    /// </remarks>
    private static readonly string[] IntegrationMembers =
    [
        "Config", "Deck", "Events", "Messages", "Notifications",
        "Scripts", "UiResources", "UserVariables", "Variables", "Widgets",
    ];

    /// <summary>The seven members of <c>ActionExecutionContext</c>, from Appendix A.2.</summary>
    private static readonly string[] ExecutionContextMembers =
    [
        "CallDepth", "CancellationToken", "Interactions", "OriginClientId", "OwnerWidgetId",
        "Parameters", "Ui",
    ];

    /// <summary>
    /// Which interface each <c>IIntegrationContext</c> member is, from Appendix A.1.
    /// </summary>
    /// <remarks>
    /// The map is the point: a row may cite the member (<c>Deck.ChangeFolderAsync</c>) or the interface
    /// (<c>IDeckNavigator.ChangeFolderAsync</c>), and both are checkable against the surface dump. What
    /// is not checkable — and so is not allowed — is citing neither.
    /// </remarks>
    private static readonly Dictionary<string, string> SurfaceOfIntegrationMember = new(StringComparer.Ordinal)
    {
        ["Config"] = "IIntegrationConfig",
        ["Deck"] = "IDeckNavigator",
        ["Events"] = "IEventPublisher",
        ["Messages"] = "IMessageChannel",
        ["Notifications"] = "IUserNotifier",
        ["Scripts"] = "IScriptApi",
        ["UiResources"] = "IUiResourceRegistry",
        ["UserVariables"] = "IUserVariableApi",
        ["Variables"] = "IVariableApi",
        ["Widgets"] = "IWidgetApi",
    };

    /// <summary>Which interface each <c>ActionExecutionContext</c> member is.</summary>
    private static readonly Dictionary<string, string> SurfaceOfContextMember = new(StringComparer.Ordinal)
    {
        ["Interactions"] = "IActionInteractions",
        ["Ui"] = "IUiInteractions",
    };

    // ---- identity -----------------------------------------------------------------------------------

    [Test]
    public void Every_kind_is_unique()
    {
        var duplicates = BlockCatalog.All
            .GroupBy(block => block.Kind, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.That(duplicates, Is.Empty, "Two rows share a kind, so Find() silently returns one of them: "
            + string.Join(", ", duplicates));
    }

    [Test]
    public void Every_kind_is_a_stable_identifier()
    {
        // The kind is what a saved document stores, so it must be a plain identifier in a namespace.
        // A space or a capital letter would make the sidecar ambiguous to read and the migration's
        // alias table impossible to grep.
        var offending = BlockCatalog.All
            .Where(block => block.Kind.Length == 0
                || block.Kind.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')
                    and not '.' and not '-'))
            .Select(block => block.Kind)
            .ToList();

        Assert.That(offending, Is.Empty, "A kind is not lowercase, dotted and hyphenated: "
            + string.Join(", ", offending));
    }

    [Test]
    public void Every_block_says_what_it_is()
    {
        var offending = BlockCatalog.All
            .Where(block => string.IsNullOrWhiteSpace(block.Label)
                || string.Equals(block.Label, block.Kind, StringComparison.Ordinal))
            .Select(block => block.Kind)
            .ToList();

        Assert.That(offending, Is.Empty,
            "A block's label is blank or is just its id, so the palette row would read as an internal "
            + "name: " + string.Join(", ", offending));
    }

    [Test]
    public void Every_block_says_what_it_does()
    {
        var offending = BlockCatalog.All
            .Where(block => block.Summary.Trim().Length < 12)
            .Select(block => $"{block.Kind} ({block.Summary})")
            .ToList();

        Assert.That(offending, Is.Empty, "A block's summary is too short to be a tooltip: "
            + string.Join(", ", offending));
    }

    [Test]
    public void Every_block_cites_the_surface_it_was_checked_against()
    {
        // IsVerified is a claim with evidence, not a mood: Part 7.1 makes an unverified block
        // unsaveable, and the only way to be verified is to name what was read.
        var unverified = BlockCatalog.All
            .Where(block => !block.IsVerified)
            .Select(block => block.Kind)
            .ToList();

        Assert.That(unverified, Is.Empty, "A block claims no verification: " + string.Join(", ", unverified));
    }

    // ---- shape --------------------------------------------------------------------------------------

    [Test]
    public void Every_container_wraps_named_bodies_and_nothing_else_does()
    {
        var offending = BlockCatalog.All
            .Where(block => block.IsContainer != (block.Shape is BlockShape.C or BlockShape.CIf
                or BlockShape.CChain))
            .Select(block => $"{block.Kind} ({block.Shape})")
            .ToList();

        Assert.That(offending, Is.Empty,
            "A block wraps a body without a C shape, or has a C shape with nothing to wrap — either "
            + "way the canvas would draw a notch that goes nowhere: " + string.Join(", ", offending));

        var containers = BlockCatalog.All.Where(block => block.IsContainer).ToList();
        Assert.That(containers, Is.Not.Empty, "No block wraps a body at all, so nothing can nest.");
    }

    [Test]
    public void Every_hat_has_somewhere_to_start()
    {
        var hats = BlockCatalog.All.Where(block => block.IsHat).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(hats, Is.Not.Empty, "A language with no hat has no script.");
            Assert.That(
                hats.Where(hat => hat.Bodies is { Count: > 0 }).Select(hat => hat.Kind),
                Is.Empty,
                "A hat wraps a body, which would put instructions above the event.");
            // Every hat has to name a target kind that can actually be written. The list is a map rather
            // than a set of excuses, because a hat nobody can place is worse than no hat: it draws in the
            // palette, it validates, and the save then refuses.
            var placeable = new Dictionary<string, (TargetKind Kind, string AnchorId)>(StringComparer.Ordinal)
            {
                ["hat.action-runs"] = (TargetKind.Action, "ExecuteAsync"),
                ["hat.script-starts"] = (TargetKind.Action, "ExecuteAsync"),
                ["hat.widget-event"] = (TargetKind.WidgetHandler, "press"),
                ["hat.config-flow-step"] = (TargetKind.ConfigFlowHook, "StartAsync"),
                ["hat.plugin-initializes"] = (TargetKind.Lifecycle, "InitializeAsync"),
                ["hat.plugin-shuts-down"] = (TargetKind.Lifecycle, "ShutdownAsync"),
            };

            // A procedure's hat declares rather than triggers: it is hoisted into whichever regions
            // call it, and there is no anchor for it because nothing splices it into a method.
            var declarations = hats.Where(hat => hat.Kind.StartsWith("proc.define", StringComparison.Ordinal));

            Assert.That(
                declarations.Select(hat => hat.Kind),
                Is.EquivalentTo(new[] { "proc.define", "proc.define-returning" }),
                "the declaring hats, and only those, are hats with no target");

            Assert.That(
                hats.Where(hat => !placeable.ContainsKey(hat.Kind))
                    .Select(hat => hat.Kind)
                    .Where(kind => !kind.StartsWith("proc.define", StringComparison.Ordinal)),
                Is.Empty,
                "A hat that no target kind can write is not implementable yet.");

            foreach (var hat in hats.Where(hat => placeable.ContainsKey(hat.Kind)))
            {
                var (kind, anchorId) = placeable[hat.Kind];
                Assert.That(
                    VisualTargetWriter.AnchorFor(new VisualTarget { Kind = kind, AnchorId = anchorId }),
                    Is.Not.Null,
                    $"{hat.Kind} claims a {kind} target, and that target has no anchor to be written into");
            }
        });
    }

    [Test]
    public void Every_placeholder_is_kept_out_of_the_palette()
    {
        var placeholders = BlockCatalog.All
            .Where(block => block.Shape == BlockShape.Placeholder)
            .Select(block => block.Kind)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(placeholders, Is.EqualTo(new[] { "legacy.unsupported" }),
                "A placeholder appeared, or the migration's one placeholder lost its shape, and it would "
                + "now be draggable — a block a user can drop and cannot fill in.");
            Assert.That(BlockCatalog.Blocks.Select(block => block.Kind), Has.None.EqualTo("legacy.unsupported"));
            Assert.That(BlockCatalog.Find("legacy.unsupported"), Is.Not.Null,
                "A document can hold the placeholder, so the emitter has to be able to resolve it.");
        });
    }

    // ---- slots, menus and bodies --------------------------------------------------------------------

    [Test]
    public void Slot_menu_and_body_names_are_unique_within_a_block()
    {
        var offenders = new List<string>();

        foreach (var block in BlockCatalog.All)
        {
            offenders.AddRange(Duplicates(block.Kind, "slot", (block.Slots ?? []).Select(slot => slot.Name)));
            offenders.AddRange(Duplicates(block.Kind, "menu", (block.Menus ?? []).Select(menu => menu.Name)));
            offenders.AddRange(Duplicates(block.Kind, "body", (block.Bodies ?? []).Select(body => body.Name)));
        }

        Assert.That(offenders, Is.Empty, "A block declares the same key twice, so one of them is "
            + "unreachable: " + string.Join(", ", offenders));

        static IEnumerable<string> Duplicates(string kind, string what, IEnumerable<string> names) =>
            names.GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{kind}.{what}[{group.Key}]");
    }

    [Test]
    public void Every_slot_menu_and_body_is_a_usable_key()
    {
        // These names are JSON keys in a saved document and are matched with StringComparison.Ordinal
        // by the emitter, so whitespace or casing is a defect the user sees as an empty slot.
        var offenders = new List<string>();

        foreach (var block in BlockCatalog.All)
        {
            foreach (var name in (block.Slots ?? []).Select(slot => slot.Name)
                .Concat((block.Menus ?? []).Select(menu => menu.Name))
                .Concat((block.Bodies ?? []).Select(body => body.Name)))
            {
                if (name.Length == 0 || name != name.Trim()
                    || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                {
                    offenders.Add($"{block.Kind}.{name}");
                }
            }
        }

        Assert.That(offenders, Is.Empty, "A slot, menu or body name is not a plain key: "
            + string.Join(", ", offenders));
    }

    [Test]
    public void Every_menu_default_is_one_of_its_options()
    {
        var offenders = BlockCatalog.All
            .SelectMany(block => (block.Menus ?? []).Select(menu => (block.Kind, menu)))
            .Where(entry => !entry.menu.Options.Contains(entry.menu.Default, StringComparer.Ordinal))
            .Select(entry => $"{entry.Kind}.{entry.menu.Name} -> {entry.menu.Default}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "A menu starts on a value it does not offer, so the canvas shows a blank dropdown and the "
            + "emitter falls through to its own default: " + string.Join(", ", offenders));
    }

    [Test]
    public void Every_menu_offers_at_least_two_distinct_options()
    {
        var offenders = BlockCatalog.All
            .SelectMany(block => (block.Menus ?? []).Select(menu => (block.Kind, menu)))
            .Where(entry => entry.menu.Options.Count < 2
                || entry.menu.Options.Distinct(StringComparer.Ordinal).Count() != entry.menu.Options.Count)
            .Select(entry => $"{entry.Kind}.{entry.menu.Name}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "A menu with one option is a label pretending to be a choice, and a duplicated option is a "
            + "value the user cannot distinguish: " + string.Join(", ", offenders));
    }

    [Test]
    public void Every_menu_that_names_something_the_sdk_defines_offers_that_exact_set()
    {
        // The error-code menu is the one place the design says "the menu IS that list, not a hand-typed
        // copy". ActionErrorCodes has nine members and no tenth, so this is checked, not trusted.
        var errorCodes = BlockCatalog.Find("control.finish-failed")!.Menu("code");

        Assert.That(errorCodes, Is.Not.Null);
        Assert.That(errorCodes!.Options, Is.EquivalentTo(new[]
        {
            "NotConfigured", "NotConnected", "PermissionDenied", "ProviderError", "ProviderRejected",
            "InvalidParameter", "NotFound", "Timeout", "Unavailable",
        }), "the menu must be exactly the SDK's ActionErrorCodes members");
        Assert.That(errorCodes.Default, Is.EqualTo("ProviderError"));
    }

    [Test]
    public void A_slot_named_after_something_the_document_owns_is_tagged_with_its_source()
    {
        // A slot whose value is a name the document or the plugin owns — a variable, a list — must
        // carry the source, because that is what the canvas turns into a dropdown of the right names.
        // Tagged Text it renders an empty box the user has to fill in and spell exactly right, and the
        // validator cannot tell a typo from an undeclared variable.
        //
        // `name` is deliberately not in this rule. It is used both ways on purpose: `proc.define`
        // declares a new procedure's name, which no dropdown can offer, while `proc.call` picks an
        // existing one, which must be a dropdown. A rule that could not tell those apart would force a
        // false choice on one of them, so the source type is what distinguishes them.
        var offenders = BlockCatalog.All
            .SelectMany(block => (block.Slots ?? []).Select(slot => (block.Kind, slot)))
            .Where(entry => entry.slot.Name is "var" or "list"
                && entry.slot.Type is SlotType.Any or SlotType.Text or SlotType.Number)
            .Select(entry => $"{entry.Kind}.{entry.slot.Name} ({entry.slot.Type})")
            .ToList();

        Assert.That(offenders, Is.Empty, "A slot names a document-or-plugin entity as a plain value: "
            + string.Join(", ", offenders));

        Assert.That(
            BlockCatalog.All.SelectMany(block => block.Slots ?? []).Select(slot => slot.Type).Distinct().Count(),
            Is.GreaterThan(5),
            "the slot vocabulary has collapsed to a couple of types, which means the palette can no "
            + "longer offer the right dropdown anywhere");
    }

    [Test]
    public void No_source_slot_starts_on_a_name_that_may_not_exist()
    {
        // A default value on a source slot is a name written into a fresh block: "items" as the list, a
        // variable called "value". If the document does not declare it, the block is born broken.
        var offenders = BlockCatalog.All
            .SelectMany(block => (block.Slots ?? []).Select(slot => (block.Kind, slot)))
            .Where(entry => entry.slot.DefaultText is not null && entry.slot.Type is
                SlotType.List or SlotType.HostVariable or SlotType.UserVariable or SlotType.Parameter
                or SlotType.Event or SlotType.Action or SlotType.Script or SlotType.Widget
                or SlotType.ConfigEntry or SlotType.Variable or SlotType.Procedure)
            .Select(entry => $"{entry.Kind}.{entry.slot.Name} = {entry.slot.DefaultText}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "A source slot has a hardcoded starting name, so a fresh block would reference something "
            + "that probably does not exist: " + string.Join(", ", offenders));
    }

    // ---- reaching the host --------------------------------------------------------------------------

    [Test]
    public void Every_host_access_cites_a_member_that_exists()
    {
        // The expression is what the emitter writes into the action file, so a member that is not on
        // IIntegrationContext is a plugin that does not compile. Checked against Appendix A.1's list.
        var offenders = new List<string>();

        foreach (var block in BlockCatalog.All)
        {
            foreach (var member in DottedMembers(block.Sdk.Expression, "_integration"))
            {
                if (!IntegrationMembers.Contains(member, StringComparer.Ordinal))
                {
                    offenders.Add($"{block.Kind}: _integration.{member}");
                }
            }

            foreach (var member in DottedMembers(block.Sdk.Expression, "context"))
            {
                if (!ExecutionContextMembers.Contains(member, StringComparer.Ordinal))
                {
                    offenders.Add($"{block.Kind}: context.{member}");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "A block reaches for a member the SDK surface does not have: " + string.Join(", ", offenders));
    }

    [Test]
    public void Every_host_access_cites_the_surface_that_member_lives_on()
    {
        // Not the same question as the test above. That one asks whether the member exists at all; this
        // one asks whether the row was read from the right place. A mapping copied from IWidgetApi but
        // claiming IDeckNavigator is a row nobody can check, which makes it unverified in substance
        // however non-empty its citation looks.
        var offenders = new List<string>();

        foreach (var block in BlockCatalog.All)
        {
            foreach (var (root, members) in new[]
            {
                ("_integration", SurfaceOfIntegrationMember),
                ("context", SurfaceOfContextMember),
            })
            {
                foreach (var member in DottedMembers(block.Sdk.Expression, root))
                {
                    if (!members.TryGetValue(member, out var surface))
                    {
                        continue;
                    }

                    if (!block.Sdk.VerifiedAgainst.Contains(member, StringComparison.Ordinal)
                        && !block.Sdk.VerifiedAgainst.Contains(surface, StringComparison.Ordinal))
                    {
                        offenders.Add($"{block.Kind}: {root}.{member} cites \"{block.Sdk.VerifiedAgainst}\"");
                    }
                }
            }
        }

        Assert.That(offenders, Is.Empty, "The mapping cites a different surface than it calls: "
            + string.Join(", ", offenders));
    }

    [Test]
    public void Every_host_guard_is_derived_from_the_expression()
    {
        // RequiresHost is derived, not stored, precisely so this holds by construction. The test still
        // exists because "by construction" is a claim about today's code, and a stored flag is exactly
        // the kind of thing a later refactor reintroduces for convenience.
        var offenders = new List<string>();

        foreach (var block in BlockCatalog.All)
        {
            var touchesIntegration = block.Sdk.Expression.Contains("_integration", StringComparison.Ordinal);
            if (touchesIntegration != block.Sdk.RequiresHost && !block.Sdk.SuppressHostGuard)
            {
                offenders.Add(block.Kind);
            }

            var touchesExecutionContext =
                block.Sdk.Expression.Contains("context.Ui", StringComparison.Ordinal)
                || block.Sdk.Expression.Contains("context.Interactions", StringComparison.Ordinal);
            if (touchesExecutionContext != block.Sdk.RequiresInteraction)
            {
                offenders.Add(block.Kind + " (interaction)");
            }
        }

        Assert.That(offenders, Is.Empty,
            "The host guard disagrees with the expression, so the emitter would either fail an action "
            + "that never needed a session or call into a null: " + string.Join(", ", offenders));
    }

    [Test]
    public void Exactly_one_block_asks_whether_a_session_exists()
    {
        // Suppressing the guard is only legal for the predicate that asks whether the guard is needed.
        var suppressions = BlockCatalog.All
            .Where(block => block.Sdk.SuppressHostGuard)
            .Select(block => block.Kind)
            .ToList();

        Assert.That(suppressions, Is.EqualTo(new[] { "sensing.session-established" }));
    }

    [Test]
    public void Every_block_that_needs_a_capability_names_one_that_exists()
    {
        var unknown = BlockCatalog.All
            .Where(block => block.CapabilityId is { Length: > 0 } id && CapabilityCatalog.Find(id) is null)
            .Select(block => $"{block.Kind} -> {block.CapabilityId}")
            .ToList();

        Assert.That(unknown, Is.Empty,
            "A block claims a capability the gallery does not know, so the warning it raises would deep "
            + "link to nothing: " + string.Join(", ", unknown));
    }

    [Test]
    public void A_block_that_drives_a_gated_surface_says_it_needs_a_capability()
    {
        // Which capability a block needs is a judgement the row makes and this test cannot re-derive —
        // except in one direction, and that direction is the one that matters: a block that calls into a
        // member only present when the plugin declares a capability must name *some* capability, or the
        // validator's vis-capability-missing warning can never fire for it and the user finds out when
        // the plugin fails at runtime.
        //
        // Notifications and UiResources are deliberately absent from the list: the host implements them
        // for every plugin, so a block that shows a notification needs nothing declared.
        var gated = new[]
        {
            "Deck", "Events", "Messages", "Scripts", "Variables", "UserVariables", "Widgets", "Config",
        };

        var missing = BlockCatalog.All
            .Where(block => string.IsNullOrWhiteSpace(block.CapabilityId)
                && gated.Any(member =>
                    block.Sdk.Expression.Contains($"_integration.{member}.", StringComparison.Ordinal)
                    || block.Sdk.Expression.Contains($"_integration.{member},", StringComparison.Ordinal)))
            .Select(block => block.Kind)
            .ToList();

        Assert.That(missing, Is.Empty, "A block drives a gated surface without declaring a capability: "
            + string.Join(", ", missing));
    }

    [Test]
    public void The_capabilities_the_palette_depends_on_are_the_documented_set()
    {
        // The list of capabilities the palette reaches for is a statement about what a plugin using
        // Visual blocks has to declare, so it is pinned rather than left to accumulate.
        var inUse = BlockCatalog.All
            .Select(block => block.CapabilityId)
            .Where(id => id is { Length: > 0 })
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.That(inUse, Is.EqualTo(new[]
        {
            "actions", "button-icons", "button-states", "config-flows", "deck", "events",
            "logging", "macrodeck-ui", "messaging", "music-players", "variables", "widget-types",
        }));
    }

    // ---- the palette --------------------------------------------------------------------------------

    [TestCase(BlockCategory.Control, 28)]
    [TestCase(BlockCategory.Deck, 13)]
    [TestCase(BlockCategory.Ui, 15)]
    [TestCase(BlockCategory.Media, 0)]
    [TestCase(BlockCategory.Events, 4)]
    [TestCase(BlockCategory.Sensing, 23)]
    [TestCase(BlockCategory.Operators, 30)]
    [TestCase(BlockCategory.Variables, 8)]
    [TestCase(BlockCategory.Lists, 13)]
    [TestCase(BlockCategory.Parameters, 9)]
    [TestCase(BlockCategory.Network, 12)]
    [TestCase(BlockCategory.Procedures, 5)]
    public void Every_category_is_the_size_the_design_says(BlockCategory category, int expected)
    {
        // Pinned on purpose. Adding a block is a decision, and a decision that changes a documented
        // number should change the document in the same commit - which is this test failing until it
        // does. The alternative is a palette that quietly grew past its design.
        Assert.That(BlockCatalog.InCategory(category), Has.Count.EqualTo(expected),
            $"{category} does not match the as-built table in Part 7.1 of visual.md.");
    }

    [Test]
    public void The_palette_is_the_size_the_design_says()
    {
        Assert.That(BlockCatalog.Blocks, Has.Count.EqualTo(160));
        Assert.That(BlockCatalog.All.Count(block => !block.IsPaletteBlock), Is.EqualTo(1),
            "one placeholder, which is the migration's record of an unknown statement");
    }

    [Test]
    public void Every_category_has_one_palette_row()
    {
        var fromEnum = Enum.GetValues<BlockCategory>()
            .Where(category => category != BlockCategory.Media)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(BlockCatalog.Categories.Select(row => row.Category), Is.EqualTo(fromEnum),
                "the rail's order is the enum's order, so a category added at the end of the enum "
                + "appears at the end of the rail rather than in the middle of it");
            Assert.That(
                BlockCatalog.Categories.Select(row => row.Name),
                Is.Unique, "two categories would wear the same heading");
            Assert.That(
                BlockCatalog.Categories.Select(row => row.Hue),
                Is.Unique, "two categories would wear the same colour");
        });
    }

    [Test]
    public void Every_category_says_what_it_is_for()
    {
        var blank = BlockCatalog.Categories
            .Where(row => row.Summary.Trim().Length < 12 || row.Name.Trim().Length == 0)
            .Select(row => row.Category.ToString())
            .ToList();

        Assert.That(blank, Is.Empty, "A rail entry with nothing under it: " + string.Join(", ", blank));
    }

    [Test]
    public void Every_category_glyph_is_one_the_icon_library_defines()
    {
        // The rail binds this straight into a SymbolIcon, where an unknown name is a blank square and
        // not an error. It is the same trap XamlMarkupTests exists for, one layer earlier.
        var unknown = BlockCatalog.Categories
            .Where(row => !Enum.IsDefined(typeof(SymbolRegular), row.Glyph))
            .Select(row => $"{row.Category}={row.Glyph}")
            .ToList();

        Assert.That(unknown, Is.Empty, "A rail glyph is not a SymbolRegular member: "
            + string.Join(", ", unknown));
    }

    [Test]
    public void Every_hue_is_a_colour_the_canvas_can_parse()
    {
        var bad = BlockCatalog.Categories
            .Where(row => row.Hue.Length != 7 || row.Hue[0] != '#'
                || !row.Hue[1..].All(Uri.IsHexDigit))
            .Select(row => $"{row.Category}={row.Hue}")
            .ToList();

        Assert.That(bad, Is.Empty, "A category's hue is not #RRGGBB: " + string.Join(", ", bad));
    }

    [Test]
    public void The_palette_is_grouped_by_category_in_rail_order()
    {
        // Rendering walks the list once and starts a new group when the category changes, so an
        // out-of-order row would appear under the wrong heading.
        var ordered = BlockCatalog.All.Select(block => (int)block.Category).ToList();

        Assert.That(ordered, Is.Ordered, "the catalog is not grouped by category");
        Assert.That(
            ordered.Distinct().Count(),
            Is.EqualTo(BlockCatalog.Categories.Count(category => BlockCatalog.InCategory(category.Category).Count > 0)),
            "a category is declared but has no blocks, so the rail would show an empty heading");
    }

    // ---- lookups ------------------------------------------------------------------------------------

    [Test]
    public void The_lookups_agree_with_the_tables()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlockCatalog.Find(null), Is.Null);
            Assert.That(BlockCatalog.Find("no.such.block"), Is.Null);
            Assert.That(BlockCatalog.IsKnown("no.such.block"), Is.False);

            foreach (var block in BlockCatalog.All)
            {
                Assert.That(BlockCatalog.Find(block.Kind), Is.SameAs(block),
                    "Find must return the row itself, not a copy: the canvas caches by reference");
                Assert.That(BlockCatalog.IsKnown(block.Kind), Is.True);
            }

            foreach (var row in BlockCatalog.Categories)
            {
                Assert.That(BlockCatalog.Category(row.Category), Is.SameAs(row));
                Assert.That(BlockCatalog.InCategory(row.Category), Is.Not.Null);
            }
        });
    }

    [Test]
    public void Searching_finds_a_block_by_its_id_its_label_and_its_summary()
    {
        // Part 9.7: at this size the palette needs search, and the three fields a user might type are
        // exactly the three the palette shows them.
        var byId = BlockCatalog.Search("control.forever");
        var byLabel = BlockCatalog.Search("forever");
        var bySummary = BlockCatalog.Search("cancelled");

        Assert.Multiple(() =>
        {
            Assert.That(byId.Select(block => block.Kind), Contains.Item("control.forever"));
            Assert.That(byLabel.Select(block => block.Kind), Contains.Item("control.forever"));
            Assert.That(bySummary.Select(block => block.Kind), Contains.Item("control.forever"));
            Assert.That(BlockCatalog.Search("  "), Has.Count.EqualTo(BlockCatalog.Blocks.Count),
                "a blank query is every block, not no blocks");
            Assert.That(BlockCatalog.Search("zzzznothing"), Is.Empty);
            Assert.That(BlockCatalog.Search("FOREVER"), Is.Not.Empty, "search is case-insensitive");
        });
    }

    [Test]
    public void Searching_never_returns_a_placeholder()
    {
        Assert.That(BlockCatalog.Search("legacy"), Is.Empty,
            "the migration's placeholder must not be findable, or it can be dragged");
    }

    // ---- what is deliberately absent -----------------------------------------------------------------

    [Test]
    public void Nothing_deferred_can_be_used()
    {
        var deferred = BlockCatalog.Deferred.Select(block => block.Kind).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(deferred, Is.Unique);
            Assert.That(deferred.Intersect(BlockCatalog.All.Select(block => block.Kind), StringComparer.Ordinal),
                Is.Empty,
                "a deferred block is offerable, but its target arrives in Phase 9 and it would emit code "
                + "against a type that does not exist yet");
            Assert.That(
                BlockCatalog.Deferred.Where(block => !block.IsVerified).Select(block => block.Kind),
                Is.Empty,
                "a deferred block still has to say what it was checked against");
        });
    }

    [Test]
    public void Nothing_dropped_can_be_used_and_every_dropped_block_says_why()
    {
        var dropped = BlockCatalog.Dropped.Select(block => block.Kind).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.Unique);
            Assert.That(dropped, Has.Count.EqualTo(28),
                "Part 7.16 records twenty-eight dropped blocks; this count is the accounting, not a "
                + "decoration of it");
            Assert.That(dropped.Intersect(BlockCatalog.All.Select(block => block.Kind), StringComparer.Ordinal),
                Is.Empty, "a dropped block is still offerable");
            Assert.That(
                BlockCatalog.Dropped.Where(block => block.Reason.Trim().Length < 12).Select(block => block.Kind),
                Is.Empty,
                "a dropped block with no reason is a block the next person re-adds");
            Assert.That(
                BlockCatalog.Dropped.Where(block => BlockCatalog.Deferred.Any(d => d.Kind == block.Kind)),
                Is.Empty, "a block is both dropped and deferred, which are different verdicts");
        });
    }

    [Test]
    public void Every_kind_the_media_category_was_supposed_to_have_is_accounted_for()
    {
        // Whole-category gate, Part 7.6: media cannot be a block because IMusicPlayer is provider-side.
        // The category stays in the enum and in the rail's table so the decision has somewhere to live.
        Assert.Multiple(() =>
        {
            Assert.That(BlockCatalog.InCategory(BlockCategory.Media), Is.Empty);
            Assert.That(Enum.GetValues<BlockCategory>(), Contains.Item(BlockCategory.Media),
                "the category stays in the vocabulary, so the decision has somewhere to live");
            Assert.That(
                BlockCatalog.Categories.Select(row => row.Category),
                Has.None.EqualTo(BlockCategory.Media),
                "the rail renders the descriptors it is given, so Media must not have one until it has "
                + "blocks to put under it");
            Assert.That(BlockCatalog.Dropped.Count(block => block.Kind.StartsWith("media.", StringComparison.Ordinal)),
                Is.EqualTo(17), "all seventeen C4 rows are dropped with one shared reason");
        });
    }

    // ---- the migration obeys the catalog -------------------------------------------------------------

    [Test]
    public void Every_legacy_statement_migrates_to_a_kind_the_catalog_knows()
    {
        var unknown = new List<string>();

        foreach (var statement in EveryLegacyStatement())
        {
            foreach (var block in Migrate(statement))
            {
                if (!BlockCatalog.IsKnown(block.Kind))
                {
                    unknown.Add($"{statement.GetType().Name} -> {block.Kind}");
                }
            }
        }

        Assert.That(unknown, Is.Empty,
            "The retired page's work would land on a canvas as an unknown kind: " + string.Join(", ", unknown));
    }

    [Test]
    public void The_migration_only_writes_keys_the_catalog_declares()
    {
        // The load-bearing test of this file. A block whose value sits in `fields` when the catalog
        // declares a slot is not a compile error and not a parse error: it is a block that emits
        // nothing, so a user's migrated action silently stops doing half of what it did.
        var offenders = new List<string>();

        foreach (var statement in EveryLegacyStatement())
        {
            foreach (var block in Migrate(statement))
            {
                offenders.AddRange(UnknownKeys(block));
            }
        }

        Assert.That(offenders, Is.Empty, "The migration writes a key the catalog does not declare: "
            + string.Join(", ", offenders));
    }

    [Test]
    public void The_migration_only_writes_keys_the_catalog_declares_when_every_option_is_set()
    {
        // The default instances above take every optional branch that is off by default. This program
        // takes all of them: a key, a level, a payload, inputs, modal data, a bearer token, a
        // parameter-valued write. Those are the paths a migrated file actually contains.
        var program = new BlockProgram
        {
            TargetActionId = "everything",
            Statements =
            [
                new LogBlock { Template = "{name}", Parameter = "amount", Level = "Warning" },
                new SetVariableBlock { VariableName = "count", FromParameter = "amount", Type = "number", Required = true },
                new SetVariableBlock { VariableName = "text", Literal = "hi", Type = "string" },
                new IfBlock
                {
                    LeftVariable = "count",
                    Operator = "==",
                    RightLiteral = "2",
                    Then = [new ThrowBlock { Message = "no" }],
                    Else = [new DelayBlock { Milliseconds = 5 }],
                },
                new ReturnResultBlock { Outcome = "failed", ErrorCode = "Timeout", Message = "late" },
                new ReturnResultBlock { Outcome = "accepted", Message = "working" },
                new HttpRequestBlock { Url = "https://example.com", IntoVariable = "body", BearerTokenParameter = "token" },
                new NotifyBlock { Title = "T", Message = "M", Level = "Error", Key = "k" },
                new RunScriptBlock { ScriptId = "refresh", Inputs = "who=name" },
                new PublishEventBlock { EventId = "changed", Payload = "n=count" },
                new ReadVariableBlock { VariableName = "shared", IntoVariable = "local" },
                new SetVariableValueBlock { VariableName = "shared", Value = "amount", UseParameter = true },
                new ShowModalBlock { ViewId = "ask", Title = "T", Data = "k=v" },
                new InvalidateIconBlock { ActionId = "log-message" },
            ],
        };

        var offenders = BlocksV1Migration.Migrate(program)
            .Blocks()
            .SelectMany(UnknownKeys)
            .ToList();

        Assert.That(offenders, Is.Empty, "A migrated key is not declared: " + string.Join(", ", offenders));
    }

    [Test]
    public void Every_legacy_operator_lands_on_a_menu_option()
    {
        var menu = BlockCatalog.Find("ops.compare")!.Menu("op")!;
        var offenders = new List<string>();

        foreach (var legacyOperator in new[]
        {
            "==", "!=", ">", "<", ">=", "<=", "isEmpty", "isNotEmpty", "isAvailable",
            "isNotAvailable", "contains", "notContains",
        })
        {
            var condition = Migrate(new IfBlock { LeftVariable = "x", Operator = legacyOperator }).Single()
                .InputBlock("condition")!;

            if (condition.Field("op") is not { } op || !menu.Options.Contains(op, StringComparer.Ordinal))
            {
                offenders.Add($"{legacyOperator} -> {condition.Field("op") ?? "(unset)"}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "A migrated condition carries a value the menu does not offer, so it would test something "
            + "other than what the user wrote: " + string.Join(", ", offenders));
    }

    [Test]
    public void Every_legacy_error_code_lands_on_a_menu_option()
    {
        var menu = BlockCatalog.Find("control.finish-failed")!.Menu("code")!;
        var offenders = new List<string>();

        foreach (var code in new[] { "Timeout", "NotFound", "timeout", "PROVIDER_ERROR", "nonsense", "" })
        {
            var block = Migrate(new ReturnResultBlock { Outcome = "failed", ErrorCode = code }).Single();
            var chosen = block.Field("code") ?? menu.Default;

            if (!menu.Options.Contains(chosen, StringComparer.Ordinal))
            {
                offenders.Add($"{code} -> {chosen}");
            }
        }

        Assert.That(offenders, Is.Empty, "A migrated error code is not one the menu offers: "
            + string.Join(", ", offenders));
    }

    [Test]
    public void Every_legacy_variable_type_lands_on_a_menu_option()
    {
        var menu = BlockCatalog.Find("var.set-from-parameter")!.Menu("type")!;

        foreach (var (legacy, expected) in new[]
        {
            ("string", "Text"), ("number", "Numeric"), ("bool", "Boolean"), ("???", "Text"),
        })
        {
            var block = Migrate(new SetVariableBlock { FromParameter = "p", Type = legacy }).Single();

            Assert.Multiple(() =>
            {
                Assert.That(menu.Options, Contains.Item(expected));
                Assert.That(block.Field("type"), Is.EqualTo(expected));
            });
        }
    }

    [Test]
    public void A_migrated_value_reaches_the_slot_the_emitter_reads()
    {
        // The point of the mapping rule, stated as a behaviour: what the user typed on the old canvas
        // has to be in the place the emitter looks, not merely somewhere in the file.
        var log = Migrate(new LogBlock { Template = "hello {name}", Parameter = "amount" }).Single();

        var notify = Migrate(new NotifyBlock { Title = "Done", Message = "ok", Level = "Warning" }).Single();

        var notifyKeyed = Migrate(new NotifyBlock { Title = "T", Message = "M", Key = "k" }).Single();

        Assert.Multiple(() =>
        {
            Assert.That(log.InputText("template"), Is.EqualTo("hello {name}"));
            Assert.That(log.InputText("param"), Is.EqualTo("amount"));
            Assert.That(log.Field("level"), Is.EqualTo("Information"));

            Assert.That(notify.Kind, Is.EqualTo("ui.notify-level"));
            Assert.That(notify.InputText("message"), Is.EqualTo("ok"));
            Assert.That(notify.Field("level"), Is.EqualTo("Warning"));

            Assert.That(notifyKeyed.Kind, Is.EqualTo("ui.notify-key"));
            Assert.That(notifyKeyed.InputText("key"), Is.EqualTo("k"));
        });
    }

    [Test]
    public void A_parameter_valued_write_nests_the_reader_in_the_slot()
    {
        // The catalog expresses "the value is a parameter, not a literal" by putting a reporter in the
        // slot, which is the whole reason the model has slots instead of only fields.
        var block = Migrate(new SetVariableValueBlock
        {
            VariableName = "shared",
            Value = "amount",
            UseParameter = true,
        }).Single();

        Assert.Multiple(() =>
        {
            Assert.That(block.Kind, Is.EqualTo("sensing.set-host-variable"));
            Assert.That(block.InputText("name"), Is.EqualTo("shared"));
            Assert.That(block.InputBlock("value")?.Kind, Is.EqualTo("data.read-payload"));
            Assert.That(block.InputBlock("value")?.InputText("name"), Is.EqualTo("amount"));
        });
    }

    [Test]
    public void A_request_with_a_bearer_token_sets_it_before_it_calls()
    {
        var blocks = Migrate(new HttpRequestBlock
        {
            Url = "https://example.com",
            IntoVariable = "body",
            BearerTokenParameter = "token",
        }).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(blocks.Select(block => block.Kind),
                Is.EqualTo(new[] { "http.set-bearer", "http.get" }));
            Assert.That(blocks[0].InputText("name"), Is.EqualTo("token"));
            Assert.That(blocks[1].InputText("into"), Is.EqualTo("body"));
        });
    }

    [Test]
    public void Every_local_the_migration_creates_is_declared()
    {
        // A set block that declares a local the document does not list is a local with no entry in the
        // palette, which is the one thing §6.2.1 says the migration exists to avoid.
        var program = new BlockProgram
        {
            Statements =
            [
                new SetVariableBlock { VariableName = "count", FromParameter = "amount", Type = "number" },
                new SetVariableBlock { VariableName = "note", Literal = "hi", Type = "string" },
                new ReadVariableBlock { VariableName = "shared", IntoVariable = "sharedValue" },
            ],
        };

        var migrated = BlocksV1Migration.Migrate(program);
        var declared = migrated.Variables.Select(variable => variable.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(declared, Is.EquivalentTo(new[] { "count", "note", "sharedValue" }));
            Assert.That(migrated.Variables.Select(variable => variable.Scope),
                Is.All.EqualTo(VariableScope.Local));
            Assert.That(migrated.Variables.Single(variable => variable.Name == "count").Type,
                Is.EqualTo("Numeric"), "the type menu is the document's vocabulary, not the old local's");
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>The members after <c>name.</c> in an expression, for the two roots that are in scope.</summary>
    private static IEnumerable<string> DottedMembers(string expression, string root)
    {
        var search = root + ".";
        var index = expression.IndexOf(search, StringComparison.Ordinal);

        while (index >= 0)
        {
            var start = index + search.Length;
            var end = start;
            while (end < expression.Length && (char.IsAsciiLetterOrDigit(expression[end]) || expression[end] == '_'))
            {
                end++;
            }

            if (end > start)
            {
                yield return expression[start..end];
            }

            index = expression.IndexOf(search, end, StringComparison.Ordinal);
        }
    }

    /// <summary>Every key in a block that its descriptor does not declare.</summary>
    private static IEnumerable<string> UnknownKeys(Block block)
    {
        if (BlockCatalog.Find(block.Kind) is not { } descriptor)
        {
            return [$"{block.Kind} is not in the catalog"];
        }

        var offenders = new List<string>();
        offenders.AddRange(block.Inputs.Keys
            .Where(key => descriptor.Slot(key) is null)
            .Select(key => $"{block.Kind}.inputs[{key}]"));
        offenders.AddRange(block.Fields.Keys
            .Where(key => descriptor.Menu(key) is null)
            .Select(key => $"{block.Kind}.fields[{key}]"));
        offenders.AddRange(block.Bodies.Keys
            .Where(key => descriptor.Bodies?.Any(body => string.Equals(body.Name, key, StringComparison.Ordinal)) is not true)
            .Select(key => $"{block.Kind}.bodies[{key}]"));

        return offenders;
    }

    /// <summary>One instance of every statement type the retired canvas can persist.</summary>
    /// <remarks>
    /// Reflected rather than listed, so a nineteenth legacy kind added to <c>BlockModel</c> is covered
    /// by these tests the moment it exists. A hand-written list is exactly the thing that would have
    /// missed the case this file was written to catch.
    /// </remarks>
    private static IEnumerable<BlockStatement> EveryLegacyStatement() =>
        typeof(BlockStatement).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(BlockStatement).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(type => (BlockStatement)Activator.CreateInstance(type)!);

    /// <summary>Migrates one statement and returns the blocks it becomes, in order.</summary>
    private static IReadOnlyList<Block> Migrate(BlockStatement statement) =>
        [.. BlocksV1Migration
            .Migrate(new BlockProgram { Statements = [statement] })
            .Targets.Single().Scripts.Single().Body];
}
