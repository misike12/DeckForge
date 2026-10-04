using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Diagnostics for one target, which is what Part 9.7's header picker needs the pane to show.
/// </summary>
/// <remarks>
/// <para>
/// The defect this closes is not "the pane shows too much". Before the picker, the canvas always drew
/// <c>Document.Targets[0]</c> and the header said "1 target" whatever the document held, so a plugin with
/// three actions could only ever be checked on the first of them. The other two actions' findings were not
/// merely inconvenient: their blocks were not on screen, so <em>nothing</em> could select or centre them,
/// and the pane was listing problems the user had no way to go and look at.
/// </para>
/// <para>
/// Which is why the interesting assertion in here is negative. A filter that hides a block's finding is a
/// normal feature; a filter that hides a finding about a block which <em>is</em> on screen is the bug, and
/// it is the one a naive implementation produces by forgetting procedures.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TargetDiagnosticsTests
{
    [Test]
    public void A_finding_about_another_targets_block_is_not_in_this_targets_pane()
    {
        var project = TwoTargets();

        var findings = VisualValidator.ForTarget(
            project, project.Targets[0], VisualValidationContext.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(
                findings.Any(finding => finding.BlockId == "b1"),
                Is.True,
                "the block is on this canvas, so its finding has to be in the pane");
            Assert.That(
                findings.Any(finding => finding.BlockId == "b2"),
                Is.False,
                "the other action's block is not on this canvas, and a finding about it cannot be acted on - "
                + "there is nothing to click");
        });
    }

    [Test]
    public void A_finding_about_a_procedure_is_in_every_targets_pane()
    {
        // Procedures are document-level and are emitted into whichever target file is being written
        // (Part 4.2), and the canvas draws them beside every target's scripts. A pane that hid them for
        // all but one target would be hiding a whole panel's worth of the user's own code.
        var project = TwoTargets();

        var procedure = new ProcedureDeclaration { Id = "proc1", Name = "Helper" };
        procedure.Body.Add(Get("written", "b3"));
        project.Procedures.Add(procedure);

        foreach (var target in project.Targets)
        {
            Assert.That(
                VisualValidator.ForTarget(project, target, VisualValidationContext.Empty)
                    .Any(finding => finding.BlockId == "b3"),
                Is.True,
                $"{target.Name} draws the procedure column, so {target.Name}'s pane has to say what is "
                + "wrong with it");
        }
    }

    [Test]
    public void A_finding_about_no_block_at_all_is_never_hidden()
    {
        // `context` is one of the generated method's own parameter names; declaring a local called that is
        // a document-level mistake with no block to point at, and the pane is the only place it is ever
        // said. Filtering on BlockId alone would drop it whenever the picker moved.
        var project = TwoTargets();
        project.Variables.Add(new VariableDeclaration
        {
            Name = "context",
            Type = "Text",
            Scope = VariableScope.Local,
        });

        foreach (var target in project.Targets)
        {
            Assert.That(
                VisualValidator.ForTarget(project, target, VisualValidationContext.Empty)
                    .Any(finding => finding.Code == "vis-local-collision"),
                Is.True,
                "a finding with no block is about the document, and hiding it hides the only thing the user "
                + "can fix in the header's own settings");
        }
    }

    [Test]
    public void One_target_means_no_filtering_because_the_canvas_is_showing_everything()
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = Target("action:one", "One");
        target.Scripts.Add(new VisualScript { Id = "s1", Name = "Main", Hat = new Block { Kind = "hat.action-runs", Id = "h1" } });
        project.Targets.Add(target);

        var all = VisualValidator.Validate(project, VisualValidationContext.Empty);

        Assert.That(
            VisualValidator.ForTarget(project, target, VisualValidationContext.Empty),
            Is.EqualTo(all),
            "filtering a pane whose canvas shows the whole document is only a chance to lose a finding, and "
            + "it would differ the moment a second target was added - which is the worst time to find out");
    }

    [Test]
    public void The_block_ids_a_target_owns_are_its_scripts_and_its_procedures_and_nothing_else()
    {
        var project = TwoTargets();

        Assert.Multiple(() =>
        {
            Assert.That(
                VisualValidator.IdsIn(project, project.Targets[0]),
                Is.EquivalentTo(new[] { "h1", "b1" }));
            Assert.That(
                VisualValidator.IdsIn(project, project.Targets[1]),
                Is.EquivalentTo(new[] { "h2", "b2" }));
        });
    }

    [Test]
    public void A_nested_block_belongs_to_its_own_scripts_target_and_not_only_to_its_top_statement()
    {
        // The reporter inside b1's slot is a block like any other and gets its own id, so a finding about
        // it has to resolve. A test that only walked the top level would pass while the canvas showed the
        // reporter and the pane stayed quiet about it.
        var project = TwoTargets();
        var reporter = new Block { Kind = "ops.number", Id = "b1a" };
        reporter.Inputs["value"] = new BlockInput { Number = 1 };
        project.Targets[0].Scripts[0].Body[0].Inputs["var"] = new BlockInput { Block = reporter };

        Assert.Multiple(() =>
        {
            Assert.That(
                VisualValidator.IdsIn(project, project.Targets[0]),
                Does.Contain("b1a"),
                "an id nobody put in the set is an id the pane can never attribute to the target that owns it");
            Assert.That(
                VisualValidator.IdsIn(project, project.Targets[1]),
                Does.Not.Contain("b1a"),
                "and it belongs to the target the statement is in, not to every target at once");
        });
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>
    /// Two actions, each with one script holding one block that names something nothing declares.
    /// </summary>
    /// <remarks>
    /// A <c>get</c> rather than a <c>set</c>, and that is not incidental: the validator treats a setter as
    /// a declaration — <c>set count to 1</c> is how a script declares <c>count</c> — so a script holding
    /// only setters produces no findings at all and this file would pass for the wrong reason. Reading a
    /// name nothing ever wrote is the check that fires whatever the validation context says.
    /// </remarks>
    private static VisualProject TwoTargets()
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        project.Targets.Add(Target("action:one", "One", "h1", "b1", Get("written", "b1")));
        project.Targets.Add(Target("action:two", "Two", "h2", "b2", Get("written", "b2")));

        return project;
    }

    private static VisualTarget Target(
        string id,
        string name,
        string? hatId = null,
        string? blockId = null,
        Block? statement = null)
    {
        var script = new VisualScript
        {
            Id = $"script-{id}",
            Name = name,
            Hat = new Block { Kind = "hat.action-runs", Id = hatId ?? $"hat-{id}" },
        };

        if (statement is not null)
        {
            script.Body.Add(statement);
        }

        return new VisualTarget
        {
            Id = id,
            Name = name,
            Kind = TargetKind.Action,
            TargetFile = $"Actions/{name}Action.cs",
            AnchorId = "ExecuteAsync",
            Scripts = [script],
        };
    }

    /// <summary>A <c>{var}</c> reading a variable nothing has declared.</summary>
    private static Block Get(string name, string id)
    {
        var block = new Block { Kind = "var.get", Id = id };
        block.Inputs["var"] = new BlockInput { Variable = name };

        return block;
    }
}
