using DeckForge.Core.Blocks;
using DeckForge.Core.Visual;
using DeckForge.Core.Visual.Migrations;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The migration from a legacy <c>.blocks.json</c> to a visual document.
/// </summary>
/// <remarks>
/// Retiring the Blocks page is only honest if nobody loses work to it, so this file pins the mapping
/// table in Part 7.15 row by row: if a kind stops mapping, the test names it rather than a user
/// discovering an empty canvas.
/// </remarks>
[TestFixture]
public sealed class VisualMigrationTests
{
    [TestCaseSource(nameof(LegacyKinds))]
    public void Every_legacy_kind_maps_onto_a_shipping_block(BlockStatement statement, string expectedKind)
    {
        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            TargetFile = "LogMessageAction.cs",
            Statements = [statement],
        };

        var migrated = BlocksV1Migration.Migrate(program);
        var body = migrated.Targets.Single().Scripts.Single().Body;

        Assert.Multiple(() =>
        {
            Assert.That(body, Has.Count.EqualTo(1), "every legacy kind produces exactly one block");
            Assert.That(body[0].Kind, Is.EqualTo(expectedKind));
            Assert.That(body[0].Id, Is.Not.Empty);
        });
    }

    [Test]
    public void A_migrated_document_has_a_target_a_script_and_a_hat()
    {
        var program = new BlockProgram
        {
            TargetActionId = "fetch-status",
            TargetFile = "FetchStatusAction.cs",
            Statements = [new DelayBlock { Milliseconds = 250 }],
        };

        var migrated = BlocksV1Migration.Migrate(program);
        var target = migrated.Targets.Single();
        var script = target.Scripts.Single();

        Assert.Multiple(() =>
        {
            Assert.That(migrated.Version, Is.EqualTo(VisualProject.CurrentVersion));
            Assert.That(migrated.DocumentId, Is.Not.Empty, "the region header needs a document id");
            Assert.That(target.Id, Is.EqualTo("action:fetch-status"));
            Assert.That(target.Kind, Is.EqualTo(TargetKind.Action));
            Assert.That(target.TargetFile, Is.EqualTo("FetchStatusAction.cs"));
            Assert.That(target.AnchorId, Is.EqualTo("ExecuteAsync"));
            Assert.That(script.Hat.Kind, Is.EqualTo("hat.action-runs"),
                "an action program is what the green flag starts, so the hat is the action hat");
            Assert.That(script.Body, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void A_conditional_keeps_its_operator_and_both_branches()
    {
        var program = new BlockProgram
        {
            Statements =
            [
                new IfBlock
                {
                    LeftVariable = "message",
                    Operator = "contains",
                    RightLiteral = "b",
                    Then = [new DelayBlock { Milliseconds = 10 }],
                    Else = [new ThrowBlock { Message = "no" }],
                },
            ],
        };

        var migrated = BlocksV1Migration.Migrate(program);
        var block = migrated.Targets.Single().Scripts.Single().Body.Single();

        Assert.Multiple(() =>
        {
            Assert.That(block.Kind, Is.EqualTo("control.if-else"));
            Assert.That(block.Body("then"), Has.Count.EqualTo(1));
            Assert.That(block.Body("else"), Has.Count.EqualTo(1));

            var condition = block.InputBlock("condition");
            Assert.That(condition, Is.Not.Null, "an if without a condition block is not a conditional");
            Assert.That(condition!.Kind, Is.EqualTo("ops.compare"));
            Assert.That(condition.Field("op"), Is.EqualTo("contains"),
                "the legacy operator set is preserved, so a migrated condition tests the same thing");
            Assert.That(condition.Inputs["a"].Variable, Is.EqualTo("message"));
            Assert.That(condition.InputText("b"), Is.EqualTo("b"));
        });
    }

    [Test]
    public void An_if_with_no_else_becomes_a_plain_if()
    {
        var program = new BlockProgram
        {
            Statements = [new IfBlock { LeftVariable = "x", Operator = "isEmpty" }],
        };

        var block = BlocksV1Migration.Migrate(program).Targets.Single().Scripts.Single().Body.Single();

        Assert.Multiple(() =>
        {
            Assert.That(block.Kind, Is.EqualTo("control.if"));
            Assert.That(block.Bodies.ContainsKey("else"), Is.False);
        });
    }

    [Test]
    public void A_local_read_from_a_parameter_is_declared_as_a_variable()
    {
        var program = new BlockProgram
        {
            Statements =
            [
                new SetVariableBlock
                {
                    VariableName = "count",
                    FromParameter = "amount",
                    Type = "number",
                    Required = true,
                },
            ],
        };

        var migrated = BlocksV1Migration.Migrate(program);

        Assert.Multiple(() =>
        {
            var variable = migrated.Variables.Single();
            Assert.That(variable.Name, Is.EqualTo("count"));
            Assert.That(variable.Type, Is.EqualTo("Numeric"),
                "the document uses the SDK's VariableType vocabulary, not the old C# local's");
            Assert.That(variable.Scope, Is.EqualTo(VariableScope.Local));

            var block = migrated.Targets.Single().Scripts.Single().Body.Single();
            Assert.That(block.Kind, Is.EqualTo("var.set-from-parameter"));
            Assert.That(block.InputText("param"), Is.EqualTo("amount"),
                "a slot value lives in inputs, where the emitter reads it — not in fields");
            Assert.That(block.Field("required"), Is.EqualTo("true"));
            Assert.That(block.Field("type"), Is.EqualTo("Numeric"),
                "a parameter arrives as an object, so the block carries the menu for reading it");
        });
    }

    [Test]
    public void A_statement_this_build_does_not_know_is_kept_and_shown_rather_than_dropped()
    {
        // Subclassing stands in for a statement type from a newer build, which is exactly the case a
        // migration has to survive: the file is newer than the code reading it.
        var program = new BlockProgram { Statements = [new UnknownStatement()] };

        var migrated = BlocksV1Migration.Migrate(program);
        var block = migrated.Targets.Single().Scripts.Single().Body.Single();

        Assert.Multiple(() =>
        {
            Assert.That(block.Kind, Is.EqualTo("legacy.unsupported"));
            Assert.That(block.Disabled, Is.True, "an unrecognised block must not emit code by accident");
            Assert.That(block.Comment, Does.Contain(nameof(UnknownStatement)));
            Assert.That(block.InputText("legacyType"), Is.EqualTo(nameof(UnknownStatement)));
        });
    }

    [Test]
    public void A_legacy_file_is_recognised_structurally_and_migrates_on_load()
    {
        // The two formats both call their version field "version" and both number it 1, so the loader
        // has to tell them apart by shape. This is the test that keeps that true.
        var program = new BlockProgram
        {
            TargetActionId = "log-message",
            TargetFile = "LogMessageAction.cs",
            Statements = [new LogBlock { Template = "hello {name}" }],
        };
        var legacyJson = BlockProgramJson.Serialize(program);

        var outcome = VisualProjectJson.TryLoad(legacyJson, out var project, out var message);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(VisualLoadOutcome.LoadedFromLegacy));
            Assert.That(message, Does.Contain("Migrated"));
            Assert.That(project.Targets, Has.Count.EqualTo(1));
            Assert.That(project.Targets[0].Scripts[0].Body[0].Kind, Is.EqualTo("ui.log"));
            Assert.That(project.Blocks().Count(), Is.EqualTo(2), "the hat and the log block");
        });
    }

    [Test]
    public void A_migrated_document_reloads_as_a_native_document()
    {
        var program = new BlockProgram
        {
            Statements = [new NotifyBlock { Title = "Done", Message = "ok", Level = "Warning" }],
        };

        var migrated = BlocksV1Migration.Migrate(program);
        var json = VisualProjectJson.Serialize(migrated);

        Assert.That(VisualProjectJson.TryLoad(json, out var reloaded, out _),
            Is.EqualTo(VisualLoadOutcome.Loaded),
            "a migrated document must be a normal document from then on, or every session re-migrates");

        var notify = reloaded.Targets.Single().Scripts.Single().Body.Single();
        Assert.Multiple(() =>
        {
            // A level other than Info migrates to the levelled variant, whose level is a menu field.
            Assert.That(notify.Kind, Is.EqualTo("ui.notify-level"));
            Assert.That(notify.Field("level"), Is.EqualTo("Warning"));
            Assert.That(notify.InputText("title"), Is.EqualTo("Done"),
                "a slot value lives in inputs, where the emitter reads it — not in fields");
            Assert.That(notify.InputText("message"), Is.EqualTo("ok"));
        });
    }

    /// <summary>One instance of each of the eighteen legacy kinds, with the block it must become.</summary>
    /// <remarks>
    /// The read-variable mapping is the one deliberate surprise in this table. The legacy block read a
    /// host variable into a local, which is one gesture: a read is a reporter, and something has to hold
    /// its value. <c>var.set-from-host</c> is that block, so the local it declares shows up in the
    /// palette instead of being invisible. Part 7.15's row is the destination for the <em>name</em> being
    /// read; this is where the read lands.
    /// </remarks>
    private static IEnumerable<TestCaseData> LegacyKinds()
    {
        yield return Case(new LogBlock(), "ui.log");
        yield return Case(new LogBlock { Parameter = "amount" }, "ui.log-with-param");
        yield return Case(new SetVariableBlock { FromParameter = "message" }, "var.set-from-parameter");
        yield return Case(new SetVariableBlock { FromParameter = null, Literal = "text" }, "var.set");
        yield return Case(new IfBlock(), "control.if");
        yield return Case(new ReturnResultBlock { Outcome = "success" }, "control.finish-success");
        yield return Case(new ReturnResultBlock { Outcome = "failed" }, "control.finish-failed");
        yield return Case(new ReturnResultBlock { Outcome = "accepted" }, "control.finish-accepted");
        yield return Case(new DelayBlock(), "control.wait-ms");
        yield return Case(new HttpRequestBlock(), "http.get");
        yield return Case(new NotifyBlock(), "ui.notify");
        yield return Case(new NavigateBlock(), "deck.open-folder");
        yield return Case(new GoToParentBlock(), "deck.go-to-parent");
        yield return Case(new GoBackBlock(), "deck.go-back");
        yield return Case(new ChangeProfileBlock(), "deck.switch-profile");
        yield return Case(new RunScriptBlock(), "sensing.run-script");
        yield return Case(new PublishEventBlock(), "events.publish");
        yield return Case(new ReadVariableBlock(), "var.set-from-host");
        yield return Case(new SetVariableValueBlock(), "sensing.set-host-variable");
        yield return Case(new ShowModalBlock(), "ui.show-modal");
        yield return Case(new InvalidateIconBlock(), "deck.invalidate-icon");
        yield return Case(new ThrowBlock(), "control.throw");

        static TestCaseData Case(BlockStatement statement, string kind) =>
            new TestCaseData(statement, kind).SetName($"Legacy_{statement.GetType().Name}_becomes_{kind}");
    }

    /// <summary>A statement type from a build this one does not have.</summary>
    private sealed class UnknownStatement : BlockStatement
    {
        public override string Description => "from a newer build";
    }
}
