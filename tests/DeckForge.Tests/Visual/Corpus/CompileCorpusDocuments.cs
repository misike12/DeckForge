using System.Globalization;
using DeckForge.Core.Visual;

namespace DeckForge.Tests.Visual.Corpus;

/// <summary>
/// The compile corpus: documents built to be emitted, snapshotted, and - where they can be - built
/// against the real SDK.
/// </summary>
/// <remarks>
/// <para>
/// The design asks for "a ~40-document compile corpus and golden-file snapshots" and records that it
/// was not written. This is it. Forty-odd small documents, each existing for one reason, between them
/// touching every block the palette offers and every awkward shape a document can hold: a slot left
/// empty, a slot holding a variable, a slot holding a reporter four reporters deep, a container nested
/// five levels, every menu option, every procedure form, a disabled block, a two-hundred-statement
/// stack, and unicode in labels, comments and literals.
/// </para>
/// <para>
/// Eleven of them are generated from the catalog, one per category, so "the corpus exercises the whole
/// catalogue" is a fact the coverage test can check rather than a claim. The rest are written by hand,
/// because the awkward shapes are not a category - they are a hundred small ways a document can be
/// wrong, and a generator that produced all of them would be a second emitter to maintain.
/// </para>
/// </remarks>
internal static class CompileCorpus
{
    private static readonly BlockCategory[] Categories =
    [
        BlockCategory.Control, BlockCategory.Deck, BlockCategory.Ui, BlockCategory.Events,
        BlockCategory.Sensing, BlockCategory.Operators, BlockCategory.Variables,
        BlockCategory.Lists, BlockCategory.Parameters, BlockCategory.Network,
        BlockCategory.Procedures,
    ];

    /// <summary>
    /// Kinds the category generator leaves to a document written for them by hand.
    /// </summary>
    /// <remarks>
    /// Each one is here for a reason a reader can check, not because the generator could not cope:
    /// a modifier has no emission, a placeholder exists only for migrated documents, a return belongs
    /// inside a procedure rather than at action scope, and hats belong on a script rather than in a
    /// body. Filling them from a catalog row would produce documents that are valid and uninteresting.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> LeftToHandWritten =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control.return"] = "returns from a procedure; a document covering it sits inside one",
            ["control.comment"] = "a modifier; its own document covers how its text is escaped",
            ["control.disable"] = "a modifier; the emitter reports it rather than emitting it",
            ["legacy.unsupported"] = "a placeholder; only a migrated document contains one",
        };

    private static CorpusCase CategoryCase(BlockCategory category)
    {
        var blocks = BlockCatalog.InCategory(category);

        return new CorpusCase(
            $"{category.ToString().ToLowerInvariant()}-blocks",
            $"Every {category} block the palette offers, with every slot and every menu filled.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                if (Uses(blocks, SlotType.List))
                {
                    script.Body.Add(doc.Make("list.define", block => block.WithText("name", CorpusDoc.ListName)));
                }

                // The response readers take the name of a request that has already run, and the emitter
                // turns a name the region has declared back into that local. A document that has not made
                // a request has nothing to name, and the generated call would be a string where the
                // helper wants an HttpResponseRecord - so this one opens with a request.
                if (blocks.Any(descriptor => descriptor.Slots?.Any(slot => slot.Name == "request") == true))
                {
                    script.Body.Add(doc.Make("http.get", block =>
                    {
                        block.WithText("url", "https://example.com/api");
                        block.WithText("into", CorpusDoc.ResponseLocal);
                    }));
                }

                if (Uses(blocks, SlotType.Procedure))
                {
                    doc.Procedure(CorpusDoc.ProcedureName);
                }

                // `break` and `continue` are CS0139 outside a loop, and a document that emits one of
                // them at the top level is not a document a user can build: the canvas only offers them
                // inside a loop body. They go in one.
                var needsLoop = blocks.Any(descriptor => descriptor.Kind is "control.break" or "control.continue");

                foreach (var descriptor in blocks)
                {
                    if (LeftToHandWritten.ContainsKey(descriptor.Kind)
                        || BlocksWithoutAWorkingEmission.ContainsKey(descriptor.Kind)
                        || descriptor.Shape == BlockShape.Hat)
                    {
                        continue;
                    }

                    var filled = doc.Filled(descriptor.Kind);
                    foreach (var slot in descriptor.Slots ?? [])
                    {
                        if (slot.Name == "request")
                        {
                            filled.WithText("request", CorpusDoc.ResponseLocal);
                        }
                    }

                    if (descriptor.IsReporter)
                    {
                        // A reporter cannot be a statement - the emitter reports it as one, which is
                        // what the reporter-at-statement-position document is for. Here it goes where
                        // a reporter really goes: into a variable.
                        script.Body.Add(doc.Make("var.set", block => block
                            .WithVariable("var", Reading)
                            .WithBlock("value", filled)));
                        continue;
                    }

                    var statement = filled;
                    foreach (var body in descriptor.Bodies ?? [])
                    {
                        statement.Body(body.Name).Add(
                            doc.Make("ui.log", log => log.WithText("template", "inside " + body.Name)));
                    }

                    if (descriptor.Kind is "control.break" or "control.continue")
                    {
                        continue;
                    }

                    script.Body.Add(statement);
                }

                if (needsLoop)
                {
                    var loop = doc.Make("control.repeat", block => block.WithNumber("count", 3));
                    loop.Body("body").Add(doc.Make("control.break"));
                    loop.Body("body").Add(doc.Make("ui.log", log => log.WithText("template", "after the break")));
                    loop.Body("body").Add(doc.Make("control.continue"));
                    script.Body.Add(loop);
                }

                return doc.Finish();
            })
        {
            CompiledForReal = true,
        };
    }

    /// <summary>
    /// Blocks the emitter can describe but not currently produce a type-checking call for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row was found by this fixture, not guessed: the generated document is written into a plugin
    /// and built, and the diagnostic is quoted in the row. The block is kept out of the category
    /// documents so that one broken row does not take the other thirty-odd blocks in its category out of
    /// the build, and it is kept in
    /// <see cref="HandWritten"/>'s <c>blocks-whose-generated-call-does-not-typecheck</c> so its emission is
    /// still pinned by a golden file.
    /// </para>
    /// <para>
    /// The fifth column is the SDK or runtime signature the catalog's expression disagrees with. That is
    /// the thing somebody has to change, so it is worth more than the CS code.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> BlocksWithoutAWorkingEmission =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["list.add"] = "CS1061: VisualRuntime.List returns IReadOnlyList<string>, which has no Add.",
            ["list.delete"] = "CS1061: IReadOnlyList<string> has no RemoveAt.",
            ["list.clear"] = "CS1061: IReadOnlyList<string> has no Clear.",
            ["list.insert"] = "CS1061: IReadOnlyList<string> has no Insert.",
            ["list.replace"] = "CS0103: the indexer cannot be assigned on an IReadOnlyList<string>.",
            ["list.join"] = "CS1061: IReadOnlyList<string> has no AddRange.",
            ["list.sort"] = "CS0411: MemoryExtensions.Sort is chosen because IReadOnlyList<string> has no Sort.",
            ["http.request"] = "CS1503: VisualRuntime.Parameters returns IReadOnlyDictionary<string, object> "
                + "and HttpAsync's fourth argument is IReadOnlyDictionary<string, string>?.",
            ["ui.update-widget"] = "CS9035: WidgetAppearanceRequest declares a required member Patch that "
                + "the catalog's object initializer does not set.",
            ["proc.call-value"] = "CS1061: nested in a slot it emits the catalog's placeholder "
                + "`await VisualRuntime.NoToken() ?? Task.CompletedTask`, because only the statement path "
                + "knows how to emit the call. CancellationToken? has no GetAwaiter.",
            ["sensing.format-date"] = "CS1503: VisualRuntime.FormatDate takes a Guid? and the slot is Any, "
                + "so any value the user can put in it arrives as a string.",
            ["sensing.seconds-since"] = "CS1503: VisualRuntime.SecondsSince takes a Guid?, same reason.",
            ["sensing.get-setting"] = "CS1503: IIntegrationConfig.GetStringAsync takes a Guid entryId and "
                + "VisualRuntime.EntryId returns a string.",
            ["sensing.set-setting"] = "CS1503: same entryId, on IIntegrationConfig.SetStringAsync.",
        };

    /// <summary>
    /// The variable every generated document puts a reporter's value into.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>value</c>. <c>var.set</c>'s template is
    /// <c>VisualRuntime.Set(VisualRuntime.ToText(var), value);</c>, and the emitter substitutes the
    /// slots one at a time, so a variable <em>named</em> <c>value</c> is already inside a string literal
    /// by the time the <c>value</c> pass runs - and the pass rewrites the name inside its own literal.
    /// That is a real emitter defect, and <c>slot-name-reused-as-a-variable-name</c> is the document
    /// that pins it. Every other document avoids the trigger so that one defect does not take the whole
    /// build with it.
    /// </remarks>
    private const string Reading = "reading";

    private static bool Uses(IEnumerable<BlockDescriptor> blocks, SlotType type) =>
        blocks.Any(descriptor => (descriptor.Slots ?? []).Any(slot => slot.Type == type));

    // ---- hand-written documents -----------------------------------------------------------------------

    private static readonly IReadOnlyList<CorpusCase> HandWritten =
    [
        new("control-loops",
            "Every loop form, with bodies, break, continue and a yield inside a loop.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                var repeat = doc.Make("control.repeat", block => block.WithNumber("count", 3));
                repeat.Body("body").Add(doc.Make("ui.log", log => log.WithText("template", "tick")));
                script.Body.Add(repeat);

                var until = doc.Make("ops.compare", compare =>
                {
                    compare.WithText("a", "1").WithText("b", "2").WithField("op", ">");
                });
                var repeatUntil = doc.Make("control.repeat-until", block => block.WithBlock("condition", until));
                repeatUntil.Body("body").Add(doc.Make("ui.log", log => log.WithText("template", "at least once")));
                script.Body.Add(repeatUntil);

                var forever = doc.Make("control.forever");
                forever.Body("body").Add(doc.Make("control.break"));
                forever.Body("body").Add(doc.Make("ui.log", log => log.WithText("template", "never reached")));
                script.Body.Add(forever);

                var whileLoop = doc.Make("control.while",
                    block => block.WithBlock("condition", doc.Make("ops.not", not => not.WithBoolean("a", true))));
                whileLoop.Body("body").Add(doc.Make("control.continue"));
                whileLoop.Body("body").Add(doc.Make("control.yield"));
                script.Body.Add(whileLoop);

                script.Body.Add(doc.Make("ui.log", log => log.WithText("template", "after the loops")));
                return doc.Finish();
            }),

        new("control-branches",
            "if, if-else, if-else-if with both arms, if-else-if without the second condition, and an if-else whose else is empty.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                var plain = doc.Make("control.if",
                    block => block.WithBlock("condition", Compare(doc, "=")));
                plain.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "then")));
                script.Body.Add(plain);

                var withElse = doc.Make("control.if-else",
                    block => block.WithBlock("condition", Compare(doc, "!=")));
                withElse.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "then")));
                withElse.Body("else").Add(doc.Make("ui.log", log => log.WithText("template", "else")));
                script.Body.Add(withElse);

                // Present but empty: the emitter drops the else arm rather than writing `else { }`.
                script.Body.Add(doc.Make("control.if-else",
                    block => block.WithBlock("condition", Compare(doc, "<"))));

                var chained = doc.Make("control.if-else-if", block =>
                {
                    block.WithBlock("condition", Compare(doc, "<="));
                    block.WithBlock("condition2", Compare(doc, ">="));
                });
                chained.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "first")));
                chained.Body("elseIf").Add(doc.Make("ui.log", log => log.WithText("template", "second")));
                chained.Body("else").Add(doc.Make("ui.log", log => log.WithText("template", "third")));
                script.Body.Add(chained);

                // An else-if body with no second condition: the emitter has to write `false` for it,
                // because a bare `else if ()` will not compile.
                var halfChain = doc.Make("control.if-else-if",
                    block => block.WithBlock("condition", Compare(doc, "contains")));
                halfChain.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "contains")));
                halfChain.Body("elseIf").Add(doc.Make("ui.log", log => log.WithText("template", "does not")));
                script.Body.Add(halfChain);

                return doc.Finish();
            }),

        new("control-caps",
            "Every cap: both stop blocks, both finish blocks, the throw, and every error code the menu offers.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                script.Body.Add(doc.Make("control.stop-script"));
                script.Body.Add(doc.Make("ui.log", log => log.WithText("template", "unreachable")));

                var codes = BlockCatalog.Find("control.finish-failed")!.Menus![0];
                foreach (var code in codes.Options)
                {
                    var failing = doc.Make("control.finish-failed", block =>
                    {
                        block.WithField("code", code);
                        block.WithText("message", "failed with " + code);
                    });
                    script.Body.Add(failing);
                }

                script.Body.Add(doc.Make("control.finish-accepted",
                    block => block.WithText("message", "still running")));

                var throwing = doc.Make("control.throw",
                    block => block.WithText("message", "the action could not run"));
                script.Body.Add(throwing);

                var second = doc.Script("hat.script-starts", "second script");
                second.Body.Add(doc.Make("control.finish-success"));
                second.Body.Add(doc.Make("control.stop-all"));
                return doc.Finish();
            }),

        new("control-modifiers",
            "The two modifiers and the migration placeholder: a comment block, the disable modifier, and a legacy type.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                script.Body.Add(doc.Make("control.comment", block => block.WithText("text", "a note to the reader")));
                script.Body.Add(doc.Make("control.disable"));
                script.Body.Add(doc.Make("legacy.unsupported",
                    block => block.WithText("legacyType", "old.custom.block")));
                script.Body.Add(doc.Make("ui.log", log => log.WithText("template", "still emitted")));
                return doc.Finish();
            })
        {
            // Two of the three blocks here are things a user cannot drop: a modifier has no emission
            // and a placeholder only exists in a migrated document. Both are reported, and the report
            // is part of what this document pins.
            ExpectsProblems = true,
        },

        new("hats-palette",
            "Every palette hat kind at statement position, which is what a migrated or hand-edited document holds.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                foreach (var hat in BlockCatalog.Blocks.Where(block => block.Shape == BlockShape.Hat))
                {
                    var block = doc.Make(hat.Kind);
                    foreach (var menu in hat.Menus ?? [])
                    {
                        block.WithField(menu.Name, menu.Options[menu.Options.Count - 1]);
                    }

                    foreach (var slot in hat.Slots ?? [])
                    {
                        block.WithText(slot.Name, "hat." + slot.Name);
                    }

                    script.Body.Add(block);
                }

                script.Body.Add(doc.Make("ui.log", log => log.WithText("template", "after the hats")));
                return doc.Finish();
            }),

        new("hats-deferred",
            "Every deferred hat kind, which the catalog deliberately does not carry.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                foreach (var hat in BlockCatalog.Deferred)
                {
                    script.Body.Add(doc.Make(hat.Kind));
                }

                return doc.Finish();
            })
        {
            ExpectsProblems = true,
        },

        new("menus-every-option",
            "Every option of every menu in the catalog, so a renamed or dropped option changes a golden.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("list.define", block => block.WithText("name", CorpusDoc.ListName)));

                // Every menu of every block, not just the first: `var.set-from-parameter` has a type menu
                // and a required menu, and a corpus that only walked menu[0] would never notice the
                // second one gaining an option.
                foreach (var descriptor in BlockCatalog.Blocks.Where(block =>
                             block.Menus is { Count: > 0 }
                             && !BlocksWithoutAWorkingEmission.ContainsKey(block.Kind)))
                {
                    foreach (var menu in descriptor.Menus!)
                    {
                        foreach (var option in menu.Options)
                        {
                            var block = doc.Make(descriptor.Kind);
                            foreach (var other in descriptor.Menus!)
                            {
                                block.WithField(other.Name, other == menu ? option : other.Default);
                            }

                            foreach (var slot in descriptor.Slots ?? [])
                            {
                                doc.FillSlot(block, slot);
                            }

                            if (descriptor.IsReporter)
                            {
                                script.Body.Add(doc.Make("var.set", set => set
                                    .WithVariable("var", Reading)
                                    .WithBlock("value", block)));
                            }
                            else
                            {
                                script.Body.Add(block);
                            }
                        }
                    }
                }

                doc.Procedure(CorpusDoc.ProcedureName);
                return doc.Finish();
            }),

        new("slots-left-empty",
            "Every block with every slot empty, so the coerced defaults are pinned: zero, false and string.Empty.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                doc.Procedure(CorpusDoc.ProcedureName);
                var loopOnly = new List<string>();

                foreach (var descriptor in BlockCatalog.Blocks)
                {
                    if (LeftToHandWritten.ContainsKey(descriptor.Kind)
                        || descriptor.Shape is BlockShape.Hat or BlockShape.Modifier)
                    {
                        continue;
                    }

                    if (descriptor.Kind is "control.break" or "control.continue")
                    {
                        loopOnly.Add(descriptor.Kind);
                        continue;
                    }

                    if (descriptor.IsReporter)
                    {
                        script.Body.Add(doc.Make("var.set", set => set
                            .WithVariable("var", Reading)
                            .WithBlock("value", doc.Make(descriptor.Kind))));
                    }
                    else
                    {
                        script.Body.Add(doc.Make(descriptor.Kind));
                    }
                }

                if (loopOnly.Count > 0)
                {
                    var loop = doc.Make("control.repeat", block => block.WithNumber("count", 3));
                    foreach (var kind in loopOnly)
                    {
                        loop.Body("body").Add(doc.Make(kind));
                    }

                    script.Body.Add(loop);
                }

                return doc.Finish();
            })
        {
            CompiledForReal = false,
            NotCompiledReason =
                "Leaving every slot empty is a shape the compiler rejects in four different ways, and the "
                + "golden file is how each one is pinned: CS0117, because `ui.notify-level` falls back to a "
                + "level of `Information` and the SDK's enum spells it `Info`; CS1503, because an empty "
                + "Any, List or request slot is emitted as the identifier `string.Empty` where the helper "
                + "wants a Guid?, an IReadOnlyList<string> or an HttpResponseRecord; CS0103, because "
                + "`proc.call` with no name emits `procedureAsync()`. The coerced defaults this document "
                + "exists for - 0, false - are visible in the golden either way.",
        },

        new("slots-literals",
            "The three literal kinds side by side in one Any slot each, plus the boolean literal.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "text").WithText("value", "hello")));
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "number").WithNumber("value", 2.5)));
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "flag").WithBoolean("value", true)));
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "negative").WithNumber("value", -0.125)));
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "big").WithNumber("value", 1e21)));
                script.Body.Add(doc.Make("var.change", block => block.WithVariable("var", "count").WithNumber("amount", 0)));
                return doc.Finish();
            }),

        new("slots-variables",
            "A slot holding a variable reference rather than a literal, which emits the identifier and not the name.",
            () =>
            {
                var doc = new CorpusDoc()
                    .Variable("count", "Numeric", "0")
                    .Variable("label", "Text", "\"hello\"")
                    .Variable("enabled", "Boolean", "false", VariableScope.Host);

                var script = doc.Script();
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "count").WithNumber("value", 0)));
                script.Body.Add(doc.Make("var.change", block => block.WithVariable("var", "count").WithBlock("amount", doc.Make("var.get", get => get.WithVariable("var", "count")))));
                var overThree = doc.Make("ops.compare", block =>
                {
                    block.WithBlock("a", doc.Make("var.get", get => get.WithVariable("var", "count")));
                    block.WithText("b", "3").WithField("op", ">");
                });
                var overThreeIf = doc.Make("control.if", block => block.WithBlock("condition", overThree));
                overThreeIf.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "more than three")));
                script.Body.Add(overThreeIf);
                var labelled = doc.Make("ops.compare", compare =>
                {
                    compare.WithBlock("a", doc.Make("var.get", get => get.WithVariable("var", "label")));
                    compare.WithText("b", "hello").WithField("op", "isNotEmpty");
                });
                script.Body.Add(doc.Make("control.wait-until", block => block.WithBlock("condition", labelled)));
                return doc.Finish();
            }),

        new("slots-reporters-nested",
            "Reporters four deep, with a variable, a literal and a nested reporter in every position.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                var fourDeep = doc.Make("ops.clamp", clamp =>
                {
                    clamp.WithBlock("value", doc.Make("ops.round", round =>
                        round.WithBlock("value", doc.Make("ops.multiply", multiply =>
                        {
                            multiply.WithBlock("a", doc.Make("ops.add", add =>
                            {
                                add.WithBlock("a", doc.Make("var.get", get => get.WithVariable("var", "count")));
                                add.WithNumber("b", 2);
                            }));
                            multiply.WithNumber("b", 1.5);
                        }))));
                    clamp.WithNumber("low", 0);
                    clamp.WithBlock("high", doc.Make("ops.add", add =>
                    {
                        add.WithNumber("a", 10);
                        add.WithBlock("b", doc.Make("var.get", get => get.WithVariable("var", "limit")));
                    }));
                });

                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "result").WithBlock("value", fourDeep)));
                script.Body.Add(doc.Make("control.wait-seconds",
                    block => block.WithBlock("seconds", doc.Make("ops.divide", divide =>
                    {
                        divide.WithNumber("a", 10);
                        divide.WithNumber("b", 4);
                    }))));
                return doc.Finish();
            }),

        new("slots-boolean-reporters",
            "Boolean blocks in every slot that takes a condition, including a boolean nested in a boolean.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                var condition = doc.Make("ops.and", and =>
                {
                    and.WithBlock("a", doc.Make("ops.compare", compare =>
                    {
                        compare.WithText("a", "1").WithText("b", "2").WithField("op", "isNotEmpty");
                    }));
                    and.WithBlock("b", doc.Make("ops.or", or =>
                    {
                        or.WithBoolean("a", false);
                        or.WithBlock("b", doc.Make("ops.not", not =>
                            not.WithBlock("a", doc.Make("ops.contains", contains =>
                            {
                                contains.WithText("text", "haystack").WithText("part", "needle");
                            }))));
                    }));
                });

                var guard = doc.Make("control.repeat-until", block => block.WithBlock("condition", condition));
                guard.Body("body").Add(doc.Make("ui.log", log => log.WithText("template", "inside")));
                script.Body.Add(guard);

                script.Body.Add(doc.Make("control.wait-until", block => block.WithBlock("condition", condition)));
                var established = doc.Make("control.if",
                    block => block.WithBlock("condition", doc.Make("sensing.session-established")));
                established.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "connected")));
                script.Body.Add(established);
                return doc.Finish();
            }),

        new("nesting-five-levels",
            "The five levels of nesting §28.1 asks for, each level holding the next.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                var level1 = doc.Make("control.forever");
                var level2 = level1.Body("body");
                var level3 = doc.Make("control.repeat", block => block.WithNumber("count", 2));
                var level4 = level3.Body("body");
                var level5 = doc.Make("control.if", block => block.WithBlock("condition", Compare(doc, "=")));
                var level6 = doc.Make("control.if-else", block => block.WithBlock("condition", Compare(doc, "!=")));

                level2.Add(level3);
                level4.Add(level5);
                level5.Body("then").Add(level6);
                level6.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "five deep")));
                level6.Body("else").Add(doc.Make("ui.log", log => log.WithText("template", "also five deep")));
                script.Body.Add(level1);
                return doc.Finish();
            }),

        new("nesting-else-if-chain",
            "A five-arm chain of else-if, the shape that is one chain in C# and five containers on the canvas.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                var chain = doc.Make("control.if-else-if", block =>
                {
                    block.WithBlock("condition", Compare(doc, "="));
                    block.WithBlock("condition2", Compare(doc, "!="));
                });

                var body = chain.Body("then");
                for (var arm = 0; arm < 5; arm++)
                {
                    var log = doc.Make("ui.log", item => item.WithText("template", "arm " + arm));
                    if (arm == 0)
                    {
                        body.Add(log);
                        continue;
                    }

                    var next = doc.Make("control.if-else-if", next => next
                        .WithBlock("condition", Compare(doc, "<"))
                        .WithBlock("condition2", Compare(doc, "contains")));
                    next.Body("then").Add(log);
                    body = next.Body("else");
                    body.Add(doc.Make("ui.log", item => item.WithText("template", "else " + arm)));
                }

                script.Body.Add(chain);
                return doc.Finish();
            }),

        new("procedure-plain",
            "A procedure with no parameters and no return value, called as a statement.",
            () =>
            {
                var doc = new CorpusDoc();
                doc.Procedure("announce");
                var procedure = doc.Procedures[^1];
                doc.Body(procedure, "ui.log", block => block.WithText("template", "announced"));

                doc.Script().Body.Add(doc.Make("proc.call", block => block.WithText("name", "announce")));
                return doc.Finish();
            }),

        new("procedure-parameters",
            "A procedure with parameters, bound into the body's locals, and a call that supplies them by name.",
            () =>
            {
                var doc = new CorpusDoc();
                doc.Procedure("greet", false,
                    new ProcedureParameter { Name = "who", Type = "Text" },
                    new ProcedureParameter { Name = "times", Type = "Numeric" });
                var procedure = doc.Procedures[^1];
                var repeat = doc.Make("control.repeat", block => block.WithNumber("count", 3));
                repeat.Body("body").Add(doc.Make("ui.log", block => block.WithText("template", "who")));
                procedure.Body.Add(repeat);
                doc.Body(procedure, "var.set", block =>
                    block.WithVariable("var", "copied").WithBlock("value", doc.Make("var.get", get => get.WithVariable("var", "missing-parameter"))));

                doc.Script().Body.Add(doc.Make("proc.call-with", block =>
                {
                    block.WithText("name", "greet");
                    block.WithText("args", "who=world\ntimes=3");
                }));
                return doc.Finish();
            }),

        new("procedure-returns",
            "A procedure that returns a value, its call used as a reporter and as a statement, and a return of a nested reporter.",
            () =>
            {
                var doc = new CorpusDoc();
                doc.Procedure("compute", true, new ProcedureParameter { Name = "n", Type = "Numeric" });
                var procedure = doc.Procedures[^1];
                var parameter = doc.Make("var.get", get => get.WithVariable("var", "n"));
                var sum = doc.Make("ops.add", add =>
                {
                    add.WithBlock("a", parameter);
                    add.WithNumber("b", 1);
                });
                var rounded = doc.Make("ops.round", round => round.WithBlock("value", sum));
                doc.Body(procedure, "control.return", block => block.WithBlock("value", rounded));

                var script = doc.Script();
                script.Body.Add(doc.Make("proc.call-value", block =>
                {
                    block.WithText("name", "compute");
                    block.WithText("args", "n=3");
                }));
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "answer")
                    .WithBlock("value", doc.Make("proc.call-value", call =>
                    {
                        call.WithText("name", "compute");
                        call.WithText("args", "n=4\nunknown=9");
                    }))));
                return doc.Finish();
            })
        {
            CompiledForReal = false,
            NotCompiledReason =
                "`proc.call-value` is shaped like a reporter, so the drop resolver will let a user plug "
                + "it into a slot - but the emitter only knows how to emit it at statement position. In a "
                + "slot it falls through to the catalog's placeholder expression and writes "
                + "`await VisualRuntime.NoToken() ?? Task.CompletedTask`, which is CS0019 and, worse, a "
                + "generated action that quietly does nothing. The golden file shows both.",
        },

        new("procedure-uncalled",
            "Two declared procedures and one call: the uncalled one must cost nothing in the region.",
            () =>
            {
                var doc = new CorpusDoc();
                doc.Procedure("used");
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "used"));
                doc.Procedure("unused");
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "never emitted"));
                doc.Procedure("nested", false, new ProcedureParameter { Name = "value", Type = "Any" });
                doc.Body(doc.Procedures[^1], "proc.call", block => block.WithText("name", "used"));

                doc.Script().Body.Add(doc.Make("proc.call", block => block.WithText("name", "used")));
                return doc.Finish();
            }),

        new("procedure-call-unknown",
            "A call to a procedure the document does not declare: the emitter reports it and still writes a call.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("proc.call", block => block.WithText("name", "neverDeclared")));
                script.Body.Add(doc.Make("proc.call-with", block =>
                {
                    block.WithText("name", "alsoMissing");
                    block.WithText("args", "a=1");
                }));
                script.Body.Add(doc.Make("proc.call-value", block => block.WithText("name", "missingValue")));
                return doc.Finish();
            })
        {
            ExpectsProblems = true,
            CompiledForReal = false,
            NotCompiledReason =
                "The emitter writes `await neverDeclaredAsync()` for a procedure the document never "
                + "declared, which is CS0103 in the generated action. That is the emitter's behaviour to "
                + "pin, not a corpus defect, so this document is snapshotted and not built. Fixing it "
                + "means changing the emitter - and when it does, this document joins the built set and "
                + "this sentence goes with it.",
        },

        new("slot-name-reused-as-a-variable-name",
            "A variable named after one of the block's own slots, which the emitter substitutes twice.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "value")
                    .WithBlock("value", doc.Make("ops.round", round => round.WithNumber("value", 7)))));
                script.Body.Add(doc.Make("var.change", block => block
                    .WithVariable("var", "amount")
                    .WithNumber("amount", 1)));
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "var")
                    .WithText("value", "safe")));
                return doc.Finish();
            })
        {
            CompiledForReal = false,
            NotCompiledReason =
                "`var.set` emits `VisualRuntime.Set(VisualRuntime.ToText(\"value\"), value);` and the "
                + "emitter fills one slot per pass, so the `value` pass finds the word inside the "
                + "variable's own string literal and rewrites it. The result is CS1003 - the region ends "
                + "up holding a reporter expression embedded in a string. Snapshot-only, and the golden "
                + "file shows the mangling on its own line; every other document avoids naming a "
                + "variable after a slot so this one defect does not fail the whole build.",
        },

        new("blocks-whose-generated-call-does-not-typecheck",
            "Every block whose generated call the C# compiler rejects, with the SDK or runtime signature it disagrees with.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("list.define", block => block.WithText("name", CorpusDoc.ListName)));
                doc.Procedure(CorpusDoc.ProcedureName);

                foreach (var descriptor in BlockCatalog.Blocks)
                {
                    if (!BlocksWithoutAWorkingEmission.ContainsKey(descriptor.Kind))
                    {
                        continue;
                    }

                    var variants = descriptor.Menus is { Count: > 0 }
                        ? descriptor.Menus.SelectMany(menu => menu.Options.Select(option => (menu, option))).ToList()
                        : [(null, null)];

                    foreach (var (chosen, option) in variants)
                    {
                        var block = doc.Filled(descriptor.Kind);
                        foreach (var body in descriptor.Bodies ?? [])
                        {
                            block.Body(body.Name).Add(doc.Make("ui.log", log => log.WithText("template", "inside")));
                        }

                        if (chosen is not null)
                        {
                            block.WithField(chosen.Name, option);
                        }

                        if (descriptor.IsReporter)
                        {
                            script.Body.Add(doc.Make("var.set", set => set
                                .WithVariable("var", Reading)
                                .WithBlock("value", block)));
                        }
                        else
                        {
                            script.Body.Add(block);
                        }
                    }
                }

                // The same coercion problem the two date blocks have, reached through a loop count:
                // a variable reference in a Number slot is emitted as a bare identifier.
                script.Body.Add(doc.Make("control.repeat", block =>
                    block.WithBlock("count", doc.Make("var.get", get => get.WithVariable("var", "times")))));
                return doc.Finish();
            })
        {
            CompiledForReal = false,
            NotCompiledReason =
                "Every block in this document generates a call the compiler rejects: CS1061 and CS0411 for "
                + "the list mutators against an IReadOnlyList<string>, CS1503 for http.request's headers "
                + "and for the two date blocks' Guid?, CS9035 for ui.update-widget's required Patch, "
                + "CS1061 for a procedure call nested in a slot, and CS0019 for a variable used as a "
                + "loop count. Each is pinned in its own golden file; this one is the summary, and it is "
                + "excluded so the rest of the corpus can still be built.",
        },

        new("declarations-variables-and-lists",
            "A document that declares variables and lists of every type, and a script that uses them.",
            () =>
            {
                var doc = new CorpusDoc()
                    .Variable("count", "Numeric", "0")
                    .Variable("label", "Text", "\"\"")
                    .Variable("enabled", "Boolean", "false")
                    .Variable("shared", "Text", null, VariableScope.Host)
                    .List(CorpusDoc.ListName, "Text", "one", "two")
                    .List("numbers", "Numeric")
                    .List("flags", "Boolean", "true");

                var script = doc.Script();
                script.Body.Add(doc.Make("list.define", block => block.WithText("name", CorpusDoc.ListName)));
                script.Body.Add(doc.Make("var.set", block =>
                    block.WithVariable("var", "size")
                        .WithBlock("value", doc.Make("list.length", length => length.WithText("list", CorpusDoc.ListName)))));
                script.Body.Add(doc.Make("var.set", block =>
                    block.WithVariable("var", "first")
                        .WithBlock("value", doc.Make("list.item", item =>
                        {
                            item.WithText("index", "1").WithText("list", CorpusDoc.ListName);
                        }))));
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "count").WithNumber("value", 1)));
                return doc.Finish();
            }),

        new("disabled-blocks",
            "A disabled statement, a disabled container with children in it, and a disabled block carrying a comment.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();

                var parked = doc.Make("ui.log", block =>
                {
                    block.Disabled = true;
                    block.WithText("template", "parked");
                });
                script.Body.Add(parked);

                var container = doc.Make("control.repeat", block =>
                {
                    block.Disabled = true;
                    block.WithNumber("count", 10);
                });
                container.Body("body").Add(doc.Make("ui.log", log => log.WithText("template", "inside a parked loop")));
                var innerIf = doc.Make("control.if", block => block.WithBoolean("condition", true));
                innerIf.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "also parked")));
                container.Body("body").Add(innerIf);
                script.Body.Add(container);

                var annotated = doc.Make("ui.log", block =>
                {
                    block.Comment = "kept for the next release";
                    block.WithText("template", "annotated");
                });
                script.Body.Add(annotated);
                return doc.Finish();
            }),

        new("disabled-script",
            "A switched-off script among switched-on ones, which the emitter skips whole.",
            () =>
            {
                var doc = new CorpusDoc();
                var live = doc.Script("hat.action-runs", "live");
                live.Body.Add(doc.Make("ui.log", block => block.WithText("template", "runs")));

                var parked = doc.Script("hat.action-runs", "parked", disabled: true);
                parked.Body.Add(doc.Make("ui.log", block => block.WithText("template", "never emitted")));
                var parkedLoop = doc.Make("control.forever");
                parkedLoop.Body("body").Add(doc.Make("ui.log", block => block.WithText("template", "nor this")));
                parked.Body.Add(parkedLoop);

                var last = doc.Script("hat.action-runs", "last");
                last.Body.Add(doc.Make("ui.log", block => block.WithText("template", "also runs")));
                return doc.Finish();
            }),

        new("wide-stack",
            "Two hundred statements in one body: the shape a generated document actually reaches.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                for (var index = 0; index < 200; index++)
                {
                    script.Body.Add(doc.Make("ui.log", block =>
                        block.WithText("template", "statement " + index.ToString(CultureInfo.InvariantCulture))));
                }

                return doc.Finish();
            }),

        new("unicode-everywhere",
            "Non-ASCII text in every place text can go: a script name, a comment, literals and a log template.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script(hat: "hat.action-runs", name: "スクリプト – скрипт");
                script.Body.Add(doc.Make("ui.log", block =>
                {
                    block.Comment = "Ünicode comment: 日本語, emoji 🎛️, Ω≈ç√∫, and a combining é.";
                    block.WithText("template", "Grüße 🎛️ 日本語 «quoted» 100% \\backslash\\");
                }));
                script.Body.Add(doc.Make("control.comment", block =>
                    block.WithText("text", "note: مرحبا بالعالم and zero-width\u200Bhere")));
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "café")
                    .WithText("value", "naïve — dash, ‹angle›, and a tab\there")));
                return doc.Finish();
            }),

        new("unicode-comment-with-newline",
            "A comment block whose text carries a newline, which the emitter writes into the comment verbatim.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("control.comment", block =>
                    block.WithText("text", "first line\nsecond line")));
                script.Body.Add(doc.Make("ui.log", block => block.WithText("template", "after the comment")));
                return doc.Finish();
            })
        {
            CompiledForReal = false,
            NotCompiledReason =
                "A `control.comment` writes its text straight after `//`, so a newline in that text "
                + "becomes a bare line of C# in the region - `second line`, uncommented, which is CS0101. "
                + "Snapshot-only until the emitter splits the comment the way it splits an attached one; "
                + "the defect is visible in the golden file, one line below the header.",
        },

        new("comments",
            "Block comments: one line, several lines, a unicode comment, and a comment on a block that is then disabled.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("ui.log", block =>
                {
                    block.Comment = "one line";
                    block.WithText("template", "first");
                }));
                script.Body.Add(doc.Make("ui.log", block =>
                {
                    block.Comment = "three lines\r\nsecond\r\nthird";
                    block.WithText("template", "second");
                }));
                var disabled = doc.Make("ui.log", block =>
                {
                    block.Disabled = true;
                    block.Comment = "the comment a disabled block loses";
                    block.WithText("template", "parked");
                });
                script.Body.Add(disabled);
                script.Body.Add(doc.Make("control.repeat", block =>
                {
                    block.Comment = "a comment on the container";
                    block.WithNumber("count", 1);
                }));
                script.Body[3].Body("body").Add(doc.Make("ui.log", block =>
                {
                    block.Comment = "and one on the child";
                    block.WithText("template", "child");
                }));
                return doc.Finish();
            }),

        new("unknown-kind",
            "A kind this build does not know, a deferred kind, and a kind that is only whitespace.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("ui.log", block => block.WithText("template", "before")));
                script.Body.Add(doc.Make("someone.elses.block"));
                script.Body.Add(doc.Make(string.Empty));
                script.Body.Add(doc.Make("ui.log", block => block.WithText("template", "after")));
                return doc.Finish();
            })
        {
            ExpectsProblems = true,
        },

        new("reporter-at-statement-position",
            "Reporters and booleans used as statements, which the canvas refuses and the emitter reports.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("ops.add", block =>
                {
                    block.WithNumber("a", 1).WithNumber("b", 2);
                }));
                script.Body.Add(doc.Make("ops.not", block => block.WithBoolean("a", true)));
                script.Body.Add(doc.Make("deck.client-count"));
                script.Body.Add(doc.Make("var.get", block => block.WithVariable("var", "count")));
                // The one exception: a call that returns a value is a statement too.
                doc.Procedure("compute", true);
                doc.Body(doc.Procedures[^1], "control.return", block => block.WithNumber("value", 1));
                script.Body.Add(doc.Make("proc.call-value", block => block.WithText("name", "compute")));
                script.Body.Add(doc.Make("ui.log", block => block.WithText("template", "unreached")));
                return doc.Finish();
            })
        {
            ExpectsProblems = true,
        },

        new("statement-in-a-slot",
            "A statement put in a value slot, and one put in a boolean slot: both reported, neither emitted as code.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("control.wait-seconds", block =>
                    block.WithBlock("seconds", doc.Make("ui.log", log => log.WithText("template", "nested")))));
                script.Body.Add(doc.Make("control.if", block => block
                    .WithBlock("condition", doc.Make("control.repeat", repeat => repeat.WithNumber("count", 1)))));
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "x")
                    .WithBlock("value", doc.Make("deck.client-count"))));
                script.Body.Add(doc.Make("ops.add", block =>
                {
                    // A reporter in a reporter's slot is fine; an unknown kind in one is not.
                    block.WithBlock("a", doc.Make("someone.elses.reporter"));
                    block.WithNumber("b", 1);
                }));
                return doc.Finish();
            })
        {
            ExpectsProblems = true,
            CompiledForReal = false,
            NotCompiledReason =
                "When a slot holds a block that is not a reporter, the emitter reports the problem and "
                + "returns a bare `string.Empty` - without the coercion the slot asked for. A Boolean slot "
                + "then reads `if (string.Empty)`, which is CS0029. The diagnostic is correct; the "
                + "substitution is not.",
        },

        new("host-reporter-in-a-slot",
            "A reporter that reaches for _integration, used inside a slot, in a document whose only other block is hostless.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", Reading)
                    .WithBlock("value", doc.Make("deck.client-count"))));
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "folder")
                    .WithBlock("value", doc.Make("deck.current-folder"))));
                return doc.Finish();
            })
        {
            CompiledForReal = false,
            NotCompiledReason =
                "The host guard is noted by the statement emitter, so a reporter nested in a slot never "
                + "asks for one: this document dereferences `_integration` with no `if (_integration is "
                + "null)` above it, which is CS8602 in a plugin with nullable enabled and a "
                + "NullReferenceException in one without. A document that also has a host-touching "
                + "statement happens to get the guard by accident, which is why this needs its own "
                + "document to be visible at all.",
        },

        new("multiple-scripts-one-target",
            "Three scripts in one target, one of them switched off, with one procedure called from two of them.",
            () =>
            {
                var doc = new CorpusDoc();
                var shared = doc.Procedure("shared", false, new ProcedureParameter { Name = "n", Type = "Numeric" });
                doc.Body(shared, "ui.log", block => block.WithText("template", "n"));

                var first = doc.Script("hat.action-runs", "first");
                first.Body.Add(doc.Make("proc.call-with", block =>
                {
                    block.WithText("name", "shared");
                    block.WithText("args", "n=1");
                }));

                var parked = doc.Script("hat.action-runs", "parked", disabled: true);
                parked.Body.Add(doc.Make("proc.call", block => block.WithText("name", "shared")));

                var third = doc.Script("hat.script-starts", "third");
                third.Body.Add(doc.Make("proc.call", block => block.WithText("name", "shared")));
                return doc.Finish();
            }),

        new("two-targets-one-procedure",
            "Two action targets on one document, with a procedure called from both and one only from the first.",
            () =>
            {
                var doc = new CorpusDoc();
                doc.Procedure("both", false);
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "both"));
                doc.Procedure("firstOnly", false);
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "first only"));

                var first = doc.Script("hat.action-runs", "first");
                first.Body.Add(doc.Make("proc.call", block => block.WithText("name", "both")));
                first.Body.Add(doc.Make("proc.call", block => block.WithText("name", "firstOnly")));

                var second = doc.AddTarget("action:second", "Second action");
                var secondScript = new VisualScript
                {
                    Id = "script-second",
                    Name = "second",
                    Hat = new Block { Kind = "hat.action-runs", Id = "hat-second" },
                };
                secondScript.Body.Add(doc.Make("proc.call", block => block.WithText("name", "both")));
                secondScript.Body.Add(doc.Make("ui.log", block => block.WithText("template", "second action")));
                second.Scripts.Add(secondScript);
                return doc.Finish();
            }),

        new("http-response-local",
            "A request whose response local is read back by the blocks that consume it.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("http.get", block =>
                {
                    block.WithText("url", "https://example.com/api");
                    block.WithText("into", "response");
                }));
                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "status")
                    .WithBlock("value", doc.Make("http.status-code", status => status.WithText("request", "response")))));

                var succeeded = doc.Make("http.is-success",
                    success => success.WithText("request", "response"));
                var branch = doc.Make("control.if", block => block.WithBlock("condition", succeeded));
                branch.Body("then").Add(doc.Make("var.set", set => set
                    .WithVariable("var", "body")
                    .WithBlock("value", doc.Make("http.response-body", body =>
                        body.WithText("request", "response")))));
                branch.Body("then").Add(doc.Make("ui.log", log => log.WithText("template", "status ${status}")));
                branch.Body("else").Add(doc.Make("var.set", set => set
                    .WithVariable("var", "timedOut")
                    .WithBlock("value", doc.Make("http.timed-out", timed =>
                        timed.WithText("request", "response")))));
                script.Body.Add(branch);

                script.Body.Add(doc.Make("var.set", block => block
                    .WithVariable("var", "type")
                    .WithBlock("value", doc.Make("http.response-header", header =>
                    {
                        header.WithText("name", "content-type").WithText("request", "response");
                    }))));
                return doc.Finish();
            }),

        new("http-into-collision",
            "Two requests declaring the same response name: the second local has to be renamed, not redeclared.",
            () =>
            {
                var doc = new CorpusDoc();
                var script = doc.Script();
                script.Body.Add(doc.Make("http.get", block =>
                {
                    block.WithText("url", "https://example.com/one");
                    block.WithText("into", "response");
                }));
                script.Body.Add(doc.Make("http.post", block =>
                {
                    block.WithText("url", "https://example.com/two");
                    block.WithText("body", "{}");
                    block.WithText("into", "response");
                }));
                script.Body.Add(doc.Make("http.download", block =>
                {
                    block.WithText("url", "https://example.com/three");
                    block.WithText("path", "third.json");
                }));
                script.Body.Add(doc.Make("http.set-bearer", block => block.WithText("name", "token")));
                return doc.Finish();
            }),

        new("identifiers-that-are-keywords",
            "Variable, list and procedure names that are C# keywords, and a name that already ends in Async.",
            () =>
            {
                var doc = new CorpusDoc()
                    .Variable("int", "Numeric")
                    .Variable("class", "Text")
                    .Variable("await", "Text");

                var script = doc.Script();
                script.Body.Add(doc.Make("list.define", block => block.WithText("name", "class")));
                script.Body.Add(doc.Make("var.set", block => block.WithVariable("var", "int").WithNumber("value", 1)));

                doc.Procedure("Wait");
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "a method-shaped name"));
                doc.Procedure("alreadyAsync", false, new ProcedureParameter { Name = "value", Type = "Any" });
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "value"));
                doc.Procedure("with space and 1", false);
                doc.Body(doc.Procedures[^1], "ui.log", block => block.WithText("template", "sanitised"));

                script.Body.Add(doc.Make("proc.call", block => block.WithText("name", "Wait")));
                script.Body.Add(doc.Make("proc.call-with", block =>
                {
                    block.WithText("name", "alreadyAsync");
                    block.WithText("args", "value=1");
                }));
                script.Body.Add(doc.Make("proc.call", block => block.WithText("name", "with space and 1")));
                return doc.Finish();
            }),
    ];

    /// <summary>A boolean block for the branch documents.</summary>
    private static Block Compare(CorpusDoc doc, string op) =>
        doc.Make("ops.compare", compare =>
        {
            compare.WithText("a", "left").WithText("b", "right").WithField("op", op);
        });

    /// <summary>
    /// Every document, in the order the goldens are reviewed.
    /// </summary>
    /// <remarks>
    /// Declared after the two collections it draws from, because a static initialiser runs in
    /// declaration order and an earlier one would have read two nulls.
    /// </remarks>
    public static IReadOnlyList<CorpusCase> All { get; } =
    [
        .. Categories.Select(CategoryCase),
        .. HandWritten,
    ];

    /// <summary>The goldens as one reviewable block of text, for the deliberate rewrite.</summary>
    public static string Header(CorpusCase corpusCase) =>
        $"// ==== golden: {corpusCase.Name} ====\n"
        + $"// Covers: {corpusCase.Covers}\n"
        + "// Rewrite deliberately: DECKFORGE_REWRITE_CORPUS_GOLDENS=1 dotnet test tests/DeckForge.Tests "
        + "--filter \"Name~Rewrite_the_golden_files\"\n"
        + $"// Compiled for real: {(corpusCase.CompiledForReal ? "yes" : "no - " + corpusCase.NotCompiledReason)}\n";
}