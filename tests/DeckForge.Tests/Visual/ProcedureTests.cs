using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// A procedure as something a user owns: an addressable body, a renameable declaration, and a set of call
/// rules that name the C# compile errors they exist to prevent.
/// </summary>
/// <remarks>
/// <para>
/// Part 7.14 lists four guarantees — unique names, a call must resolve, recursion is warned about, and a
/// parameter name may clash with nothing in scope. Three of the four existed before Phase 7, from the
/// emitter's side. What did not exist was the other half: a procedure was something the emitter read and
/// nothing could edit, so there was no way to get one wrong, and therefore nothing to check.
/// </para>
/// <para>
/// Every test here builds the smallest document that can express the thing being tested. A procedure test
/// written against a sample project tests the sample's shape as much as the rule.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProcedureTests
{
    [Test]
    public void A_procedure_body_is_addressable_by_its_declaration_id()
    {
        var project = Project();
        var procedure = project.Procedures[0];
        var block = Block("ui.log");
        procedure.Body.Add(block);

        var where = BodyRef.ProcedureBody(procedure.Id);

        Assert.Multiple(() =>
        {
            Assert.That(DocumentLists.ListFor(project, where), Is.SameAs(procedure.Body));
            Assert.That(DocumentLists.Locate(project, block), Is.EqualTo(where),
                "without this a block inside a procedure could be selected and then not editable: nothing "
                + "could say which body it was in, and every edit aimed at it was refused");
        });
    }

    [Test]
    public void Editing_inside_a_procedure_body_is_not_refused_as_a_missing_body()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var procedure = project.Procedures[0];
        var where = BodyRef.ProcedureBody(procedure.Id);

        var result = editor.Insert(where, 0, [Block("ui.log")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(procedure.Body, Has.Count.EqualTo(1));
            Assert.That(editor.Undo(), Is.True, "and it is on the undo stack like any other insert");
            Assert.That(procedure.Body, Is.Empty);
        });
    }

    [Test]
    public void Renaming_a_procedure_does_not_move_its_body()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var procedure = project.Procedures[0];
        procedure.Body.Add(Block("ui.log"));
        var reference = BodyRef.ProcedureBody(procedure.Id);

        editor.EditProcedure(procedure, "renamed");

        Assert.Multiple(() =>
        {
            Assert.That(procedure.Name, Is.EqualTo("renamed"));
            Assert.That(DocumentLists.Locate(project, procedure.Body[0]), Is.EqualTo(reference),
                "a rename must not invalidate the address of the body: an undo entry holding a name-derived "
                + "reference would resolve to nothing the moment the name changed");
        });
    }

    [Test]
    public void A_rename_is_one_undo_entry_and_an_edit_that_changes_nothing_is_none()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var procedure = project.Procedures[0];

        editor.EditProcedure(procedure, "ann");
        editor.EditProcedure(procedure, "announce");

        Assert.Multiple(() =>
        {
            Assert.That(editor.Depth, Is.EqualTo(1),
                "typing a name is one gesture; two keystrokes are not two things to undo");
            Assert.That(editor.Undo(), Is.True);
            Assert.That(procedure.Name, Is.EqualTo("myProcedure"),
                "and it goes back to the name from before the first application, not to the intermediate one");
        });

        var noop = editor.EditProcedure(procedure, "myProcedure");
        Assert.Multiple(() =>
        {
            Assert.That(noop.Applied, Is.False);
            Assert.That(editor.Depth, Is.Zero,
                "a refused no-change edit must not reach the stack, or pressing Ctrl+Z appears to do nothing");
        });
    }

    [Test]
    public void Adding_a_procedure_can_be_undone_and_redone()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var added = new ProcedureDeclaration { Id = "proc2", Name = "announce" };

        editor.AddProcedure(added);

        Assert.Multiple(() =>
        {
            Assert.That(project.Procedures.Select(p => p.Name), Is.EqualTo(new[] { "myProcedure", "announce" }));
            Assert.That(editor.Undo(), Is.True);
            Assert.That(project.Procedures, Has.Count.EqualTo(1));
            Assert.That(editor.Redo(), Is.True);
            Assert.That(project.Procedures.Select(p => p.Name), Is.EqualTo(new[] { "myProcedure", "announce" }));
        });
    }

    [Test]
    public void Deleting_a_procedure_undoes_back_to_the_same_place_in_the_list()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        project.Procedures.Add(new ProcedureDeclaration { Id = "proc2", Name = "middle" });
        project.Procedures.Add(new ProcedureDeclaration { Id = "proc3", Name = "last" });

        editor.DeleteProcedure(project.Procedures[1]);
        Assert.That(project.Procedures.Select(p => p.Name), Is.EqualTo(new[] { "myProcedure", "last" }));

        editor.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(project.Procedures.Select(p => p.Name),
                Is.EqualTo(new[] { "myProcedure", "middle", "last" }),
                "a delete that comes back at the end of the list is a delete that reordered the document, "
                + "and the id-keyed lookup makes that easy to get wrong");
            Assert.That(project.Procedures[1].Id, Is.EqualTo("proc2"));
        });
    }

    [Test]
    public void Editing_a_procedures_parameters_leaves_its_body_alone()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var procedure = project.Procedures[0];
        var body = procedure.Body;
        body.Add(Block("ui.log"));

        editor.EditProcedure(procedure, procedure.Name, [
            new ProcedureParameter { Name = "text", Type = "Text" },
            new ProcedureParameter { Name = "count", Type = "Number" },
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(procedure.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "text", "count" }));
            Assert.That(procedure.Body, Is.SameAs(body),
                "the copy the command holds must share the body's list: a deep copy would leave the "
                + "canvas projecting a detached list and the procedure would appear to lose everything the "
                + "moment a parameter was renamed");
            Assert.That(procedure.Body, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void A_script_can_be_renamed_and_get_a_different_hat_without_stranding_its_body()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var script = project.Targets[0].Scripts[0];
        var block = Block("ui.log");
        script.Body.Add(block);
        var originalHat = script.Hat;

        editor.EditScript(script, "on load");

        var newHat = BlockFactory.Create(BlockCatalog.Find("hat.script-starts")!, () => "hat-2");
        editor.EditScript(script, hat: newHat);

        Assert.Multiple(() =>
        {
            Assert.That(script.Name, Is.EqualTo("on load"));
            Assert.That(script.Hat.Id, Is.EqualTo(newHat.Id),
                "the hat is replaced by a new block rather than mutated: a hat's id addresses its script "
                + "body, so changing the kind in place would leave every undo entry holding the id of a "
                + "hat that no longer exists");
            Assert.That(DocumentLists.Locate(project, block), Is.EqualTo(BodyRef.ScriptBody(newHat.Id)));
            Assert.That(originalHat.Kind, Is.EqualTo("hat.action-runs"), "and the old block is not edited");
        });

        editor.Undo();
        Assert.That(script.Hat, Is.SameAs(originalHat), "undo puts the exact block back, not an equal one");
    }

    [Test]
    public void A_call_that_passes_the_wrong_number_of_arguments_is_named()
    {
        var project = Project();
        project.Procedures[0].Parameters.Add(new ProcedureParameter { Name = "text" });
        project.Targets[0].Scripts[0].Body.Add(Call("proc.call-with", "myProcedure", args: "text,other"));

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code),
            Does.Contain("vis-call-arity"),
            "a wrong argument count emits a C# compile error against the user's own file, which is the one "
            + "class of problem the diagnostics pane exists to move earlier");
    }

    [Test]
    public void A_call_that_matches_the_procedures_arity_is_quiet()
    {
        var project = Project();
        project.Procedures[0].Parameters.Add(new ProcedureParameter { Name = "text" });
        project.Targets[0].Scripts[0].Body.Add(Call("proc.call-with", "myProcedure", args: "text"));

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code),
            Does.Not.Contain("vis-call-arity"));
    }

    [Test]
    public void A_procedure_that_does_not_return_cannot_be_used_as_a_value()
    {
        var project = Project();
        project.Targets[0].Scripts[0].Body.Add(Call("proc.call-value", "myProcedure"));

        Assert.Multiple(() =>
        {
            Assert.That(VisualValidator.Validate(project).Select(d => d.Code),
                Does.Contain("vis-call-value-void"));
            Assert.That(VisualValidator.Validate(project).Select(d => d.Message),
                Has.Some.Contains("does not return a value"));
        });
    }

    [Test]
    public void Calling_a_procedure_that_returns_throws_nothing_away_without_saying_so()
    {
        var project = Project();
        project.Procedures[0].Returns = true;
        project.Targets[0].Scripts[0].Body.Add(Call("proc.call", "myProcedure"));

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code), Does.Contain("vis-call-value-unused"));
    }

    [Test]
    public void A_return_in_the_wrong_kind_of_procedure_is_an_error_in_both_directions()
    {
        var project = Project();
        var procedure = project.Procedures[0];
        procedure.Body.Add(Block("control.return"));

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code), Does.Contain("vis-return-value-void"),
            "handing a value back from a procedure declared to return nothing is a C# error");

        procedure.Returns = true;
        procedure.Body.Clear();
        var bare = Block("control.return");
        bare.Inputs["value"] = new BlockInput();
        procedure.Body.Add(bare);

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code), Does.Contain("vis-return-no-value"),
            "and a bare `return` in a Task<object?> function is the same error the other way round");
    }

    [Test]
    public void A_return_with_a_value_in_a_procedure_that_returns_is_quiet()
    {
        var project = Project();
        var procedure = project.Procedures[0];
        procedure.Returns = true;
        var returned = Block("control.return");
        returned.Inputs["value"] = BlockInput.Of("text");
        procedure.Body.Add(returned);

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code),
            Does.Not.Contain("vis-return-value-void").And.Not.Contain("vis-return-no-value"));
    }

    [Test]
    public void Recursion_is_warned_about_rather_than_refused()
    {
        var project = Project();
        project.Procedures[0].Body.Add(Call("proc.call", "myProcedure"));

        var direct = VisualValidator.Validate(project);

        Assert.Multiple(() =>
        {
            Assert.That(direct.Select(d => d.Code), Does.Contain("vis-procedure-recursion"));
            Assert.That(direct.Single(d => d.Code == "vis-procedure-recursion").Severity,
                Is.EqualTo(VisualSeverity.Warning),
                "a warning, because the design says so: recursion is bounded by the action's timeout, and "
                + "refusing it would be a claim the editor cannot keep - mutually recursive procedures with "
                + "no base case are legal C#");
        });
    }

    [Test]
    public void Recursion_through_another_procedure_is_found_too()
    {
        var project = Project();
        var first = project.Procedures[0];
        var second = new ProcedureDeclaration { Id = "proc2", Name = "second" };
        project.Procedures.Add(second);

        second.Body.Add(Call("proc.call", "myProcedure"));
        first.Body.Add(Call("proc.call", "second"));

        var diagnostics = VisualValidator.Validate(project)
            .Where(d => d.Code == "vis-procedure-recursion")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Is.Not.Empty,
                "the interesting case is not A calling A: two procedures calling each other produce nothing "
                + "from a direct check and hang exactly the same way");
            Assert.That(diagnostics.Select(d => d.Message), Has.Some.Contains("second"));
        });
    }

    [Test]
    public void A_procedure_that_calls_a_different_one_is_not_recursive()
    {
        var project = Project();
        project.Procedures.Add(new ProcedureDeclaration { Id = "proc2", Name = "other" });
        project.Procedures[0].Body.Add(Call("proc.call", "other"));

        Assert.That(VisualValidator.Validate(project).Select(d => d.Code),
            Does.Not.Contain("vis-procedure-recursion"));
    }

    [Test]
    public void An_unknown_procedure_is_reported_once_and_not_as_an_arity_error()
    {
        var project = Project();
        project.Targets[0].Scripts[0].Body.Add(Call("proc.call", "nowhere"));

        var codes = VisualValidator.Validate(project).Select(d => d.Code).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(codes, Does.Contain("vis-procedure-missing"));
            Assert.That(codes, Does.Not.Contain("vis-call-arity"),
                "saying it twice with a second code leaves one fix still showing an error");
        });
    }

    [Test]
    public void A_document_written_before_procedures_had_ids_opens_and_gains_them()
    {
        const string json = """
            {
              "version": 1,
              "targets": [],
              "procedures": [ { "name": "announce", "parameters": [], "returns": true, "body": [] } ]
            }
            """;

        var project = VisualProjectJson.Deserialize(json);

        Assert.That(project.Procedures[0].Id, Is.Not.Empty,
            "a canvas that saved perfectly well must not stop opening because a field was added to the "
            + "format");
        Assert.That(VisualProjectJson.Serialize(project), Does.Contain("\"id\""));
    }

    [Test]
    public void Procedure_ids_are_stable_across_a_save_and_reload()
    {
        var project = Project();
        var first = VisualProjectJson.Serialize(project);

        var reloaded = VisualProjectJson.Deserialize(first);

        Assert.Multiple(() =>
        {
            Assert.That(VisualProjectJson.Serialize(reloaded), Is.EqualTo(first),
                "ids derived from the index rather than randomly, or saving the same document twice churns "
                + "the file and VisualStore.IsDirty reports unsaved work that does not exist");
            Assert.That(reloaded.Procedures[0].Id, Is.EqualTo(project.Procedures[0].Id));
        });
    }

    [Test]
    public void A_procedure_body_offers_drop_gaps_addressed_to_the_procedure()
    {
        var project = Project();
        var procedure = project.Procedures[0];
        procedure.Body.Add(Block("ui.log"));

        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal)
        {
            [procedure.Body[0].Id] = new BlockRect(0, 0, 200, 30, 30),
            [procedure.Body[1 - 1].Id + "hat"] = new BlockRect(0, 0, 200, 30, 30),
        };

        var candidates = DropResolver.CandidatesFor(
            BodyRef.ProcedureBody(procedure.Id),
            procedure.Body,
            hat: null,
            rects);

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Is.Not.Empty,
                "the canvas enumerates procedure columns as drop targets, and a column with no candidates is "
                + "a place a block can never be dropped - no indicator, and a refusal that says nothing "
                + "about where to aim");
            Assert.That(candidates.Select(candidate => candidate.Where), Has.All.EqualTo(BodyRef.ProcedureBody(procedure.Id)),
                "every candidate has to carry the procedure's own address, or the drop lands in whatever "
                + "body happens to share the block id");
            Assert.That(candidates.Select(candidate => candidate.Kind), Does.Not.Contain(DropTargetKind.HatSlot),
                "a procedure has no hat to attach to, and offering the hat slot would show an indicator "
                + "that leads nowhere");
        });
    }

    [Test]
    public void A_drop_into_a_procedure_body_writes_to_the_procedure()
    {
        var project = Project();
        var editor = new DocumentEditor(project);
        var procedure = project.Procedures[0];
        procedure.Body.Add(Block("ui.log"));

        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal)
        {
            [procedure.Body[0].Id] = new BlockRect(0, 0, 200, 30, 30),
        };

        // The landing the canvas would report for a pointer over the statement itself: index 1 is the
        // gap below it.
        var landing = DropResolver.CandidatesFor(
            BodyRef.ProcedureBody(procedure.Id),
            procedure.Body,
            hat: null,
            rects).First(candidate => candidate.Index == 1);

        var result = DropPlan.Apply(
            editor,
            project.Targets[0],
            DragPayload.FromPalette("ui.log"),
            landing);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True, result.Problem);
            Assert.That(procedure.Body.Select(block => block.Kind), Is.EqualTo(new[] { "ui.log", "ui.log" }),
                "the block has to land in the procedure, which is the whole point of the candidate carrying "
                + "which kind of body it belongs to");
        });
    }

    /// <summary>
    /// The smallest document that has a procedure in it: one target, one script, one void procedure.
    /// </summary>
    /// <remarks>
    /// Built rather than trimmed out of <see cref="VisualSampleProject"/>, which is the point the fixture
    /// comment makes: every assertion in this file is about a procedure, and a sample with eleven scripts
    /// and three procedures in it turns "the call passes one argument" into a statement about which
    /// script happened to be kept.
    /// </remarks>
    private static VisualProject Project()
    {
        var script = new VisualScript
        {
            Id = "script-1",
            Name = "script",
            Hat = Block("hat.action-runs"),
        };

        var procedure = new ProcedureDeclaration { Id = "proc1", Name = "myProcedure" };

        return new VisualProject
        {
            DocumentId = VisualProject.NewDocumentId(),
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:one",
                    Name = "One",
                    Kind = TargetKind.Action,
                    TargetFile = "Actions/OneAction.cs",
                    AnchorId = "ExecuteAsync",
                    Scripts = [script],
                },
            ],
            Procedures = [procedure],
        };
    }

    /// <summary>A call block of the given kind, naming <paramref name="name"/>.</summary>
    private static Block Call(string kind, string name, string? args = null)
    {
        var block = Block(kind);
        block.Inputs["name"] = BlockInput.OfVariable(name);

        if (args is not null)
        {
            block.Inputs["args"] = BlockInput.Of(args);
        }

        return block;
    }

    private static Block Block(string kind)
    {
        var descriptor = BlockCatalog.Find(kind);
        var block = new Block { Kind = kind, Id = kind + "-" + Guid.NewGuid().ToString("N")[..4] };

        foreach (var slot in descriptor?.Slots ?? [])
        {
            block.Inputs[slot.Name] = BlockFactory.DefaultInput(descriptor!, slot);
        }

        foreach (var menu in descriptor?.Menus ?? [])
        {
            block.Fields[menu.Name] = menu.Default;
        }

        return block;
    }
}