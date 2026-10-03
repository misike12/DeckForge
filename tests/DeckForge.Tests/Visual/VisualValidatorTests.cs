using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The validator, against the diagnostic catalogue in Appendix F.
/// </summary>
/// <remarks>
/// <para>
/// Every test names its code. The catalogue is a promise to the user — these are the findings you will
/// see and no others — so a test that says <c>vis-forever-no-wait</c> is the only place that promise is
/// checked, one finding at a time.
/// </para>
/// <para>
/// The documents are built by hand rather than by the migration so the tests show the shape being
/// checked: a hat, a body, a slot. <see cref="Script"/> assembles the one thing every document here
/// needs and nothing else.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualValidatorTests
{
    [Test]
    public void A_clean_document_has_no_findings()
    {
        var script = Script(
            Hat(),
            Stack("ui.log", b => b.WithText("template", "hello")));

        Assert.That(Validate(script), Is.Empty);
    }

    [Test]
    public void A_stack_block_in_a_value_slot_is_a_shape_error()
    {
        // A C-block in a slot is the strongest case: the block itself is reported as misplaced, and its
        // body's unbound required slot is reported too, so the user sees both problems at once.
        var script = Script(
            Hat(),
            Stack("control.wait-seconds", b => b
                .WithBlock("seconds", Stack("control.forever", fb => { }, body =>
                    body.Add(Stack("ui.notify", nb => nb.WithText("title", "T")))))));

        var diagnostics = Validate(script);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Where(d => d.Code == "vis-shape-mismatch").ToList(), Has.Count.EqualTo(1),
                "the misplaced container is reported once, where it sits");
            Assert.That(diagnostics.Where(d => d.Code == "vis-unbound-slot").Select(d => d.Message),
                Has.Some.Contains("message"),
                "the body's unbound slot is not hidden behind the shape error");
        });
    }

    [Test]
    public void A_statement_in_a_source_slot_is_reported_where_it_sits()
    {
        // The misplaced log block has no template: two findings, one per problem.
        var script = Script(
            Hat(),
            Stack("var.set", b => b
                .WithVariable("var", "name")
                .WithBlock("value", Stack("ui.log"))));

        var diagnostics = Validate(script);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Where(d => d.Code == "vis-shape-mismatch").Select(d => d.Message),
                Has.Some.Contains("value slot"));
            Assert.That(diagnostics.Where(d => d.Code == "vis-unbound-slot").Select(d => d.Message),
                Has.Some.Contains("template"));
        });
    }

    [Test]
    public void A_text_in_a_numeric_slot_is_a_warning_not_an_error()
    {
        // Scratch's freedom is kept: any value fits any slot, and the emitter coerces. The warning is
        // the honest note that the coercion may not do what the user meant.
        var script = Script(
            Hat(),
            Stack("control.wait-seconds", b => b.WithText("seconds", "a while")));

        var diagnostics = Validate(script);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Where(d => d.Code == "vis-type-mismatch").ToList(), Has.Count.EqualTo(1));
            Assert.That(diagnostics.All(d => d.Severity != VisualSeverity.Error));
        });
    }

    [Test]
    public void A_number_literal_in_a_numeric_slot_is_clean()
    {
        var script = Script(
            Hat(),
            Stack("control.wait-seconds", b => b.WithNumber("seconds", 2)));

        Assert.That(Validate(script), Is.Empty);
    }

    [Test]
    public void An_empty_required_slot_is_an_error_and_an_empty_optional_slot_is_not()
    {
        // show-modal's title is optional; the log block's template is required. One block for each
        // direction of the rule.
        var emptyOptional = Script(
            Hat(),
            Stack("ui.show-modal", b => b.WithText("view", "ask")));
        var emptyRequired = Script(
            Hat(),
            Stack("ui.log"));

        var optionalDiagnostics = Validate(emptyOptional);
        var requiredDiagnostics = Validate(emptyRequired);

        Assert.Multiple(() =>
        {
            Assert.That(optionalDiagnostics.Where(d => d.Code == "vis-unbound-slot").ToList(), Is.Empty,
                "an optional slot may be empty");
            Assert.That(requiredDiagnostics.Where(d => d.Code == "vis-unbound-slot").ToList(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void An_unknown_menu_key_is_an_error_that_names_the_valid_choices()
    {
        var script = Script(
            Hat(),
            Stack("ui.log", b => b
                .WithText("template", "hi")
                .WithField("level", "Loud")));

        var diagnostics = Validate(script);

        var finding = diagnostics.Single(d => d.Code == "vis-menu-key-unknown");
        Assert.Multiple(() =>
        {
            Assert.That(finding.Severity, Is.EqualTo(VisualSeverity.Error));
            Assert.That(finding.Hint, Does.Contain("Information").And.Contain("Error"));
        });
    }

    [Test]
    public void An_unset_menu_takes_the_default_and_is_not_flagged()
    {
        var script = Script(
            Hat(),
            Stack("ui.log", b => b.WithText("template", "hi")));

        Assert.That(Validate(script), Is.Empty);
    }

    [Test]
    public void A_break_outside_a_loop_is_an_error_and_inside_one_is_clean()
    {
        var outside = Script(Hat(), Stack("control.break"));
        var inside = Script(
            Hat(),
            Stack("control.forever", b => { }, body =>
            {
                body.Add(Stack("control.break"));
                body.Add(Stack("control.wait-seconds", wb => wb.WithNumber("seconds", 1)));
            }));

        Assert.Multiple(() =>
        {
            Assert.That(Validate(outside).Where(d => d.Code == "vis-loop-control-outside-loop").ToList(),
                Has.Count.EqualTo(1));
            Assert.That(Validate(inside).Where(d => d.Code == "vis-loop-control-outside-loop").ToList(),
                Is.Empty);
        });
    }

    [Test]
    public void A_break_inside_a_nested_loop_but_not_an_outer_one_is_clean()
    {
        // The depth check is the whole point: a break one level down must not be diagnosed from the
        // count one level up, and a nested repeat must raise the depth it actually raises.
        var script = Script(
            Hat(),
            Stack("control.repeat", b => b.WithNumber("count", 3), body =>
                body.Add(Stack("control.if", ib => ib.WithBlock("condition", Boolean("ops.compare",
                    cb => cb.WithText("b", "1"))), ifBody =>
                    {
                        ifBody.Add(Stack("control.continue"));
                    }))));

        Assert.That(Validate(script).Where(d => d.Code == "vis-loop-control-outside-loop").ToList(),
            Is.Empty);
    }

    [Test]
    public void A_return_in_a_script_is_an_error_and_in_a_procedure_is_clean()
    {
        var procedure = new ProcedureDeclaration
        {
            Name = "compute",
            Returns = true,
            Body = [Stack("control.return", b => b.WithText("value", "x"))],
        };
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [Target(Script(Hat()))],
            Procedures = [procedure],
        };

        Assert.Multiple(() =>
        {
            Assert.That(Validate(Script(Hat(), Stack("control.return", b => b.WithText("value", "x"))))
                .Where(d => d.Code == "vis-return-outside-procedure").ToList(),
                Has.Count.EqualTo(1));
            Assert.That(VisualValidator.Validate(project).Where(d => d.Code == "vis-return-outside-procedure").ToList(),
                Is.Empty);
        });
    }

    [Test]
    public void A_forever_with_a_wait_inside_is_clean_and_one_without_is_a_warning()
    {
        var sleeping = Script(
            Hat(),
            Stack("control.forever", b => { }, body =>
                body.Add(Stack("control.wait-seconds", wb => wb.WithNumber("seconds", 1)))));
        var spinning = Script(
            Hat(),
            Stack("control.forever", b => { }, body =>
                body.Add(Stack("ui.log", lb => lb.WithText("template", "busy")))));

        var clean = Validate(sleeping).Where(d => d.Code == "vis-forever-no-wait").ToList();
        var warned = Validate(spinning).Where(d => d.Code == "vis-forever-no-wait").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(clean, Is.Empty);
            Assert.That(warned, Has.Count.EqualTo(1));
            Assert.That(warned[0].Severity, Is.EqualTo(VisualSeverity.Warning));
        });
    }

    [Test]
    public void A_forever_that_calls_the_host_is_clean_even_without_a_wait()
    {
        // A host call yields: the SDK round-trip is the wait. This is the finding that keeps the rule
        // from nagging every honest deck watcher.
        var script = Script(
            Hat(),
            Stack("control.forever", b => { }, body =>
                body.Add(Stack("events.publish", b2 => b2.WithText("event", "tick")))));

        Assert.That(Validate(script).Where(d => d.Code == "vis-forever-no-wait").ToList(), Is.Empty);
    }

    [Test]
    public void Statements_after_a_cap_are_unreachable_and_caps_themselves_are_not_flagged_twice()
    {
        var script = Script(
            Hat(),
            Stack("control.finish-success"),
            Stack("ui.log", b => b.WithText("template", "never")));

        var diagnostics = Validate(script);

        var unreachable = diagnostics.Where(d => d.Code == "vis-cap-unreachable").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(unreachable, Has.Count.EqualTo(1));
            Assert.That(unreachable[0].BlockId, Is.Not.EqualTo(script.Body[0].Id),
                "the cap itself is reachable; what follows it is not");
        });
    }

    [Test]
    public void A_parameter_the_plugin_does_not_declare_is_a_warning()
    {
        var script = Script(
            Hat(),
            Stack("parameter.get", b => b.WithText("name", "apiKey")));

        var diagnostics = Validate(script, new VisualValidationContext(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal)));

        var finding = diagnostics.Single(d => d.Code == "vis-param-unknown");
        Assert.That(finding.Severity, Is.EqualTo(VisualSeverity.Warning));
    }

    [Test]
    public void A_declared_parameter_is_clean()
    {
        var script = Script(
            Hat(),
            Stack("parameter.get", b => b.WithText("name", "apiKey")));

        var diagnostics = Validate(script, new VisualValidationContext(
            new HashSet<string>(["apiKey"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal)));

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void An_unknown_host_variable_is_a_warning_with_the_variables_docs_link()
    {
        var script = Script(
            Hat(),
            Stack("sensing.get-host-variable", b => b.WithText("name", "shared")));

        var diagnostics = Validate(script);

        var finding = diagnostics.Single(d => d.Code == "vis-host-var-unknown");
        Assert.Multiple(() =>
        {
            Assert.That(finding.Severity, Is.EqualTo(VisualSeverity.Warning));
            Assert.That(finding.DocsPath, Is.EqualTo("features/variables"));
        });
    }

    [Test]
    public void A_variable_used_before_it_is_set_is_flagged()
    {
        var script = Script(
            Hat(),
            Stack("var.set", b => b
                .WithVariable("var", "count")
                .WithBlock("value", Variable("neverSet"))));

        var diagnostics = Validate(script);

        // Its own code, not vis-name-duplicate. Nothing here duplicates anything: `neverSet` is simply not
        // declared, and reporting it under the duplicate code sent anyone reading the log to the wrong part
        // of the design.
        var findings = diagnostics.Where(d => d.Code == "vis-name-unknown").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(findings, Has.Count.EqualTo(1),
                "the read of neverSet is the finding; the outer var slot is the declaration being made");
            Assert.That(findings.Single().Message, Does.Contain("neverSet"));
        });
    }

    [Test]
    public void A_variable_set_in_the_same_body_is_clean()
    {
        var script = Script(
            Hat(),
            Stack("var.set", b => b.WithVariable("var", "count").WithText("value", "0")),
            Stack("var.change", b => b.WithVariable("var", "count").WithNumber("amount", 1)));

        Assert.That(Validate(script), Is.Empty);
    }

    [Test]
    public void A_call_naming_no_procedure_is_an_error_and_a_declared_one_is_clean()
    {
        var call = Script(Hat(), Stack("proc.call", b => b.WithText("name", "missing")));

        Assert.Multiple(() =>
        {
            Assert.That(Validate(call).Where(d => d.Code == "vis-procedure-missing").ToList(),
                Has.Count.EqualTo(1));

            var declared = Script(Hat(), Stack("proc.call", b => b.WithText("name", "known")));
            var project = new VisualProject
            {
                DocumentId = VisualProject.NewDocumentId(),
                Targets = [Target(declared)],
                Procedures = [new ProcedureDeclaration { Name = "known" }],
            };
            Assert.That(VisualValidator.Validate(project).Where(d => d.Code == "vis-procedure-missing").ToList(), Is.Empty);
        });
    }

    [Test]
    public void A_local_named_after_a_reserved_name_is_an_error()
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [Target(Script(Hat()))],
            Variables =
            [
                new VariableDeclaration { Name = "context", Scope = VariableScope.Local },
            ],
        };

        var diagnostics = VisualValidator.Validate(project);

        Assert.That(diagnostics.Where(d => d.Code == "vis-local-collision").Select(d => d.Message),
            Has.Some.Contains("context"));
    }

    [Test]
    public void Two_lists_with_one_name_are_flagged_once()
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [Target(Script(Hat()))],
            Lists =
            [
                new ListDeclaration { Name = "items" },
                new ListDeclaration { Name = "items" },
            ],
        };

        var diagnostics = VisualValidator.Validate(project);

        Assert.That(diagnostics.Where(d => d.Code == "vis-name-duplicate").Select(d => d.Message),
            Has.Exactly(1).Contains("list"));
    }

    [Test]
    public void Two_procedures_with_one_name_are_an_error()
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [Target(Script(Hat()))],
            Procedures =
            [
                new ProcedureDeclaration { Name = "run" },
                new ProcedureDeclaration { Name = "run" },
            ],
        };

        var diagnostics = VisualValidator.Validate(project);

        Assert.That(diagnostics.Where(d => d.Code == "vis-name-duplicate").Select(d => d.Message),
            Has.Some.Contains("procedures are named"));
    }

    [Test]
    public void A_disabled_block_is_checked_for_names_but_not_for_required_slots()
    {
        // A disabled block is the user's parked work, not their rubbish: the names in it still show in
        // dropdowns, so they still have to resolve, while an empty slot in it is exactly what they will
        // fill in when they switch it back on.
        var script = Script(
            Hat(),
            Stack("ui.log", b =>
            {
                b.Disabled = true;
                b.WithText("template", "hi");
                b.WithText("name", "noSuchVariable");
            }));

        var diagnostics = Validate(script);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Where(d => d.Code == "vis-unbound-slot"), Is.Empty);
            Assert.That(diagnostics.Where(d => d.Code == "vis-menu-key-unknown"), Is.Empty);
        });
    }

    [Test]
    public void An_unknown_kind_is_reported_as_one()
    {
        var script = Script(Hat(), new Block { Kind = "someone.elses.block", Id = "b1" });

        var diagnostics = Validate(script);

        Assert.That(diagnostics.Where(d => d.Message.Contains("someone.elses.block")).ToList(),
            Has.Count.EqualTo(1));
    }

    [Test]
    public void A_script_whose_hat_is_not_a_hat_is_an_error()
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:x",
                    Name = "x",
                    Kind = TargetKind.Action,
                    Scripts =
                    [
                        new VisualScript
                        {
                            Id = "s1",
                            Hat = new Block { Kind = "ui.log", Id = "b1" },
                        },
                    ],
                },
            ],
        };

        var diagnostics = VisualValidator.Validate(project);

        Assert.That(diagnostics.Where(d => d.Code == "vis-shape-mismatch").Select(d => d.Message),
            Has.Some.Contains("starting block"));
    }

    [Test]
    public void Every_code_this_validator_uses_is_one_appendix_f_declares()
    {
        // The catalogue is the promise. A new finding invented on the spot is either added to the
        // catalogue or renamed to one it already makes.
        var declared = new[]
        {
            "vis-shape-mismatch", "vis-type-mismatch", "vis-unbound-slot", "vis-menu-key-unknown",
            "vis-name-duplicate", "vis-name-unknown", "vis-local-collision", "vis-param-unknown", "vis-host-var-unknown",
            "vis-loop-control-outside-loop", "vis-return-outside-procedure", "vis-forever-no-wait",
            "vis-cap-unreachable", "vis-procedure-missing", "vis-unverified-block",
        };

        var busy = Script(
            Hat(),
            Stack("control.break"),
            Stack("control.forever", b => { }, body => body.Add(Stack("control.finish-success"))),
            Stack("control.finish-success"),
            Stack("ui.log", b => b.WithText("template", "x").WithField("level", "Loud")));

        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [Target(busy)],
            Variables = [new VariableDeclaration { Name = "context", Scope = VariableScope.Local }],
            Lists = [new ListDeclaration { Name = "items" }, new ListDeclaration { Name = "items" }],
        };

        var codes = VisualValidator.Validate(project)
            .Concat(Validate(Script(Hat(), Stack("parameter.get", b => b.WithText("name", "p")))))
            .Select(d => d.Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.That(codes, Is.SubsetOf(declared),
            "a code outside the catalogue: add it to Appendix F or use the code the catalogue declares");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>Validates a one-script document built from the given hat and body.</summary>
    private static IReadOnlyList<VisualDiagnostic> Validate(VisualScript script, VisualValidationContext? context = null)
    {
        var project = new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets = [Target(script)],
        };
        return context is null
            ? VisualValidator.Validate(project)
            : VisualValidator.Validate(project, context);
    }

    private static VisualTarget Target(VisualScript script) => new()
    {
        Id = "action:test",
        Name = "test",
        Kind = TargetKind.Action,
        Scripts = [script],
    };

    private static VisualScript Hat() => new()
    {
        Id = "s1",
        Name = "main",
        Hat = new Block { Kind = "hat.action-runs", Id = "b0" },
    };

    /// <summary>A script whose body is the given statements, ids numbered in order.</summary>
    private static VisualScript Script(VisualScript script, params Block[] body)
    {
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

    private static Block Stack(string kind, Action<Block>? configure = null, Action<List<Block>>? body = null)
    {
        var block = new Block { Kind = kind };
        configure?.Invoke(block);
        body?.Invoke(block.Body("body"));
        return block;
    }

    private static Block Boolean(string kind, Func<Block, Block>? configure = null)
    {
        var block = new Block { Kind = kind };
        configure?.Invoke(block);
        return block;
    }

    private static Block Variable(string name) => new()
    {
        Kind = "var.get",
        Inputs = { ["var"] = BlockInput.OfVariable(name) },
    };
}
