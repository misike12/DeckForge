using System.Text;
using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Providers;
using DeckForge.Core.Visual.Runtime;
using NUnit.Framework;
using Wpf.Ui.Controls;

namespace DeckForge.Tests.Visual;

/// <summary>
/// A third-party block provider: what it may contribute, and what the catalogue it joins has already had.
/// </summary>
/// <remarks>
/// <para>
/// Part 23 asked for a seam and §28.5 said "not started — the catalogue is closed". The interesting part is
/// not that a provider can add blocks; it is that it cannot add a block that breaks something the built-in
/// rows are held to. Every rule <see cref="BlockProviderRules"/> applies is a rule
/// <c>BlockCatalogTests</c> already enforces on the catalogue — contrast at 4.5:1 and 3:1, a label whose
/// holes exist, a menu whose default it offers, a container that wraps something — and a check that only
/// the build runs is not a check.
/// </para>
/// <para>
/// The built-in registry is asserted byte for byte against <see cref="BlockCatalog"/> first, because "the
/// default catalogue stays exactly as it is" has to be a property of the code. A registry that sorted,
/// copied or filtered the catalogue would make the palette depend on whether an extension happened to be
/// installed, which is the kind of difference a user reports as "the blocks moved".
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualBlockProviderTests
{
    [Test]
    [Description("Part 23.1 - with no providers loaded the catalogue is exactly the catalogue.")]
    public void No_providers_leaves_the_catalogue_exactly_as_it_was()
    {
        var registry = VisualBlockRegistry.Load([]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Blocks.Count, Is.EqualTo(BlockCatalog.All.Count));
            Assert.That(registry.Categories.Count, Is.EqualTo(BlockCatalog.Categories.Count));

            // Reference equality, not equality of values: the canvas caches descriptors by reference, and a
            // copy would silently stop being the row it was resolved from.
            foreach (var block in BlockCatalog.All)
            {
                Assert.That(registry.Blocks[Array.IndexOf(BlockCatalog.All.ToArray(), block)], Is.SameAs(block));
            }

            foreach (var category in BlockCatalog.Categories)
            {
                Assert.That(
                    registry.Categories[Array.IndexOf(BlockCatalog.Categories.ToArray(), category)],
                    Is.SameAs(category));
            }

            Assert.That(registry.Findings, Is.Empty);
            Assert.That(registry.OwnerOf("control.forever"), Is.Null);
        });
    }

    [Test]
    public void A_contributed_block_joins_the_catalogue_under_the_providers_namespace()
    {
        var registry = VisualBlockRegistry.Load([Music()]);

        var block = registry.Find("sampleext.music.thrust");

        Assert.Multiple(() =>
        {
            Assert.That(block, Is.Not.Null);
            Assert.That(registry.Blocks.Count, Is.EqualTo(BlockCatalog.All.Count + 1));
            Assert.That(registry.OwnerOf("sampleext.music.thrust"), Is.Not.Null);
            Assert.That(registry.OwnerOf("sampleext.music.thrust")!.Id, Is.EqualTo("sampleext"));
            Assert.That(registry.Knows("control.forever"), Is.True, "the built-in rows are still there");
            Assert.That(registry.Findings.Where(finding => finding.IsRefusal), Is.Empty);
        });
    }

    [Test]
    public void A_kind_that_is_not_namespaced_by_its_provider_is_refused()
    {
        var registry = VisualBlockRegistry.Load([Rogue("control.forever")]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("control.forever"), Is.SameAs(BlockCatalog.Find("control.forever")),
                "the catalogue's own row, untouched");
            Assert.That(registry.Findings.Any(finding =>
                finding.Subject == "control.forever" && finding.IsRefusal), Is.True,
                "a contributed kind has to be namespaced, or a document naming it means two different "
                + "blocks depending on which was loaded");
        });
    }

    [Test]
    public void A_contributed_kind_two_providers_both_claim_is_refused_for_the_second()
    {
        var registry = VisualBlockRegistry.Load([Music(), Music()]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Blocks.Count, Is.EqualTo(BlockCatalog.All.Count + 1),
                "the first one is in the palette and the second is not: a document naming that id means "
                + "two different blocks depending on which was loaded, and there is no honest way to pick");
            Assert.That(registry.Findings.Count(finding => finding.IsRefusal), Is.EqualTo(1));
            Assert.That(registry.Findings.Single(finding => finding.IsRefusal).Subject,
                Is.EqualTo("sampleext.music.thrust"));
        });
    }

    [Test]
    [Description("28.2 - a contributed category is measured against the same contrast requirements.")]
    public void A_contributed_category_with_an_ordinary_hue_meets_the_contrast_the_built_in_ones_meet()
    {
        var registry = VisualBlockRegistry.Load([Painted("#4C97FF")]);

        Assert.That(registry.Findings, Is.Empty,
            "the requirement decides the colours, so a hue in the same range as the built-in ones gets the "
            + "same guaranteed contrast — which is the property that stops a third party reintroducing what "
            + "Events' gold once had: "
            + string.Join("; ", registry.Findings.Select(finding => finding.Message)));
    }

    [Test]
    [Description("28.2 - a hostile hue cannot carry a label, and is reported rather than drawn.")]
    public void A_contributed_category_with_a_hostile_hue_is_reported_with_the_number_it_failed_at()
    {
        var registry = VisualBlockRegistry.Load([Painted("#111111")]);

        var contrast = registry.Findings
            .Where(finding => finding.Message.Contains("label would be", StringComparison.Ordinal)
                || finding.Message.Contains("outline would be", StringComparison.Ordinal))
            .ToList();

        Assert.That(contrast, Is.Not.Empty,
            "a near-black hue cannot carry near-black ink on a light fill, and the built-in palette gets "
            + "away with its hues only because they are mid-tones. This is the guarantee a third party "
            + "cannot opt out of: "
            + string.Join("; ", registry.Findings.Select(finding => finding.Message)));
        Assert.That(contrast.Select(finding => finding.Message), Has.Some.Contains("4.5:1"));
    }

    [Test]
    public void A_hue_that_is_not_a_colour_is_reported_rather_than_rendered_as_one()
    {
        var registry = VisualBlockRegistry.Load([Painted("periwinkle")]);

        Assert.That(registry.Findings.Select(finding => finding.Message), Has.Some.Contains("#RRGGBB"));
    }

    [Test]
    public void The_media_category_is_the_one_a_provider_may_fill_and_the_rail_gains_a_heading_for_it()
    {
        // Part 7.16 dropped the media blocks because the SDK's music surface is provider-side, which left
        // the enum member with no descriptor and the rail with a hole. A provider that declares it is
        // filling the hole the design left, and the rail only draws the descriptors it is given — so the
        // registry has to hand it one.
        var registry = VisualBlockRegistry.Load([WithCategory(BlockCategory.Media)]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Findings.Where(finding => finding.IsRefusal), Is.Empty);
            Assert.That(registry.Categories.Select(category => category.Category),
                Is.EqualTo(BlockCatalog.Categories.Select(category => category.Category).Append(BlockCategory.Media)));
            Assert.That(registry.InCategory(BlockCategory.Media).Select(block => block.Kind),
                Is.EqualTo(new[] { "sampleext.music.thrust" }));
        });
    }

    [Test]
    public void A_block_that_fails_what_the_built_in_rows_are_held_to_is_reported_by_name()
    {
        var registry = VisualBlockRegistry.Load([Rude("sampleext.music.broken")]);

        var findings = registry.Findings.Where(finding => finding.Subject == "sampleext.music.broken").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("sampleext.music.broken"), Is.Not.Null,
                "a bad label is something a user could work around, and refusing the block over a tooltip "
                + "would leave a provider with no way to see its own mistake");

            Assert.That(findings.Select(finding => finding.Message), Has.Some.Contains("label"));
            Assert.That(findings.Select(finding => finding.Message), Has.Some.Contains("tooltip"));
            Assert.That(findings.Select(finding => finding.Message), Has.Some.Contains("Menu"));
            Assert.That(findings.Select(finding => finding.Message), Has.Some.Contains("unverified"));
        });

        Assert.That(registry.Findings.All(finding => finding.ProviderId == "sampleext"), Is.True,
            "a finding with no provider on it cannot be acted on: the person who has to fix it is the person "
            + "who wrote the extension");
    }

    [Test]
    public void A_glyph_the_icon_library_does_not_define_is_reported()
    {
        // The rail binds this straight into a SymbolIcon, where an unknown name is a blank square and not
        // an error — the same trap XamlMarkupTests exists for, one layer earlier. The check needs the icon
        // library, which Core may not reference, so it is a predicate the host supplies; this is the
        // assertion that the plumbing is what makes the guarantee possible.
        var withCheck = VisualBlockRegistry.Load(
            [Music()],
            new VisualBlockProviderOptions(GlyphExists: Known));

        var withoutCheck = VisualBlockRegistry.Load([Music()]);

        Assert.Multiple(() =>
        {
            Assert.That(withCheck.Findings, Is.Empty, string.Join("; ", withCheck.Findings.Select(f => f.Message)));
            Assert.That(withoutCheck.Findings, Is.Empty,
                "a consumer with no icon library cannot check the glyph, and saying nothing about it is the "
                + "honest outcome — what it may not do is check it for the catalogue and not for providers");
        });

        var bad = VisualBlockRegistry.Load(
            [WithGlyph("ThisGlyphDoesNotExist")],
            new VisualBlockProviderOptions(GlyphExists: Known));

        Assert.That(bad.Findings.Select(finding => finding.Message), Has.Some.Contains("icon library"));
    }

    [Test]
    public void A_block_naming_a_slot_it_does_not_declare_is_reported()
    {
        var registry = VisualBlockRegistry.Load([Rude("sampleext.music.holey")]);

        Assert.That(registry.Findings.Select(finding => finding.Message),
            Has.Some.Contains("draws the raw marker"),
            "a hole naming nothing renders as the literal text `{count}` on the tile, which is the cheapest "
            + "guard there is against a whole class of visible-but-subtle defect — and a contributed row is "
            + "exactly where it gets written");
    }

    [Test]
    public void A_category_the_built_in_rail_already_has_is_refused()
    {
        var registry = VisualBlockRegistry.Load([WithCategory(BlockCategory.Control)]);

        Assert.That(registry.Findings.Where(finding => finding.IsRefusal)
            .Select(finding => finding.Message), Has.Some.Contains("already has a palette row"));
    }

    [Test]
    public void A_disabled_provider_contributes_nothing_and_the_rest_still_load()
    {
        var registry = VisualBlockRegistry.Load(
            [Music()],
            new VisualBlockProviderOptions(DisabledProviderIds: new HashSet<string>(["sampleext"])));

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("sampleext.music.thrust"), Is.Null);
            Assert.That(registry.Blocks.Count, Is.EqualTo(BlockCatalog.All.Count),
                "extensions are off by default, and that has to be true of a block provider as much as of a "
                + "page");
        });
    }

    [Test]
    public void Providers_are_ordered_by_order_then_by_id_so_the_palette_does_not_move()
    {
        var first = VisualBlockRegistry.Load([Provider("bprov", 10), Provider("aprov", 10)]);
        var second = VisualBlockRegistry.Load([Provider("aprov", 10), Provider("bprov", 10)]);

        Assert.Multiple(() =>
        {
            Assert.That(first.Providers.Select(provider => provider.Id), Is.EqualTo(new[] { "aprov", "bprov" }),
                "a container gives no ordering promise, so a rail whose order changed when an unrelated "
                + "service was registered would be a bug report about something nobody touched");
            Assert.That(second.Providers.Select(provider => provider.Id), Is.EqualTo(first.Providers.Select(p => p.Id)));
        });
    }

    [Test]
    public void A_block_whose_provider_is_not_loaded_is_reported_by_the_extension_that_supplies_it()
    {
        var project = new VisualProject
        {
            DocumentId = "abc1234",
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:log-message",
                    Name = "log-message",
                    Scripts =
                    [
                        new VisualScript
                        {
                            Id = "s1",
                            Hat = new Block { Id = "b1", Kind = "hat.action-runs" },
                            Body =
                            [
                                new Block { Id = "b2", Kind = "sampleext.music.thrust" },
                                new Block { Id = "b3", Kind = "control.forever" },
                            ],
                        },
                    ],
                },
            ],
        };

        var findings = VisualBlockRegistry.BuiltIn.MissingProviders(project);

        Assert.Multiple(() =>
        {
            Assert.That(findings, Has.Count.EqualTo(1), "one missing block, reported once even though the "
                + "diagnostic is the only thing standing between a document and a silent drop");
            Assert.That(findings[0].Code, Is.EqualTo("vis-provider-missing"),
                "Appendix F's own code, so the pane and the catalogue agree");
            Assert.That(findings[0].Severity, Is.EqualTo(VisualSeverity.Error),
                "a document holding a block nothing can emit does not compile");
            Assert.That(findings[0].Message, Does.Contain("sampleext"),
                "the id is namespaced precisely so the diagnostic can name the thing to enable");
            Assert.That(findings[0].BlockId, Is.EqualTo("b2"));
            Assert.That(findings[0].Hint, Does.Contain("Extensions"));
        });
    }

    [Test]
    public void An_unknown_kind_with_no_provider_in_it_is_not_called_a_missing_provider()
    {
        var project = new VisualProject
        {
            DocumentId = "abc1234",
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:log-message",
                    Scripts =
                    [
                        new VisualScript
                        {
                            Id = "s1",
                            Hat = new Block { Id = "b1", Kind = "hat.action-runs" },
                            Body = [new Block { Id = "b2", Kind = "madeup" }],
                        },
                    ],
                },
            ],
        };

        Assert.That(VisualBlockRegistry.BuiltIn.MissingProviders(project), Is.Empty,
            "an unknown undotted kind is a kind this build does not know at all, which is a different finding "
            + "with a different answer — sending the user to the Extensions page for a block that was never "
            + "anybody's would be a wrong turn with a confident message");
    }

    [Test]
    public void A_provider_that_throws_while_declaring_itself_is_contained_rather_than_allowed_to_escape()
    {
        var registry = VisualBlockRegistry.Load([new ThrowingProvider(), Music()]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Providers.Select(provider => provider.Id), Is.EqualTo(new[] { "sampleext" }),
                "one provider failing must not stop DeckForge from starting, which is the position "
                + "ExtensionService takes for the same reason");
            Assert.That(registry.Findings.Any(finding => finding.Message.Contains("threw")), Is.True);
            Assert.That(registry.Find("sampleext.music.thrust"), Is.Not.Null, "and the others still load");
        });
    }

    [Test]
    public void The_emitter_and_the_interpreter_step_are_the_provider_s_own_and_a_null_expression_means_not_mine()
    {
        var provider = Music();
        var block = BlockFactory.Create(BlockCatalog.All[0], () => "b1");

        Assert.Multiple(() =>
        {
            Assert.That(provider.EmitExpression(EmitContext(), block), Is.Null,
                "\"not mine\" is how the emitter finds the right provider without a registry mapping every "
                + "kind to an object first");

            var mine = new Block { Id = "b2", Kind = "sampleext.music.thrust" }
                .WithNumber("power", 3);
            Assert.That(provider.EmitExpression(EmitContext(), mine), Does.Contain("Thrust"));

            var text = new StringBuilder();
            var writer = new IndentedCodeWriter(text);
            writer.Open();
            provider.EmitStatement(EmitContext(), mine, writer);
            provider.EmitStatement(EmitContext(), mine, writer);
            Assert.That(text.ToString(), Does.Contain("    await ThrustAsync(3);"),
                "the writer indents what it is handed, and the depth is the provider's to respect");
        });
    }

    [Test]
    public async Task A_contributed_block_can_be_run_by_the_simulator_through_the_same_host_the_built_ins_use()
    {
        var provider = Music();
        var host = new SimulatedHost();

        var steps = new List<ExecutionStep>();
        await foreach (var step in provider.ExecuteAsync(
            new InterpretContext(host, DeclaredParameters: ["power"]),
            new Block { Id = "b2", Kind = "sampleext.music.thrust" },
            CancellationToken.None))
        {
            steps.Add(step);
        }

        Assert.Multiple(() =>
        {
            Assert.That(steps, Is.Not.Empty, "a block with no step cannot be previewed, which breaks the "
                + "promise that the canvas shows what will run — §23.2 makes all three members required for "
                + "exactly that reason");
            Assert.That(host.Log.Lines, Has.Some.Contains("thrust"));
            Assert.That(steps.Select(step => step.Kind), Has.Some.EqualTo(ExecutionStepKind.BlockEntered));
        });
    }

    [Test]
    public void The_writer_a_provider_is_handed_carries_the_indentation_and_nothing_else()
    {
        var text = new StringBuilder();

        var writer = new IndentedCodeWriter(text);
        writer.Line("outer();");
        writer.Open();
        writer.Line("inner();");

        Assert.Multiple(() =>
        {
            Assert.That(text.ToString(), Is.EqualTo(
                "outer();" + Environment.NewLine
                + "{" + Environment.NewLine
                + "    inner();" + Environment.NewLine));
            Assert.That(
                new IndentedCodeWriter(new StringBuilder()).Pad(),
                Is.Empty,
                "a depth-zero writer emits no leading spaces, so a contributed block cannot arrive with its "
                + "own idea of indentation");
        });
    }

    [Test]
    public void A_provider_that_declares_a_reserved_name_gets_a_different_one()
    {
        var locals = new VisualProviderLocals();

        Assert.Multiple(() =>
        {
            Assert.That(locals.Declaring("count"), Is.EqualTo("count"));
            Assert.That(locals.Declaring("count"), Is.Not.EqualTo("count"),
                "a provider cannot know what else the region declares, so its block must not fail to "
                + "compile because another block wanted the same word");
            Assert.That(locals.Has("count"), Is.True, "the first declaration still counts as declared");
        });
    }

    [Test]
    public void Discovery_skips_deckforge_s_own_assemblies_and_a_native_file()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisualBlockProviderLoader.FromDirectory(null), Is.Empty);
            Assert.That(VisualBlockProviderLoader.FromDirectory("no-such-directory"), Is.Empty);
            Assert.That(VisualBlockProviderLoader.FromAssembly("no-such-file.dll"), Is.Empty);
            Assert.That(
                VisualBlockProviderLoader.UserExtensionDirectory,
                Is.EqualTo(DeckForge.Core.Extensions.ExtensionService.UserExtensionDirectory),
                "one folder documented, not two: a user who found one and did not find the other would be "
                + "right to be annoyed");
        });
    }

    // ---- the fixture providers -----------------------------------------------------------------------

    /// <summary>Whether a name is a <c>SymbolRegular</c> member — the check Core cannot make for itself.</summary>
    private static bool Known(string glyph) => Enum.IsDefined(typeof(SymbolRegular), glyph);

    private static BlockEmitContext EmitContext() =>
        new(VisualSampleProject.Build(), "log-message", new VisualProviderLocals());

    /// <summary>A well-behaved provider: one namespaced block in a free category.</summary>
    private static SampleProvider Music() => new("sampleext", 100, [], [Thrust()], null);

    private static SampleProvider WithGlyph(string glyph) =>
        new("sampleext", 100, [SampleCategory("sampleext", BlockCategory.Media, "#9966FF", glyph)], [Thrust()], null);

    private static SampleProvider WithCategory(BlockCategory category) =>
        new("sampleext", 100, [SampleCategory("sampleext", category, "#9966FF", "PlayCircle24")], [Thrust()], null);

    private static SampleProvider Painted(string hue) =>
        new("sampleext", 100, [SampleCategory("sampleext", BlockCategory.Media, hue, "PlayCircle24")], [Thrust()], null);

    private static SampleProvider Provider(string id, int order) =>
        new(id, order, [],
            [new BlockDescriptor(
                id + ".thing.do",
                BlockCategory.Procedures,
                "do the thing",
                "Does the thing this extension adds.",
                BlockShape.Stack,
                new SdkMapping("string.Empty", "plain C#"),
                [new SlotDescriptor("what", SlotType.Any)])],
            null);

    /// <summary>A provider claiming a kind that is not namespaced by it.</summary>
    private static SampleProvider Rogue(string kind) => new("sampleext", 100, [],
        [new BlockDescriptor(
            kind,
            BlockCategory.Procedures,
            "rogue",
            "Tries to take an id it did not namespace.",
            BlockShape.Stack,
            new SdkMapping("string.Empty", "plain C#"))],
        null);

    /// <summary>A provider whose block breaks four of the rules the catalogue is held to.</summary>
    private static SampleProvider Rude(string kind) => new("sampleext", 100, [],
        [
            new BlockDescriptor(
                kind,
                BlockCategory.Procedures,
                "broken {nope}",
                "too short",
                BlockShape.Stack,
                new SdkMapping("string.Empty", string.Empty),
                null,
                [new MenuDescriptor("mode", new[] { "this", "that" }, "sideways")]),            new BlockDescriptor(
                kind + ".holey",
                BlockCategory.Procedures,
                "holey {count}",
                "Names a slot it never declares.",
                BlockShape.Stack,
                new SdkMapping("string.Empty", "plain C#")),
        ],
        null);

    private static BlockDescriptor Thrust() => new(
        "sampleext.music.thrust",
        BlockCategory.Media,
        "thrust {power}",
        "Shoves the deck one more than it wanted to go.",
        BlockShape.Stack,
        new SdkMapping("string.Empty", "IThrustSink.ThrustAsync"),
        [new SlotDescriptor("power", SlotType.Number, DefaultText: "3")]);

    private static BlockCategoryDescriptor SampleCategory(
        string id,
        BlockCategory category,
        string hue,
        string glyph) =>
        new(category, id + " " + category, hue, glyph, "Blocks this extension adds for " + category + ".");

    private sealed class SampleProvider : IVisualBlockProvider
    {
        private readonly IReadOnlyList<BlockCategoryDescriptor> _categories;
        private readonly IReadOnlyList<BlockDescriptor> _blocks;

        public SampleProvider(
            string id,
            int order,
            IReadOnlyList<BlockCategoryDescriptor> categories,
            IReadOnlyList<BlockDescriptor> blocks,
            IReadOnlyList<string>? parameters)
        {
            Id = id;
            Order = order;
            _categories = categories;
            _blocks = blocks;
            Parameters = parameters ?? [];
        }

        public string Id { get; }

        public int Order { get; }

        public IReadOnlyList<string> Parameters { get; }

        public IReadOnlyList<BlockCategoryDescriptor> Categories => _categories;

        public IReadOnlyList<BlockDescriptor> Blocks => _blocks;

        public string? EmitExpression(BlockEmitContext context, Block block) =>
            string.Equals(block.Kind, "sampleext.music.thrust", StringComparison.Ordinal)
                ? "Thrust(" + (block.InputNumber("power")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0") + ")"
                : null;

        public void EmitStatement(BlockEmitContext context, Block block, IndentedCodeWriter writer) =>
            writer.Line("await ThrustAsync(" + (block.InputNumber("power")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0") + ");");

        public async IAsyncEnumerable<ExecutionStep> ExecuteAsync(
            InterpretContext context,
            Block block,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new ExecutionStep(ExecutionStepKind.BlockEntered, block.Id, "thrust");

            var power = block.InputNumber("power") ?? 1;
            context.Host.Log.Write(
                NotificationLevel.Information,
                $"thrust {power.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            yield return new ExecutionStep(ExecutionStepKind.LogEmitted, block.Id, "thrust");
            yield return new ExecutionStep(ExecutionStepKind.BlockExited, block.Id, "thrust");

            await Task.CompletedTask;
        }
    }

    private sealed class ThrowingProvider : IVisualBlockProvider
    {
        public string Id => "throwing";

        public int Order => 0;

        // Throws on the second read, so `Load` sees a provider that declared itself and then failed — the
        // only shape in which "is it well-formed?" can itself throw.
        private int _reads;

        public IReadOnlyList<BlockCategoryDescriptor> Categories => _reads++ > 0 ? [] : [];

        public IReadOnlyList<BlockDescriptor> Blocks => _reads++ > 0
            ? throw new InvalidOperationException("the extension's own bug")
            : [];

        public string? EmitExpression(BlockEmitContext context, Block block) => null;

        public void EmitStatement(BlockEmitContext context, Block block, IndentedCodeWriter writer)
        {
        }

        public IAsyncEnumerable<ExecutionStep> ExecuteAsync(
            InterpretContext context,
            Block block,
            CancellationToken ct) => throw new InvalidOperationException("the extension's own bug");
    }
}