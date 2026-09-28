using System.Diagnostics;
using System.Text;
using DeckForge.CodeGen.Generation;
using Microsoft.Extensions.DependencyInjection;
using DeckForge.Core.Blocks;
using DeckForge.Core.Code;
using DeckForge.Core.Plugins;
using DeckForge.Core.Widgets;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Compiles generated output against the real Macro Deck SDK.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that matters. The three editors that generate code each had a defect that
/// only the C# compiler could see: an action parameter with no name, a <c>switch</c> arm with no
/// <c>switch</c>, and a call to an <c>IDeckNavigator.OpenFolder</c> that does not exist. Every
/// one of them was invisible to a string assertion, and all three shipped.
/// </para>
/// <para>
/// So the assertions here are not about strings. They write a whole plugin project, run
/// <c>dotnet build</c> against <c>MacroDeck.Sdk 3.0.0-beta.14</c>, and fail on any CS diagnostic.
/// Set <c>DECKFORGE_SKIP_SLOW_TESTS=1</c> to skip, which is what a watch loop wants.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class GeneratedCodeCompilesTests
{
    private static bool Skip => Environment.GetEnvironmentVariable("DECKFORGE_SKIP_SLOW_TESTS") == "1";

    private static string Sandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "deckforge-compile-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Writes a stock project plus the given extra files, then builds it.</summary>
    private static (string Root, string Output) WriteAndBuild(
        NewProjectOptions options,
        IReadOnlyList<(string RelativePath, string Content)> extras)
    {
        var sandbox = Sandbox();
        var scoped = options with { ParentDirectory = sandbox };
        var services = new ServiceCollection().BuildServiceProvider();
        var generator = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance);
        var root = generator.Generate(scoped);

        foreach (var (relative, content) in extras)
        {
            var path = Path.Combine(root, ProjectContentBuilder.ToOsPath(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        var slnx = Directory.GetFiles(root, "*.slnx").First();
        return (root, RunBuild(Path.GetDirectoryName(slnx)!));
    }

    private static string RunBuild(string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in new[] { "build", "--nologo", "-v", "q", "-clp:NoSummary" })
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(600_000);

        return $"exit={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}";
    }

    private static void AssertNoCompilerErrors(string output)
    {
        var errors = output
            .Split('\n')
            .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                        || l.Contains(": warning MDP", StringComparison.OrdinalIgnoreCase)
                        || l.Contains(": warning MDLOC", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .ToList();

        Assert.That(errors, Is.Empty, "Generated code did not compile cleanly:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors));
    }

    /// <summary>
    /// Options for a throwaway project. The id and project name carry a per-test suffix so a
    /// rerun never collides with a previous run's directory in TEMP.
    /// </summary>
    private static NewProjectOptions Options(string? name = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var pluginName = name ?? "Compile Probe";
        return new NewProjectOptions
        {
            PluginName = pluginName,
            PluginId = "com.example.probe-" + suffix,
            Publisher = "Example",
            Description = "Compiles every generated surface.",
            ParentDirectory = Path.GetTempPath(),
            ProjectName = "Probe" + suffix,
            Platforms = ["win-x64"],
        };
    }

    // ---------------------------------------------------------------- stock template

    [Test]
    public void Stock_project_compiles_against_the_real_sdk()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var (_, output) = WriteAndBuild(Options(), []);
        AssertNoCompilerErrors(output);
    }

    // ---------------------------------------------------------------- actions

    /// <summary>
    /// One action carrying a parameter of every editor type the SDK declares. This is the test
    /// that was impossible to write before, because the emitters lived in the WPF project.
    /// </summary>
    [Test]
    public void Every_action_editor_type_compiles()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var parameters = new List<ActionParameterSpec>();
        foreach (var type in ActionParameterTypes.All)
        {
            parameters.Add(ExtremeFor(type));
        }

        var design = new ActionDesign
        {
            ActionId = "kitchen-sink",
            ActionName = "Kitchen sink",
            ActionDescription = "One parameter of every editor type.",
            Parameters = parameters,
        };

        var options = Options();
        var source = ActionGenerator.Render(design, options.ProjectName!);
        var (_, output) = WriteAndBuild(options, [($"{design.ClassName}.cs", source)]);
        AssertNoCompilerErrors(output);
    }

    /// <summary>Each editor type, filled with values that exercise its optional arguments.</summary>
    private static ActionParameterSpec ExtremeFor(ActionParameterTypeInfo type)
    {
        // Parameter names are persisted wire ids, so they are kebab-case - which is what the
        // SDK's own MDP1002 analyzer requires.
        var wireName = Kebab(type.Name);

        var baseSpec = new ActionParameterSpec
        {
            Name = wireName,
            EditorType = type.Name,
            Label = $"{type.Name} label",
            Description = $"{type.Name} description",
            Required = true,
            DefaultValue = type.WireType switch
            {
                "Boolean" => "true",
                "Number" => "7",
                _ => "sample",
            },
        };

        return baseSpec with
        {
            Placeholder = type.SupportsPlaceholder ? "sample placeholder" : baseSpec.Placeholder,
            Min = type.SupportsRange ? 1 : null,
            Max = type.SupportsRange ? 10 : null,
            Step = type.SupportsRange ? 0.5 : null,
            Options = type.SupportsOptions
                ? [new ParameterOption("first", "First"), new ParameterOption("second", "Second")]
                : [],
            OptionsSourceId = type.SupportsOptionsSourceId ? "sample-source" : baseSpec.OptionsSourceId,
            FileExtensions = type.SupportsFileExtensions ? [".png", ".jpg"] : [],
            Language = type.SupportsLanguage ? "csharp" : baseSpec.Language,
            ValidationRegex = type.SupportsValidationRegex ? "^[a-z]+$" : baseSpec.ValidationRegex,
            MaxLength = type.SupportsMaxLength ? 64 : null,
            AutoPrefixHttps = type.SupportsAutoPrefixHttps,
            SupportsReset = type.SupportsSupportsReset,
            LiteralOnly = type.SupportsLiteralOnly,
            AllowSelf = false,
            WidgetTypes = type.SupportsWidgetTargetOptions ? ["gauge"] : [],
            Children = type.SupportsChildren
                ? [new ActionParameterSpec { Name = "child", Label = "Child" }]
                : [],
            ItemTemplate = type.SupportsItemTemplate
                ? new ActionParameterSpec { Name = "item", Label = "Item" }
                : null,
        };
    }

    private static string Kebab(string pascal)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    [Test]
    public void Action_with_hostile_parameter_names_still_compiles()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        // Values chosen to break escaping, sanitisation and the resx key rules.
        var design = new ActionDesign
        {
            ActionId = "set-light",
            ActionName = "Set \"light\" \\ level\ttab",
            ActionDescription = "A \"description\" with a \n newline and a \\ backslash.",
            Parameters =
            [
                new ActionParameterSpec
                {
                    Name = "name",
                    EditorType = "Text",
                    Label = "Label with \"quotes\" and <angle> & ampersand",
                    Description = "Description with {braces} and \"quotes\"",
                    Placeholder = "Placeholder \"quoted\"",
                    Required = true,
                },
                new ActionParameterSpec
                {
                    Name = "level",
                    EditorType = "Number",
                    Label = "Level",
                    Min = 0,
                    Max = 100,
                    Step = 5,
                    DefaultValue = "50",
                },
            ],
        };

        var options = Options();
        var source = ActionGenerator.Render(design, options.ProjectName!);

        // Every string the generated code references must actually be produced, or the
        // generated Strings class has no member for it.
        var strings = ActionGenerator.BuildStrings(design);
        foreach (System.Text.RegularExpressions.Match reference in
                 System.Text.RegularExpressions.Regex.Matches(source, @"Strings\.([A-Za-z0-9_.]+)\(\)"))
        {
            Assert.That(strings.Keys, Contains.Item(reference.Groups[1].Value),
                $"The generated code reads Strings.{reference.Groups[1].Value}() but no resx entry produces it.");
        }

        var (_, output) = WriteAndBuild(options, [($"{design.ClassName}.cs", source)]);
        AssertNoCompilerErrors(output);
    }

    [Test]
    public void WidgetTarget_without_explicit_required_does_not_emit_a_required_parameter()
    {
        var call = ActionParameterFactory.Emit(
            new ActionParameterSpec { Name = "target", EditorType = "WidgetTarget", Label = "Target" },
            p => $"Strings.X.{p}()");
        Assert.That(call, Does.Contain("required: false"), call);
    }

    [Test]
    public void Duration_uses_defaultMilliseconds_and_never_defaultValue()
    {
        var call = ActionParameterFactory.Emit(
            new ActionParameterSpec
            {
                Name = "timeout", EditorType = "Duration", Label = "Timeout", DefaultValue = "5000", Required = true,
            },
            p => $"Strings.X.{p}()");

        Assert.That(call, Does.Contain("defaultMilliseconds: 5000"), call);
        Assert.That(call, Does.Not.Contain("defaultValue:"), call);
    }

    [Test]
    public void Every_emitted_action_parameter_starts_with_its_name()
    {
        foreach (var type in ActionParameterTypes.All)
        {
            var call = ActionParameterFactory.Emit(
                ExtremeFor(type),
                p => $"Strings.X.{p}()");

            Assert.That(
                call,
                Does.StartWith($"ActionParameter.{type.Name}(\""),
                $"{type.Name} did not emit its name first: {call}");
        }
    }

    // ---------------------------------------------------------------- blocks

    [Test]
    public void Every_block_kind_compiles_inside_an_executor()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            Statements =
            [
                new LogBlock { Template = "Ran \"fast\"\nsecond line", Level = "Warning" },
                new LogBlock { Template = "With {message} and {count}", Parameter = "extra" },
                new SetVariableBlock { VariableName = "class", FromParameter = "message", Type = "string", Required = true },
                new SetVariableBlock { VariableName = "count", FromParameter = "count", Type = "number" },
                new SetVariableBlock { VariableName = "flag", Literal = "true", Type = "bool" },
                new SetVariableBlock { VariableName = "twice", FromParameter = "message", Type = "string" },
                new SetVariableBlock { VariableName = "again", FromParameter = "count", Type = "number" },
                new IfBlock
                {
                    LeftVariable = "class",
                    Operator = ">",
                    RightLiteral = "b",
                    Then = [new ReturnResultBlock { Outcome = "success" }],
                    Else = [new ReturnResultBlock { Outcome = "failed", ErrorCode = "InvalidParameter", Message = "nope" }],
                },
                new IfBlock { LeftVariable = "count", Operator = "isEmpty", Then = [new LogBlock { Template = "empty" }] },
                new IfBlock { LeftVariable = "count", Operator = "isNotEmpty", Then = [new LogBlock { Template = "set" }] },
                new IfBlock { LeftVariable = "class", Operator = "contains", RightLiteral = "a", Then = [new LogBlock { Template = "has a" }] },
                new DelayBlock { Milliseconds = 25 },
                new HttpRequestBlock { Url = "https://example.com/api", IntoVariable = "response", BearerTokenParameter = "token" },
                new NotifyBlock { Title = "Done", Message = "Finished", Level = "Warning", Key = "probe" },
                new NavigateBlock { FolderId = "folder-1" },
                new GoToParentBlock(),
                new GoBackBlock(),
                new ChangeProfileBlock { ProfileId = "profile-1" },
                new RunScriptBlock { ScriptId = "script-1", Inputs = "a=message\nb=count" },
                new PublishEventBlock { EventId = "did-a-thing", Payload = "name=message" },
                new ReadVariableBlock { VariableName = "counter", IntoVariable = "current" },
                new SetVariableValueBlock { VariableName = "counter", Value = "count", UseParameter = true },
                new SetVariableValueBlock { VariableName = "counter", Value = "literal-value" },
                new ShowModalBlock { ViewId = "confirm-dialog", Title = "Sure?", Data = "subject=message" },
                new InvalidateIconBlock { ActionId = "set-light" },
                new ReturnResultBlock { Outcome = "accepted", Message = "pending" },
            ],
        };

        var compiled = BlockCompiler.Compile(program);
        var services = new ServiceCollection().BuildServiceProvider();
        var generator = new PluginProjectGenerator(services, NullLogger<PluginProjectGenerator>.Instance);
        var options = Options();
        var root = generator.Generate(options);

        var actionPath = Path.Combine(root, "src", options.ProjectName!, "LogMessageAction.cs");
        var actionSource = File.ReadAllText(actionPath);

        // The block region needs a captured IIntegrationContext, which the stock example action
        // does not have. The production helper adds the plumbing in the file's own indentation.
        var wired = BlockCompiler.EnsureIntegrationContext(actionSource);
        Assert.That(wired.Success, Is.True, wired.Message);
        actionSource = wired.Content;
        Assert.That(actionSource, Does.Contain("_integration"), "The integration field was not added.");

        var spliced = BlockCompiler.Splice(actionSource, compiled, "public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)")
            ?? throw new InvalidOperationException("Could not splice the block region.");
        var asynced = BlockCompiler.EnsureAsyncExecutor(spliced);

        // The region uses types from several namespaces.
        asynced = asynced.Replace(
            "using MacroDeck.Localization;",
            "using MacroDeck.Localization;\nusing MacroDeck.Sdk.Notifications;\nusing MacroDeck.Sdk.Ui;",
            StringComparison.Ordinal);

        File.WriteAllText(actionPath, asynced, new UTF8Encoding(false));

        var output = RunBuild(root);
        AssertNoCompilerErrors(output);
    }

    [Test]
    public void Block_program_round_trips_through_json()
    {
        var program = new BlockProgram
        {
            TargetActionId = "set-light",
            Statements =
            [
                new LogBlock { Template = "hello {name}", Level = "Debug" },
                new IfBlock
                {
                    LeftVariable = "x", Operator = "isNotEmpty",
                    Then = [new DelayBlock { Milliseconds = 10 }],
                    Else = [new ReturnResultBlock { Outcome = "failed", ErrorCode = "NotFound" }],
                },
                new NotifyBlock { Title = "t", Level = "Error" },
            ],
        };

        var json = BlockProgramJson.Serialize(program);
        Assert.That(BlockProgramJson.TryDeserialize(json, out var restored), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.TargetActionId, Is.EqualTo("set-light"));
            Assert.That(restored.Statements, Has.Count.EqualTo(3));
            Assert.That(restored.Statements[0], Is.TypeOf<LogBlock>());
            Assert.That(((LogBlock)restored.Statements[0]).Template, Is.EqualTo("hello {name}"));
            Assert.That(((LogBlock)restored.Statements[0]).Level, Is.EqualTo("Debug"));
            Assert.That(restored.Statements[1], Is.TypeOf<IfBlock>());
            Assert.That(((IfBlock)restored.Statements[1]).Else, Has.Count.EqualTo(1));
            Assert.That(((IfBlock)restored.Statements[1]).Then[0], Is.TypeOf<DelayBlock>());
        });
    }

    [Test]
    public void Every_block_kind_has_a_factory_and_round_trips_through_json()
    {
        foreach (var kind in BlockCompiler.Kinds)
        {
            Assert.That(BlockCompiler.FindKind(kind.Id), Is.Not.Null, kind.Id);

            var block = BlockCompiler.Create(kind.Id);
            Assert.DoesNotThrow(() => BlockCompiler.CompileBody(new BlockProgram { Statements = [block] }), kind.Id);

            // The kind discriminator must survive a round trip, or a saved canvas cannot be read back.
            var json = BlockProgramJson.Serialize(new BlockProgram { Statements = [block] });
            Assert.That(json, Does.Contain($"\"kind\": \"{kind.Id}\""), kind.Id);
            Assert.That(BlockProgramJson.TryDeserialize(json, out var restored), Is.True, kind.Id);
            Assert.That(restored.Statements, Has.Count.EqualTo(1), kind.Id);
            Assert.That(restored.Statements[0].GetType(), Is.EqualTo(block.GetType()), kind.Id);
        }
    }

    // ---------------------------------------------------------------- widgets

    /// <summary>
    /// A widget view using every node type in the catalog, several levels deep, with events.
    /// </summary>
    [Test]
    public void Every_ui_node_type_compiles()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var design = new WidgetDesign
        {
            WidgetTypeId = "kitchen-sink",
            WidgetName = "Kitchen sink",
            WidgetDescription = "Every node type.",
            HasConfiguration = true,
            SupportsFlows = true,
            Nodes = [],
            States = [new DesignedState { Name = "on", Background = "#4F8CFF", Text = "On" }],
            SchemaProperties = [new DesignedSchemaProperty { Name = "value", Required = true, Title = "Value" }],
        };

        // A stack root, then every type the catalog knows, each given whatever properties it has.
        var root = new DesignedNode { Key = "root", NodeType = "ui.stack", ParentKey = null };
        design.Nodes.Add(root);

        var index = 0;
        foreach (var type in UiNodeCatalog.All.Where(t => t.WireType != "ui.stack"))
        {
            index++;
            var node = new DesignedNode
            {
                Key = "n" + index,
                NodeType = type.WireType,
                ParentKey = "root",
                Text = "Sample",
                Size = 0.1,
                Background = "#222222",
            };

            // Set every property the type declares, so a mistranscribed name is caught.
            foreach (var property in type.Properties)
            {
                node.Properties[property.Name] = property.Kind switch
                {
                    UiPropertyKind.Flag => "true",
                    UiPropertyKind.Number or UiPropertyKind.ValueNumber => "1",
                    _ => property.Name switch
                    {
                        "points" => "0.1,0.5,0.9",
                        "shape" => "rounded-rect",
                        "direction" => "column",
                        "weight" => "semibold",
                        "interaction" => "drag",
                        "transition" => "fade",
                        "clip" => "rect",
                        "format" => "time",
                        "icon" => "Play",
                        _ => "sample",
                    },
                };
            }

            if (type.Events.Count > 0)
            {
                node.Events.Add(new DesignedEvent { Name = type.Events[0], Body = "_logger.Information(\"handled\");" });
            }

            design.Nodes.Add(node);
            root.Children.Add(node);
        }

        var options = Options();
        var source = WidgetGenerator.Render(design, options.ProjectName!);
        var (_, output) = WriteAndBuild(options, [($"{design.ClassName}.cs", source)]);
        AssertNoCompilerErrors(output);
    }

    [Test]
    public void Several_top_level_nodes_are_wrapped_rather_than_dropped()
    {
        if (Skip)
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        var design = new WidgetDesign
        {
            WidgetTypeId = "multi-root",
            WidgetName = "Multi root",
            HasConfiguration = false,
            Nodes =
            [
                new DesignedNode { Key = "a", NodeType = "ui.stack" },
                new DesignedNode { Key = "b", NodeType = "ui.stack" },
                new DesignedNode { Key = "c", NodeType = "ui.stack" },
            ],
        };

        var source = WidgetGenerator.Render(design, "Probe");

        // The previous BuildTree assigned root = element per parentless node, so only the last
        // survived. Every one must appear.
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("\"a\""));
            Assert.That(source, Does.Contain("\"b\""));
            Assert.That(source, Does.Contain("\"c\""));
            Assert.That(source, Does.Contain("Children = rootChildren"));
        });
    }

    [Test]
    public void A_configuration_surface_without_a_schema_is_refused()
    {
        var design = new WidgetDesign
        {
            WidgetTypeId = "no-schema",
            WidgetName = "No schema",
            HasConfiguration = true,
            Nodes = [new DesignedNode { Key = "root", NodeType = "ui.stack" }],
        };

        Assert.That(
            design.Validate().Any(p => p.Contains("schema", StringComparison.OrdinalIgnoreCase)),
            Is.True,
            "The host refuses a configuration surface with no schema, so the generator must too.");
    }

    [Test]
    public void A_node_parented_to_a_leaf_is_refused()
    {
        var design = new WidgetDesign
        {
            WidgetTypeId = "bad-tree",
            WidgetName = "Bad tree",
            HasConfiguration = false,
            Nodes =
            [
                new DesignedNode { Key = "leaf", NodeType = "ui.text" },
                new DesignedNode { Key = "child", NodeType = "ui.text", ParentKey = "leaf" },
            ],
        };

        Assert.That(design.Validate().Any(p => p.Contains("cannot contain", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void The_catalog_holds_exactly_the_twenty_four_component_types()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiNodeCatalog.All, Has.Count.EqualTo(24));
            Assert.That(UiNodeCatalog.All.Count(t => t.WireType.StartsWith("ui.", StringComparison.Ordinal)), Is.EqualTo(20));
            Assert.That(UiNodeCatalog.All.Count(t => t.WireType.StartsWith("macrodeck.", StringComparison.Ordinal)), Is.EqualTo(4));
            Assert.That(UiNodeCatalog.All.Select(t => t.WireType).Distinct().Count(), Is.EqualTo(24));
            Assert.That(UiNodeCatalog.All.Select(t => t.ClassName).Distinct().Count(), Is.EqualTo(24));
        });
    }

    [Test]
    public void Every_catalog_property_name_is_unique_within_its_type()
    {
        foreach (var type in UiNodeCatalog.All)
        {
            var duplicates = type.Properties
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.That(duplicates, Is.Empty, $"{type.WireType}: {string.Join(", ", duplicates)}");
        }
    }

    [Test]
    public void A_widget_configuration_always_emits_a_data_schema()
    {
        var design = new WidgetDesign
        {
            WidgetTypeId = "with-schema",
            WidgetName = "With schema",
            HasConfiguration = true,
            Nodes = [new DesignedNode { Key = "root", NodeType = "ui.stack" }],
            SchemaProperties =
            [
                new DesignedSchemaProperty { Name = "level", SchemaType = "number", Title = "Level", Required = true, DefaultValue = "0" },
            ],
        };

        var source = WidgetGenerator.Render(design, "Probe");
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DataSchema:"));
            Assert.That(source, Does.Contain("HasConfiguration: true"));
            // The schema is a C# string literal, so its quotes are escaped in the source.
            Assert.That(source, Does.Contain("\\\"" + "type\\\": \\\"object\\\""));
            Assert.That(source, Does.Contain("IReadOnlyList<UiSurfaceDeclaration> Surfaces"));
        });
    }

    [Test]
    public void Unknown_error_code_falls_back_instead_of_emitting_a_missing_member()
    {
        var compiled = BlockCompiler.CompileBody(new BlockProgram
        {
            Statements = [new ReturnResultBlock { Outcome = "failed", ErrorCode = "NotARealCode", Message = "x" }],
        });

        Assert.That(compiled, Does.Contain("ActionErrorCodes.ProviderError"), compiled);
        Assert.That(compiled, Does.Not.Contain("NotARealCode"), compiled);
    }
}
