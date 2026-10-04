using DeckForge.CodeGen.Generation;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Reading a generated block region back into blocks, without a compiler.
/// </summary>
/// <remarks>
/// <para>
/// Part 24's round trip. The emitter writes a region and the canvas has to be able to read its own output
/// back, because that is the only way a hand-edited action — or one written by a build this one does not know
/// — can be opened on the canvas instead of being lost.
/// </para>
/// <para>
/// The property these tests protect is <strong>nothing is lost</strong>, not "everything is understood": a
/// line the subset parser does not recognise has to come back as preserved text in the same position, and a
/// region it cannot identify at all has to be refused with a code rather than read as empty.
/// </para>
/// <para>
/// The sweep is the load-bearing test. The recognised subset and the scan that decides where a preserved run
/// starts are two lists that have to agree, and nothing at runtime can report a disagreement — a line the
/// reader understands but the scan does not recognise simply disappears into the previous grey tile, and the
/// document still looks plausible.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BlockRegionReaderTests
{
    /// <summary>
    /// One statement of every construct in the recognised subset, in the order the emitter writes them.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than emitted, and deliberately: <c>VisualEmitter</c> can produce a hundred and
    /// sixty constructs and the subset is a deliberate fraction of them, so the sweep has to name the subset
    /// rather than derive from something larger. Everything here is copied from what the emitter actually
    /// writes for the corresponding catalogue row.
    /// </remarks>
    private const string RecognisedSubset = """
        async Task<object?> AnnounceAsync(object? who)
        {
            VisualRuntime.Set("who", who);
            context.CancellationToken.ThrowIfCancellationRequested();
            _logger.Information(VisualRuntime.LogTemplate("in a procedure", context.Parameters));
        }

        if (_integration is null)
        {
            return ActionResult.Failed(ActionErrorCodes.NotConnected, "No host session is established yet.");
        }

        if (context.Ui is null || context.Interactions is null)
        {
            return ActionResult.Failed(ActionErrorCodes.Unavailable, "No client surface is available yet.");
        }

        async Task CountdownAsync()
        {
            VisualRuntime.Set(string.Empty, string.Empty);
        }

        // script: main
        _logger.Information(VisualRuntime.LogTemplate("first", context.Parameters));
        await _integration.Deck.GoToParentAsync(context.OriginClientId, context.CancellationToken);
        await _integration.Deck.GoBackAsync(context.OriginClientId, context.CancellationToken);
        await Task.Delay((int)(VisualRuntime.ToNumber(2) * 1000), context.CancellationToken);
        await Task.Delay((int)VisualRuntime.ToNumber(250), context.CancellationToken);
        for (var _i0 = 0; _i0 < (int)VisualRuntime.ToNumber(3); _i0++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await Task.Delay((int)VisualRuntime.ToNumber(500), context.CancellationToken);
        }
        while (true)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            break;
        }
        continue;
        await AnnounceAsync("Ada, welcome");
        return ActionResult.Accepted(VisualRuntime.ToText("working"));
        return ActionResult.Failed(ActionErrorCodes.Timeout, VisualRuntime.ToText("late"));
        return ActionResult.Success();
        // script: second
        break;
        """;

    [Test]
    public void The_markers_the_reader_looks_for_are_the_ones_the_writer_writes()
    {
        // Core cannot reference CodeGen — CodeGen is above it — so the two marker strings exist in both
        // assemblies. That is duplication, and this is what stops it rotting: change one and the round trip
        // silently reads nothing and hands back an empty document, which looks exactly like a region that
        // was never written.
        Assert.Multiple(() =>
        {
            Assert.That(BlockRegion.BeginMarker, Is.EqualTo(BlockCompiler.BeginMarker));
            Assert.That(BlockRegion.EndMarker, Is.EqualTo(BlockCompiler.EndMarker));
        });
    }

    [Test]
    public void The_recognised_subset_reads_back_with_nothing_preserved()
    {
        var read = BlockRegionReader.Read(Region(RecognisedSubset));

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True, read.Problem);
            Assert.That(read.Opaque, Is.Empty,
                "every construct in the subset is recognised, so nothing should have been preserved. A line "
                + "here means the reader and the preserved-run scan disagree — the reader understood it and "
                + "the scan did not, so it was swallowed into a grey tile:\n  "
                + string.Join("\n  ", read.Opaque.Select(chunk => $"{chunk.Line}: {chunk.Text}")));
            Assert.That(read.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Every_recognised_statement_comes_back_as_the_block_it_was_written_from()
    {
        var read = BlockRegionReader.Read(Region(RecognisedSubset));

        var scripts = read.Project!.Targets[0].Scripts;
        var body = scripts[0].Body;

        Assert.Multiple(() =>
        {
            Assert.That(scripts.Select(script => script.Name), Is.EqualTo(new[] { "main", "second" }),
                "the emitter closes every container before the next script header, so a header at the top "
                + "level is a new script rather than something inside the last one");

            Assert.That(body.Select(block => block.Kind), Is.EqualTo(new[]
            {
                "ui.log",
                "deck.go-to-parent",
                "deck.go-back",
                "control.wait-seconds",
                "control.wait-ms",
                "control.repeat",
                "control.forever",
                "control.continue",
                "proc.call",
                "control.finish-accepted",
                "control.finish-failed",
                "control.finish-success",
            }));

            Assert.That(body[0].Field("level"), Is.EqualTo("Information"));
            Assert.That(body[0].InputText("template"), Is.EqualTo("first"));
            Assert.That(body[3].InputNumber("seconds"), Is.EqualTo(2));
            Assert.That(body[4].InputNumber("ms"), Is.EqualTo(250));
            Assert.That(body[5].Body("body").Select(block => block.Kind), Is.EqualTo(new[] { "control.wait-ms" }),
                "the cancellation check is scaffolding the emitter writes around a block, not a block, so it "
                + "must not become a tile the next save would then emit a second one of");
            Assert.That(body[6].Body("body").Single().Kind, Is.EqualTo("control.break"));
            Assert.That(body[8].InputText("name"), Is.EqualTo("Announce"));
            Assert.That(body[9].InputText("message"), Is.EqualTo("working"));
            Assert.That(body[10].Field("code"), Is.EqualTo("Timeout"));
            Assert.That(body[10].InputText("message"), Is.EqualTo("late"));
            Assert.That(scripts[1].Body.Single().Kind, Is.EqualTo("control.break"));
        });
    }

    [Test]
    public void A_procedure_comes_back_with_its_name_and_parameters()
    {
        var read = BlockRegionReader.Read(Region(RecognisedSubset));

        Assert.Multiple(() =>
        {
            Assert.That(read.Project!.Procedures.Select(procedure => procedure.Name),
                Is.EqualTo(new[] { "Announce", "Countdown" }),
                "the emitter appends Async to every procedure identifier so it cannot shadow a Task method, "
                + "and the block's own name slot holds the name without it");
            Assert.That(read.Project.Procedures[0].Parameters.Select(parameter => parameter.Name),
                Is.EqualTo(new[] { "who" }),
                "the emitter declares every parameter as `object?`, and a parameter stored as \"object? who\" "
                + "would be a name no call could bind and no rename could find");
            Assert.That(read.Project.Procedures[0].Returns, Is.True);
            Assert.That(read.Project.Procedures[0].Body.Single().Kind, Is.EqualTo("ui.log"));
            Assert.That(read.Project.Procedures[1].Returns, Is.False);
        });
    }

    [Test]
    public void Reading_the_same_region_twice_produces_the_same_document()
    {
        var first = BlockRegionReader.Read(Region(RecognisedSubset));
        var second = BlockRegionReader.Read(Region(RecognisedSubset));

        first.Project!.DocumentId = second.Project!.DocumentId = "same";

        Assert.That(VisualStore.Serialize(second.Project), Is.EqualTo(VisualStore.Serialize(first.Project)),
            "determinism is what makes a parsed region a statement about the file rather than about the "
            + "machine: ids come from a counter in parse order, and the document id is the one thing two "
            + "reads legitimately produce differently");
    }

    [Test]
    public void A_file_with_no_region_is_refused_with_a_code_rather_than_read_as_empty()
    {
        var read = BlockRegionReader.Read("""
            public sealed class LogMessageAction : IActionExecutor
            {
                public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => Task.CompletedTask;
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.False);
            Assert.That(read.Project, Is.Null);
            Assert.That(read.Code, Is.EqualTo(BlockRegion.RegionConflictCode),
                "one vocabulary of vis- codes for the caller to handle; a code outside Appendix F fails the "
                + "suite that checks the catalogue and the product agree");
            Assert.That(read.Problem, Does.Contain(BlockRegion.BeginMarker),
                "the message has to say what was looked for, or the user cannot tell a file DeckForge never "
                + "wrote from one it failed to read");
        });
    }

    [Test]
    public void Two_regions_in_one_file_are_refused_because_neither_can_be_trusted()
    {
        var read = BlockRegionReader.Read("""
            // <macrodeck-blocks>
            // script: one
            // </macrodeck-blocks>
            // <macrodeck-blocks>
            // script: two
            // </macrodeck-blocks>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.False);
            Assert.That(read.Code, Is.EqualTo(BlockRegion.RegionConflictCode));
            Assert.That(read.Problem, Does.Contain("2"));
        });
    }

    [Test]
    public void Unrecognised_lines_are_preserved_verbatim_in_the_same_place()
    {
        var read = BlockRegionReader.Read(Region("""
            // script: main
            _logger.Information(VisualRuntime.LogTemplate("first", context.Parameters));
            foreach (var row in rows.Select(r => r.Trim()))
            {
                _logger.Debug(VisualRuntime.LogTemplate(row, context.Parameters));
            }
            await Task.Delay((int)VisualRuntime.ToNumber(500), context.CancellationToken);
            """));

        var body = read.Project!.Targets[0].Scripts[0].Body;

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True, read.Problem);
            Assert.That(body.Select(block => block.Kind),
                Is.EqualTo(new[] { "ui.log", "legacy.unsupported", "control.wait-ms" }),
                "the preserved run takes its own braces and its own body with it, so the block inside the "
                + "user's foreach is not hoisted out of it: keeping every line and losing the shape would be "
                + "worse than not reading it at all");
            Assert.That(read.Opaque, Has.Count.EqualTo(1));
            Assert.That(read.Opaque[0].Count, Is.EqualTo(4), "the header, its brace, the block and the brace");
            Assert.That(read.Opaque[0].Line, Is.EqualTo(3), "one-based, against the region's own lines");
            Assert.That(read.Opaque[0].Text, Does.Contain("foreach (var row in rows.Select(r => r.Trim()))"));
            Assert.That(read.Diagnostics.Select(diagnostic => diagnostic.Code),
                Is.All.EqualTo(BlockRegionReader.OpaqueCode));
            Assert.That(read.Diagnostics.Single().Severity, Is.EqualTo(VisualSeverity.Warning));
            Assert.That(read.Diagnostics.Single().Hint, Does.Contain("unchanged"),
                "Part 24.1.4's own words, because the user's next question is whether this code is safe");
        });

        Assert.That(body[1].InputText("legacyType"), Is.EqualTo(read.Opaque[0].Text),
            "the text lives in the document as well as on the result, because the document is the thing "
            + "that gets saved and a list beside it is not");
    }

    [Test]
    public void A_line_this_build_cannot_read_is_never_dropped()
    {
        // Part 24.2: regions written by a much older build degrade to opaque blocks with a diagnostic,
        // "never to a refusal". Every foreign line has to come back verbatim, because preservation is the
        // whole answer — a grey tile the user can see beats a grey tile they cannot, and both beat a line
        // that quietly went missing.
        var foreign = new[]
        {
            "if (VisualRuntime.ToBool(VisualRuntime.ToText(\"ready\")))",
            "try",
            "catch (Exception error)",
            "throw new InvalidOperationException(VisualRuntime.ToText(\"failed\"));",
            "var rows = await VisualRuntime.QueryAsync(context.CancellationToken);",
        };

        var read = BlockRegionReader.Read(Region(string.Join(
            "\n",
            ["// script: main", .. foreign, "return ActionResult.Success();"])));

        var preserved = string.Join("\n", read.Opaque.Select(chunk => chunk.Text));

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True, read.Problem);
            Assert.That(read.Opaque.Count, Is.EqualTo(1), "the five lines are one run: nothing between them "
                + "is readable, and splitting them would make one thing to look at into five");
            foreach (var line in foreign)
            {
                Assert.That(preserved, Does.Contain(line), $"\"{line}\" was dropped");
            }

            Assert.That(read.Project!.Targets[0].Scripts[0].Body.Last().Kind, Is.EqualTo("control.finish-success"),
                "the readable statement after the run is still read: a preserved run that swallowed the rest "
                + "of the region would preserve more than it lost");
        });
    }

    [Test]
    public void A_condition_degrades_to_preserved_lines_because_reading_one_back_would_mean_inverting_the_emitter()
    {
        // The subset's boundary, asserted rather than documented. The emitter lowers a comparison into two
        // temporaries and a switch, so reading a condition back is re-deriving the reporter that produced
        // it — and an `if` is preserved with its body rather than partially understood.
        var read = BlockRegionReader.Read(Region("""
            // script: main
            if (VisualRuntime.ToBool(VisualRuntime.ToText("ready")))
            {
                _logger.Information(VisualRuntime.LogTemplate("inside", context.Parameters));
            }
            """));

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True);
            Assert.That(read.Project!.Targets[0].Scripts[0].Body, Has.Count.EqualTo(1));
            Assert.That(read.Project.Targets[0].Scripts[0].Body.Single().Kind, Is.EqualTo("legacy.unsupported"));
            Assert.That(read.Opaque.Single().Count, Is.EqualTo(4), "the header, its brace, the block and the brace");
        });
    }

    [Test]
    public void A_region_of_nothing_but_scaffolding_is_an_empty_document_and_not_a_refusal()
    {
        var read = BlockRegionReader.Read(Region("""
            if (_integration is null)
            {
                return ActionResult.Failed(ActionErrorCodes.NotConnected, "No host session is established yet.");
            }
            """));

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True, read.Problem);
            Assert.That(read.Opaque, Is.Empty);
            Assert.That(read.Project!.Targets[0].Scripts, Has.Count.EqualTo(1));
            Assert.That(read.Project.Targets[0].Scripts[0].Body, Is.Empty);
            Assert.That(read.Project.Targets[0].Scripts[0].Hat.Kind, Is.EqualTo("hat.action-runs"),
                "an empty canvas is a document with a hat, and \"the region had no statements\" is not a "
                + "refusal");
        });
    }

    [Test]
    public void The_guards_do_not_become_blocks()
    {
        var read = BlockRegionReader.Read(Region("""
            if (_integration is null)
            {
                return ActionResult.Failed(ActionErrorCodes.NotConnected, "No host session is established yet.");
            }
            // script: main
            await _integration.Deck.GoBackAsync(context.OriginClientId, context.CancellationToken);
            """));

        Assert.Multiple(() =>
        {
            Assert.That(read.Opaque, Is.Empty);
            Assert.That(read.Project!.Targets[0].Scripts, Has.Count.EqualTo(1),
                "a guard read as a script boundary would leave two scripts where the region has one");
            Assert.That(read.Project.Targets[0].Scripts[0].Body.Select(block => block.Kind),
                Is.EqualTo(new[] { "deck.go-back" }),
                "a guard read as a block would be a tile the emitter then writes a second guard for, and the "
                + "region would grow one every round trip");
        });
    }

    [Test]
    public void A_carriage_return_does_not_change_the_document()
    {
        var lf = BlockRegionReader.Read(Region(RecognisedSubset));
        var crlf = BlockRegionReader.Read(
            BlockRegion.BeginMarker + "\r\n"
            + RecognisedSubset.Replace("\n", "\r\n", StringComparison.Ordinal)
            + "\r\n" + BlockRegion.EndMarker);

        lf.Project!.DocumentId = crlf.Project!.DocumentId = "same";

        Assert.Multiple(() =>
        {
            Assert.That(crlf.Ok, Is.True, crlf.Problem);
            Assert.That(VisualStore.Serialize(crlf.Project), Is.EqualTo(VisualStore.Serialize(lf.Project)),
                "a file written on Windows and read elsewhere has to produce the same document, and a stray "
                + "\\r left in the preserved text is a difference between two machines that have no business "
                + "differing");
        });
    }

    [Test]
    public void A_real_emitted_region_reads_back_rather_than_falling_over()
    {
        // The sample document contains every shipping block, so its region is far wider than the recognised
        // subset — which is the point. What has to hold is that a region this build wrote still produces a
        // document, and that the parts outside the subset come back as preserved lines rather than as a
        // refusal or, worse, as nothing.
        var project = VisualSampleProject.Build();
        var (code, _) = VisualEmitter.CompileTarget(project.Targets[0], project);

        var read = BlockRegionReader.Read(Region(code));

        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True, read.Problem);
            Assert.That(read.Opaque, Is.Not.Empty,
                "the subset is a deliberate fraction of what the emitter writes, and this asserts that the "
                + "rest of it is preserved rather than dropped — if the subset ever grows to cover the "
                + "sample this fails and the sweep above is the one to update");
            Assert.That(read.Diagnostics.Select(diagnostic => diagnostic.Code),
                Is.All.EqualTo(BlockRegionReader.OpaqueCode));

            // Every block the document holds came out of the region, and every one of them is a kind this
            // build knows: a reader that invented a kind would produce a canvas the emitter then refuses.
            Assert.That(read.Project!.Blocks(), Is.Not.Empty);
            Assert.That(read.Project.Blocks().Where(block => !BlockCatalog.IsKnown(block.Kind)).ToList(),
                Is.Empty);
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>Wraps body text in the markers, which is what a generated action carries.</summary>
    private static string Region(string body) =>
        BlockRegion.BeginMarker + "\n" + body + "\n" + BlockRegion.EndMarker;
}