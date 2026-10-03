using DeckForge.CodeGen.Generation;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Part 8.5's three targets that did not exist: the widget node's handler, the setup flow's hooks and
/// the integration's lifecycle.
/// </summary>
/// <remarks>
/// The thing all four writers share is anchoring, and anchoring is the thing that goes wrong on the second
/// save rather than the first. So most of what is asserted here is that a second write replaces the
/// region instead of stacking another one beside it, and that a target with nowhere to go is refused
/// rather than written into something that happens to compile.
/// </remarks>
[TestFixture]
public sealed class VisualTargetWriterTests
{
    private const string WidgetProvider = """
        using MacroDeck.Sdk.Widgets;

        namespace Probe;

        public sealed class GaugeWidgetProvider : IWidgetTypeProvider
        {
            public WidgetTypeRegistration Register(IWidgetTypeProviderContext context) => new("gauge", (b, node) =>
            {
                b.Node("ui.text", new WidgetNodeOptions { Text = "Hello" }, node, new[]
                {
                    UiEventHandler.On("press", () =>
                    {
                        // TODO: react to the event.
                        _ = 0;
                    }),
                    UiEventHandler.On("change", () =>
                    {
                        _ = 0;
                    }),
                });
            });
        }
        """;

    private const string Integration = """
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.Actions;

        namespace Probe;

        public sealed class PluginIntegration : IPluginIntegration
        {
        	public PluginIntegration(ILogger logger)
        	{
        		Actions = [new LogMessageAction(logger)];
        	}

        	public IReadOnlyList<IActionDefinition> Actions { get; }

        	public Task InitializeAsync(IIntegrationContext context)
        	{
        		_logger.Information("Initialized.");
        		return Task.CompletedTask;
        	}

        	public Task ShutdownAsync() => Task.CompletedTask;
        }
        """;

    private const string ConfigFlowIntegration = """
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.ConfigFlow;

        namespace Probe;

        public sealed class PluginIntegration : IPluginIntegration
        {
            public IConfigFlow CreateConfigFlow() => new SetupFlow();

            private sealed class SetupFlow : IConfigFlow
            {
                public Task<ConfigFlowResult> StartAsync(
                    IConfigFlowContext context,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(ConfigFlowResult.Complete("Setup complete."));
                }
            }
        }
        """;

    [Test]
    public void Every_kind_resolves_an_anchor_and_an_unknown_id_does_not()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Anchor(TargetKind.Action, "ExecuteAsync"), Is.Not.Null);
            Assert.That(Anchor(TargetKind.Lifecycle, "InitializeAsync"), Is.Not.Null);
            Assert.That(Anchor(TargetKind.Lifecycle, "ShutdownAsync"), Is.Not.Null);
            Assert.That(Anchor(TargetKind.ConfigFlowHook, "StartAsync"), Is.Not.Null);
            Assert.That(Anchor(TargetKind.ConfigFlowHook, "SubmitAsync"), Is.Not.Null);
            Assert.That(Anchor(TargetKind.WidgetHandler, "press"), Is.Not.Null);

            Assert.That(
                Anchor(TargetKind.Lifecycle, "DisposeEverything"),
                Is.Null,
                "a lifecycle hook the SDK does not have has no anchor, and an anchor that is nearly right "
                + "writes the user's blocks into someone else's method - which still compiles");
            Assert.That(Anchor(TargetKind.WidgetHandler, "  "), Is.Null,
                "and a widget target with no event named is a target with no handler");
        });
    }

    [Test]
    public void The_anchor_for_a_widget_handler_stops_before_the_brace_so_the_splice_finds_the_lambda()
    {
        // The provider is one collection initialiser full of lambdas. An anchor that included the brace
        // would find the brace that opens the *next* node's handler, and the blocks would land in a
        // neighbouring event.
        var anchor = Anchor(TargetKind.WidgetHandler, "press")!;

        Assert.Multiple(() =>
        {
            Assert.That(anchor, Is.EqualTo("UiEventHandler.On(\"press\""));
            Assert.That(anchor, Does.Not.Contain("{"),
                "Splice looks for the first brace after the anchor itself, which is this lambda's");
        });
    }

    [Test]
    public void A_widget_handlers_blocks_land_in_that_events_lambda_and_not_in_its_neighbour()
    {
        var project = Project(TargetKind.WidgetHandler, "press");
        var result = VisualTargetWriter.Write(WidgetProvider, project, project.Targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(
                result.Content,
                Does.Contain("UiEventHandler.On(\"press\", () =>"),
                "the anchor's own line must survive, or the handler it names is gone");
            Assert.That(RegionOf(result.Content), Does.Contain("VisualRuntime.Set"),
                "the region belongs inside the lambda named by the anchor");
            Assert.That(
                Location(result.Content, "VisualRuntime.Set"),
                Is.LessThan(Location(result.Content, "UiEventHandler.On(\"change\"")),
                "and before the next handler, which is the only thing that distinguishes this anchor from "
                + "a prefix match");
        });
    }

    [Test]
    public void Saving_twice_replaces_the_region_rather_than_adding_one()
    {
        // The defect the action writer already had, and the reason this writer exists rather than a
        // second splicing path: a region appended to a region is a file that is right on the first save
        // and wrong on the second, which is the hardest kind of bug to notice.
        var project = Project(TargetKind.WidgetHandler, "press");
        var target = project.Targets[0];

        var once = VisualTargetWriter.Write(WidgetProvider, project, target);
        var twice = VisualTargetWriter.Write(once.Content, project, target);

        Assert.Multiple(() =>
        {
            Assert.That(twice.Success, Is.True, twice.Message);
            Assert.That(Count(twice.Content, "// <macrodeck-blocks>"), Is.EqualTo(1));
            Assert.That(Count(twice.Content, "// </macrodeck-blocks>"), Is.EqualTo(1));
            Assert.That(Count(twice.Content, "VisualRuntime.Set"), Is.EqualTo(1),
                "the second save duplicated the blocks instead of replacing them");
            Assert.That(twice.Content, Is.EqualTo(once.Content),
                "and a second save of an unchanged canvas is a byte-for-byte no-op, which is the property "
                + "that makes the file reviewable in git");
        });
    }

    [Test]
    public void A_lifecycle_hook_lands_inside_the_method_its_anchor_names()
    {
        var project = Project(TargetKind.Lifecycle, "InitializeAsync");
        var result = VisualTargetWriter.Write(Integration, project, project.Targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(RegionOf(result.Content), Does.Contain("VisualRuntime.Set"));

            // Before the stock return, so the method still returns and the plugin still builds. After it
            // would be CS0162, and the whole file would stop compiling on the first save.
            Assert.That(
                Location(result.Content, "VisualRuntime.Set"),
                Is.LessThan(Location(result.Content, "return Task.CompletedTask;")),
                "the region goes before the method's own return, or the blocks are unreachable code");
        });
    }

    [Test]
    public void A_setup_flows_blocks_land_inside_StartAsync()
    {
        var project = Project(TargetKind.ConfigFlowHook, "StartAsync");
        var result = VisualTargetWriter.Write(ConfigFlowIntegration, project, project.Targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(RegionOf(result.Content), Does.Contain("VisualRuntime.Set"));
            Assert.That(
                Location(result.Content, "VisualRuntime.Set"),
                Is.LessThan(Location(result.Content, "Task.FromResult(ConfigFlowResult.Complete")),
                "before the stock return, or the flow completes without running the setup");
        });
    }

    [Test]
    public void A_target_with_nowhere_to_go_is_refused_with_the_reason_rather_than_written_somewhere()
    {
        var project = Project(TargetKind.Lifecycle, "DisposeEverything");
        var result = VisualTargetWriter.Write(Integration, project, project.Targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Content, Is.EqualTo(Integration), "the file is untouched");
            Assert.That(result.Message, Does.Contain("DisposeEverything"),
                "the refusal names what is wrong, because the user has to be able to act on it");
        });
    }

    [Test]
    public void A_missing_anchor_in_a_file_that_has_been_edited_is_refused_rather_than_guessed_at()
    {
        var project = Project(TargetKind.Lifecycle, "InitializeAsync");
        var renamed = Integration.Replace("InitializeAsync", "Initialize");

        var result = VisualTargetWriter.Write(renamed, project, project.Targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Content, Is.EqualTo(renamed));
            Assert.That(result.Message, Does.Contain("InitializeAsync"),
                "and it quotes the anchor, so the user can see which line the editor is looking for");
        });
    }

    [Test]
    public void An_empty_target_is_refused_because_there_is_nothing_to_write()
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget
        {
            Id = "widget:press",
            Kind = TargetKind.WidgetHandler,
            AnchorId = "press",
            Name = "press",
        };
        project.Targets.Add(target);

        var result = VisualTargetWriter.Write(WidgetProvider, project, target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("no blocks"));
        });
    }

    [Test]
    public void A_target_that_calls_the_host_is_refused_when_the_plugin_has_no_integration()
    {
        // The action writer has this check and it is the reason a plugin without the capability produces
        // an explanation rather than a file that will not compile.
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget
        {
            Id = "lifecycle:InitializeAsync",
            Kind = TargetKind.Lifecycle,
            AnchorId = "InitializeAsync",
            Name = "InitializeAsync",
        };
        target.Scripts.Add(new VisualScript
        {
            Id = "s",
            Name = "init",
            Hat = Block("hat.plugin-initializes", "p1"),
            Body = [Block("deck.go-to-parent", "b1")],
        });
        project.Targets.Add(target);

        var result = VisualTargetWriter.Write(Integration, project, target, integrationAvailable: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("integration capability"),
                "and it says which page to go to, because " + "a refusal that only says no" + " is not an explanation");
        });
    }

    [Test]
    public void The_procedure_that_a_widget_handler_calls_is_hoisted_into_its_region_too()
    {
        var project = Project(TargetKind.WidgetHandler, "press");
        project.Procedures.Add(new ProcedureDeclaration
        {
            Id = "p-greet",
            Name = "greet",
            Parameters = [new ProcedureParameter { Name = "who", Type = "string" }],
        });

        var target = project.Targets[0];
        target.Scripts[0].Body.Add(Block("proc.call-with", "c1", block =>
        {
            block.Inputs["name"] = new BlockInput { Variable = "greet" };
            block.Inputs["args"] = new BlockInput { Text = "who=world" };
        }));

        var result = VisualTargetWriter.Write(WidgetProvider, project, target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);

            // A local function cannot outlive the method it is declared in, and a widget handler is a
            // lambda - so the procedure has to be inside the region or the file will not compile.
            Assert.That(RegionOf(result.Content), Does.Contain("GreetAsync").IgnoreCase,
                "a call the emitted code cannot resolve is a CS0103 on the user's first save");
        });
    }

    [Test]
    public void A_block_that_needs_an_action_context_is_refused_in_a_widget_handler_by_name()
    {
        // The SDK calls a widget node's event handler with no parameter, so `context` and `_logger` do not
        // exist there. Emitting such a block anyway produced CS0103 in a generated plugin - and the
        // refusal has to name the block, because "something needs a context" is not something a user can
        // act on.
        var project = Project(TargetKind.WidgetHandler, "press");
        project.Targets[0].Scripts[0].Body.Add(Block("ui.log", "b3", block =>
            block.Inputs["template"] = new BlockInput { Text = "hello" }));

        var result = VisualTargetWriter.Write(WidgetProvider, project, project.Targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Content, Is.EqualTo(WidgetProvider), "the provider is untouched");
            Assert.That(result.Message, Does.Contain("ui.log"), "the offending block is named");
            Assert.That(result.Message, Does.Contain("no context and no logger"),
                "and the reason is a fact about the SDK rather than a rule the user has to learn");
        });
    }

    [Test]
    public void The_anchor_ids_a_kind_offers_match_the_kinds_that_have_a_fixed_list()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                VisualTargetWriter.AnchorIdsFor(TargetKind.Lifecycle),
                Is.EqualTo(new[] { "InitializeAsync", "ShutdownAsync" }));
            Assert.That(
                VisualTargetWriter.AnchorIdsFor(TargetKind.ConfigFlowHook),
                Is.EqualTo(new[] { "StartAsync", "SubmitAsync" }));
            Assert.That(
                VisualTargetWriter.AnchorIdsFor(TargetKind.WidgetHandler),
                Is.Empty,
                "a widget's anchor ids are its node's event names, which come from the designer rather than "
                + "from a fixed list here");
        });
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static string? Anchor(TargetKind kind, string anchorId) =>
        VisualTargetWriter.AnchorFor(new VisualTarget { Kind = kind, AnchorId = anchorId });

    /// <summary>A project with one target of the given kind, whose script logs a line.</summary>
    private static VisualProject Project(TargetKind kind, string anchorId)
    {
        var hat = kind switch
        {
            TargetKind.WidgetHandler => "hat.widget-event",
            TargetKind.ConfigFlowHook => "hat.config-flow-step",
            TargetKind.Lifecycle => anchorId == "ShutdownAsync" ? "hat.plugin-shuts-down" : "hat.plugin-initializes",
            _ => "hat.action-runs",
        };

        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget
        {
            Id = $"{kind}:{anchorId}",
            Kind = kind,
            AnchorId = anchorId,
            Name = anchorId,
        };
        project.Targets.Add(target);

        target.Scripts.Add(new VisualScript
        {
            Id = $"script-{anchorId}",
            Name = anchorId,
            Hat = Block(hat, $"hat-{anchorId}"),
            // var.set and var.change, because a non-action target has no `context` and no `_logger`, and
            // the log block needs the logger. The refusal for the rest has its own test below.
            Body =
            [
                Block("var.set", "b1", block =>
                {
                    block.Inputs["var"] = new BlockInput { Variable = "a" };
                    block.Inputs["value"] = new BlockInput { Text = "1" };
                }),
                Block("var.change", "b2", block =>
                {
                    block.Inputs["var"] = new BlockInput { Variable = "a" };
                    block.Inputs["amount"] = new BlockInput { Number = 2 };
                }),
            ],
        });

        return project;
    }

    private static Block Block(string kind, string id, Action<Block>? setUp = null)
    {
        var block = new Block { Kind = kind, Id = id };
        setUp?.Invoke(block);

        return block;
    }

    private static string RegionOf(string content)
    {
        var begin = content.IndexOf("// <macrodeck-blocks>", StringComparison.Ordinal);
        var end = content.IndexOf("// </macrodeck-blocks>", StringComparison.Ordinal);

        Assert.That(begin, Is.GreaterThanOrEqualTo(0), "the region markers are missing from:\n" + content);
        Assert.That(end, Is.GreaterThan(begin));

        return content[begin..end];
    }

    private static int Location(string content, string needle)
    {
        var at = content.IndexOf(needle, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"'{needle}' is not in the written file:\n{content}");

        return at;
    }

    private static int Count(string content, string needle)
    {
        var count = 0;
        for (var at = content.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = content.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}