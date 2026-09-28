using DeckForge.CodeGen.Generation;
using DeckForge.Core.Blocks;
using DeckForge.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// The canvas save path, against a real generated project.
/// </summary>
/// <remarks>
/// The save used to be a WPF view model doing its own string surgery, so no test could reach it -
/// and it was broken in three ways that only showed up as a compile error in the user's plugin:
/// the region went in at a hardcoded eight-space indent, the executor was never made <c>async</c>,
/// and the action was never given a host context. A canvas containing a delay or a navigation step
/// therefore wrote code that did not compile, and the page reported success.
/// </remarks>
[TestFixture]
public sealed class BlockProgramWriterTests
{
    private static string _projectRoot = "";

    [OneTimeSetUp]
    public void GenerateProject()
    {
        var services = new ServiceCollection();
        var root = Path.Combine(Path.GetTempPath(), "blocks-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        _projectRoot = new PluginProjectGenerator(
            services.BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).Generate(new NewProjectOptions
        {
            PluginName = "Blocks",
            PluginId = "com.example.blocks",
            Publisher = "Example",
            ParentDirectory = root,
            ProjectName = "Blocks",
            InitGit = false,
        });
    }

    private string ActionFile => Path.Combine(_projectRoot, "src", "Blocks", "LogMessageAction.cs");

    private string ReadAction() => File.ReadAllText(ActionFile);

    private void WriteAction(string source) => File.WriteAllText(ActionFile, source);

    [Test]
    public void A_canvas_using_the_host_and_an_await_produces_source_that_declares_both()
    {
        WriteAction(ReadAction());
        var before = ReadAction();

        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            Statements =
            [
                new DelayBlock { Milliseconds = 25 },
                new GoBackBlock(),
                new NotifyBlock { Title = "Done", Message = "Finished." },
            ],
        };

        var result = BlockProgramWriter.Write(before, program);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Content, Does.Contain("async Task<ActionResult> ExecuteAsync"),
                "A delay puts an await in the body, so the executor has to be async.");
            Assert.That(result.Content, Does.Contain("IIntegrationContext"),
                "A host call needs a context the action is actually given.");
            Assert.That(BlockCompiler.BeginMarker, Is.Not.Null);
            Assert.That(result.Content, Does.Contain(BlockCompiler.BeginMarker));
        });
    }

    [Test]
    public void Saving_twice_replaces_the_region_rather_than_adding_a_second_one()
    {
        WriteAction(ReadAction());

        BlockProgramWriter.Write(ReadAction(), new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [new LogBlock { Template = "first" }],
        });

        var second = BlockProgramWriter.Write(ReadAction(), new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [new LogBlock { Template = "second" }],
        });

        Assert.That(second.Success, Is.True, second.Message);
        Assert.Multiple(() =>
        {
            Assert.That(CountOccurrences(second.Content, BlockCompiler.BeginMarker), Is.EqualTo(1),
                "A second region means the markers are nested and nothing can be replaced again.");
            Assert.That(second.Content, Does.Contain("second"));
            Assert.That(second.Content, Does.Not.Contain("\"first\""),
                "The previous save's blocks are still in the file.");
        });
    }

    [Test]
    public void An_empty_canvas_is_refused_rather_than_writing_an_empty_region()
    {
        var before = ReadAction();

        var result = BlockProgramWriter.Write(before, new BlockProgram { TargetActionId = "log-message" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Content, Is.EqualTo(before), "A refused write must not touch the file.");
            Assert.That(result.Message, Is.Not.Empty);
        });
    }

    [Test]
    public void An_action_with_no_executor_reports_the_anchor_it_looked_for()
    {
        // Patchable enough to get past the host-context step, so the missing anchor is what the
        // caller is told about. A message about the wrong step sends them looking in the wrong place.
        var before = string.Join(
            "\n",
            "namespace Blocks;",
            "",
            "using MacroDeck.Sdk;",
            "using Serilog;",
            "",
            "public sealed class Odd : IActionDefinition",
            "{",
            "    private readonly ILogger _logger;",
            "",
            "    public Odd(ILogger logger) => _logger = logger;",
            "",
            "    public IActionExecutor CreateExecutor() => new Executor(_logger);",
            "",
            "    private sealed class Executor : IActionExecutor",
            "    {",
            "        private readonly ILogger _logger;",
            "",
            "        public Executor(ILogger logger) => _logger = logger;",
            "    }",
            "}",
            "");

        var result = BlockProgramWriter.Write(before, new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [new LogBlock { Template = "x" }],
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Content, Is.EqualTo(before), "A refused write must not touch the file.");
            Assert.That(result.Message, Does.Contain(BlockProgramWriter.ExecutorAnchor),
                "The message has to name the anchor, or there is nothing to act on.");
        });
    }

    [Test]
    public void Every_block_kind_survives_the_round_trip_through_the_canvas()
    {
        // The canvas is persisted so a user can come back to it. A kind the serializer does not
        // know about would come back as a base BlockStatement, silently losing its configuration.
        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [.. BlockCompiler.Kinds.Select(kind => BlockCompiler.Create(kind.Id))],
        };

        var json = BlockProgramJson.Serialize(program);
        Assert.That(BlockProgramJson.TryDeserialize(json, out var restored), Is.True, json);

        Assert.That(restored!.Statements.Select(s => s.GetType()), Is.EqualTo(program.Statements.Select(s => s.GetType())));
    }

    [Test]
    public void The_canvas_file_name_is_derived_from_the_target_action()
    {
        var program = new BlockProgram { TargetActionId = "log-message" };

        var name = BlockProgramJson.FileNameFor(program);

        Assert.Multiple(() =>
        {
            Assert.That(name, Is.EqualTo("log-message.blocks.json"));
            Assert.That(name, Does.Not.Contain(Path.DirectorySeparatorChar),
                "The target id reaches the file name, so it has to be sanitised.");
        });
    }

    [Test]
    public void Saving_the_same_canvas_repeatedly_produces_the_same_file()
    {
        // Replacing a region used to keep the whitespace in front of the begin marker and indent the
        // replacement on top of it, so the first line of the region gained a level on every save.
        // Unbounded, and invisible until someone opened the file.
        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [new LogBlock { Template = "hello" }],
        };

        var first = BlockProgramWriter.Write(ReadAction(), program);
        Assert.That(first.Success, Is.True, first.Message);

        var second = BlockProgramWriter.Write(first.Content, program);
        var third = BlockProgramWriter.Write(second.Content, program);

        Assert.Multiple(() =>
        {
            Assert.That(second.Content, Is.EqualTo(first.Content), "A second save changed the file.");
            Assert.That(third.Content, Is.EqualTo(first.Content), "A third save changed the file again.");
        });
    }

    [Test]
    public void The_region_keeps_the_indentation_of_the_method_it_goes_into()
    {
        var first = BlockProgramWriter.Write(ReadAction(), new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [new LogBlock { Template = "hello" }],
        });

        // The stock example action is tab-indented, so the region has to be too. One level, in one
        // style - not a tab and four spaces on the same line.
        var lines = first.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var markerLine = lines.First(l => l.TrimStart().StartsWith(BlockCompiler.BeginMarker, StringComparison.Ordinal));
        var bodyLine = lines[Array.IndexOf(lines, markerLine) + 1];

        Assert.Multiple(() =>
        {
            Assert.That(markerLine, Does.StartWith("\t"), $"marker was [{markerLine.Replace("\t", "<TAB>")}]");
            Assert.That(bodyLine, Does.StartWith("\t"), $"body was [{bodyLine.Replace("\t", "<TAB>")}]");
        });
    }

    [Test]
    public void A_name_the_region_relies_on_cannot_be_shadowed_by_a_canvas_variable()
    {
        // The region reads `context`, `_logger` and `_integration`. A canvas variable called
        // `_integration` declares a local that shadows the field, so the null guard the compiler
        // emits is permanently false and every host call underneath it throws.
        var before = ReadAction();

        foreach (var reserved in BlockCompiler.ReservedNames)
        {
            var result = BlockProgramWriter.Write(before, new BlockProgram
            {
                TargetActionId = "log-message",
                Statements =
                [
                    new SetVariableBlock { VariableName = reserved, Literal = "", Type = "string" },
                    new NotifyBlock { Title = "Done", Message = "x" },
                ],
            });

            Assert.That(result.Success, Is.False, $"shadowing {reserved} was accepted");
            Assert.That(result.Message, Does.Contain(reserved));
        }
    }

    [Test]
    public void Two_blocks_wanting_the_same_variable_name_are_refused_rather_than_one_being_renamed()
    {
        // The compiler renames the second declaration, but an `If` refers to the variable by name,
        // so the conditional would keep reading the first one and test the wrong value.
        var result = BlockProgramWriter.Write(ReadAction(), new BlockProgram
        {
            TargetActionId = "log-message",
            Statements =
            [
                new HttpRequestBlock { Url = "https://example.com", IntoVariable = "payload" },
                new SetVariableBlock { VariableName = "payload", Literal = "", Type = "string" },
                new IfBlock { LeftVariable = "payload", Operator = "isEmpty", Then = [new LogBlock { Template = "x" }] },
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("payload"));
        });
    }

    [Test]
    public void A_declaration_inside_an_if_branch_is_checked_too()
    {
        // A branch is a nested scope, so a declaration there shadows the enclosing local, which is
        // CS0136 rather than CS0128 and just as uncompilable. Not reachable from the flat canvas UI,
        // so only an imported or hand-edited canvas can produce it.
        var result = BlockProgramWriter.Write(ReadAction(), new BlockProgram
        {
            TargetActionId = "log-message",
            Statements =
            [
                new IfBlock
                {
                    LeftVariable = "message",
                    Operator = "isNotEmpty",
                    Then = [new SetVariableBlock { VariableName = "message", Literal = "", Type = "string" }],
                },
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("message"));
        });
    }

    [Test]
    public void A_canvas_variable_may_shadow_a_class_member()
    {
        // The stock action has `public string Id`. A local is free to shadow a member, so refusing
        // this would be a false positive - and the earlier whole-file scan refused it.
        var result = BlockProgramWriter.Write(ReadAction(), new BlockProgram
        {
            TargetActionId = "log-message",
            Statements = [new SetVariableBlock { VariableName = "Id", Literal = "x", Type = "string" }],
        });

        Assert.That(result.Success, Is.True, result.Message);
    }

    [Test]
    public void Every_name_in_the_executor_is_reserved_not_just_the_first_of_each_list()
    {
        // The earlier pattern needed a `(` right before the type, so a second executor parameter was
        // missed and its name became a CS0128 in the user's plugin.
        var source = string.Join(
            "\n",
            "namespace Blocks;",
            "",
            "using System.Threading.Tasks;",
            "",
            "public sealed class Thing",
            "{",
            "    public Task<int> " + BlockProgramWriter.ExecutorAnchor + ", string extra, int more)",
            "    {",
            "        var first = context;",
            "        string? second = null;",
            "        double? third = null;",
            "        return Task.FromResult(1);",
            "    }",
            "}",
            "");

        var names = BlockProgramWriter.DeclaredNamesIn(source);

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("context"), "The executor's own parameter was not captured.");
            Assert.That(names, Does.Contain("extra"), "Only the first parameter was captured.");
            Assert.That(names, Does.Contain("more"), "Only the first parameter was captured.");
            Assert.That(names, Does.Contain("first"));
            Assert.That(names, Does.Contain("second"), "A nullable local was not captured.");
            Assert.That(names, Does.Contain("third"), "A nullable local was not captured.");
        });
    }

    [Test]
    public void A_calls_argument_names_are_not_treated_as_in_scope()
    {
        // `Helper(int a, int b)` puts a and b in the callee's scope, not the caller's. Reserving them
        // refused saves the user could legitimately make.
        var source = string.Join(
            "\n",
            "namespace Blocks;",
            "",
            "using System.Threading.Tasks;",
            "",
            "public sealed class Thing",
            "{",
            "    public Task<int> " + BlockProgramWriter.ExecutorAnchor + ")",
            "    {",
            "        Helper(alpha, beta);",
            "        return Task.FromResult(1);",
            "    }",
            "",
            "    private void Helper(int a, int b) { }",
            "}",
            "");

        var names = BlockProgramWriter.DeclaredNamesIn(source);

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("context"));
            Assert.That(names, Does.Not.Contain("Task"), "A type name leaked in from a call.");
            Assert.That(names, Does.Not.Contain("a"));
            Assert.That(names, Does.Not.Contain("b"));
        });
    }

    [Test]
    public void A_variable_that_collides_with_the_hosts_own_local_is_refused_rather_than_renamed()
    {
        // The stock example action declares `var message`. Renaming the canvas's declaration would
        // leave every block that reads it still referring to `message` - which would resolve to the
        // host's local, so the conditional would silently test the wrong value.
        var before = ReadAction();
        Assert.That(BlockProgramWriter.DeclaredNamesIn(before), Does.Contain("message"),
            "The collision has to be found in the action's own locals for this test to mean anything.");

        var result = BlockProgramWriter.Write(before, new BlockProgram
        {
            TargetActionId = "log-message",
            Statements =
            [
                new SetVariableBlock { VariableName = "message", FromParameter = "message", Type = "string" },
                new IfBlock { LeftVariable = "message", Operator = "isNotEmpty", Then = [new LogBlock { Template = "x" }] },
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Content, Is.EqualTo(before), "A refused write must not touch the file.");
            Assert.That(result.Message, Does.Contain("message"),
                "The message has to name the variable, or the user cannot act on it.");
        });
    }

    [Test]
    public void A_canvas_covering_every_block_kind_writes_a_plugin_that_compiles()
    {
        if (Environment.GetEnvironmentVariable("DECKFORGE_SKIP_SLOW_TESTS") == "1")
        {
            Assert.Ignore("DECKFORGE_SKIP_SLOW_TESTS=1");
        }

        // The assertion that matters. Every earlier test in this fixture checks the source the
        // writer produced; this one builds it, against the real SDK. A canvas that names a field
        // nobody declared, or drops an await into a method that cannot hold one, only fails here.
        var sandbox = Path.Combine(Path.GetTempPath(), "blocks-build-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);

        var root = new PluginProjectGenerator(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<PluginProjectGenerator>.Instance).Generate(Options() with { ParentDirectory = sandbox });

        var actionFile = Path.Combine(root, "src", "Blocks", "LogMessageAction.cs");

        // One of every kind, so the region exercises the whole compiler surface at once. The
        // variable is named `text` rather than `message` because the stock example action already
        // declares a local called `message`, and a canvas that collides is refused by design.
        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            Statements =
            [
                new LogBlock { Template = "start" },
                new SetVariableBlock { VariableName = "text", FromParameter = "message", Type = "string" },
                new IfBlock
                {
                    LeftVariable = "text",
                    Operator = "contains",
                    RightLiteral = "a",
                    Then = [new LogBlock { Template = "has a" }],
                },
                new DelayBlock { Milliseconds = 5 },
                new HttpRequestBlock { Url = "https://example.com", IntoVariable = "response" },
                new NotifyBlock { Title = "Done", Message = "Finished." },
                new NavigateBlock { FolderId = "some-folder" },
                new GoToParentBlock(),
                new GoBackBlock(),
                new ChangeProfileBlock { ProfileId = "some-profile" },
                new RunScriptBlock { ScriptId = "some-script" },
                new PublishEventBlock { EventId = "some-event" },
                new ReadVariableBlock { VariableName = "shared" },
                new SetVariableValueBlock { VariableName = "shared", Value = "v" },
                new ShowModalBlock { ViewId = "some.view" },
                new InvalidateIconBlock { ActionId = "log-message" },
                new ThrowBlock { Message = "no" },
                new ReturnResultBlock { Outcome = "success" },
            ],
        };

        var written = BlockProgramWriter.Write(File.ReadAllText(actionFile), program);
        Assert.That(written.Success, Is.True, written.Message);
        File.WriteAllText(actionFile, written.Content);

        var slnx = Directory.GetFiles(root, "*.slnx").First();
        var output = RunBuild(Path.GetDirectoryName(slnx)!);

        Assert.That(output, Does.StartWith("exit=0"), "dotnet build failed:" + Environment.NewLine + output);

        var diagnostics = output
            .Split('\n')
            .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                        || l.Contains(": warning MDP", StringComparison.OrdinalIgnoreCase)
                        || l.Contains(": warning MDLOC", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .ToList();

        Assert.That(diagnostics, Is.Empty, "The written plugin did not compile cleanly:" + Environment.NewLine
            + string.Join(Environment.NewLine, diagnostics));
    }

    private static NewProjectOptions Options() => new()
    {
        PluginName = "Blocks",
        PluginId = "com.example.blocks",
        Publisher = "Example",
        ParentDirectory = Path.GetTempPath(),
        ProjectName = "Blocks",
        InitGit = false,
    };

    private static string RunBuild(string workingDirectory)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
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

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(600_000);

        return $"exit={process.ExitCode}{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}";
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
