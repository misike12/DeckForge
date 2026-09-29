using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The visual document model: its JSON shape, its tree helpers, and what the loader does with a file
/// it cannot use.
/// </summary>
/// <remarks>
/// These run without a window on purpose. Every one covers a rule the design states in Part 6, and each
/// rule exists because the retired Blocks canvas got it wrong: a document that reloads differently is a
/// canvas that loses work, and a loader that cannot tell four failures apart leaves a user staring at
/// an empty canvas with nothing to act on.
/// </remarks>
[TestFixture]
public sealed class VisualModelTests
{
    [Test]
    public void A_document_survives_a_round_trip_through_json_unchanged()
    {
        var project = SampleDocument();
        var first = VisualProjectJson.Serialize(project);

        var outcome = VisualProjectJson.TryLoad(first, out var reloaded, out var message);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(VisualLoadOutcome.Loaded), message);
            Assert.That(message, Is.Empty);
            Assert.That(VisualProjectJson.Serialize(reloaded), Is.EqualTo(first),
                "The document is not text-stable across a save and a load, so every save would produce "
                + "a diff that hides the change the user actually made.");
        });
    }

    [Test]
    public void Every_input_kind_round_trips_under_its_own_json_member()
    {
        var json = VisualProjectJson.Serialize(SampleDocument());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"slot\""), "a nested reporter is written under 'slot'");
            Assert.That(json, Does.Contain("\"var\""), "a variable reference is written under 'var'");
            Assert.That(json, Does.Contain("\"text\""), "a literal text value is written under 'text'");
            Assert.That(json, Does.Contain("\"number\""), "a literal number is written under 'number'");
            Assert.That(json, Does.Contain("\"bool\""), "a literal boolean is written under 'bool'");
        });

        Assert.That(VisualProjectJson.TryLoad(json, out var reloaded, out _),
            Is.EqualTo(VisualLoadOutcome.Loaded));

        var kinds = reloaded.Blocks().SelectMany(block => block.Inputs.Values)
            .Select(input => input.Kind)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(kinds, Does.Contain(BlockInputKind.Block));
            Assert.That(kinds, Does.Contain(BlockInputKind.Variable));
            Assert.That(kinds, Does.Contain(BlockInputKind.Text));
            Assert.That(kinds, Does.Contain(BlockInputKind.Number));
            Assert.That(kinds, Does.Contain(BlockInputKind.Boolean));
        });
    }

    [Test]
    public void The_json_omits_what_is_empty()
    {
        var project = new VisualProject
        {
            DocumentId = "abc1234",
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:x",
                    Name = "x",
                    TargetFile = "XAction.cs",
                    Scripts =
                    [
                        new VisualScript
                        {
                            Id = "s1",
                            Hat = new Block { Kind = "hat.action-runs", Id = "b1" },
                            Body = [new Block { Kind = "deck.go-back", Id = "b2" }],
                        },
                    ],
                },
            ],
        };

        var json = VisualProjectJson.Serialize(project);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("\"inputs\""), "a block with no slots writes no inputs object");
            Assert.That(json, Does.Not.Contain("\"fields\""));
            Assert.That(json, Does.Not.Contain("\"bodies\""));
            Assert.That(json, Does.Not.Contain("null"), "explicit nulls are noise in a file meant to be reviewed");
            Assert.That(json, Does.Contain("\"kind\": \"action\""),
                "a target kind is written as its camel-case name, so the file reads as prose");
        });
    }

    [Test]
    public void A_tree_walk_reaches_nested_slots_and_bodies()
    {
        var script = SampleDocument().Targets[0].Scripts[0];

        var ids = script.Blocks().Select(block => block.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Count.EqualTo(9), string.Join(", ", ids));
            Assert.That(ids, Does.Contain("b4"), "the comparison reporter lives in a slot, not a body");
            Assert.That(ids, Does.Contain("b6"), "a reporter inside a nested body must still be reached");
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), "ids must be unique");
        });
    }

    [Test]
    public void Cloning_a_subtree_reissues_every_id_and_shares_nothing()
    {
        var original = SampleDocument().Targets[0].Scripts[0].Body[1];
        var issued = 100;

        var copy = original.Clone(() => "c" + (++issued).ToString());

        Assert.Multiple(() =>
        {
            Assert.That(copy.Walk().Select(block => block.Id),
                Is.All.Matches<string>(id => id.StartsWith('c')),
                "a duplicate that reused an id would make two blocks share a layout entry and a breakpoint");
            Assert.That(copy.Walk().Count(), Is.EqualTo(original.Walk().Count()));
            Assert.That(copy.Walk().Select(block => block.Kind),
                Is.EqualTo(original.Walk().Select(block => block.Kind)));
            Assert.That(copy.Comment, Is.EqualTo(original.Comment));
        });

        // Mutating the copy must not reach back into the original. A shallow copy passes an id check
        // and fails this one, which is why it is here.
        copy.Body("then").Add(new Block { Kind = "control.throw", Id = "c99" });
        Assert.That(original.Body("then"), Has.Count.EqualTo(1));
    }

    [Test]
    public void Variable_uses_are_found_wherever_they_are()
    {
        var script = SampleDocument().Targets[0].Scripts[0];
        var conditional = script.Body[1];

        var uses = conditional.VariableUses("response").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(uses, Has.Count.EqualTo(2), "the condition's left operand and the body's reporter");
            Assert.That(uses, Is.All.Matches<BlockInput>(input => input.Variable == "response"));
        });
    }

    [Test]
    public void The_next_block_id_is_one_past_the_highest_in_use()
    {
        Assert.That(SampleDocument().NextBlockId(), Is.EqualTo("b11"),
            "a document with b1..b10 must hand out b11, or two blocks end up sharing an id");
    }

    [Test]
    public void A_file_that_is_not_json_is_reported_not_thrown()
    {
        var outcome = VisualProjectJson.TryLoad("{ this is not json", out var project, out var message);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(VisualLoadOutcome.NotJson));
            Assert.That(project.Blocks(), Is.Empty);
            Assert.That(message, Does.Contain("left alone"),
                "the message has to say the file was not touched, because that is the user's real worry");
        });
    }

    [Test]
    public void A_document_from_a_newer_build_is_refused_rather_than_downgraded()
    {
        var json = VisualProjectJson.Serialize(SampleDocument())
            .Replace("\"version\": 1", "\"version\": 99", StringComparison.Ordinal);

        var outcome = VisualProjectJson.TryLoad(json, out var project, out var message);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(VisualLoadOutcome.UnsupportedSchema));
            Assert.That(project.Blocks(), Is.Empty, "a newer document must not be loaded partially");
            Assert.That(message, Does.Contain("read-only"));
        });
    }

    [Test]
    public void A_slot_holding_two_values_is_refused_by_block_id()
    {
        // Hand-written JSON. No writer produces this, but a merge conflict or a hand edit can, and
        // picking one of the two values would silently change the program.
        const string json = """
            {
              "version": 1,
              "documentId": "abc1234",
              "targets": [
                {
                  "id": "action:x",
                  "name": "x",
                  "kind": "action",
                  "targetFile": "XAction.cs",
                  "anchorId": "ExecuteAsync",
                  "scripts": [
                    {
                      "id": "s1",
                      "name": "main",
                      "x": 0,
                      "y": 0,
                      "disabled": false,
                      "hat": { "kind": "hat.action-runs", "id": "b1" },
                      "body": [
                        {
                          "kind": "deck.open-folder",
                          "id": "b2",
                          "inputs": { "folder": { "var": "somewhere", "text": "also here" } }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var outcome = VisualProjectJson.TryLoad(json, out var project, out var message);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(VisualLoadOutcome.Corrupt));
            Assert.That(project.Blocks(), Is.Empty);
            Assert.That(message, Does.Contain("b2").And.Contain("folder"),
                "the message must name the block and the slot, or it cannot be fixed");
            Assert.That(message, Does.Contain("left alone"));
        });
    }

    /// <summary>
    /// One document that uses every part of the model: nested bodies, a reporter in a slot, all four
    /// literal kinds, menus, a disabled block, a comment, a variable, a list and a procedure.
    /// </summary>
    /// <remarks>
    /// The ids run b1 to b10 so that <see cref="VisualProject.NextBlockId"/> has something to be one
    /// past, and so a walk that misses a level shows up as a missing id rather than a wrong count.
    /// </remarks>
    private static VisualProject SampleDocument()
    {
        var conditional = new Block
        {
            Kind = "control.if-else",
            Id = "b3",
            Comment = "the important branch",
        };

        conditional.WithBlock("condition", new Block { Kind = "ops.compare", Id = "b4" }
            .WithField("op", "=")
            .WithVariable("a", "response")
            .WithText("b", "ok"));

        conditional.Body("then").Add(new Block { Kind = "ui.notify", Id = "b5" }
            .WithField("level", "Info")
            .WithField("title", "Fetched")
            .WithBlock("message", new Block { Kind = "http.response-body", Id = "b6" }
                .WithVariable("request", "response")));

        conditional.Body("else").Add(new Block { Kind = "ui.log", Id = "b7" }
            .WithField("level", "Error")
            .WithText("template", "failed"));

        var script = new VisualScript
        {
            Id = "s1",
            Name = "main",
            X = 60,
            Y = 40,
            Hat = new Block { Kind = "hat.action-runs", Id = "b1" },
        };

        script.Body.Add(new Block { Kind = "http.get", Id = "b2" }
            .WithField("into", "response")
            .WithField("bearer", "token")
            .WithText("url", "https://example.com/health"));
        script.Body.Add(conditional);
        script.Body.Add(new Block { Kind = "control.wait-ms", Id = "b8", Disabled = true }
            .WithNumber("ms", 500));
        script.Body.Add(new Block { Kind = "var.set", Id = "b9" }
            .WithField("var", "flag")
            .WithField("valueKind", "bool")
            .WithBoolean("value", true));

        return new VisualProject
        {
            DocumentId = "abc1234",
            Variables =
            [
                new VariableDeclaration { Name = "response", Type = "Text" },
                new VariableDeclaration { Name = "host-name", Type = "Text", Scope = VariableScope.Host },
            ],
            Lists = [new ListDeclaration { Name = "items", ItemType = "Text", Initial = ["a", "b"] }],
            Procedures =
            [
                new ProcedureDeclaration
                {
                    Name = "announce",
                    Parameters = [new ProcedureParameter { Name = "who", Type = "Any" }],
                    Body = [new Block { Kind = "control.yield", Id = "b10" }],
                },
            ],
            Targets =
            [
                new VisualTarget
                {
                    Id = "action:fetch-status",
                    Name = "fetch-status",
                    TargetFile = "FetchStatusAction.cs",
                    Scripts = [script],
                },
            ],
        };
    }
}
