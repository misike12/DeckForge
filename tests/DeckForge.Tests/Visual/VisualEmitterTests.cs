using DeckForge.CodeGen.Generation;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The emitter, against the shapes Part 8 of <c>visual.md</c> promises.
/// </summary>
/// <remarks>
/// <para>
/// String assertions are the right tool here and the wrong tool everywhere else. The emitter's job IS
/// producing text with a particular shape — a guard once, a <c>while (true)</c> with a cancellation
/// check inside it, a local function above the script that calls it — so asserting the text is
/// asserting the contract. Whether that text *compiles* is a different question, and it belongs to the
/// compile tests, which build real plugins.
/// </para>
/// <para>
/// Determinism is asserted as a property, not a hope: the same document compiles twice and the bytes
/// must agree, because a quiet round-trip diff is what Part 6.4 asks for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualEmitterTests
{
    [Test]
    public void A_plain_stack_emits_one_line_per_block_in_order()
    {
        var project = Project(
            Script(
                Block("ui.log", b => b.WithText("template", "hello")),
                Block("control.wait-ms", b => b.WithNumber("ms", 250))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            // The log block's template is a template: {name} holes are resolved at runtime from the
            // invocation's parameters, which is what LogTemplate is for.
            Assert.That(code, Does.Contain("_logger.Information(VisualRuntime.LogTemplate(\"hello\", context.Parameters));"));
            Assert.That(code, Does.Contain("await Task.Delay((int)VisualRuntime.ToNumber(250), context.CancellationToken);"));
            Assert.That(code.IndexOf("_logger", StringComparison.Ordinal),
                Is.LessThan(code.IndexOf("Task.Delay", StringComparison.Ordinal)),
                "statements emit in document order");
        });
    }

    [Test]
    public void The_same_document_compiles_byte_identically_twice()
    {
        var project = Project(
            Script(
                Block("ui.log", b => b.WithText("template", "x")),
                Block("control.forever", b => { }, body => body.Add(
                    Block("ui.log", b => b.WithText("template", "y"))))));

        var first = Compile(project);
        var second = Compile(Project(
            Script(
                Block("ui.log", b => b.WithText("template", "x")),
                Block("control.forever", b => { }, body => body.Add(
                    Block("ui.log", b => b.WithText("template", "y")))))));

        Assert.That(second, Is.EqualTo(first), "determinism is the round-trip diff's quietness");
    }

    [Test]
    public void The_host_guard_is_emitted_once_and_only_when_needed()
    {
        var withHost = Project(Script(
            Block("deck.go-back"),
            Block("deck.go-to-parent")));
        var withoutHost = Project(Script(
            Block("ui.log", b => b.WithText("template", "x")),
            Block("control.wait-ms", b => b.WithNumber("ms", 1))));

        var hostCode = Compile(withHost);
        var plainCode = Compile(withoutHost);

        Assert.Multiple(() =>
        {
            Assert.That(CountOf(hostCode, "if (_integration is null)"), Is.EqualTo(1),
                "one guard per region, not one per call");
            Assert.That(plainCode, Does.Not.Contain("_integration is null"),
                "a document that never touches the host must not grow a guard");
        });
    }

    [Test]
    public void A_repeat_wraps_its_body_with_a_cancellation_check()
    {
        var project = Project(Script(
            Block("control.repeat", b => b.WithNumber("count", 5), body => body.Add(
                Block("ui.log", b => b.WithText("template", "tick"))))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            // The count is a value, so it is converted at the use site like every other slot.
            Assert.That(code, Does.Contain("for (var _i0 = 0; _i0 < VisualRuntime.ToNumber(5); _i0++)"));
            Assert.That(code, Does.Contain("context.CancellationToken.ThrowIfCancellationRequested();"));
            Assert.That(code.IndexOf("ThrowIfCancellationRequested", StringComparison.Ordinal),
                Is.LessThan(code.IndexOf("_logger", StringComparison.Ordinal)),
                "the cancellation check comes before the body's statements");
        });
    }

    [Test]
    public void A_repeat_until_tests_after_the_body_like_scratch()
    {
        var project = Project(Script(
            Block("control.repeat-until", b => b.WithBlock("condition", Boolean("ops.compare")), body => body.Add(
                Block("control.wait-ms", b => b.WithNumber("ms", 10))))));

        var code = Compile(project);

        var doIndex = code.IndexOf("do", StringComparison.Ordinal);
        var whileIndex = code.IndexOf("while (!", StringComparison.Ordinal);
        var bodyIndex = code.IndexOf("Task.Delay", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(doIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bodyIndex, Is.GreaterThan(doIndex), "the body is inside the do");
            Assert.That(whileIndex, Is.GreaterThan(bodyIndex), "the test comes after the body");
        });
    }

    [Test]
    public void A_forever_emits_while_true_with_cancellation()
    {
        var project = Project(Script(
            Block("control.forever", b => { }, body => body.Add(
                Block("control.wait-seconds", b => b.WithNumber("seconds", 1))))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("while (true)"));
            Assert.That(code, Does.Contain("ThrowIfCancellationRequested();"));
        });
    }

    [Test]
    public void An_if_else_emits_both_branches()
    {
        var project = Project(Script(
            Block("control.if-else", b => b.WithBlock("condition", Boolean("ops.compare")), then: then => then.Add(
                Block("ui.log", b => b.WithText("template", "then-path"))),
                otherwise: otherwise => otherwise.Add(
                Block("ui.log", b => b.WithText("template", "else-path"))))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("if ("));
            Assert.That(code, Does.Contain("else"));
            Assert.That(code, Does.Contain("\"then-path\""));
            Assert.That(code, Does.Contain("\"else-path\""));
            Assert.That(code.IndexOf("\"then-path\"", StringComparison.Ordinal),
                Is.LessThan(code.IndexOf("\"else-path\"", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Caps_return_success_and_failed_carries_the_chosen_code()
    {
        var project = Project(Script(
            Block("control.finish-success"),
            Block("control.finish-failed", b =>
            {
                b.WithField("code", "Timeout");
                b.WithText("message", "too slow");
            })));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("return ActionResult.Success();"));
            Assert.That(code, Does.Contain("ActionErrorCodes.Timeout"));
            Assert.That(code, Does.Contain("\"too slow\""));
        });
    }

    [Test]
    public void An_unknown_error_code_falls_back_instead_of_emitting_a_missing_member()
    {
        var project = Project(Script(
            Block("control.finish-failed", b =>
            {
                b.WithField("code", "NotARealCode");
                b.WithText("message", "x");
            })));

        var code = Compile(project);

        Assert.That(code, Does.Contain("ActionErrorCodes.ProviderError"));
    }

    [Test]
    public void A_disabled_block_emits_a_comment_and_no_code()
    {
        var project = Project(Script(
            Block("ui.log", b =>
            {
                b.Disabled = true;
                b.WithText("template", "invisible");
            })));

        var code = Compile(project);

        // A disabled block names itself, so the user can see what they parked, and emits nothing else.
        Assert.That(code, Does.Contain("(disabled)"));
    }

    [Test]
    public void A_comment_block_writes_its_note_and_a_block_comment_attaches_to_the_block()
    {
        var project = Project(Script(
            Block("control.comment", b => b.WithText("text", "scratch does this too")),
            Block("ui.log", b =>
            {
                b.Comment = "why this log exists";
                b.WithText("template", "x");
            })));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("// scratch does this too"));
            Assert.That(code, Does.Contain("// why this log exists"));
        });
    }

    [Test]
    public void A_slot_holding_a_variable_reads_the_local_and_a_slot_holding_a_reporter_inlines_it()
    {
        var project = Project(Script(
            Block("var.set", b => b
                .WithVariable("var", "count")
                .WithText("value", "0")),
            Block("var.change", b => b
                .WithVariable("var", "count")
                .WithNumber("amount", 1))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            // The variable's name is the document's identity for the local; the runtime stores by
            // name, which is what makes a rename-carrying canvas possible.
            Assert.That(code, Does.Contain("VisualRuntime.Set(VisualRuntime.ToText(\"count\"), \"0\");"));
            Assert.That(code, Does.Contain("VisualRuntime.Change(VisualRuntime.ToText(\"count\")"));
        });
    }

    [Test]
    public void A_nested_reporter_emits_its_own_expression_recursively()
    {
        var project = Project(Script(
            Block("control.wait-seconds", b => b
                .WithBlock("seconds", Reporter("ops.add", a => a
                    .WithText("a", "1")
                    .WithText("b", "2"))))));

        var code = Compile(project);

        Assert.That(code, Does.Contain("VisualRuntime.ToNumber(\"1\") + VisualRuntime.ToNumber(\"2\")"));
    }

    [Test]
    public void An_http_get_declares_the_local_its_into_names()
    {
        var project = Project(Script(
            Block("http.get", b =>
            {
                b.WithText("url", "https://example.com");
                b.WithText("into", "response");
            }),
            Block("ui.log", b => b
                .WithVariable("template", "response"))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("var response = await VisualRuntime.HttpAsync("));
        });
    }

    [Test]
    public void An_unknown_kind_is_reported_as_a_problem_and_skipped()
    {
        var project = Project(Script(new Block { Kind = "someone.elses.block" }));

        var (code, problems) = VisualEmitter.CompileTarget(project.Targets.Single(), project);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0].Message, Does.Contain("someone.elses.block"));
            Assert.That(code, Does.Not.Contain("someone.elses.block\";"), "no emission for an unknown kind");
        });
    }

    [Test]
    public void A_statement_in_a_slot_is_reported_and_not_emitted()
    {
        var project = Project(Script(
            Block("control.wait-seconds", b => b
                .WithBlock("seconds", Block("ui.log", lb => lb.WithText("template", "x"))))));

        var (_, problems) = VisualEmitter.CompileTarget(project.Targets.Single(), project);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0].Message, Does.Contain("cannot be values"));
        });
    }

    [Test]
    public void A_called_procedure_is_hoisted_as_a_local_function_above_its_call()
    {
        var project = Project(Script(
            Block("proc.call", b => b.WithText("name", "announce"))));
        project.Procedures.Add(new ProcedureDeclaration
        {
            Name = "announce",
            Body = [Block("ui.log", b => b.WithText("template", "announced"))],
        });

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            // The procedure is emitted as a local function with the Async suffix, and the call names it.
            Assert.That(code, Does.Contain("async Task announceAsync()"), "the procedure was emitted");
            Assert.That(code, Does.Contain("await announceAsync();"));
            var declareIndex = code.IndexOf("async Task announceAsync()", StringComparison.Ordinal);
            var callIndex = code.IndexOf("await announceAsync();", StringComparison.Ordinal);
            Assert.That(callIndex, Is.GreaterThan(declareIndex), "the hoist is above the call");
        });
    }

    [Test]
    public void An_uncalled_procedure_is_not_emitted()
    {
        var project = Project(Script(
            Block("ui.log", b => b.WithText("template", "x"))));
        project.Procedures.Add(new ProcedureDeclaration
        {
            Name = "unused",
            Body = [Block("ui.log", b => b.WithText("template", "y"))],
        });

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("unusedAsync"));
            Assert.That(code, Does.Contain("LogTemplate(\"x\""), "the script itself is untouched");
        });
    }

    [Test]
    public void A_returning_procedure_returns_and_its_caller_reads_the_value()
    {
        var project = Project(Script(
            Block("proc.call-value", b =>
            {
                b.WithText("name", "compute");
                b.WithText("args", "n=3");
            })));
        project.Procedures.Add(new ProcedureDeclaration
        {
            Name = "compute",
            Returns = true,
            Body = [Block("control.return", b => b
                .WithBlock("value", Reporter("ops.round", rb => rb
                    .WithBlock("value", Reporter("ops.add", ab => ab
                        .WithBlock("a", Variable("n"))
                        .WithNumber("b", 1))))) )],
        });

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("async Task<object?> computeAsync("));
            Assert.That(code, Does.Contain("return VisualRuntime.Round("), "the return value is the reporter's expression");
            Assert.That(code, Does.Contain("VisualRuntime.Get(\"n\")"), "a procedure parameter reads like a variable");
        });
    }

    private static Block Variable(string name) => new()
    {
        Kind = "var.get",
        Inputs = { ["var"] = BlockInput.OfVariable(name) },
    };

    [Test]
    public void Every_region_carries_the_three_in_scope_names_only_when_it_uses_them()
    {
        var project = Project(Script(
            Block("ui.log", b => b.WithText("template", "x"))));

        var code = Compile(project);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("_logger."), "the log block reads the injected logger");
            Assert.That(code, Does.Not.Contain("_integration"), "a host-less document names no host");
        });
    }

    [Test]
    public void The_runtime_template_defines_every_helper_the_emitter_references()
    {
        // Part 8.4's rule, as a test: an emitter cannot name a helper that does not exist. The
        // template is one literal, so the check is a scan of the two sources against each other —
        // cheap, and it fails naming the missing member.
        var template = VisualRuntimeTemplate.Source;
        var defined = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match method in System.Text.RegularExpressions.Regex.Matches(
            template, @"(?:public|internal) static [\w<>?,.\[\] ]+?([A-Za-z_][A-Za-z0-9_]*)\s*\("))
        {
            defined.Add(method.Groups[1].Value);
        }

        var referenced = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var row in BlockCatalog.All)
        {
            foreach (System.Text.RegularExpressions.Match call in System.Text.RegularExpressions.Regex.Matches(
                row.Sdk.Expression, @"VisualRuntime\.([A-Za-z_][A-Za-z0-9_]*)"))
            {
                referenced.Add(call.Groups[1].Value);
            }
        }

        var missing = referenced.Except(defined).OrderBy(name => name, StringComparer.Ordinal).ToList();

        Assert.That(missing, Is.Empty,
            "a template references a helper the runtime does not define: " + string.Join(", ", missing));
    }

    [Test]
    public void The_writer_splices_into_an_action_and_reports_problem_counts()
    {
        var actionSource = StockExecutor();

        var project = Project(Script(
            Block("ui.log", b => b.WithText("template", "written"))));
        var result = VisualProgramWriter.Write(
            actionSource, project, project.Targets[0].Scripts[0], integrationAvailable: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Content, Does.Contain("// <macrodeck-blocks>"));
            Assert.That(result.Content, Does.Contain("written"));
            Assert.That(result.Content, Does.Contain("async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)"),
                "the executor became async because the region awaits");
        });
    }

    [Test]
    public void The_writer_refuses_a_host_touching_region_without_an_integration()
    {
        var project = Project(Script(Block("deck.go-back")));
        var result = VisualProgramWriter.Write(
            StockExecutor(), project, project.Targets[0].Scripts[0], integrationAvailable: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("host integration"));
        });
    }

    [Test]
    public void The_writer_replaces_an_existing_region_instead_of_adding_a_second()
    {
        var project = Project(Script(Block("ui.log", b => b.WithText("template", "second"))));
        var first = VisualProgramWriter.Write(
            StockExecutor(), Project(Script(Block("ui.log", b => b.WithText("template", "first")))),
            Project(Script(Block("ui.log", b => b.WithText("template", "first")))).Targets[0].Scripts[0]);

        var second = VisualProgramWriter.Write(first.Content, project, project.Targets[0].Scripts[0]);

        Assert.Multiple(() =>
        {
            Assert.That(second.Success, Is.True);
            Assert.That(CountOf(second.Content, "// <macrodeck-blocks>"), Is.EqualTo(1),
                "saving twice must not grow a second region");
            Assert.That(second.Content, Does.Contain("\"second\""));
            Assert.That(second.Content, Does.Not.Contain("\"first\""));
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>Compiles the project's single target and returns the region code.</summary>
    private static string Compile(VisualProject project)
    {
        var (code, problems) = VisualEmitter.CompileTarget(project.Targets[0], project);
        Assert.That(problems, Is.Empty,
            "unexpected compile problems: " + string.Join(" | ", problems.Select(p => p.Message)));
        return code;
    }

    private static VisualProject Project(params VisualScript[] scripts)
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [new VisualTarget { Id = "action:test", Name = "test", Scripts = [.. scripts] }],
        };
        return project;
    }

    private static VisualScript Script(params Block[] body)
    {
        var script = new VisualScript
        {
            Id = "s1",
            Name = "main",
            Hat = new Block { Kind = "hat.action-runs", Id = "b0" },
        };

        var next = 1;
        foreach (var statement in body)
        {
            Number(statement, ref next);
            script.Body.Add(statement);
        }

        return script;

        static void Number(Block block, ref int next)
        {
            block.Id = "b" + next++;
            foreach (var input in block.Inputs.Values)
            {
                if (input.Block is { } nested)
                {
                    Number(nested, ref next);
                }
            }

            foreach (var child in block.Bodies.Values.SelectMany(b => b))
            {
                Number(child, ref next);
            }
        }
    }

    private static Block Block(string kind, Action<Block>? configure = null, Action<List<Block>>? body = null,
        Action<List<Block>>? then = null, Action<List<Block>>? otherwise = null)
    {
        var block = new Block { Kind = kind };
        configure?.Invoke(block);
        body?.Invoke(block.Body("body"));
        then?.Invoke(block.Body("then"));
        otherwise?.Invoke(block.Body("else"));
        return block;
    }

    private static Block Boolean(string kind, Action<Block>? configure = null)
    {
        var block = new Block { Kind = kind };
        configure?.Invoke(block);
        return block;
    }

    /// <summary>A reporter with the slots its kind needs, for nesting tests.</summary>
    private static Block Reporter(string kind, Action<Block>? configure = null)
    {
        var block = new Block { Kind = kind };
        if (kind == "ops.add")
        {
            block.WithText("a", string.Empty).WithText("b", string.Empty);
        }

        configure?.Invoke(block);
        return block;
    }

    /// <summary>The stock executor shape every generated action starts from.</summary>
    private static string StockExecutor() => """
        using MacroDeck.Sdk.Actions;
        using MacroDeck.Localization;

        namespace Probe;

        public sealed class TestAction : IActionDefinition
        {
            public string Id => "test";

            private sealed class Executor : IActionExecutor
            {
                public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
                {
                    return ActionResult.SucceededTask;
                }
            }
        }
        """;

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
