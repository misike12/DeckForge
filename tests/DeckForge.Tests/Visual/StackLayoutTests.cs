using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Geometry and drop resolution, the two pure pieces the canvas is a thin shell over.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that make the drag-and-drop promise testable at all: the same
/// <see cref="StackLayout"/> and <see cref="DropResolver"/> the WPF layer calls on every pointer move
/// are called here, with rectangles asserted rather than pixels eyeballed. When a snap goes wrong on
/// screen, one of these tests gains a case.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StackLayoutTests
{
    // ---- layout -------------------------------------------------------------------------------------

    [Test]
    public void A_stack_of_two_blocks_lays_out_top_to_bottom_with_a_gap()
    {
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "a")),
            Stack("ui.log", b => b.WithText("template", "b")));

        var rects = StackLayout.Layout(script);

        var first = rects[script.Body[0].Id];
        var second = rects[script.Body[1].Id];

        Assert.Multiple(() =>
        {
            Assert.That(second.Y, Is.GreaterThan(first.Y));
            Assert.That(first.X, Is.EqualTo(second.X), "a plain stack does not indent");
            Assert.That(second.Y, Is.GreaterThanOrEqualTo(first.Y + first.Height),
                "the second block starts at or below the first's bottom");
        });
    }

    [Test]
    public void A_container_lays_out_its_body_indented_and_its_notch_below_the_body()
    {
        var script = Script(
            Stack("control.forever", b => { }, body =>
            {
                body.Add(Stack("ui.log", lb => lb.WithText("template", "inner")));
            }),
            Stack("ui.log", b => b.WithText("template", "after")));

        var rects = StackLayout.Layout(script);

        var container = rects[script.Body[0].Id];
        var inner = rects[script.Body[0].Body("body")[0].Id];
        var after = rects[script.Body[1].Id];

        Assert.Multiple(() =>
        {
            Assert.That(inner.X, Is.GreaterThan(container.X),
                "the body's statements sit on the indent rail");
            Assert.That(inner.Y, Is.GreaterThan(container.Y));
            Assert.That(container.Height, Is.GreaterThan(44),
                "a C-block is taller than its label line once it has a body");
            Assert.That(container.NotchY, Is.EqualTo(container.Y + container.Height).Within(0.001),
                "the next statement lands below the whole container, not below its label");
            Assert.That(after.Y, Is.GreaterThanOrEqualTo(container.NotchY));
        });
    }

    [Test]
    public void Two_bodies_of_an_if_else_do_not_overlap()
    {
        var script = Script(
            Stack("control.if-else", b => b.WithBlock("condition", Compare()),
                then: then =>
                {
                    then.Add(Stack("ui.log", lb => lb.WithText("template", "in then")));
                    then.Add(Stack("ui.log", lb => lb.WithText("template", "still then")));
                },
                otherwise: otherwise =>
                {
                    otherwise.Add(Stack("ui.log", lb => lb.WithText("template", "in else")));
                }));

        var rects = StackLayout.Layout(script);
        var container = script.Body[0];
        var thenBlocks = container.Body("then");
        var elseBlocks = container.Body("else");
        var thenLast = rects[thenBlocks[^1].Id];
        var elseFirst = rects[elseBlocks[0].Id];

        Assert.Multiple(() =>
        {
            Assert.That(elseFirst.Y, Is.GreaterThan(thenLast.Y),
                "the else body starts below everything in the then body");
            Assert.That(rects[container.Id].NotchY, Is.GreaterThan(elseLast(rects, elseBlocks).Y));
        });

        static BlockRect elseLast(IReadOnlyDictionary<string, BlockRect> rects, List<Block> blocks) =>
            rects[blocks[^1].Id];
    }

    [Test]
    public void An_empty_body_still_takes_a_mouths_height()
    {
        var script = Script(
            Stack("control.forever", b => { }));

        var rects = StackLayout.Layout(script);

        Assert.That(rects[script.Body[0].Id].Height, Is.GreaterThan(44),
            "the canvas must draw an openable gap, so layout reserves one");
    }

    [Test]
    public void Zoom_scales_every_rect()
    {
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "a")),
            Stack("ui.log", b => b.WithText("template", "b")));

        var at1 = StackLayout.Layout(script, 1);
        var at2 = StackLayout.Layout(script, 2);

        Assert.Multiple(() =>
        {
            Assert.That(at2[script.Body[0].Id].Height, Is.EqualTo(at1[script.Body[0].Id].Height * 2).Within(0.5));
            Assert.That(
                StackLayout.TotalHeight(script, 2),
                Is.EqualTo(StackLayout.TotalHeight(script, 1) * 2).Within(2),
                "zoom scales the whole script's height, gaps included");
        });
    }

    [Test]
    public void A_cached_height_is_used_when_the_caller_supplies_one()
    {
        // Appendix H: 200 blocks under 0.5ms warm. The cache contract is that the layout uses the
        // caller's height when it has one, so a measured WPF height beats the estimate.
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "a")),
            Stack("ui.log", b => b.WithText("template", "b")));

        var rects = StackLayout.Layout(script, heightOf: new Dictionary<string, double>
        {
            [script.Body[0].Id] = 100,
        });

        Assert.That(rects[script.Body[0].Id].Height, Is.EqualTo(100));
        Assert.That(rects[script.Body[1].Id].Y, Is.GreaterThanOrEqualTo(100),
            "the next block starts below the cached height, not below the estimate");
    }

    // ---- candidates ----------------------------------------------------------------------------------

    [Test]
    public void A_flat_script_offers_gaps_at_the_top_between_and_after_every_block()
    {
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "a")),
            Stack("ui.log", b => b.WithText("template", "b")));

        var rects = StackLayout.Layout(script);
        var candidates = DropResolver.CandidatesFor(script, rects);

        Assert.Multiple(() =>
        {
            Assert.That(candidates.Where(c => c.Kind == DropTargetKind.HatSlot).ToList(), Has.Count.EqualTo(1));
            // Four, not three: the middle boundary offers two coincident gaps - "insert before the
            // second" and "insert after the first". They land in the same place and differ only in
            // which block the document editor anchors to; the stability bonus is what decides between
            // them, which is exactly the situation the bonus was designed for.
            Assert.That(candidates.Where(c => c.Kind == DropTargetKind.StackGap).ToList(), Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void A_container_offers_a_mouth_and_inner_gaps()
    {
        var script = Script(
            Stack("control.forever", b => { }, body =>
                body.Add(Stack("ui.log", lb => lb.WithText("template", "inner")))));

        var rects = StackLayout.Layout(script);
        var candidates = DropResolver.CandidatesFor(script, rects);

        Assert.Multiple(() =>
        {
            Assert.That(candidates.Where(c => c.Kind == DropTargetKind.Mouth).ToList(), Has.Count.EqualTo(2),
                "a mouth is offered at the top of the body and again at its foot, so a run dropped "
                + "below the contents goes inside the loop rather than after it");
            Assert.That(candidates.Where(c => c.Kind == DropTargetKind.StackGap).ToList(), Has.Count.GreaterThanOrEqualTo(2),
                "one inside the body, one below the container");
        });
    }

    [Test]
    public void Every_gap_says_where_it_would_insert()
    {
        // P1c left every candidate's index at zero, which was harmless while nothing acted on one. Now
        // something does, and a candidate that cannot say where it goes is a candidate the canvas has to
        // keep its own notes about - which is how a drop ends up two gaps from where the indicator was.
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "one")),
            Stack("control.forever", b => { }, body =>
            {
                body.Add(Stack("ui.log", lb => lb.WithText("template", "inner")));
            }),
            Stack("ui.log", b => b.WithText("template", "three")));

        var candidates = DropResolver.CandidatesFor(script, StackLayout.Layout(script));
        var gaps = candidates.Where(c => c.Kind == DropTargetKind.StackGap).ToList();
        var inScript = gaps.Where(c => c.BodyName is not { Length: > 0 });
        var inLoop = gaps.Where(c => c.BodyName == "body");

        Assert.Multiple(() =>
        {
            Assert.That(inScript.Select(c => c.Index), Is.EquivalentTo(new[] { 0, 1, 1, 2, 2, 3 }),
                "the script's own gaps are numbered against the script's body, and the loop's two "
                + "statements contribute a gap above and below each");
            Assert.That(inLoop.Select(c => c.Index), Is.EqualTo(new[] { 1 }),
                "the gap below the loop's one statement is the loop's body's own second index, not the "
                + "script's - which is the difference between dropping inside the loop and after it");
            Assert.That(candidates.Where(c => c.Kind == DropTargetKind.Mouth).Select(c => c.Index),
                Is.EquivalentTo(new[] { 0, 1 }), "the mouth is filled from the top or the foot");
            Assert.That(inLoop.All(c => c.ParentId == script.Body[1].Id),
                Is.True, "a nested gap names the container it opens into");
        });
    }

    [Test]
    public void A_value_slot_the_canvas_measured_becomes_a_candidate()
    {
        var slots = DropResolver.ValueSlotCandidates(
        [
            ("b1", "condition", 120, 40),
            ("b2", "seconds", 12, 96),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(slots, Has.Count.EqualTo(2));
            Assert.That(slots[0].SlotName, Is.EqualTo("condition"));
            Assert.That(slots[0].X, Is.EqualTo(120));
        });

        var reporter = DropResolver.Resolve("deck.current-folder", 121, 41, slots);
        var stack = DropResolver.Resolve("ui.log", 121, 41, slots);

        Assert.Multiple(() =>
        {
            Assert.That(reporter?.Candidate.Kind, Is.EqualTo(DropTargetKind.ValueSlot),
                "a reporter belongs in a hole");
            Assert.That(stack, Is.Null, "and a statement does not, however close the pointer is");
        });
    }

    // ---- scoring -------------------------------------------------------------------------------------

    [Test]
    public void A_reporter_is_refused_by_every_stack_zone_and_accepted_by_a_slot()
    {
        var reporter = BlockCatalog.Find("var.get")!;
        var stack = BlockCatalog.Find("ui.log")!;
        var hat = BlockCatalog.Find("hat.action-runs")!;

        Assert.Multiple(() =>
        {
            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.StackGap, "p"), reporter), Is.False);
            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.Mouth, "p"), reporter), Is.False);
            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.HatSlot, "p"), reporter), Is.False);
            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.ValueSlot, "p", SlotName: "v"), reporter), Is.True);

            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.StackGap, "p"), stack), Is.True);
            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.ValueSlot, "p", SlotName: "v"), stack), Is.False);

            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.HatSlot, "p"), hat), Is.True);
            Assert.That(DropResolver.Accepts(new DropCandidate(DropTargetKind.StackGap, "p"), hat), Is.False);
        });
    }

    [Test]
    public void The_nearest_gap_wins()
    {
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "a")),
            Stack("ui.log", b => b.WithText("template", "b")),
            Stack("ui.log", b => b.WithText("template", "c")));

        var rects = StackLayout.Layout(script);
        var candidates = DropResolver.CandidatesFor(script, rects);

        var middleGap = candidates
            .Where(c => c.Kind == DropTargetKind.StackGap)
            .OrderBy(c => c.Y)
            .ToList()[1];

        var resolution = DropResolver.Resolve("ui.log", middleGap.X, middleGap.Y, candidates);

        Assert.Multiple(() =>
        {
            Assert.That(resolution, Is.Not.Null);
            Assert.That(resolution!.Candidate.ParentId, Is.EqualTo(middleGap.ParentId));
            Assert.That(resolution.Magnetic, Is.True, "the pointer is exactly on the notch");
            Assert.That(resolution.SnapY, Is.EqualTo(middleGap.Y).Within(0.001),
                "a magnetic snap aligns the ghost's notch with the target's");
        });
    }

    [Test]
    public void The_stability_bonus_breaks_a_tie()
    {
        // Two gaps at the same distance: the one the pointer was already over wins, so the indicator
        // cannot flicker between them. This is Part 9.5's rule 4, and it is only observable as a
        // tie-break, which is why the test builds an exact tie.
        var left = new DropCandidate(DropTargetKind.StackGap, "left", X: 100, Y: 100);
        var right = new DropCandidate(DropTargetKind.StackGap, "right", X: 100, Y: 100);
        var candidates = new[] { left, right };

        var resolving = DropResolver.Resolve("ui.log", 100, 100, candidates, recentParentId: "right");

        Assert.That(resolving!.Candidate.ParentId, Is.EqualTo("right"));
    }

    [Test]
    public void Outside_the_magnet_radius_the_ghost_follows_the_pointer()
    {
        var candidate = new DropCandidate(DropTargetKind.StackGap, "p", X: 100, Y: 100);
        var resolution = DropResolver.Resolve("ui.log", 400, 400, [candidate], zoom: 1);

        Assert.Multiple(() =>
        {
            Assert.That(resolution, Is.Not.Null);
            Assert.That(resolution!.Magnetic, Is.False);
            Assert.That(resolution.SnapX, Is.EqualTo(400));
            Assert.That(resolution.SnapY, Is.EqualTo(400));
        });
    }

    [Test]
    public void A_gap_sits_where_the_block_actually_is_on_the_canvas()
    {
        // Every coordinate in a candidate comes from the block's own rectangle rather than from an
        // arithmetic walk down from the hat. Two defects forced that, and both were invisible until the
        // real window was driven with a screenshot taken mid-drag.
        var script = Script(
            Stack("ui.log", b => b.WithText("template", "one")),
            Stack("control.forever", b => { }, body =>
            {
                body.Add(Stack("ui.log", lb => lb.WithText("template", "inner")));
            }));

        // The rectangles the canvas measures off the live tiles: the script sits well to the right, and
        // the loop is taller than its label line because it has a statement in it.
        var rects = new Dictionary<string, BlockRect>(StringComparer.Ordinal)
        {
            [script.Hat.Id] = new(700, 40, 180, 44, 84),
            [script.Body[0].Id] = new(700, 84, 160, 44, 128),
            [script.Body[1].Id] = new(700, 128, 200, 132, 260),
            [script.Body[1].Body("body")[0].Id] = new(716, 172, 150, 44, 216),
        };

        var candidates = DropResolver.CandidatesFor(script, rects);

        var innerTop = candidates.Single(c => c is { Kind: DropTargetKind.Mouth, Index: 0, BodyName: "body" });
        var innerFoot = candidates.Single(c => c is { Kind: DropTargetKind.Mouth, Index: 1, BodyName: "body" });

        Assert.Multiple(() =>
        {
            Assert.That(candidates.Where(c => c.Kind == DropTargetKind.StackGap && c.BodyName is not { Length: > 0 }),
                Has.All.Property(nameof(DropCandidate.X)).EqualTo(700),
                "the script's own gaps are at the script's x, not at the canvas origin - every script "
                + "claiming x = 0 gave the scorer no way to prefer the one under the pointer");

            Assert.That(innerTop.Y, Is.EqualTo(172), "the mouth's top is at the first statement's top");
            Assert.That(innerTop.X, Is.EqualTo(716), "and inside the mouth, where that statement is");

            Assert.That(innerFoot.Y, Is.EqualTo(216),
                "the mouth's foot is just below the last statement. It used to be the container's own "
                + "bottom - 260 - which put the top and the foot of the mouth in the same place");
            Assert.That(innerFoot.X, Is.EqualTo(716));
        });
    }

    [Test]
    public void The_magnet_radius_scales_with_zoom()
    {
        var candidate = new DropCandidate(DropTargetKind.StackGap, "p", X: 100, Y: 100);

        // 35px away along y: inside the radius at 100% (40px), outside at 50% (20px).
        Assert.Multiple(() =>
        {
            Assert.That(DropResolver.Resolve("ui.log", 100, 135, [candidate], zoom: 1)!.Magnetic, Is.True);
            Assert.That(DropResolver.Resolve("ui.log", 100, 135, [candidate], zoom: 0.5)!.Magnetic, Is.False);
        });
    }

    [Test]
    public void An_unknown_dragged_kind_resolves_to_nothing()
    {
        var candidate = new DropCandidate(DropTargetKind.StackGap, "p", X: 0, Y: 0);

        Assert.That(DropResolver.Resolve("no.such.kind", 0, 0, [candidate]), Is.Null);
    }

    [Test]
    public void No_candidates_means_no_resolution()
    {
        Assert.That(DropResolver.Resolve("ui.log", 0, 0, []), Is.Null);
    }

    // ---- helpers ------------------------------------------------------------------------------------

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
            foreach (var child in block.Bodies.Values.SelectMany(b => b))
            {
                Number(child, ref next);
            }
        }
    }

    private static Block Stack(
        string kind,
        Action<Block>? configure = null,
        Action<List<Block>>? body = null,
        Action<List<Block>>? then = null,
        Action<List<Block>>? otherwise = null)
    {
        var block = new Block { Kind = kind };
        configure?.Invoke(block);
        body?.Invoke(block.Body("body"));
        then?.Invoke(block.Body("then"));
        otherwise?.Invoke(block.Body("else"));
        return block;
    }

    private static Block Compare() => new()
    {
        Kind = "ops.compare",
        Inputs = { ["a"] = BlockInput.Of(string.Empty) },
    };
}
