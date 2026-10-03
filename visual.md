# visual.md — DeckForge Visual Block Editor

**Status:** design of record, pre-implementation.
**Owner:** DeckForge App / Core / CodeGen.
**Supersedes:** the MVP `Blocks` page (`Pages/BlockActionPage.xaml`, `ViewModels/BlockActionViewModel.cs`).

This document is the specification, the plan and the reference for a Scratch-class visual block
editor shipped as a new sidebar tab called **Visual**.

Nothing in this document is a wish: each claim is tied to a real constraint already present in this
repository, and the constraints are named where they apply. Every block in the catalog carries a
verification flag, and a block may only ship once Appendix A records the SDK member that was
inspected.

### Table of contents

| Part | Title | What it settles |
|---|---|---|
| 1 | Executive summary | the four structural facts that drive the design |
| 2 | Analysis | what Scratch is; which past defects become rules |
| 3 | Product specification | locked decisions, goals, non-goals |
| 4 | Architecture fit | module map, the sealed action class, greenfield drag |
| 5 | Scratch semantics translated | nouns, shape grammar, the coercion table |
| 6 | Domain model | document, block tree, migration, validation |
| 7 | Block catalog | 187 blocks across 12 categories |
| 8 | Code generation | emitters, generated runtime library, target writers |
| 9 | User interface | layout, visual language, drag and drop |
| 10 | Simulator, tracer and debugger | interpreter, stage, breakpoints |
| 11 | Persistence | workspace layout, order-safe save |
| 12 | Testing | unit, compile, geometry, real-window |
| 13 | Phased implementation plan | P0–P10 with an exit criterion each |
| 14 | Risks and mitigations | |
| 15 | File manifest | new, changed, deleted |
| 16 | Open questions for P0 | the SDK gates |
| 17 | Gap analysis: Visual versus Scratch | where we equal, exceed and trail |
| 18 | Interaction specification | every pointer gesture and keyboard binding |
| 19 | Motion and animation | durations, easing, reduced motion |
| 20 | Settings | new keys, defaults, ranges, storage |
| 21 | Localization of block labels | resx keys, RTL, plurals |
| 22 | Versioning and deprecation | schema policy and block-id guarantees |
| 23 | Extension contract | third-party block providers |
| 24 | Round-trip | blocks from code already in the file |
| 25 | Sharing, export and clipboard | `.dfblock`, PNG, SVG, paste |
| 26 | Scratch `.sb3` import | mapping and honest degradation |
| 27 | Safety, trust and threat model | network, secrets, extensions |
| 28 | Acceptance criteria and metrics | the definition of done |
| A | SDK inventory | the gate for every block row |
| B | Semantics table | interpreter/emitter parity |
| C | Glossary | |
| D | Worked example: fetch and notify | JSON, generated C#, interpreter trace |
| E | Worked example: loop and procedure | JSON, generated C#, interpreter trace |
| F | Diagnostics catalogue | codes, severities, hints |
| G | Undo/redo command catalogue | commands, inverses, coalescing |
| H | Performance budget | targets and how each is measured |
| I | Contributor review checklist | what a reviewer checks on a Visual PR |
| — | Change log | history of this document |

> **Reading order for an implementer**
> 1. Part 4 (architecture) and Part 5 (semantics) — the two decisions that shape everything.
> 2. Part 6 (model) and Part 8 (code generation) — what gets built first, in Core.
> 3. Part 9 (UI) and Part 10 (simulator) — the visible half.
> 4. Part 13 (phases) — the order of work, with an exit criterion per phase.
> 5. Appendix A — the SDK inventory that gates the block catalog. **Read before marking any block
>    `IsVerified: true`.**
> 6. Appendix D and E — worked examples: a canvas, its sidecar JSON, and the exact C# it emits. The
>    fastest way to understand the emission model.

---

## Progress tracker

**Current position: P10 in progress — **P10a–P10f complete** (one command table behind the palette, the sheet and the keys; block comments with undo coalescing; palette recents and pins per workspace; zoom, pan and a clickable minimap; §9.9's performance budget, measured on the half of a frame Core owns; the SVG export, drawn from the layout model; three contrast levels — 820 green). Outstanding: driving the stage panel in the real window, because the synthetic input harness stopped reaching the window. Next in P10: block-label localization and the documentation sweep.**

| Phase | Status | Evidence |
|---|---|---|
| P0 — Ground truth | ✅ **complete** | Appendix A filled from the real assembly; Part 7.16 records the verdicts; baseline build and tests recorded below |
| P1a — Core document model | ✅ **complete** | `Core/Visual/{BlockShapes,Block,VisualProject,VisualProjectJson}.cs` + `Migrations/BlocksV1Migration.cs`; 38 new tests, full suite 387 green |
| P1b — Block catalog | ✅ **complete** | `BlockCatalog.cs` + `BlockCatalogRows.cs`: **156 shipping blocks** across 11 palette categories (Media stays empty by design), 7 deferred, 28 dropped, 1 placeholder; 7 discovered blocks included in the 156. 31 new tests in `BlockCatalogTests`, full suite 444 green. As-built §7.17 records the drift from the §7.3–§7.14 tables and the design decisions the tests forced |
| P1c — Validator, metrics, stack layout, drop resolver | ✅ **complete** | `VisualValidator.cs` (+ `VisualValidationContext.cs`): Appendix F document-level diagnostics — shape, required, menu keys, control flow, reachability, name references, the placeholder's info note; a disabled block still has its names checked. `BlockMetrics.cs` / `StackLayout.cs`: pure geometry, cached heights, one geometry for canvas + export + resolver. `DropResolver.cs`: shape hard-filter, distance + stability bonus, magnet radius 40px × zoom, candidate enumeration per script. 42 new tests (`VisualValidatorTests`, `StackLayoutTests`) |
| P2 — Emitters | ✅ **complete** | `VisualEmitter.cs` (template-driven: the catalog row's expression IS the emission), `VisualProgramWriter.cs` (splice + async + host check), `VisualRuntimeTemplate.cs` (the §8.4 support file — **compile-verified against the real MacroDeck.Sdk 3.0.0-beta.14**, which caught five latent defects before any user saw them). Procedures as hoisted local functions with parameters bound into `Locals` by name; `name=value` call args bound positionally in declaration order. 25 new tests (24 emitter + 1 `EnsureAsyncExecutor` regression), full suite **511 green**. As-built §8.6 records the drift from §8.1–§8.4 |
| P3 — Palette and rendering | ✅ **complete** | `Core/Visual/{BlockOutline,BlockLabel,BlockFactory,VisualSampleProject}.cs`, `App/Controls/Blocks/*` (BlockTile, InputSlotView, CategoryRail, PaletteList, ScriptStrip, BlockWorkspace, BlockShapeGeometry, BlockTheme, LabelPartTemplateSelector), `App/ViewModels/Visual/*`, `Pages/VisualEditorPage`, `Liquid.Block*` theme tokens generated from the catalogue. 49 new tests (BlockOutlineTests 21, BlockLabelTests 11, VisualSampleProjectTests 8, plus one new markup assertion), full suite **561 green**. Driven in the real window: all 11 categories, search (including the no-match state), block selection from both surfaces, the empty-mouth state, light and dark, and at 960×640 and 1400×850. §9.10 records the design changes this forced |
| P4a — Document editing, undo/redo | ✅ **complete** | `Core/Visual/DocumentCommands.cs` (BodyRef + 11 reversible commands + DocumentTransaction) and `Core/Visual/DocumentEditor.cs` (Execute, Transaction with rollback, Undo, Redo, ClearHistory, the refusal rules). `DropResolver.cs` gained index-carrying gap candidates and `DropTargetKind.OntoStatement`. 26 new tests (`DocumentEditorTests`, incl. a 1000-gesture random property test that undoes everything and compares the serialized document byte for byte), full suite **587 green**. Typed text coalesces into one undo; a refused or throwing transaction unwinds itself. The P4a as-built record lists the four things that were wrong first |
| P4b — Drag service, ghost, indicator | ✅ **complete** | `Core/Visual/DropPlan.cs` (a landing zone and a payload become the commands that carry it out — pure, window-free) + `Core/Visual/StackLayout.LayoutRun` (a hatless run, for the ghost). `App/Controls/Blocks/CanvasHitTest.cs` measures the live tiles, `DragController.cs` owns the pointer state, `DragAdorner.cs` draws the ghost and the indicator, `AutoScroll.cs` scrolls at the edges, and `BlockWorkspace` is the host. The view model now owns the `DocumentEditor`, projects the document rather than the sample, and keeps the selection pointed at the same block across a rebuild. 14 new tests (13 `DropPlanTests` incl. a 1000-gesture random round trip, 1 geometry test pinning the corrections below), full suite **601 green**. Driven in the real window: palette → canvas into a loop's mouth, canvas → canvas moving a run, a drop released in empty space, and undo — all with a screenshot taken *mid-drag*, because the ghost and the indicator exist only between the press and the release |
| P4c — Keyboard | ✅ **complete** | `Core/Visual/KeyboardMoves.cs`: traversal, moving, deleting, duplicating, cycling, and the drop-zone cursor — all pure and all tested without a window. `DocumentLists.Locate` moved from the editor to Core so a keyboard gesture can be aimed with a document rather than a constructed history. The page has one `PreviewKeyDown`; tiles take keyboard focus on click and the canvas re-focuses the selection after every rebuild; the view model owns the carry state and the header shows it. 17 new tests, full suite **618 green**. Driven in the real window: Down walks the stack, Ctrl+D duplicates, Ctrl+Z and Ctrl+Y undo and redo, Space picks up, arrows move the cursor, Enter drops — and four defects the window found that 601 passing tests did not |
| P5a — Editing and the inspector's editors | ✅ **complete** | `Core/Visual/SlotValue.cs`: reading a slot's value and turning a typed one into an input, so which member of a `BlockInput` carries the value is decided in Core rather than in ten editors. `App/ViewModels/Visual/InspectorViewModel.cs`: the panel's rows, the spinners, the toggle, the dropdowns, the row-expression preview and the generated-C# pane, all writing through `DocumentEditor`. Clicking a hole on the canvas selects the block *and* the slot. Diagnostics are clickable and select the offending block. 10 new tests (`SlotValueTests`, incl. an exhaustiveness check over every `SlotType`), full suite **628 green**. Driven in the real window: typing into a count, the spinner, the toggle, clear, a diagnostic click, and the generated pane |
| P5b — Save, load, dirty state | ✅ complete | **milestone 11** |
| P6 — Shell integration, retire the old Blocks page | ✅ complete | **milestone 12** |
| P7 — Procedures and multi-script | ✅ complete | **milestones 13–14** |
| P8 — Simulator, tracer, debugger | 🟡 code complete, window check outstanding | **P8a** Core/Visual/Runtime/{IVisualHost,SimulatedHost,Values,BlockSemantics,ExecutionStep,ScriptInterpreter}.cs: a simulated host with no window and no network, a block-semantics table both engines are asserted against, and an interpreter that steps one block, steps into a container, honours breakpoints, seeds its RNG and ends what does not end at a budget that says so. **P8b** Core/Visual/Runtime/StageSession.cs plus App/Controls/Blocks/StagePanel.xaml and App/ViewModels/Visual/StageViewModel.cs: the transport, the speed, the watch table, the parameter form, the trace timeline, the mocked deck, the notifications and the honesty statement, with a breakpoint dot in every tile's gutter. 18 new tests, full suite **725 green**. The stage's logic is in Core so the whole of Part 8 is testable without a window; **not yet driven in the real window** — see the work log |
| P9 — Multi-target codegen | ✅ **complete** | **P9a** `CodeGen/Generation/VisualTargetWriter.cs`: one writer for all four target kinds, because the only thing that differs between them is the anchor. Anchors: the action executor's; `InitializeAsync` / `ShutdownAsync` on `PluginIntegration`; `StartAsync` / `SubmitAsync` on the scaffolded `SetupFlow`; and `UiEventHandler.On("<event>"` for a widget node's designed event, **stopping before the `{`** because the provider is one collection initialiser full of lambdas. **P9b** four target hats (`when widget {event}`, `when setup flow {step}`, `when plugin initializes`, `when plugin shuts down`) as **dropdowns, not typed slots**, and `BlockCatalogTests` turned "every hat has somewhere to start" into a hat → target-kind map. **P9c** `MultiTargetCodegenTests` builds a widget provider, a config flow and a stock integration with canvas blocks in them. 17 new tests, full suite **741 green**. Control 24 → 28 and the catalogue 156 → 160, both pinned counts moving because the decision moved them |
| P10 — Polish and hardening | 🟡 in progress | **P10a** `Core/Visual/Commands/VisualCommands.cs`: one command table that is the palette's list, the shortcut sheet's rows *and* the page's key dispatch — so a sheet cannot claim a key nothing does. Plus the palette (Ctrl+K), the sheet (Ctrl+/), the stage keys (F5 / F10 / F11 / F9 / Shift+F5), palette search focus (Ctrl+F), Ctrl+S, and **block comments**: `SetComment` with keystroke coalescing, an inspector row and a tile marker. **P10b** `PaletteMemory` + `PaletteStore`: recents (most-recent-first, capped at twelve, deduped by moving) and pins, per workspace, in a file beside the canvas rather than inside it. **P10c** `CanvasView` + `Minimap`: zoom clamped to 0.25–2.5, pan in screen pixels, fit-to-never-enlarge, and a clickable minimap — the arithmetic in Core because three consumers have to agree about where a block is. **P10d** `CanvasPerformanceBudget`: §9.9's "60fps drag with 500 visible tiles", measured on the half of a frame Core owns — cached metric reads and one hit-test pass — against a budget of a tenth of the frame, with the WPF half left to the harness and the measurement saying so. **P10e** `SvgRenderer`: the vector export drawn from the same `StackLayout` rects the canvas draws, carrying labels and the user's literals and nothing else, with an *Export SVG* button in the header. A PNG writer is deliberately absent — rasterising belongs to the caller that already has a drawing surface, and a third-party encoder in Core would make the whole thing untestable. **P10f** `BlockContrastRules`: three contrast levels (standard, strong, monochrome) as a Core decision, because §9.8's rule is about design rather than brushes — a tile resolves what survives, and a palette may drop a colour key but not a category name. The system is asked, and a monochrome level is offered through `DECKFORGE_BLOCK_MONOCHROME` because no OS setting offers it. **P10g–P10h** §21 localization, end to end: `BlockLabelKeys` generates `Blocks.<Category>.<BlockId>`, `…​.Slot.<slot>`, `…​.Menu.<option>`, `Blocks.Category.<Category>` and `Blocks.Hat.<HatId>` in Core, with the identifier PascalCased so a rename cannot orphan a translation; `ResxTextLookup` reads them out of the workspace's own `Strings.resx`, and `BlockLabel.Plan(descriptor, lookup)` re-splits the translated string over the row's holes. English is the default and a malformed resx is not an error, because a build with no localization loaded is the ordinary case and a hand-edited file can be mid-edit when a canvas opens. 131 new tests across the phase, full suite **845 green**. **Remaining: driving the stage panel, the zoom row and the minimap in the real window**, because the input harness that opened Visual in P3–P7 stopped delivering clicks and chords to this window |

### Baseline (P0)

| Check | Result |
|---|---|
| `dotnet build DeckForge.slnx -v q --nologo` | succeeded, **0 errors, 4 warnings** (2× NU1701 from `WPF-UI` in the test project, CS9113 and CS8604 in `PrerequisiteInstaller.cs`). Note: `EXTENDING.md` claims 0 warnings; that claim is stale, and replacing it is out of this feature's scope |
| `dotnet test tests/DeckForge.Tests --nologo` | **349 passed, 0 failed** (45s) |
| `dotnet --version` | 10.0.400 |
| SDK pin inspected | `macrodeck.sdk` / `macrodeck.localization` **3.0.0-beta.14** |

### Work log

| Date | Completed | Commit |
|---|---|---|
| 2026-10-03 | **P10h**: the labels actually go through localization. `ResxTextLookup` reads the same `Strings.resx` the Localization page edits and the diagnostics validate, preferring `Strings.<culture>.resx` and falling back to the neutral file, so a translator fills in one row and the block says it. It is tolerant by design — a missing, unreadable or malformed resx means "no translations", because a hand-edited file can be mid-edit when a canvas opens and the labels are the least important thing on the page; throwing there would remove the only way to reach the fix. `BlockLabel.Plan(descriptor, lookup)` re-splits the *translated* string over the row's holes, which is why §21.1 keys a whole label rather than each of its words: word-level keys would survive a reworded label better but would leave the translator responsible for word order. A translation whose placeholders do not match degrades exactly as a bad English label does, marker visible on the block. `MenuText` translates a chosen option only when the row declares it, because the value in the document is user data. In the App, `BlockText` is a static — the same shape as `ShellMessenger` — so the thirty places that build a block view model say a localized word without being handed a lookup, and the page installs the open workspace's lookup before anything is built. 14 tests, full suite **845 green** (the stdout/stderr ordering test failed once and passed on rerun, as it does) | **milestone 22 - see commit** |
| 2026-10-03 | **P10g**: the labels go through localization, and the keys live in Core. §21.1 asks for `Blocks.<Category>.<BlockId>`, `…​.Slot.<slot>`, `…​.Menu.<option>`, `Blocks.Category.<Category>` and `Blocks.Hat.<HatId>`; `BlockLabelKeys` generates exactly that, and `LabelId` PascalCases the identifier half — a key built from the catalogue's dotted kind would move the moment a row was renamed, and every translation would become an orphan in a file nobody reads, with nothing reporting a problem. `IBlockTextLookup` returns null for "nothing here" rather than an empty string, because "no translation" and "a translation of nothing" are different and only the first may fall back: a label with nothing in it reads as a rendering fault rather than a missing string. `NoTranslations` is the default, so a build with no localization loaded is the ordinary case rather than a special one. The tests pin the key shapes, assert that every block, slot, menu option, category and hat in the catalogue has one, that they are unique across the whole catalogue — two blocks sharing a key is one of them silently taking the other's translation — and that each is a legal resx key. 11 tests, full suite **831 green** | **milestone 21 - see commit** |
| 2026-10-03 | **P10f**: high contrast, as a decision rather than a brush. §9.8 asks that tiles "fall back to border-only fills with strong outlines when the system asks", and the **system** is asked rather than a settings page, because a user who has set the OS to high contrast has already answered the question. Three levels rather than a switch, because "high contrast" and "no fill at all" are not the same thing: **monochrome** drops the fills entirely, and it is the only level at which the palette's colour key becomes redundant and its category *name* must not. The rule lives in Core because it is a decision about which colours carry meaning and which are decoration — a design that lives in a WPF theme cannot be checked, only looked at — and the tile's `OnRender` now resolves what survives rather than deciding it. Outlines are **derived** from each category's hue rather than picked from a table, because the table version went stale the first time a hue changed and a slightly-wrong outline still looks like an outline. There is no OS setting for monochrome, so `DECKFORGE_BLOCK_MONOCHROME` offers it: a hidden switch in a settings page would mean "what does the monochrome canvas look like" has no answer except "change your OS". A test asserts every category at every level gets an outline that is *not* its own fill, and that the four silhouettes still differ when no colour survives — the other half of "colour is never the only signal". 9 new tests, full suite **820 green** | **milestone 20 — see commit** |
| 2026-10-03 | **P10d–P10e**: the budget, and the export. §9.9 asks for 60fps drag with 500 visible tiles, and 60fps is 16.7ms of which **half is Core's** — the geometry a drag re-scores and the candidates a pointer move considers — and half is WPF arranging and drawing a thousand tiles, which only the harness can see. So the budget measures Core's half and *says which half it measured*: a budget with no stated scope is a number somebody quotes in a release note, and a unit test calling itself a frame rate would be a fiction. The document is synthetic and it **nests**, because a flat list measures the case every implementation is already fast at; and it is counted through `Blocks()` rather than the body list, because a container brings its child and a "500-block" benchmark otherwise measures 601. Cold and warm are measured separately, which is Part 9.9's own rule made testable — a benchmark that only measured the cold path would fail a correctly-cached implementation and pass one that recomputes everything. **The first version of this benchmark was wrong and the test that caught it is kept**: it enumerated the document 500 times per frame because that is what "consider 500 tiles" sounded like, and came out four times over budget. The honest conclusion was that the *benchmark* was wrong. The SVG export is drawn from the same `StackLayout` rects the canvas draws — "a vector render produced from the layout model" is the whole design, and a renderer that laid blocks out itself would be a second geometry that disagrees with the screen after the first change to either. It carries labels and the user's literals (a variable is shown as its *name*, because a value is a run's business and an export happens whether or not one has), escapes them, and formats numbers invariantly: a Hungarian machine writes `1,5`, which is not a number in SVG and fails in the viewer rather than in the export. There is deliberately **no PNG writer** — rasterising belongs to the caller that already has a drawing surface, and a third-party encoder in Core would make the whole thing untestable. 17 new tests, full suite **811 green** | **milestone 19 — see commit** |
| 2026-10-03 | **P10b–P10c**: what a workspace remembers about you, and where you are on the canvas. `PaletteMemory` holds two ordered lists — recents and pins — because **the order is the feature in both**: a `HashSet` promises neither, and one that happened to keep insertion order reorders the moment something is removed and re-added, which is exactly what re-pinning does. Recents cap at twelve and the cap trims the *end*, because a recency list that keeps the oldest entries is a history. `Restore` walks the stored list **backwards**, since `Note` puts the newest at the front and restoring in file order silently reversed the row — the one thing a recency list must never do, and invisible until there were more than two entries to look at. The habit lives in `.deckforge/palette.json`, beside the canvas and not inside it: saving a habit must never make the user's *script* look modified. A corrupt file is an empty habit, not a failed launch, and it is left on disk so the user can look at it. Zoom and pan are `CanvasView` in Core rather than a `ScaleTransform` in a control, for the reason `StackLayout` is in Core: the canvas draws through a view, the drop resolver scores gaps against one, and the minimap thumbnails one, and a zoom that lives in a control can only be *read* by the control. The arithmetic that mattered: **a pan delta is in screen pixels**, because taking it as workspace units makes the document race the pointer at 200% and crawl at 50%; **fit-to-never-enlarge**, because "show me everything" answered by doubling a three-block script is the wrong answer; and **the minimap's viewport rectangle is clamped into the map**, because zoomed out past the whole document it would otherwise start off the edge and be unclickable. Fit-to also learned to stop at the minimum zoom and hand back a zoom its callers can use, rather than a 0.18 the canvas would refuse. Not yet driven in the window, for the reason the tracker carries. 53 new tests, full suite **794 green** | **milestone 18 — see commit** |
| 2026-10-03 | **P10a**: one command table, the palette and the sheet, the stage keys, and comments. The table is the point: the palette's list, the shortcut sheet's rows and the page's key dispatch are all the same rows, so a sheet that listed a shortcut nothing did would be documentation a user trusts and the suite would not notice — the keys live in a `PreviewKeyDown` and the sheet is a list of strings, and nothing tied the two together. A test now says no gesture is claimed twice in one scope, that every canvas command is findable by its own title, and that Part 18.3's stage row matches the commands the handler dispatches. The stage keys are *looked up* in the table rather than switched on again, which is the only way that test stays true after somebody adds F12. Comments existed on the model since P1 with no command, no editor and no marker: a comment is on the undo stack, coalesces per keystroke the way the slot editors do, and the merge keeps the **oldest** `Before`, so one Ctrl+Z takes the whole sentence back instead of leaving `Why this exis` behind. Two things found on the way. A search that found nothing when the user typed a capital letter — the comparison was Ordinal against lower-cased terms, so the palette silently returned *every* command. And Backspace inside a slot deleted the block: the field-focus rule now precedes the plain-key switch, so a single-key shortcut cannot eat a character. A comment travels into the generated code as `//` lines, which is Part 8.1's "a comment or nothing at all" and is right — the generated file is read by people too, and a note that stopped at the canvas would be a note half the audience never sees. 20 new tests, full suite **761 green** | **milestone 17 — see commit** |
| 2026-10-03 | **P9**: multi-target codegen, and the three targets the design deferred to it. One `VisualTargetWriter` rather than three, because the only difference between the four is the anchor; the anchoring itself is `BlockCompiler.Splice`, reused, as Part 8.5 insists. A widget handler's anchor stops *before* the `{`, because the generated provider is one collection initialiser full of lambdas and an anchor including the brace finds the next node's handler — which is the difference between writing a user's blocks into the event they chose and into its neighbour. The four target hats are dropdowns rather than typed slots: the writer has to find a method by name, so free text would be a name nothing can resolve, and `BlockCatalogTests` turned "every hat has somewhere to start" into a hat → target-kind map so a hat with nowhere to go fails instead of drawing. **The exit criterion is a build, and it earned its keep three times over.** The stock widget handler **did not compile** — `RenderHandler` put a `// TODO` comment on the same one-line lambda and it swallowed the closing brace, and nothing in this project builds a generated widget provider by default, so it had shipped. A widget node's event handler turns out to be handed **no** context and no logger, so every block reaching for `context` or `_logger` has to be refused there, by name; the SDK's UI event surface offers nothing to splice one in and inventing a parameter would be inventing API. And `VisualRuntimeTemplate`'s `namespace __NAMESPACE__;` had no substitution anywhere in the project, so nothing referencing `VisualRuntime` had ever compiled in a plugin nobody had hand-fixed. `Splice` learned an empty-body case (the stock `ShutdownAsync` is an expression body, and anchoring on it dropped the region *outside* the method, as class-member siblings) and stopped re-indenting the closing brace on top of the indent already in front of it, which had been drifting the region one level deeper on **every** save. Control moved 24 → 28 and the catalogue 156 → 160, which is what the pinned counts are for. The six C5 event hats stay deferred: they are implementable now that this is the phase with an owner for their subscriptions, but each needs a subscription stored and disposed rather than a statement written, and that is its own milestone rather than an afterthought to one about anchors. 17 new tests, full suite **741 green** | **milestone 16 — see commit** |
| 2026-10-02 | **P8b**: the stage, the tracer and the debugger. `StageSession` went into **Core** rather than into a view model, and that is the whole design of the phase: the test project deliberately does not reference `DeckForge.App` — `XamlMarkupTests` reads the App's markup off disk on purpose — so a stage written as a view model could only have been checked by looking at a window, which is the checking Part 8 exists to stop relying on. `StageViewModel` is now a projection and holds no state. The transport paces a run **one block per scheduled tick** instead of calling the interpreter's batch `Run`, because a transport that runs a script to its end inside one click cannot be stopped between two blocks, and a Stop button that only works when the script happens to finish is the one button nobody needs until they need it. **Three defects the tests found and no screenshot would have.** `Run` was quietly `Step`: the tick did not re-arm itself, so pressing Run ran exactly one block and called it a transport. A pause says nothing on its own, because stepping pauses after every block; the stage asks the interpreter *why* it paused (`PausedAtBreakpoint`) rather than re-deriving it by looking the last trace id up in the breakpoint set, which is the guess that is wrong one step later. And an empty `forever` never stopped — a loop restart executed no blocks, so nothing incremented the budget, so the pump spun for ever. Passes are counted against it now, and a run that stops says the budget stopped it. Loops are real loops: `repeat` counts once at the start so a variable cannot change it halfway, `while` tests before the body, `repeat until` after it, `if / else if / else` in catalogue order, Scratch's order deliberately. The trace is capped at 500 rows because a `forever` at sixty steps a second takes the window down with it. Every tile's header padding holds a breakpoint dot, and the run's current block gets a ring drawn outside its silhouette so it can be found on a canvas where a hundred blocks are the same colour. Breakpoints are not edits and never touch the undo stack, because Ctrl+Z after setting one would otherwise undo the block the user had just written. **Not driven in the real window**: the input harness that opened Visual in P3–P7 stopped delivering clicks and `ctrl+9` to this window, so the stage panel is unverified by eye and the tracker marker says so rather than claiming a look that was not taken | **milestone 15 — see commit** |
| 2026-10-01 | **P7b**: the strip, the panel, and the column. A procedure is a **column**, not a form — `ColumnViewModel` gives scripts and procedures one template, one items source and one hit test, with `HasHat` as the whole difference, and because procedures sit in `Columns` the arrow keys reach their bodies. My Blocks goes **above** the scripts, inverting §9.2's sketch on purpose: with eleven scripts the sketched order put a document's own procedures and the only button that creates one below the fold of every window. **Three defects the window found, none a test could have.** An `Expander` bound `IsExpanded` to a read-only property, which throws on every load and is swallowed by the crash handler — the page came up looking almost right with an empty canvas. The parameter list never updated, because the document holds plain `List<T>` which raises no notification: the editor changed the document and the panel showed what it had been built with. And the delete button named its procedure through a `DataContext` that a `DataTemplate` cannot reach by `ElementName` or by ancestor search — both were tried, and both produced a button that looked wired and did nothing; it now reads the selected procedure from the editor, the only one the panel can be showing. One refusal was simply backwards: un-ticking "returns a value" was allowed while a call was *waiting* for a result, so the checkbox produced two red diagnostics instead of an explanation. The drop path needed `DropCandidate.ProcedureBody`: a procedure gap is identical to a script gap until you write into it, and the rebuilt reference was a script reference to a hat that does not exist — so a legal drop was refused as a missing body. The canvas is no longer capped at 1600px, which had made the far end of the document unreachable. Verified in the window against a saved workspace: select and scroll to a procedure, rename, add and remove parameters, the refusal, a palette block dropped into a procedure body, an added procedure named `myProcedure2` rather than colliding, Save, restart restoring all four exactly. Also **P7a**, committed separately: `ProcedureDeclaration.Id`, `BodyRef.ProcedureBody`, and the call and return rules. 22 new tests, full suite **665 green** | **milestones 13–14 — see commit** |
| 2026-10-01 | **P6**: shell integration, and the old Blocks page retired. `BlockActionPage.xaml`, its code-behind and `BlockActionViewModel.cs` are deleted along with the `"blocks"` registry entry and the two DI registrations; `BlockCompiler` and `BlockProgramWriter` stay, because the compiler's public surface is the engine the new emitters sit behind and `BlockProgramWriterTests` still exercises the marker round-trip — `BlockProgramWriter` now has no production caller, which is a decision rather than an oversight. The risky part was positional: `MainWindow` reads the sidebar in order, so Ctrl+9 is simply the ninth item, and leaving Visual where it was would have handed Widget Designer the number Blocks had. Visual went into Blocks' exact slot. `NavigationShortcutTests` asserted only that every item has a tag and no tag repeats — both true through *any* reorder, so nothing recorded which digit meant which page and a renumbering would have passed the suite. The order is now pinned as a list, plus an assertion that at least ten items exist, because ten digits is a requirement the markup never states. Precisely: Ctrl+1–8 unchanged, Ctrl+9 now opens Visual, and Ctrl+0 now opens Widget Designer, which had no shortcut at all as the eleventh item — nothing lost a number. Verified in the window after a wait long enough to matter. 2 new tests, full suite **643 green** | **milestone 12 — see commit** |
| 2026-10-01 | **P5b**: save, load, dirty state. `VisualStore` in Core owns the file: the document is a sidecar at `<workspace>/.deckforge/canvas.json` and the C# is a projection of it, never the other way round, because the action source is generated and anything hand-edited inside it is lost on the next write. Three decisions that are refusals in disguise: writes go beside the file and then replace it, so a crash cannot leave a canvas that cannot be parsed; every save keeps the previous file as `canvas.previous.json`; and an unreadable canvas falls back to that backup, says so, and leaves the unreadable file alone — it is the only copy of whatever happened, and the warning names the consequence, because "saving will replace it" is actionable and "something went wrong" is not. Dirty state is *computed*, not remembered: the document is serialised and compared with the disk, so an undo back to the saved state stops claiming unsaved work — an indicator that is always on is indistinguishable from one that is never on. `DocumentEditor.ReplaceDocument` drops the history, because every command on the stack carries objects from the old document and replaying one would edit a document nobody can see and report success. **One defect the window found, which no test could**: with a corrupt canvas and a good backup the page drew the previous save *silently*, and the header read "Unsaved changes" — true, and silent about the fact that what is on screen is not what is on disk, so the recovery looked like the app losing work. `VisualStoreResult` now carries `Recovered`. 11 new tests; full suite **641 green** | **milestone 11 — see commit** |
| 2026-10-01 | **P5a**: the inspector's editors, the generated C# pane, and diagnostics that select. `SlotValue` in Core reads a slot and turns a typed one back into an input, which puts the one decision that matters here — *which member of the `BlockInput` carries the value* — in a tested place instead of in ten editors. A number in `Text` is a document that validates, emits, and produces something other than what the user typed, and that failure is completely silent. The rows read through to the block and write only through the editor, so the panel cannot disagree with the canvas. Two refusals: a number slot rejects a word and a boolean rejects anything but true/false, before the keystroke reaches the document; and Clear is hidden for booleans, because emptying a required one produces a document nothing can compile. The row-expression preview is labelled "Row expression" and not "Generated", since emitting one block needs a whole action around it — the pane at the bottom of the panel goes through `VisualEmitter.CompileTarget` and is the one that may claim to be what Save writes. Clicking a hole on the canvas selects the block *and* the slot. **One defect no test could find**: typing `7` into a count of `10` produced `710`, because the row committed the keystroke, the canvas rebuilt, and the rebuild handed the binding its own freshly-read value back, so the next character appended. The row now reads through to the block whenever its draft matches it. 10 new tests, including one that walks every `SlotType` in the catalogue; full suite **628 green** | **milestone 10 — see commit** |
| 2026-10-01 | **P4c**: the keyboard. Part 9.5's "nothing requires a mouse" — every decision about where the cursor is and what the gesture means lives in `KeyboardMoves` in Core, stateless, so the whole layer is a pure function of (document, selection, key) and 17 tests cover the ends of stacks, nested bodies, and the shapes that fit nothing. Two gestures deliberately disagree with the drag: Ctrl+↓ moves **one** block and swaps it, and Delete removes **one** block, because carrying the tail makes Ctrl+↓ a no-op for every block except the top and turns Delete into a data-loss trap. Both were written the drag's way first and the tests caught it. `DocumentLists.Locate` moved out of the editor so a gesture can be aimed with a document rather than a constructed history. The page has one `PreviewKeyDown`; the workspace re-focuses the selection after every rebuild. **Four defects the window found**: no tile could take focus at all, because the workspace's preview handler marks the event handled and so the tile's own bubbling handler never ran — selection worked and the keyboard addressed something else; the keyboard was lost after the first edit, because every rebuild replaces the focused tile, so "drag works, then Ctrl+Z does nothing"; Ctrl+Z did nothing while carrying, because the carry branch returned first; and the ghost's pitch for nested blocks. A fifth was in the *driver*: a swallowed key-up left Ctrl held for the rest of the session, so a shortcut worked once and then silently stopped — evidence that points squarely at the application. The driver now releases stale modifiers before each keystroke and accepts key *names*, since mapping "down" to the letter D sends Ctrl+D for every arrow key. 17 new tests; full suite **618 green** | **milestone 9 — see commit** |
| 2026-10-01 | **P4b**: the pointer. `DropPlan` in Core is the decision half of a drop — payload plus landing zone becomes the commands — and has no WPF in it, because what a zone *does* to the document is the half with the interesting failures and the half a window cannot test; the 1000-gesture round trip through it is the P4a property test with the drag's four command shapes in front of it. `CanvasHitTest` measures the live tiles rather than computing them, so a candidate is compared against where WPF actually put the block. The ghost is drawn from real `BlockOutline` silhouettes laid out by a new `StackLayout.LayoutRun` — a hatless run, which the Part 25 thumbnails want anyway — and the indicator is a bar rather than a real gap, because opening one would reflow the canvas on every pointer move. **Four defects the window found and 601 passing tests did not**: a drop put a block in the *wrong script*, because every candidate claimed `x = 0` and the scorer weighs x at half a point per pixel; a loop's mouth opened at its *foot*, because a y walk reported a container's inner gaps at the container's bottom; a drag released in empty space *teleported* the block, because the scorer always names a nearest candidate; and a drag out of the palette that never crossed the canvas *did nothing at all*, because the workspace only captured the pointer when the press started over it. The first two were one cause — candidates computed a geometry that could disagree with the canvas's — and are now read off the blocks' own rectangles instead. The harness gained `press`/`move`/`release` so a screenshot can be taken mid-drag; without that the ghost and the indicator are unobservable, and a canvas drawing nothing looks like a screenshot taken too late. 14 new tests; full suite **601 green** | **milestone 8 — see commit** |
| 2026-10-01 | **P4a**: the editing engine — eleven reversible commands (`InsertRun`, `DeleteRun`, `MoveRun`, `WrapRun`, `UnwrapRun`, `EditField`, `BindSlot`, `ToggleDisable`, `AddScript`, `DeleteScript`, `MoveScript`) behind one `DocumentEditor`, plus `DocumentTransaction` and `DocumentLists`. Each command captures the index it needs to put itself back, because "put it back where it was" cannot be recomputed after the fact. `RunFrom` returns the grabbed block *and everything below it*, which is what makes a stack drag one run — and why a downward move within its own stack is a no-op rather than a reorder. A transaction applies in order and reverts the earlier commands when a later one is refused: refusals are returns, not exceptions, so it has to unwind by hand. `EditField` coalesces with the command already on top (keeping the older `Before`, the newer `After`) so a typed word is one undo rather than one per keystroke. `DropResolver`'s gaps now carry the body and the index they would insert at — they could not be acted on before, and P1c's `Index: 0` throughout was a stub that only became obvious once something wanted to act on it; a container's mouth is offered at the foot of its body as well as the top. 26 new tests, including a 1000-gesture random property test that undoes everything and compares the serialized document byte for byte against the start; full suite **587 green**. The P4a as-built record lists the four things that were wrong first, including a test generator that threw on an empty stack — a legal state, since a drag that takes everything can empty one | **milestone 7 — see commit** |
| 2026-09-30 | **P3**: the palette, the canvas and the shapes. `BlockOutline` decides every silhouette as numbers — notch, tab, dome, pill, hexagon, one hole per mouth — and `BlockShapeGeometry` only walks the list, because geometry written in a template is geometry nothing can test and the whole reason the shapes are in Core is that a notch that stops lining up with the tab above it is invisible until a screenshot. `BlockLabel` splits a row's `repeat {count}` into words and holes (and a hole naming something the row does not declare renders as the raw marker, which is now a failing test over all 156 rows); `BlockFactory` builds a fresh block of any row filled in with plausible defaults, so the palette and the sample are projections of the catalogue rather than two hand-written lists. `VisualSampleProject` builds one script per category containing every shipping block, and a test fails when a new row is missing from it. The tile draws its own silhouette in `OnRender` from the size it was actually given and hosts the label in markup; the recursive template refers to itself by key, which is only legal because template content is instantiated after the dictionary is parsed. Clicking a block selects it through a bubbling routed event whose *arguments* carry the block, not its `Source` — WPF builds the route by walking the tree from `Source`, so a view model there sends the event nowhere. `Liquid.Block*` tokens are generated from the catalogue's hues, so a category cannot exist without its colours. 49 new tests; full suite 561 green. Design changes, and four defects the real window found that reading the code did not, are in §9.10 | **milestone 6 — see commit** |
| 2026-09-30 | **P2**: the emitters — one data-driven `VisualEmitter` replaces the spec's three classes (a row's template *is* the emission, so catalog and generator cannot disagree); braced `{holes}` and bare slot-name holes filled per slot type; control flow special-cased with Scratch semantics (repeat-until tests after the body); procedures hoisted as local functions whose parameters are bound into `Locals` by name and called positionally from parsed `name=value` lines; guards deduped at flush; the writer reuses `Splice`/`EnsureAsyncExecutor` with a substring-safe anchor. The runtime template was compile-checked end to end against the real SDK (throwaway plugin probe) — it had never been compiled, and the check caught a duplicate `IsNumeric` (CS0102), `System.Json` for `System.Text.Json`, `System.ActionExecutionContext` for `MacroDeck.Sdk.Actions.ActionExecutionContext`, an invalid `List<string>(comparer)` construction, and the class's own `Convert` shadowing `System.Convert`. `EnsureAsyncExecutor` hardened: an already-async body with `SucceededTask`/`Task.FromResult` returns is now repaired instead of skipped (regression test added). 25 new tests; full suite 511 green. Design changes recorded in §8.6 | **milestone 5 — see commit** || 2026-09-29 | The design document itself (Parts 1–28, Appendices A–I) | (doc only, uncommitted) |
| 2026-09-29 | **P1c**: the validator and the geometry — `VisualValidator` checks a document against the catalog and produces the Appendix F codes a document can own (shape mismatch as error, type coercion as warning, unbound required slots, unknown menu keys, break/continue depth via a walk that carries loop depth, return-inside-procedure, forever-without-yield, unreachable-after-cap, name references against declared parameters/host variables/procedures, reserved-name collisions). Setters and `list.define` declare their names *as the walk reaches them*, so `set x to x + 1` reads clean and a genuinely-forward reference is caught. `BlockMetrics` + `StackLayout` give canvas, export and resolver one geometry; `DropResolver` filters by shape, scores by distance plus a stability bonus, snaps inside 40px × zoom. Design notes recorded in §9.5-as-built (§9.10) | **milestone 4 — see commit** |
| 2026-09-29 | **P1b**: the block catalog — 156 shipping blocks in 11 categories, `Search`, per-category counts pinned by test, every expression checked against the Appendix A surface dump, capabilities cross-checked against `CapabilityCatalog`. The migration was rewritten: slot values now land in `inputs` (they were in `fields`, which parsed fine and emitted nothing), variants are chosen by what the legacy block actually set (`ui.log-with-param`, `ui.notify-key`, `sensing.run-script-with-inputs`), `read-variable` migrates to `var.set-from-host`, literals lose their redundant `valueKind`/`type` menu, `==` normalises to `=`, error codes normalise case-insensitively against the real nine. Design changes recorded in §6.2.1 and §7.17 | **milestone 3 — see commit** |
| 2026-09-29 | **P1a**: the visual document model — `Block`/`BlockInput`, the document types, hand-written JSON converters, structural legacy detection, and the 18-kind migration. 38 new tests (`VisualModelTests`, `VisualMigrationTests`); full suite 387 green. The class hierarchy in §6.2 became one generic node (§6.2.1) | **milestone 2 — see commit** |
| 2026-09-29 | **P0**: added `tools/SdkInventory` (re-runnable surface dumper) and dumped `MacroDeck.Sdk` + `MacroDeck.Localization` at the pin; recorded the baseline; filled Appendix A; added Part 7.16 verdicts (28 blocks dropped, 6 deferred, 7 discovered) | **milestone 1 — `c23f67d`** |

---

## 1. Executive summary

DeckForge generates Macro Deck 3 plugins. Today the "Blocks" page is an MVP: a flat list of
eighteen statement kinds, no nesting, no data flow, no drag and drop. This document specifies the
replacement: a real block editor with a shape grammar, a category palette, drag-and-drop with both
highlighted drop zones and pixel-perfect snapping, nested loops and conditionals, reporter blocks
that plug into slots, user-defined procedures, multiple scripts per target, live C# generation into
the user's plugin, and an in-app simulator with breakpoints so blocks can be *run* without building
the plugin.

Four structural facts drive the design:

1. **Generated action classes are `sealed`, not `partial`.** `public sealed class LogMessageAction :
   IActionDefinition, IIntegrationContextAware`, with a nested `private sealed class Executor`.
   Procedures therefore cannot be added as class members from another generated file. They are
   emitted as **local functions inside the block region** instead — legal C#, `async`-capable,
   callable before declaration, and capturing `context`, `_logger` and `_integration` exactly as
   required. This keeps everything inside the `// <macrodeck-blocks>` markers, so idempotence and
   hand-code preservation are untouched.
2. **The marker/splice contract is sacred.** The engine writes a region between
   `// <macrodeck-blocks>` and `// </macrodeck-blocks>`, before the method's final top-level
   `return`, applies the file's own indentation style, and preserves everything outside. All new
   emission goes through that same machinery.
3. **The test project cannot reference WPF.** Therefore every piece of drag-and-drop intelligence —
   metrics, layout, drop-zone enumeration, scoring, snapping — is pure code in `DeckForge.Core` and
   is unit-testable with no window. The WPF layer is a thin shell over it.
4. **The SDK is authoritative.** This repository's standing rule is that the CLI and SDK define
   truth and DeckForge never reimplements protocol behaviour. Every block must map to a verified SDK
   call, and generated code must compile with zero warnings on a user's machine.

---

## 2. Analysis

### 2.1 What Scratch is, structurally

Scratch is not "a UI with colourful blocks". Four properties make it work, and each has a cost we
must either pay or design around.

1. **A shape grammar.** A block's silhouette encodes its role: stack blocks have a notch above and
   a tab below; C-blocks wrap a body; reporters are ovals; booleans are hexagons; hats have a flat
   top. The grammar is the type system made visible — a user cannot express "put a value where a
   statement goes", because the shapes will not connect. This is the single most valuable property
   and the hardest to reproduce in WPF.
2. **A flat, id-keyed block graph.** `.sb3` stores blocks in a dictionary keyed by id with `next`,
   `parent`, `inputs` and `fields`. That representation is excellent for a canvas (a splice is a
   pointer rewrite) and miserable for emitting and testing.
3. **Loose dynamic typing.** Any reporter fits any slot; joining a number works; a boolean in a
   text slot becomes `"true"`. Users feel this as freedom.
4. **A live stage.** Code visibly does something. Nobody learns Scratch by generating a file.

Two ergonomics matter as much as the four above, and are always missed when absent: **dragging
carries everything below the grabbed block**, and **dropping a block between two others silently
closes the gap**.

### 2.2 What Macro Deck changes

- **There is no stage in the product.** Plugin code runs inside Macro Deck's host. There is no
  sprite, no coordinate space, no mouse position. Several Scratch blocks have no referent at all and
  must be *translated* (Part 5), not copied.
- **Code lands in a user's file.** The plugin project belongs to the user. Anything DeckForge writes
  must preserve their hand-written code and must be regenerable without drift.
- **Types are static where Scratch's are not.** Generated C# must coerce explicitly rather than
  hoping for magic.

### 2.3 Defects this design must not repeat

The repository's own history is a requirements list. Each entry below was a real defect, and each
becomes a design rule.

| Past defect | Design rule here |
|---|---|
| `{Binding Describe()}` rendered every block row blank with no error, because WPF binds to properties. | Every block exposes `Description` and friends as **properties**; `XamlMarkupTests` enforces no method bindings anywhere. |
| A `ui:SymbolIcon` naming a non-existent `SymbolRegular` member made `MainWindow` throw during startup; the crash handler swallowed it, leaving a live process and no window. | New markup uses only members checked by `XamlMarkupTests`; icons are verified in the same commit they are added. |
| The Blocks page had "no place yet to put statements inside a branch" — branches compiled to empty blocks. | Nesting is the first-class feature of this design, not a later addition. |
| `If` blocks read their operand by name, so renaming a declaration silently changed what was tested. | Local names are **never** auto-renamed; collisions are refused with an explanatory message (reuses `BlockCompiler.LocalNameConflicts`). |
| Five compile-test assertions were vacuous because they wrote outside `src/<Project>/`, so the generated file was never compiled. | Visual compile tests write into the project directory and assert the build saw the file. |
| The old view model wrote the `.cs` before the resx and the patcher, leaving a half-written action on failure. | Save is **order-safe** and never reports success before every step lands (Part 11). |
| A canvas reload on plain navigation silently discarded unsaved work. | `RefreshOnNavigate` reloads only when the workspace actually changed (Part 9.1). |
| `BlockCompiler.Splice` reused stale indices after removing a region, breaking the second save. | New target writers reuse the shared, tested splice machinery instead of re-implementing anchoring. |

---

## 3. Product specification

### 3.1 Locked decisions

Agreed with the user before design began:

| Question | Decision |
|---|---|
| Block vocabulary | **Everything.** Every SDK capability a script can drive becomes a block. |
| Old `Blocks` page | **Retired.** Visual replaces it. Its 18 kinds are re-homed, its sidecar migrated, its engine kept and extended. |
| Runtime behaviour | **All three:** generate real C#, plus a mock-host **simulator stage** with breakpoint debugging, plus a **dry-run tracer** driven by typed-in parameter values. |
| Drag & drop | **Both:** highlighted drop zones *and* pixel-perfect magnetic snapping, stack splitting, and pull-with-everything-below. |
| Codegen targets | **Every reachable surface:** action executors first, then widget handlers, config-flow hooks, lifecycle surfaces. |
| My Blocks & multi-script | **Both:** several named scripts per target with hats, plus user-defined procedures with typed parameters. |

### 3.2 Goals

1. A sidebar tab named **Visual** that behaves like Scratch: category palette, drag-and-drop,
   unlimited nesting, reporters in slots, hats, multiple scripts, procedures, and a stage.
2. Every offered block compiles to real, warning-free C# against the pinned SDK
   (`MacroDeckSdkInfo.DefaultVersion` = `3.0.0-beta.14`) and splices into the plugin inside the
   existing markers.
3. It must be **better** than Scratch, not a tribute: searchable palette, block-level breakpoints,
   live C#, a diagnostics list, a variable watch table, zoom/pan, undo/redo with transactions,
   full keyboard-only operation, first-class light/dark/high-contrast, and a look that belongs to
   DeckForge's Liquid design system rather than to Scratch's flat palette.

### 3.3 Non-goals

- No `.sb3` import/export in the first scope. It is designed as an extension point (Part 12.6).
- Not a general-purpose language: no classes, generics, reflection or threading blocks.
- No change to generated-project fidelity: the plugin remains the stock Macro Deck template plus
  DeckForge-owned files.
- No reimplementation of host protocol behaviour. Blocks call the SDK; they never speak to the host
  themselves.

---

## 4. Architecture fit

### 4.1 Module map

Dependency direction is unchanged: `App → {CodeGen, Validators, CliAdapter} → Core`.

| Concern | Home |
|---|---|
| Document model, block catalog, shapes, geometry, drop resolution, validation, interpreter, simulated host | `DeckForge.Core/Visual/` |
| Emitters, runtime-library template, target writers | `DeckForge.CodeGen/Visual/` |
| Page, controls, view models, services | `DeckForge.App/{Pages,Controls,ViewModels,Services}` |
| Tests | `tests/DeckForge.Tests/Visual/` |

No new project is added. `BlockCompiler` and `BlockProgramWriter` remain the public engine and become
facades over the new emitters, so all 344 existing tests must keep passing at every phase boundary.

### 4.2 The two structural findings

**Finding 1 — the action class is sealed.** Verified in `MacroDeckTemplateFactory` and
`ActionGenerator`:

```csharp
public sealed class LogMessageAction : IActionDefinition, IIntegrationContextAware
{
    public string Id => "log-message";

    private sealed class Executor : IActionExecutor
    {
        public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context) { … }
    }
}
```

Consequences:

- Procedures cannot be class members added from a second generated file (the class is `sealed`, and
  making it `partial` would change the template — a parity deviation we do not want to pay for yet).
- **Resolution:** procedures are emitted as **local functions** at the top of the block region.
  C# permits local functions anywhere in a block, they may be `async`, they may be called before
  their declaration, and they close over `context`, `_logger` and `_integration` — which is exactly
  the host handle a procedure needs. Everything stays inside the markers, so the existing
  preservation and idempotence guarantees hold unchanged.
- A future option is recorded in Part 16: if action classes ever become `partial`, class-level
  procedures (callable across actions) become possible without disturbing this design.

**Finding 2 — there is no drag-and-drop code in this application.** No `DragDrop`, no `Adorner`, no
`Thumb`, no hit-testing helper anywhere in `DeckForge.App`. The drag system is greenfield, so it is
designed as a pure Core brain plus a thin WPF shell (Part 9.5). This is also what makes it testable:
the scorer is a function, not a window.

---

## 5. Scratch semantics translated onto Macro Deck

### 5.1 Noun mapping

Scratch's nouns do not all exist here. The translation is deliberate, not cosmetic.

| Scratch | DeckForge Visual | Rationale |
|---|---|---|
| Sprite | **Target** — an artefact that can hold scripts: an Action, a Widget node's events, a Config-flow hook, a lifecycle surface | A sprite is "a thing that runs scripts". In a plugin, that is a code target. |
| Stage | **Plugin scope** — global variables, lists, procedures, plus the simulator panel | Project-wide state. |
| Green flag | **`when action runs`** hat (the action's `ExecuteAsync`) | The action is the entry point. |
| Key pressed / sprite clicked / backdrop switches / receive message | `when key pressed`, `when client connected` / `disconnected`, `when profile changed`, `when folder opened`, `when message received`, `when event received`, `when widget pressed` / `long-pressed` / `changed`, `when setup step submitted` | Each maps to a real host trigger or a declared event binding. |
| Motion | **Deck** — folders, profiles, clients, button state and icon | Navigation is what moves. |
| Looks | **UI / Looks** — notifications, modals, issues, logs, assets | Everything the user sees. |
| Sound | **Media** — the Music Players capability | The SDK names it. |
| Pen / Data | **Variables + Lists** — local, and host-shared | Same idea, two stores. |
| My Blocks | **Procedures** → local async functions | Finding 1. |
| Sensors | **Sensing** — parameters, host variables, clock, session, ids | There is no mouse; the sensors are host facts. |

**Blocks that are deliberately dropped** because they have no referent: mouse position, pen drawing,
sprite rotation/coordinates, "touching colour", clone management, and stage backdrop manipulation.
They are listed here so that their absence is a decision rather than an oversight.

### 5.2 Shape grammar

| Shape | Meaning | May occupy |
|---|---|---|
| `Hat` | starts a script; flat top | the top of a script only |
| `Stack` | statement; notch above, tab below | stack gaps |
| `Cap` | ends a flow; rounded bottom | stack gaps (makes following blocks unreachable → warning) |
| `C` | wraps one body | stack gaps; exposes a mouth gap and inner gaps |
| `CIf` | wraps `then` and/or `else` | as above; two bodies |
| `CChain` | `if / else if / else`, unlimited middle bars | as above |
| `Reporter` | oval; produces a value | value slots only |
| `BooleanReporter` | hexagon; produces `bool` | predicate slots and value slots |
| `Menu` | inline dropdown; not draggable | inside its owner block |
| `Modifier` | attaches to a block (comment, disable) | a block's shoulder |

The grammar is enforced by the drop scorer (Part 9.5). It is the shape-grammar equivalent of a type
checker, and it is the reason a user cannot build something nonsensical by dragging.

### 5.3 Value semantics — loose like Scratch, compilable like C#

- Every slot declares a **preferred type** — `text`, `number`, `bool`, `list`, `any` — used for
  editor affordances and for **warnings**, never to block a drop.
- Any reporter fits any slot. The emitter coerces at the use site through the generated runtime
  library (Part 8.4).
- A literal is a `BlockValue` node like any other, so "a slot" has exactly one representation and
  one editor path. This is what stops the editor growing a second, parallel notion of "raw text
  field" that drifts from the model.

Coercion rules (implemented once in `VisualRuntime`, asserted by a table test):

| Target | From `null` | From text | From number | From bool | From list |
|---|---|---|---|---|---|
| `ToText` | `""` | itself | invariant `ToString` | `"true"` / `"false"` | joined with `", "` |
| `ToNumber` | `0` | invariant parse, else `0` | itself | `1` / `0` | item count |
| `ToBool` | `false` | `""` and `"0"` are false, else true | `0` is false, else true | itself | non-empty |
| `Compare` | ordinal, null sorts first | ordinal | numeric when both numeric, else text | text projection | text projection |

---

## 6. Domain model — `DeckForge.Core/Visual/`

### 6.1 Document

```
VisualProject                       one file per plugin workspace
├─ Version                          schema version (1 after migration; 0 = Blocks v1 origin)
├─ Targets[]                        "sprites"
│   ├─ Id, Name, TargetKind         Action | WidgetHandler | ConfigFlowHook | Lifecycle
│   ├─ TargetFile, AnchorId
│   └─ Scripts[]
│       ├─ Id, Name, X, Y, Disabled, Comment
│       ├─ Hat                        HatBlock (kind + configuration)
│       └─ Body: Block[]              the statement stack
├─ Variables[]                       name, type, initial, scope: local | host
├─ Lists[]                           name, itemType, initial
└─ Procedures[]                      name, parameters[], returns?, body: Block[]
```

`X`/`Y` are the script's position on the canvas, in workspace units, so arranging scripts is part of
the document and survives a reload.

### 6.2 Block tree

Statements and reporters live in **one** hierarchy; `Shape` is the discriminator.

```
Block (abstract)
├─ Id                       stable across edits: layout cache, undo, diagnostics, selection
├─ Shape
├─ Inputs: BlockInput[]     ordered, each with a label and a preferred type
└─ Fields                   inline menu selections and literal state

BlockInput  = Literal(BlockValue) | Slot(BlockValue?) | Menu(key) | VarRef(name)
BlockValue  = LiteralText | LiteralNumber | LiteralBool | LiteralColor
            | VariableGet | ListGet | ParameterGet | PayloadGet | FlowFieldGet
            | Arithmetic | Compare | Logic | TextOp | MathFn | Random
            | SensingReporter | HostVariableGet | HttpReporter | ProcedureCall
```

Statement node families:

- **Hats** (§5.1 table) — one node per trigger with its configuration.
- **Flow:** `WaitSeconds`, `WaitUntil`, `RepeatN`, `RepeatUntil`, `Forever`, `While`, `If`, `IfElse`,
  `IfElseIf`, `Break`, `Continue`, `StopScript`, `StopAll`, `ReturnValue`, `Yield`, `Throw`,
  `FinishSuccess`, `FinishFailed`, `FinishAccepted`.
- **Cap:** the four `Finish*` blocks and `StopScript` / `StopAll` / `ReturnValue`.
- **Modifiers:** `Comment`, `Disable`.
- Plus one node per non-reporter catalog entry (Part 7).

### 6.2.1 As built (P1) — one node, described by the catalog

The sketch above describes a class hierarchy. The first implementation replaced it with **one generic
node**, and the reason is worth keeping: a class per block is right for eighteen statement kinds and
absurd for a hundred and fifty-three. Every one would be a file of properties the catalog already
declares, every new block would need a new class plus a serializer registration, and the emitter would
switch on a C# type — where a forgotten case is a silent fall-through rather than a failing test.

What shipped instead:

| Type | Role |
|---|---|
| `Block` | one node: `Kind`, `Id`, `Inputs`, `Fields`, `Bodies`, `Disabled`, `Comment` — plus `Walk`, `Body`, `Clone`, `VariableUses` |
| `BlockInput` | the slot union: `{slot: Block}`, `{var: name}`, `{text: …}`, `{number: …}`, `{bool: …}` — exactly one member, `Kind` decides which |
| `BlockShape` / `SlotType` / `BlockCategory` | the shape grammar, slot preferences and palette groups |
| `SlotDescriptor` / `MenuDescriptor` / `BodyDescriptor` / `SdkMapping` | what a catalog row declares about its block |
| `VisualProject` / `VisualTarget` / `VisualScript` / `VariableDeclaration` / `ListDeclaration` / `ProcedureDeclaration` | the document |
| `VisualProjectJson` | the format, with hand-written converters for `Block` and `BlockInput` |

Three consequences, each of which changed a decision in this document:

1. **An unknown block kind needs no placeholder type.** The kind is a string, so a block from a newer
   build already round-trips intact; the validator reports it. The earlier `PlaceholderBlock` idea is
   unnecessary, and dropping it removes a type whose only job was to hold data the document can hold
   itself.
2. **The document's variable vocabulary is the SDK's** (`Text`, `Numeric`, `Boolean` — `VariableType`),
   not C#'s `string`/`number`/`bool`. A host variable is created through `IUserVariableApi.CreateAsync`,
   which takes that enum, so keeping both spellings would mean translating in two places for ever.
3. **Menu keys are stable identifiers, not display text.** The emitter switches on them and the
   document stores them, so renaming a label (which Part 21 localizes) never invalidates a document.

Two defects the first test run caught, recorded because both are the kind that a compiler cannot see:

- A static factory named `Variable` collided with the `Variable` property on the same type (CS0102).
  The factory is `OfVariable`.
- The migration normalised a variable's type twice — reading back an already-normalised `Numeric`
  through the legacy spellings produced `Text`, so every numeric local was silently redeclared as text.

### 6.3 Why a nested tree, not Scratch's flat map

| Criterion | Flat id map (Scratch) | Nested tree (chosen) |
|---|---|---|
| Emitter complexity | index bookkeeping, re-parenting logic | a four-line recursive walk |
| Unit tests | assert indices and pointer rewrites | assert subtrees and shapes |
| Sidecar readability | dense, id-heavy | readable and git-diffable |
| Continuity with this repo | would replace `IfBlock.Then`/`Else` | extends it |
| Canvas splicing | excellent | fine at this scale (derived index on demand) |
| Undo/redo | complex pointer surgery | compare/substitute a subtree |

A real script holds tens to a few hundred blocks. Deriving a flat index for hit-testing on demand is
cheap at that scale, which removes the only real advantage of the flat map.

### 6.4 Serialization and migration

- `System.Text.Json`, polymorphic `kind` discriminator, `camelCase`, indented, invariant culture —
  the existing pattern, extended from one program to a project.
- `VisualProjectJson.TryDeserialize` runs a **migration pipeline**:

  | From | To | What changes |
  |---|---|---|
  | v0 — `BlockProgram` / `<action>.blocks.json` | v1 | 18 legacy kinds re-homed and aliased; a single flat `Statements` list becomes one target with one script under the `when action runs` hat; `TargetActionId`/`TargetFile` become the target's identity |

- Each migration is a pure function with its own test.
- Reading is forgiving: an unknown block kind becomes a `PlaceholderBlock` **with a diagnostic**
  rather than an exception, so a sidecar written by a newer build is visible and repairable instead
  of fatal.
- Legacy ids remain valid aliases forever, which is what "retire the page" means without losing
  anyone's work.

### 6.5 Validation — `VisualValidator`

Each diagnostic carries a severity, the block id, a human message, and (where one exists) a docs
path. Clicking it in the diagnostics pane selects and centres the block.

| Class | Examples | Severity |
|---|---|---|
| Shape/type | reporter in a statement gap; number slot holding text | Error / Warning |
| Required | unbound required slot; empty name | Error |
| Naming | duplicate procedure / variable / list; collides with a hand-written local | Error |
| Reference | `parameter {x}` not declared by the action; `get host variable {y}` unknown | Warning |
| Control flow | `break` outside a loop; `return` outside a procedure | Error |
| Infinite | `forever` with no wait and no host call | Warning |
| Capability | media block in a plugin without Music Players | Warning, with a Capabilities deep link |
| Target | file missing, anchor missing, region claimed elsewhere | Error |
| Unreachable | statements after a `Cap` or a `forever` | Warning |

---

## 7. Block catalog

### 7.1 Descriptor shape

```csharp
new BlockDescriptor(
    Id: "deck.open-folder",
    Category: BlockCategory.Deck,
    Name: "open folder {folder}",
    Summary: "Opens a folder on the pressing client.",
    Shape: BlockShape.Stack,
    Slots: [Slot.Required("folder", SlotType.Text, label: "folder id")],
    Menu: null,
    Sdk: "IIntegrationContext.Deck.ChangeFolderAsync(id, context.OriginClientId, ct)",
    CapabilityId: "deck",
    DocsPath: "features/deck",
    Interpreter: "fake deck switches folder, logs the move",
    IsVerified: true)
```

Rules the catalog is held to:

- **Unique id**, non-empty name and summary, a category, a shape.
- **Every entry has a palette row, an emitter and an interpreter step.** A block missing any of the
  three is a failing test, which is what makes "a block for everything" safe to grow.
- **`IsVerified` is test-enforced.** It may only be `true` when Appendix A records the SDK member
  that was inspected. Unverified blocks appear in the palette tagged *not yet verified* and cannot
  be saved.
- **Legacy aliases** (§7.4) must resolve to a real block.

### 7.2 Categories

| # | Category | Hue | Count |
|---|---|---|---|
| C1 | Control | `#FFAB19` | 24 |
| C2 | Deck (Motion) | `#4C97FF` | 14 |
| C3 | UI / Looks | `#9966FF` | 15 |
| C4 | Media (Sound) | `#CF63CF` | 17 |
| C5 | Events | `#FFBF00` | 15 |
| C6 | Sensing | `#5CB1D6` | 20 |
| C7 | Operators | `#59C059` | 32 |
| C8 | Variables | `#FF8C1A` | 8 |
| C9 | Lists | `#EA7B2C` | 12 |
| C10 | Parameters / Data | `#2FB8A6` | 10 |
| C11 | Network | `#6B5BD2` | 15 |
| C12 | My Blocks (Procedures) | `#FF6680` | 5 |
| | **Total** | | **187** |

Across all categories, `⚠` marks a mapping that Appendix A must confirm before the block ships.
These are the only rows that may be dropped or deferred, and the catalog never lies about which.

### 7.3 C1 — Control (amber `#FFAB19`) — 24

| Block | Shape | Inputs | Emitted / behaviour |
|---|---|---|---|
| `when action runs` | Hat | — | the action's `ExecuteAsync` entry; one per action target |
| `when script starts` | Hat | — | a script invoked only by `call` |
| `wait {n} seconds` | Stack | number | `await Task.Delay((int)(n * 1000), context.CancellationToken);` |
| `wait {n} ms` | Stack | number | `await Task.Delay(n, context.CancellationToken);` |
| `wait until <c>` | Stack | boolean | `await VisualRuntime.WaitUntilAsync(() => c, 50, ct);` |
| `repeat {n}` | C | number, body | `for (var i = 0; i < ToNumber(n); i++) { ct.ThrowIfCancellationRequested(); … }` |
| `repeat until <c>` | C | boolean, body | `do { … } while (!ToBool(c));` — body first, matching Scratch |
| `while <c>` | C | boolean, body | `while (ToBool(c)) { … }` — precondition, the Scratch `forever if` shape done honestly |
| `forever` | C | body | `while (true) { ct.ThrowIfCancellationRequested(); … }` |
| `if <c>` | C | boolean, then | `if (ToBool(c)) { … }` |
| `if <c> else` | CIf | boolean, then, else | `if (…) { … } else { … }` |
| `if / else if / else` | CChain | n conditions, n bodies | chained `else if`; unlimited middle bars |
| `break` | Stack | — | `break;` — refuted outside a loop |
| `continue` | Stack | — | `continue;` — refuted outside a loop |
| `stop this script` | Cap | — | `return ActionResult.Success();` |
| `stop all scripts` | Cap | — | drops remaining iterations; emits a comment explaining the semantics |
| `yield to host` | Stack | — | `await Task.Yield();` plus a cancellation check |
| `return {value}` | Cap | value | `return value;` — procedures only |
| `finish: success` | Cap | — | `return ActionResult.Success();` |
| `finish: failed {code} {message}` | Cap | menu, text | `return ActionResult.Failed(ActionErrorCodes.CODE, "message");` |
| `finish: accepted {message}` | Cap | text | `return ActionResult.Accepted("message");` |
| `throw {message}` | Stack | text | `throw new InvalidOperationException("message");` |
| `comment {text}` | Modifier | text | `// text` at the block's indent; never changes behaviour |
| `disable` | Modifier | — | the block and its body are skipped by emitter *and* interpreter |

`ActionErrorCodes` members come from `BlockCompiler.ErrorCodes` (transcribed from the SDK, nine of
them) — the menu is that list, not a hand-typed copy.

### 7.4 C2 — Deck / Motion (blue `#4C97FF`) — 14

| Block | Shape | Inputs | Emitted / behaviour |
|---|---|---|---|
| `open folder {id}` | Stack | text | `_integration.Deck.ChangeFolderAsync(id, context.OriginClientId, ct)` |
| `open folder {id} on client {c}` ⚠ | Stack | text, text | same, with an explicit client id |
| `go to parent folder` | Stack | — | `_integration.Deck.GoToParentAsync(context.OriginClientId, ct)` |
| `go back` | Stack | — | `_integration.Deck.GoBackAsync(context.OriginClientId, ct)` |
| `switch profile {id}` | Stack | text | `_integration.Deck.ChangeProfileAsync(id, context.OriginClientId, ct)` |
| `open profile start folder {id}` ⚠ | Stack | text | profile switch to its start folder |
| `current folder` | Reporter | — | the origin client's open folder ⚠ |
| `current profile` | Reporter | — | the active profile ⚠ |
| `folder of client {c}` | Reporter | text | per-client folder ⚠ |
| `connected client count` | Reporter | — | number ⚠ |
| `connected clients` | Reporter(list) | — | client ids ⚠ |
| `is client connected {c}` | Boolean | text | ⚠ |
| `set button state {action} {state}` | Stack | text, menu | the Button States capability |
| `invalidate icon {action}` | Stack | text | `_integration.Widgets.InvalidateIconAsync(id, ct)` |

### 7.5 C3 — UI / Looks (purple `#9966FF`) — 15

| Block | Shape | Inputs | Emitted / behaviour |
|---|---|---|---|
| `notify {title} {message}` | Stack | text, text | `_integration.Notifications.Notify(new MacroDeck.Sdk.Notifications.UserNotificationRequest { … })` |
| `notify {level} {title} {message}` | Stack | menu info/warning/error, text, text | as above with `Level` |
| `notify {key} {title} {message}` | Stack | text, text, text | as above with `Key`, which replaces a previous notification |
| `clear notification {key}` ⚠ | Stack | text | dismiss by key |
| `show modal {view} {title}` | Stack | text, text | `_integration.Ui.ShowModalAsync(new ModalDefinition { … })` |
| `show modal {view} {title} with {data}` | Stack | text, text, key=value lines | as above with `Data` |
| `log {level} {template}` | Stack | menu, text | `_logger.Information(…)` with `{name}` holes resolved from `context.Parameters` |
| `log {level} {template} with {param}` | Stack | menu, text, picker | as above plus a structured argument |
| `report issue {id} {title}` | Stack | text, text | the Integration Issues capability; a standing, resolvable condition |
| `clear issue {id}` | Stack | text | resolves a previously reported issue |
| `set button state to {state}` | Stack | menu | the Button States capability ⚠ |
| `set icon from {asset}` ⚠ | Stack | text | the Button Icons capability |
| `update widget {id}` ⚠ | Stack | text | asks the widget host to refresh |
| `invalidate widget icon {id}` | Stack | text | same call as `invalidate icon`, named for widgets |
| `upload asset {path}` ⚠ | Stack | text | the `assets:upload` permission surface |

### 7.6 C4 — Media / Sound (pink `#CF63CF`) — 17

Play / pause / resume / stop / next track / previous track / set volume `{n} %` / change volume by
`{n}` / seek to `{n} s` / set shuffle `{on|off}` / set repeat `{mode}` — all **Stack**, all driving the
Music Players capability ⚠.

Reporters and predicates: `volume`, `position`, `duration`, `current track`, `playback state`
(**Reporter**), `is playing` (**Boolean**) — all ⚠.

**Whole-category gate.** Appendix A must establish whether Music Players exposes a callable action
surface, or only metadata for the Music Player widget. If it is metadata only, C4 is reduced to
whatever the widget surface supports and the rest of the category is dropped rather than faked.

### 7.7 C5 — Events (yellow `#FFBF00`) — 15

| Block | Shape | Inputs | Emitted / behaviour |
|---|---|---|---|
| `when event received {id}` | Hat | menu of declared events | the event target's handler |
| `when key pressed {key}` | Hat | text/keys menu ⚠ | host key binding |
| `when client connected` / `when client disconnected` | Hat | — | ⚠ lifecycle |
| `when profile changed` | Hat | — | ⚠ |
| `when folder opened` | Hat | — | ⚠ |
| `when message received {topic}` | Hat | text | `IIntegrationContext.Messages` subscription |
| `publish event {id}` | Stack | menu | `_integration.Events.PublishAsync(…)` |
| `publish event {id} with {payload}` | Stack | menu, key=value lines | as above with a payload map |
| `publish and wait` ⚠ | Stack | menu | whether the host acknowledges |
| `send message {topic} {body}` | Stack | text, text | Messaging capability |
| `broadcast to self {id}` ⚠ | Stack | menu | publish routed back to this plugin |
| `event id of last occurrence` ⚠ | Reporter | — | from the last delivered occurrence |
| `message topic of last` ⚠ | Reporter | — | |
| `message body of last` ⚠ | Reporter | — | |
| `unbind all event handlers` ⚠ | Stack | — | teardown |

### 7.8 C6 — Sensing (cyan `#5CB1D6`) — 20

| Block | Shape | Inputs | Emitted / behaviour |
|---|---|---|---|
| `parameter {name}` | Reporter | menu of declared parameters | bounds-checked read from `context.Parameters` |
| `parameter {name} or {default}` | Reporter | menu, value | with a fallback |
| `parameter exists {name}` | Boolean | menu | `context.Parameters.ContainsKey(…)` |
| `get host variable {name}` | Reporter | text | the Variables capability |
| `host variable exists {name}` | Boolean | text | ⚠ |
| `set host variable {name} to {v}` | Stack | text, value | ⚠ write access must be confirmed |
| `change host variable {name} by {n}` | Stack | text, number | ⚠ read-modify-write |
| `get setting {key}` ⚠ | Reporter | text | plugin settings |
| `set setting {key} to {v}` ⚠ | Stack | text, value | plugin settings |
| `timer` | Reporter | — | seconds since the script began (per invocation) |
| `reset timer` | Stack | — | |
| `uptime` | Reporter | — | process uptime |
| `current {menu}` | Reporter | menu: year, month, day, weekday, hour, minute, second, date, time, timestamp — local and UTC | `DateTime.Now` / `DateTime.UtcNow`, invariant |
| `format date {v} as {pattern}` | Reporter | value, text | invariant `ToString(pattern)` |
| `seconds since {date}` | Reporter | value | |
| `origin client id` | Reporter | — | `context.OriginClientId` |
| `origin widget id` | Reporter | — | `context.OwnerWidgetId` |
| `action id` | Reporter | — | this action's id |
| `session established` | Boolean | — | `_integration is not null` |
| `run host script {id}` / `… with {inputs}` | Stack | text, pairs | `_integration.Scripts.RunAsync(…)` |

### 7.9 C7 — Operators (green `#59C059`) — 32

All pure; none requires the host; all are warning-free C#.

Arithmetic and maths: `+`, `-`, `*`, `/`, `remainder`, `round {n}`, `{n} math {menu}` where the menu
is *abs, floor, ceiling, sqrt, sin, cos, tan, asin, acos, atan, ln, log, e^, 10^*, `clamp`,
`random {a} to {b}`, `random item of {list}`.

Comparison and logic: `{a} < {b}`, `=`, `{a} > {b}` (**Boolean**), `and`, `or`, `not`
(**Boolean**).

Text: `join {a} {b}`, `join {list} with {sep}`, `letter {n} of {text}`, `length of {text}`,
`{text} contains {sub}` (menu: exact / ignoring case), `substring of {text} from {a} length {b}`,
`replace {find} with {replace} in {text}`, `{text} uppercase`, `lowercase`, `trim`,
`split {text} by {sep}` → list, `format {template} with {args}`.

Encoding and data: `url encode {text}`, `base64 encode {text}`, `base64 decode {text}`,
`json get {path} from {text}`.

Emission notes: everything routes through `VisualRuntime.ToText/ToNumber/ToBool/Compare`, so a
division by a text slot is well-defined rather than a compile error; comparisons use
`StringComparison.Ordinal`, matching the current compiler's stance; a one-character `contains`
needle is emitted as a char literal to avoid CA1847 (the existing compiler already does this).

### 7.10 C8 — Variables (orange `#FF8C1A`) — 8

| Block | Shape | Emitted / behaviour |
|---|---|---|
| `set {var} to {value}` | Stack | declares the local with an inferred type (`string?` / `double?` / `bool?` / `VisualList?`) |
| `change {var} by {n}` | Stack | `var = ToNumber(var) + n;` with a text-append special case when the variable holds text |
| `{var}` | Reporter | reads the local, coerced at the use site |
| `watch {var}` | Stack | registers the variable in the debugger watch table (the adaptation of Scratch's *show variable*) |
| `unwatch {var}` | Stack | removes it |
| `set {var} from parameter {p}` | Stack | the legacy `set-variable` behaviour, with the `Required` guard |
| `set {var} from host variable {n}` | Stack | ⚠ host read |
| `delete all my variables` | Stack | clears locals (debug convenience, emitted as a no-op comment block if there are none) |

### 7.11 C9 — Lists (deep orange `#EA7B2C`) — 12

`add {item} to {list}` · `delete {n} of {list}` · `delete all of {list}` · `insert {item} at {n} of
{list}` · `replace item {n} of {list} with {item}` — **Stack**, all `VisualList` operations.

`item {n} of {list}` · `item # of {item} in {list}` · `length of {list}` · `{list}` ·
`join {list A} to {list B}` · `sort {list}` — **Reporter**.

`{list} contains {item}` — **Boolean**.

### 7.12 C10 — Parameters / Data (teal `#2FB8A6`) — 10

`read parameter {name} as {type}` · `read event payload {name}` · `read flow field {name}` ·
`parameter has value {name}` (**Boolean**) · `to number {v}` · `to text {v}` ·
`to boolean {v}` · `is numeric {v}` (**Boolean**) · `default if empty {v} {fallback}` ·
`decode json {text}` → list of pairs.

### 7.13 C11 — Network (indigo `#6B5BD2`) — 15

| Block | Shape | Emitted / behaviour |
|---|---|---|
| `HTTP GET {url}` | Stack | `await VisualRuntime.HttpTextAsync(url, "GET", null, null, ct)` into a local |
| `HTTP POST {url} body {body}` | Stack | as above with `"POST"` and a body |
| `HTTP {method} {url}` | Stack | menu GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS |
| `HTTP request {method} {url} with headers {h} body {b} as {v}` | Stack | full form; headers are `name=value` lines |
| `set bearer token from parameter {p}` | Stack | sets `Authorization: Bearer …` from `context.Parameters` |
| `retry {n} times waiting {ms}` | C | wraps a body and retries on failure |
| response reporters | Reporter | `response body of {v}`, `status code of {v}`, `response header {name} of {v}` |
| `response is success of {v}` | Boolean | 2xx |
| `download {url} to {path}` ⚠ | Stack | writes a file |
| `request timed out` | Boolean | from the last request |
| `upload file {path} to {url}` ⚠ | Stack | multipart |
| `websocket connect {url}` / `websocket send {text}` ⚠ | Stack | only if the SDK documents a client surface |

The emitter keeps the existing stance on clients: one `HttpClient` per call with a comment pointing
at `IHttpClientFactory`, because a block has nowhere to cache one. Request blocks declare a local
holding a small result record so the reporters have something to read.

### 7.14 C12 — My Blocks / Procedures (magenta `#FF6680`) — 5

| Block | Shape | Emitted / behaviour |
|---|---|---|
| `define {name}` | Hat | a local function, plus one input slot per parameter |
| `define {name} returning {v}` | Hat | a local function with a non-void return |
| `call {name}` | Stack | `await NameAsync(args);` |
| `call {name} with {args}` | Stack | as above with arguments |
| `{name} with {args}` | Reporter | the reporter form for a returning procedure |

Procedures hoist to the top of the region so a call reads naturally before its declaration, and are
emitted as:

```csharp
async Task<object?> MyProcedureAsync(string? who)
{
    …body…
}
```

Validation guarantees: unique names, a call must resolve, recursion is detected and warned about
(bounded by the action's timeout, not by us), and a parameter name may clash with nothing in scope.

### 7.15 Legacy alias map

The retired page's eighteen kinds remain loadable, and appear in the palette only via their new
homes:

| Legacy id | New block |
|---|---|
| `log` | C3 `log {level} {template}` |
| `set-variable` | C8 `set {var} from parameter {p}` |
| `if` | C1 `if <c>` (with the legacy operand/operator mapped to a comparison reporter) |
| `return-result` | C1 `finish: success` / `finish: failed` / `finish: accepted` |
| `delay` | C1 `wait {n} ms` |
| `http-request` | C11 `HTTP GET {url}` |
| `notify` | C3 `notify {level} {title} {message}` |
| `navigate` | C2 `open folder {id}` |
| `go-to-parent` | C2 `go to parent folder` |
| `go-back` | C2 `go back` |
| `change-profile` | C2 `switch profile {id}` |
| `run-script` | C6 `run host script {id} with {inputs}` |
| `publish-event` | C5 `publish event {id} with {payload}` |
| `read-variable` | C6 `get host variable {name}` |
| `set-variable-value` | C6 `set host variable {name} to {v}` |
| `show-modal` | C3 `show modal {view} {title} with {data}` |
| `invalidate-icon` | C2 `invalidate icon {action}` |
| `throw` | C1 `throw {message}` |

---

### 7.16 P0 verdicts — every ⚠ resolved

The inventory in Appendix A is done: the pinned `MacroDeck.Sdk` and `MacroDeck.Localization` assemblies
were read with `tools/SdkInventory`, and every row below is a verdict on a `⚠` in §7.3–§7.14.

| Category | Ships | Deferred to Phase 9 | Dropped | Discovered |
|---|---|---|---|---|
| C1 Control | 24 | — | — | — |
| C2 Deck | 14 | — | — | — |
| C3 UI / Looks | 12 | — | 3 | 3 |
| C4 Media | **0** | — | **17** | — |
| C5 Events | 3 | 6 | 6 | 2 |
| C6 Sensing | 20 | — | — | — |
| C7 Operators | 32 | — | — | — |
| C8 Variables | 8 | — | — | 2 |
| C9 Lists | 12 | — | — | — |
| C10 Parameters | 10 | — | — | — |
| C11 Network | 13 | — | 2 | — |
| C12 Procedures | 5 | — | — | — |
| **Total** | **153** | **6** | **28** | **7** |

#### Dropped, with the reason

| Block | Why it cannot ship |
|---|---|
| All 17 C4 media blocks | `IMusicPlayer` (play, pause, next, seek, volume, shuffle, repeat, state) is an interface the **plugin implements** to supply a player to Macro Deck. `IIntegrationContext` has no music member, so an action has nothing to call. The right home is `MacroDeck.Sdk.MusicPlayer.Actions` (`MusicPlayerActions`, `MusicPlayerActionDefinition`, `MusicPlayerItemActionDefinition`, `MusicPlayerStateActionDefinition`, `MusicPlayerDeviceActionDefinition`) — that is the **Actions editor's** job, not a block. |
| `report issue`, `clear issue` | `IIntegrationIssueProvider` is implemented by the plugin; the host *polls* `GetIssuesAsync` and calls `ResolveIssueAsync`. Not callable from an action. |
| `set icon from {asset}` | Icons are supplied through `IIconProviderActionDefinition`. There is no host call that pushes an icon for an action id. |
| `publish and wait` | `IEventPublisher.Publish` is `void` and synchronous. There is no acknowledgement API anywhere in the event surface. |
| `broadcast to self` | No self-delivery concept; `Publish` goes to the host. |
| `event id of last occurrence` | No such state exists. |
| `message topic of last`, `message body of last` | `IMessageChannel` is subscribe-based; there is no "last message" state to read. |
| `unbind all event handlers` | Lifetime management, not an action-time operation. |
| `websocket connect`, `websocket send` | No WebSocket client surface in the SDK. Network blocks stay HTTP, which plain .NET already covers. |

#### Deferred to Phase 9 (non-action targets)

The six C5 hats — `when event received`, `when key pressed`, `when client connected`,
`when client disconnected`, `when profile changed`, `when folder opened`, `when message received` —
are real triggers, but they belong to **integration-lifetime and widget** surfaces, not to an action
body. `IMessageChannel.SubscribeAsync` returns an `IAsyncDisposable`, which is a lifecycle
subscription; `IEventPublisher.GetBindings()` plus `BindingsChanged` describe host-side bindings. So
these hats ship with the Phase 9 targets (`LifecycleHookTarget`, `WidgetHandlerTarget`) where an
owner exists to dispose them.

#### Discovered (not anticipated by any row)

| Block | Surface |
|---|---|
| `show modal and get the result` (reporter) | `IUiInteractions.ShowModalAsync<T>` returns `Task<ModalResult<T>>` — the SDK can hand a value back, so Scratch's `ask and wait` has a real analogue. |
| `ask the user to pick an item` | `IActionInteractions.RequestItemPicker(originClientId, instanceId, kind, prompt)` |
| `ask the user to pick a device` | `IActionInteractions.RequestDevicePicker(originClientId, instanceId, startPlayback, prompt)` |
| `request over a channel` (command + reporter) | `IMessageChannel.RequestAsync(topic, payload, timeout, ct)` → `JsonElement?` |
| `widget exists {id}` (boolean) | `IWidgetApi.Exists(widgetId)` |
| `toggle host variable {name}` | `UserVariableOperation.Toggle` |
| `append to host variable {name}` | `UserVariableOperation.Append` |

#### Corrections that change the design, not just the catalog

1. **Interaction surfaces live on `ActionExecutionContext`, not on the integration context.**
   `context.Ui` (`IUiInteractions`) and `context.Interactions` (`IActionInteractions`) are the modal and
   picker entry points. The existing emitter is already correct about this; the new emitter must route
   every modal and picker block through `context`, and the host guard does not apply to them — it
   applies to `_integration` only. `IUiInteractions` is null until a session exists, so those blocks
   need a null check of their own.

2. **Reading a host variable and writing one are two different APIs.** `IVariableApi.GetByNameAsync` →
   `VariableHandle.Value` reads a declared variable; `IUserVariableApi.ApplyAsync` writes a user
   variable with an operation (`Set`, `Add`, `Toggle`, `Append`). One block cannot cover both, and the
   picker lists declared variables while the writer lists user variables. The catalog must split them.

3. **`ActionErrorCodes` is a static class of string constants, not an enum.** The nine values are
   `INVALID_PARAMETER`, `NOT_CONFIGURED`, `NOT_CONNECTED`, `NOT_FOUND`, `PERMISSION_DENIED`,
   `PROVIDER_ERROR`, `PROVIDER_REJECTED`, `TIMEOUT`, `UNAVAILABLE` — and they are the **values**, which
   is what a menu must offer, because the existing `BlockCompiler.ErrorCodes` transcription matches the
   **member names** and happens to produce the same set. A test now pins both.

4. **`LocalizedText` has `op_Implicit(String)`.** A raw string literal is legal wherever a
   `LocalizedText` is expected, which validates every existing `ActionResult.Failed(code, "…")` and
   `Notify({ Message = "…" })` emission. A localized message is `LocalizedString` (a `LocalizationKey`
   plus arguments) implicitly converted, so text that has a resx key should use that form.

5. **The repository's "29 editor types" claim is wrong: `ActionParameterType` has 27 values.**
   (String, Number, Boolean, Password, Secret, Choice, DynamicChoice, Autocomplete, MultiSelect, Color,
   File, Folder, Hotkey, Duration, DateTime, Json, Code, KeyValue, Object, Array, IpAddress, Url, Icon,
   Image, KeyboardSequence, KeyboardCombo, WidgetTarget.) This is recorded rather than fixed: it is a
   pre-existing documentation inaccuracy outside this feature, and correcting it belongs with a pass
   that reconciles `README.md`, `ROADMAP.md` and the editor's own type table.

6. **Plugin settings are keyed by a config entry id, not by a free-text key.** `IIntegrationConfig`
   exposes `GetEntriesAsync`, which returns `ConfigEntrySnapshot`s, and reads/writes take a `Guid`
   `entryId` plus a key. So `get setting` / `set setting` need a **config entry picker** rather than a
   text box, and `GetSecretAsync` existing is exactly why Part 27.2's rule (a secret is read at
   runtime, never written into the document) is enforceable rather than aspirational.

### 7.17 As built (P1b) — what the catalog is, and what changed

The catalog ships in two files: `BlockCatalog.cs` holds the rules, lookups and the deliberate-absence
lists (`Deferred`, `Dropped`); `BlockCatalogRows.cs` holds the vocabulary, one method per category.
`Search` matches kind, label and summary — Part 9.7's requirement, testable before a window exists.

**The counts moved from 153 to 156, and each +1 is a decision with a name.** The §7.3–§7.14 tables were
written before the P0 verdicts; building the rows against the real surface found blocks the tables had
no row for, plus three the tables promised that the surface could not honour:

| Change | Why |
|---|---|
| C5 **+2**: `events.request-message` (the discovered `Messages.RequestAsync` reporter), `events.send-message` kept, `publish`/`publish-with-payload` split as the table already listed | the two discovered blocks from §7.16 needed rows, not footnotes |
| C9 **+1**: `list.define` | Scratch declares lists implicitly on first use; a code generator cannot. An explicit declaration block is the one honest way to scope a list, and the editor uses it to drive the palette of list names |
| C11 **−2**, then **+1**: `websocket connect/send` dropped (no client surface, §7.16), `http.set-bearer` added | the legacy `http-request` block carried a bearer parameter; as a slot it would have vanished on migration. A separate block keeps the rule visible and the request blocks simple |
| C10 **−1**: `read flow field` folded into `data.read-payload` | the P0 verdict found no separate flow-field surface; one reader with a picker covers both |
| `control.finish-failed`'s code menu is exactly the nine `ActionErrorCodes` **member names**, pinned by test | §7.16 finding 3: the values are upper-snake constants; the member names are what the emitter writes |
| `sensing.get-host-variable` routes through a runtime helper that maps `GetByNameAsync`'s **throw-on-absent** to null | the SDK has no try-read; a bare `?.Value` on a throwing call would be a bug in every generated action |
| `ui.show-modal*` and `ui.update-widget` construct `ModalDefinition { ViewId = … }` / `WidgetAppearanceRequest { WidgetId = … }` directly | §A.2: the surface defines no `Create` factory; the first draft invented two |
| Categories: the enum keeps **Media** (empty, with the reason in its comment), the rail renders the 11 that have blocks | the decision needs somewhere to live; an empty heading does not |
| One **`Placeholder`** shape and one `legacy.unsupported` block, never draggable, in the catalog but not the palette | the migration's record of an unknown statement must resolve for the emitter but must not be offerable |
| `SlotType` gained `Variable` and `Procedure` and lost nothing; source slots (list, variable, parameter, procedure, …) never carry a hardcoded default name | the canvas turns a source slot into a dropdown of the right names; a default like "items" would be a block born referencing a list that does not exist |

**The migration was rewritten against the catalog, and the first version was wrong in a way that
parsed.** It wrote slot values with `WithField`, which is legal JSON, round-trips perfectly, and emits
nothing: the emitter and the canvas read `inputs`, so every migrated block would have run with empty
slots. `BlockCatalogTests` now migrates one instance of every legacy kind — reflected from
`BlockModel`, so a nineteenth kind is covered the moment it exists — and refuses any key the block's
descriptor does not declare. The other corrections the test suite forced:

- Variants are chosen by what the legacy block actually set: `ui.log` vs `ui.log-with-param`,
  `ui.notify` vs `ui.notify-level` vs `ui.notify-key`, `ui.show-modal` vs `ui.show-modal-data`,
  `sensing.run-script` vs `sensing.run-script-with-inputs`, `events.publish` vs
  `events.publish-with-payload`. The legacy model had one block with every property optional; the
  catalog's variants are honest about what they do.
- `read-variable` migrates to **`var.set-from-host`**, not `sensing.get-host-variable`: the legacy
  block read into a local, and a read with nowhere to put it is not a program.
- A literal set loses its `type`/`valueKind` menu entirely: the slot's own value kind is the type, and
  design C8 says the local is declared by inference. Two records of one type disagree; one is kept.
- `==` normalises to `=` (the catalog's menu key), error codes normalise case-insensitively against
  the real nine, log levels against the real five — a menu value that is not one of its options is a
  blank dropdown on the canvas and a fall-through in the emitter.

---

## 8. Code generation — `DeckForge.CodeGen/Visual/`

### 8.1 Pieces

```
ExpressionEmitter      → a C# expression for a BlockValue, coerced via VisualRuntime
StatementEmitter       → C# statements for a Block tree: loops, branches, guards, cancellation
ProcedureEmitter       → local async functions and their call sites
VisualRuntimeTemplate  → the generated support file (Part 8.4)
VisualTargetWriter     → per-target splice, diagnostics, idempotence
BlockCompiler          → facade: unchanged public surface, delegates to the above
```

### 8.2 Inherited invariants (must not regress)

- Exactly three names are always in scope: `context`, `_logger`, `_integration`.
- Every host call is preceded by the `_integration is null` guard, emitted once per region.
- The region lives between `// <macrodeck-blocks>` and `// </macrodeck-blocks>`; everything outside
  survives regeneration.
- The executor is made `async` when a block awaits (`EnsureAsyncExecutor`), including repairing the
  stock template's `SucceededTask` / `Task.FromResult` returns.
- The region is inserted **before** the method's final top-level `return`
  (`LastTopLevelReturn`), never after it — appending after made the region dead code and produced
  CS0162 on every build.
- The file's own indentation style is respected (the official template is tab-indented).
- No local may collide with the three names or with a hand-written local. Collisions are **refused
  with a message**, never renamed, because `If` reads its operand by name.
- Output is deterministic: the same document always produces byte-identical C#.
- Generated code carries no analyzer warnings (MDP3001 and friends), because a warning in a user's
  build is a support ticket.

### 8.3 New behaviour

- **Inferred local types** from the value's inferred type; reads coerce.
- **Cancellation per iteration** in every loop: `context.CancellationToken.ThrowIfCancellationRequested()`.
- **`repeat until` tests after the body** (do-while), matching Scratch; the interpreter reads the
  same `BlockSemantics` table so the two cannot disagree (§Appendix B).
- **Cap blocks** end the region honestly: a `return` in the middle of a stack makes later statements
  unreachable, so the emitter warns and the validator flags it.
- **Procedures as local functions**, hoisted (Finding 1).
- **Modifiers** (`comment`, `disable`) emit a comment or nothing at all, and the interpreter skips
  disabled blocks too, so preview and generated code agree.
- **Guard deduplication:** the host guard is emitted once per region rather than before every call,
  which is what the current compiler does and what keeps the generated method readable.

### 8.4 The generated runtime library

Scratch semantics need helpers that raw SDK calls cannot express: loose coercion, lists, timers,
polling loops. Emitting them per site would bloat the user's method, so one file is generated per
plugin that has visual scripts:

```
src/<Plugin>/VisualRuntime.cs
```

- Header `// <auto-generated by DeckForge. Edits are overwritten.>`
- `internal static class VisualRuntime`
- `ToText(object?)`, `ToNumber(object?)`, `ToBool(object?)`, `Compare(object?, object?)` per §5.3
- `VisualList` — add, delete, insert, replace, item, index-of, length, contains, clear, sort, join,
  and an invariant text projection
- `Now()`, per-invocation timer helpers, `WaitUntilAsync(cond, poll, ct)`, `RepeatForeverAsync(…)`
  with cancellation honoured
- `HttpTextAsync(url, method, headers, body, ct)` and a small `HttpResult` record
- Written idempotently by a CodeGen contributor; regenerated on save; removed only on request

A test asserts the file compiles **and** that its public surface matches exactly what the emitters
reference — an emitter cannot name a helper that does not exist.

### 8.5 Target writers

| Target | Status | Anchor |
|---|---|---|
| `ActionExecutorTarget` | exists (via `BlockProgramWriter`) | `ExecuteAsync(ActionExecutionContext context)` |
| `WidgetHandlerTarget` | Phase 9, gated on Appendix A | the generated widget provider's handler bodies |
| `ConfigFlowHookTarget` | Phase 9, gated | the generated `IConfigFlow` step hooks |
| `LifecycleHookTarget` | Phase 9, gated | `IIntegration.InitializeAsync` / teardown |

Each reuses `Splice`, `MatchingBrace` and `LastTopLevelReturn` rather than re-implementing anchoring —
the last re-implementation of that logic produced a defect that only appeared on the *second* save.

### 8.6 As built (P2) — one emitter, not three, and a compile-verified runtime

As built, Part 8 shrank and hardened:

- **One emitter, data-driven (§8.1 → one class).** `ExpressionEmitter` / `StatementEmitter` /
  `ProcedureEmitter` collapsed into a single `VisualEmitter`: the catalog row's `SdkMapping.Expression`
  *is* the emission, with holes filled per slot type. A new block therefore needs no emitter change,
  and the risk that the catalog and the generator disagree about what a block does is designed away
  rather than tested for. Control flow (repeat, forever, if family, caps, break/continue, return) is
  special-cased in code because its emission is structure, not an expression. A reporter at statement
  position is a compile problem, never a silently empty statement — except `proc.call-value`, which
  the palette shapes like a reporter but which is a fire-and-forget call as a statement.
- **Holes come in two spellings, filled identically.** Braced `{seconds}` is the documented form; a
  bare hole — the slot's own name as a whole word, as in `VisualRuntime.ToNumber(ms)` — reads as if
  the slot were a local, which is how the rows are written. Rendering is per slot type: a `List` slot
  becomes the local's identifier, a `Variable`/`Parameter`/host-variable slot becomes the name as a
  string literal (the runtime stores by name, which is what makes a rename-carrying canvas possible),
  a `Procedure` slot becomes the local function's identifier with the `Async` suffix, and value slots
  become literals converted at the use site or a nested reporter's expression, recursively.
- **Procedures: parameters travel as objects, bound by name at entry.** The spec's typed parameters
  could not survive a call site that only knows `name=value` text (a `double? n` parameter and a call
  `greetAsync("n=1")` cannot compile). As built, every parameter is `object?`, the body opens with
  `VisualRuntime.Set(name, param)` lines that bind each into `Locals` under its own name, and the
  call site parses its `name=value` lines into positional arguments in declaration order, verified
  against the document's procedure declarations. An unknown procedure keeps the whole-string
  behaviour *and* reports a problem, so intent is never silently scrambled. Emission stays hoisted
  local functions (Finding 1), emitted only into regions that reference them, `Async`-suffixed.
- **Guards dedupe at flush, not at emission.** Blocks note `WantsHostGuard` / `WantsInteractionGuard`
  on the emit state; `Flush()` prefixes the deduplicated guards. `sensing.session-established` reads
  `_integration is not null` directly, so its row suppresses the guard (`SuppressHostGuard`) rather
  than swallowing itself.
- **The writer reuses the old writer's proven parts.** `VisualProgramWriter` = compile → host check
  (region names `_integration` but the plugin has none → refuse with the Capabilities-page remedy) →
  wrap in `// <macrodeck-blocks>` → `BlockCompiler.Splice` → `EnsureAsyncExecutor`. The executor
  anchor is a substring (`Task<ActionResult> ExecuteAsync(…)`) precisely so it still matches after
  the first save made the method `async` — the exact second-save defect the old compiler once had.
- **`VisualRuntimeTemplate` is compile-verified against the real SDK (§8.4 kept).** The template is
  one raw string literal, so nothing compiled it until now. A throwaway probe materialized it into a
  minimal plugin project referencing `MacroDeck.Sdk 3.0.0-beta.14` and built: **five latent defects
  surfaced and were fixed** — a duplicate `IsNumeric` (CS0102 waiting to happen), `System.Json` for
  `System.Text.Json`, `System.ActionExecutionContext` for `MacroDeck.Sdk.Actions.ActionExecutionContext`,
  an invalid `new List<string>(StringComparer.Ordinal)` construction, and the runtime's own `Convert`
  method shadowing `System.Convert` inside `ToNumber`. The emitter-surface cross-check test (§8.4's
  "an emitter cannot name a helper that does not exist") scans the template against every
  `VisualRuntime.*` call the catalog makes and fails naming the missing member. The generated file's shape is unchanged from §8.4 (`internal static class VisualRuntime`,
  auto-generated header, `HttpResponseRecord`); `VisualList` became plain `List<string>` helpers.
- **`EnsureAsyncExecutor` hardened (§8.2's async invariant).** The old early-out on "already async"
  left `return ActionResult.SucceededTask;` in place when a user had made their executor async by
  hand — the moment a visual region adds an `await`, that method is CS4016. Both paths now repair
  task-returning returns through one `RepairReturns` helper; a regression test pins it.
- **Test count:** 24 emitter tests + 1 writer regression test; full suite 486 → **511 green**.

---

## 9. User interface — `DeckForge.App`

### 9.1 Shell integration

- `MainWindow.xaml`: `NavigationViewItem Content="Visual" Tag="visual"` added **in the slot the
  "Blocks" item occupied**, so the Ctrl+digit shortcuts of every page after it (Widget Designer
  onward) do not shift. Icon must be a real `SymbolRegular` member — which `XamlMarkupTests`
  verifies, because a bad icon here previously produced a live process with no window.
- `Tag` must be unique across the sidebar: `NavigationShortcutTests` enforces it.
- `App.xaml.cs`: singleton registrations for the page, its view models and services.
- `PageRegistry.Create`: `"visual" => …`. The `"blocks"` entry and nav item are removed, and
  `BlockActionPage` / `BlockActionViewModel` deleted (Phase 6).
- Singletons preserve unsaved canvas state across navigation. The page implements
  `IRefreshOnNavigate` and reloads **only when the workspace actually changed** — the old canvas
  discarded unsaved work by reloading on every navigation.

### 9.2 Layout

```
┌ header: Visual · target picker · undo/redo · zoom · Save · Export · Docs ──────┬ inspector ──┐
│ ┌ category rail ┐ ┌──────── palette (search, recents) ────────┐                │ selected    │
│ │ Control       │ │ a live miniature of every block, drag me  │                │ block's     │
│ │ Deck          │ │                                          │                │ fields,     │
│ │ Looks · Media │ │                                          │                │ docs link,  │
│ │ Events        │ │                                          │                │ block C#    │
│ │ Sensing       │ │                                          │                └─────────────┘
│ │ Operators     │ └──────────────────────────────────────────┘
│ │ Variables     │ ┌ script strip ┐┌ canvas ──────────────────────────────────┐
│ │ Lists         │ │ ▸ script 1   ││ [hat]                 [hat]              │
│ │ Parameters    │ │ ▸ script 2   ││  stack…                stack…            │
│ │ Network       │ │ ▸ procedure  ││                                          │
│ │ My Blocks     │ └──────────────┘└──────────────────────────────────────────┘
└─────────────────────────────────────────────┬ code pane ── diagnostics pane ─────┘
```

- Every panel collapses; the right dock has **Generated C#** and **Diagnostics** tabs so the canvas
  keeps most of the window.
- Columns use star/auto widths with `MinWidth`. The fixed-column budget enforced by
  `XamlMarkupTests` (900px) is respected, because the application has no horizontal page scrolling
  and overflow would be unreachable.

### 9.3 Visual language

- Category hues from §7.2. Each has a vertical fill gradient, a darker border tint, an inner top
  highlight, and a light-mode variant.
- New `Liquid.Block*` tokens are added to `Themes/LiquidTheme.cs` with dark and light variants, and
  referenced as `{StaticResource Liquid.Block…}`. **No hard-coded colours**, matching the existing
  rule that pages use only `Liquid.*`.
- Tiles: 10px radius, 44px minimum height, 13.5px `Liquid.FontFamily`, semi-bold labels, a 12%
  black shadow, hover lifts and lightens by 4%, pressed compresses, focus draws a 2px accent ring.
- **Real notch and tab geometry drawn with `Path`**, so a stack reads as physically connected rather
  than as a list of rounded rectangles.
- States: hover, pressed, focus, selected, invalid (amber outline plus a diagnostic dot), disabled
  (40% opacity, dashed outline), running (accent pulse — stage only), drop-target (accent glow on
  the receiving gap).
- Reporters are content-sized ovals; booleans are hexagons; both are vertically centred in their
  slot.
- Real empty states: the canvas explains how to start; an empty search says so; an empty target list
  points at the Actions page.

### 9.4 Rendering

- `BlockWorkspace` = `ScrollViewer` → zoom `ScaleTransform` → `Canvas` hosting one `ScriptView` per
  script at its document `X`/`Y`.
- `ScriptView` = the hat tile plus a recursive statement stack.
- `BlockTile` is a `ContentControl` whose template is chosen by `Shape` through a
  `DataTemplateSelector`. The `C` / `CIf` / `CChain` template contains an `ItemsControl` bound to
  the body, whose item template is **the same keyed `DataTemplate` referenced recursively** —
  legal in WPF because resource lookup defers.
- Bodies render a left indent rail with an inner notch so depth is legible at a glance.
- Inputs render as `InputSlotView`, which is either a static literal field or a host for a nested
  reporter tile.
- Menus render *inside* the tile, as Scratch does.

### 9.5 Drag and drop

**Two input paths, one resolver.**

| Path | Mechanism | Why |
|---|---|---|
| Palette → canvas | `DragDrop.DoDragDrop` with a `DataObject` carrying a block descriptor, plus a rendered bitmap as the drag image | cross-control, OS-level, standard affordances |
| Canvas → canvas | pointer capture plus an `Adorner` | magnetic feedback must be continuous; OLE drag cannot give smooth snapping feedback |

The **scorer is pure Core code** (`StackLayout` + `DropResolver`), so every rule is unit-testable
with no window.

**Candidate zones**

| Zone | Accepts |
|---|---|
| Value slot | reporters, boolean reporters |
| Stack gap (before/after each statement) | stacks, C-blocks, caps |
| C-block mouth and inner gaps (including when empty) | stacks |
| Hat slot (script top) | hats |
| Free canvas | stacks (creates a new script) or hats |
| Replace-on-top of a statement | stacks (replaces the statement) |

**Scoring**

1. Distance from the dragged block's notch to the zone's notch.
2. Horizontal overlap between the dragged footprint and the target stack.
3. Shape compatibility — a hard filter, not a weight.
4. A **stability bonus** for the zone most recently entered, so an indicator cannot flicker between
   two equidistant gaps.
5. A magnet radius of 40px at 100% zoom, scaled by zoom factor. Inside it, the ghost's notch aligns
   exactly with the target's tab and free-follow is suspended.

### 9.5.1 As built (P1c) — the resolver in Core

`DropResolver.cs` implements rules 1, 3, 4 and 5 exactly as written; it takes the candidates the
canvas enumerates and returns the winner plus the snapped ghost position. Two notes for the next
reader:

- **A shared boundary offers two gaps.** "After block *n*" and "before block *n+1*" are different
  candidates at the same coordinates — they differ in which block the document editor anchors the
  insert to. The stability bonus is what decides between them, which is the situation rule 4 was
  written for; the test suite pins the tie, not just the winner.
- **Distance weights y over x** (`|Δy| + |Δx|/2`): a stack is a vertical list, so a pointer slightly
  left of a gap should still land in it, while a pointer slightly above or below is probably aiming
  at the neighbouring gap. Rule 2's horizontal overlap is enforced by the canvas when enumerating
  candidates rather than in the scorer — Core sees only zones the pointer could plausibly mean.

`StackLayout.cs` is the third consumer of the same geometry the canvas draws and the exporter renders:
rects per block id, notch positions, body indents, cached heights by id when the caller has measured
ones. Layout is a pure function of (document, zoom, font scale), which is what makes the Appendix H
layout budget a stopwatch test instead of a feeling.

**Gesture rules**

- **Dragging carries everything below.** Grabbing statement *n* of a stack drags *n..end*, exactly
  like Scratch. Alt narrows the drag to the single block, with a tooltip that says so.
- **Pulling out of a C-block:** dropping a run on free canvas detaches it; dropping it into a gap
  re-parents it; the old body closes up behind it.
- **Wrap:** dropping a stack onto a C-block's mouth wraps it in one gesture.
- **Unwrap:** Alt+click on a C-block's shoulder splices its body into the parent stack. Scratch users
  reach for this constantly and never find it.
- **Auto-scroll** near any canvas edge, speed proportional to overshoot.
- **Auto-expand** a collapsed body after a 400ms hover during a drag.

**Undo/redo.** Every mutation goes through `DocumentEditor` as a command with an inverse:
`Insert`, `Delete`, `Move`, `EditField`, `Wrap`, `Unwrap`, `AddScript`, `DeleteScript`,
`RenameVariable`, `DefineProcedure`, `SetScriptPosition`, … One gesture is one transaction; field
typing coalesces per field per 800ms. A property test applies random command sequences and asserts
the document round-trips, which is how we avoid the classic "undo restored a deleted block twice"
class of bug.

**Keyboard.** Focus a tile; `↑`/`↓` traverse a stack; `Ctrl+↑`/`Ctrl+↓` move the block within its
body; `Delete`; `Ctrl+D` duplicate; `Ctrl+Z` / `Ctrl+Y`; `Tab` cycles slots; `Enter` opens a menu;
`Space` picks up and drops (a keyboard drag mode that reuses the same resolver); `Ctrl+K` opens
"add block…". **Nothing requires a mouse.**

### 9.6 Fields and the inspector

Slot editors by preferred type:

| Type | Editor |
|---|---|
| text | auto-growing single-line field |
| number | spinner with step 1, drag-to-scrub |
| bool | toggle rendered as a Scratch-style boolean dropdown |
| menu | inline dropdown inside the tile |
| variable | picker listing locals plus host variables |
| list | picker listing lists |
| parameter | picker listing the action's declared parameters |
| event | picker listing the plugin's declared events |
| icon | opens the Icon Studio gallery |
| colour | swatch plus picker (the SDK has colour editors) |
| file | file picker for asset/upload blocks |

The inspector mirrors the same fields with labels, summaries, a docs deep link, and a one-block C#
preview. Selecting a block in the canvas selects it in the inspector and vice versa.

### 9.7 Live code and diagnostics

- **Header:** target picker, script name, `Save to plugin`, `Export PNG`, `Export SVG`, undo/redo,
  zoom controls, overflow menu (new script, delete script, insert hat, format document).
- **Generated C# pane:** tinted, read-only, monospace, copy button, labelled "this is what Save
  writes". It recompiles on every mutation, debounced.
- **Diagnostics pane:** severity icon, message, click to select and centre the offending block, and
  a docs link where one exists.

### 9.8 Accessibility

- Full keyboard operation as above; focus order follows document order.
- Every tile exposes an automation peer with its label and shape, so screen readers can announce
  "repeat 10, C block, 3 statements inside".
- High-contrast: tiles fall back to border-only fills with strong outlines when the system asks.
- Text scales with the system font setting; tiles grow rather than clip.
- Colour is never the only signal: category is also encoded in the palette's text label and the
  block's dropdown affordance shape.

### 9.9 Performance budget

- Metrics cached per block id; invalidated on mutation, not recomputed per frame.
- Collapsible bodies for huge stacks; `VirtualizingStackPanel` wherever nesting allows.
- **Budget: 60fps drag with 500 visible tiles.** Measured by the user-driver harness and logged with
  a regression threshold.

### 9.10 As built (P3) — where the shapes live, and what the window found

Four pieces of Part 9 turned out to be Core rather than XAML, and each moved for the same reason:
`tests/DeckForge.Tests` cannot reference WPF, so anything written in a template is anything nothing can
check.

1. **`BlockOutline` decides the silhouettes (§9.3, §9.4).** It takes a shape and a width and height and
   returns closed contours of straight edges and elliptical arcs, plus the notch's position and, for a
   container, its header height and arm width. `BlockShapeGeometry` converts that to a
   `StreamGeometry`; a container's body is a *hole* in the same figure, filled even-odd, so one `Path`
   draws a C-block and its border without the edge showing through the mouth. Arcs are sampled rather
   than emitted as `ArcTo` segments: WPF measures arc angles the other way round from Core's, and a
   half-circle facing the wrong way is invisible in a build and obvious on screen.
2. **The size is an argument, not a property.** WPF measures first and asks afterwards, so the tile hands
   over the rectangle it actually got and the outline is exact. §9.4's `BlockMetrics` estimates stay for
   the layout engine and the drop resolver, which have to work before anything is measured — and P4 will
   pass the tile's measured heights back in through the `heightOf` parameter that `StackLayout` already
   takes.
3. **`BlockLabel` splits the label; `BlockFactory` builds the block.** A row's `repeat {count}` is
   written once and read from two places, so it is parsed once into words and holes, in Core, and a test
   walks all 156 rows asserting that no hole names a slot or menu the row does not declare — because a
   hole that does renders as the literal text `{count}` on a block, which looks like a block with a bug
   in its name rather than a catalogue error. A fresh block of any row is then derived from that row, so
   the palette and the sample document are projections of the catalogue rather than two hundred and
   fifty hand-written objects that a new row would be missing from.
4. **`VisualSampleProject` is the page's regression test.** One script per palette category containing
   every shipping block that can sit in a stack, with the declarations it needs collected from the blocks
   themselves so it validates without a single error. Hats are the four rows it cannot contain — a hat
   may only sit at the top of a script, and one per script means two of them have nowhere to go — and
   they reach the page through the palette, where all 156 are drawn.

Three things the real window found that reading the code did not, kept here because each is a class of
mistake this repository has already paid for once:

- **A `DataContext` assigned from a property-changed callback is overwritten when the element joins the
  tree.** `InputSlotView` took its slot that way, so every value hole rendered as an empty pill: the
  control was there, the text was not, and nothing anywhere said why. The data context is now set in
  markup on the child, where nothing can replace it.
- **A `RoutedEventArgs.Source` must be the element raising the event.** WPF builds the route by walking
  the visual tree from it, so putting a view model there — the obvious way to carry "which block was
  clicked" — builds a route from an object that is not in the tree and delivers the event to nobody. The
  block travels in a `BlockSelectedEventArgs` instead. Selection looks broken rather than failing, which
  is worse.
- **`ItemsControl.ItemTemplate` takes a `DataTemplate`; a selector needs `ItemTemplateSelector`.** The
  wrong one throws during measure, once per tile, and a hundred and fifty of them is a crash loop.

Two layout facts, both from driving the window at 960×640 rather than from reading §9.2:

- **The four panels do not fit side by side at the application's own minimum window**, and this
  application has no horizontal scrolling, so the diagnostics pane was drawn past the right-hand edge
  with no way to reach it. `XamlMarkupTests`' fixed-column budget cannot see it — every column is
  proportional — which is why §12's "real window" row exists. There is now a breakpoint at a page width
  of 1080: above it the canvas is a column between the palette and the diagnostics pane, below it the
  canvas takes a row of its own under the other three. That is §9.2's "every panel collapses", as a
  breakpoint rather than as four buttons, because a button that hides a panel has to be found again to
  bring it back and at that width there is no room for one per panel.
- **A horizontal `StackPanel` measures its children with infinite width**, so a wrapped string in one has
  nothing to wrap to and its tail is cut off. The diagnostics heading is a grid now.

Two smaller as-built notes:

- The tile's `Background` is `Transparent` and its children are not hit-testable — a `TextBlock` with no
  background and a `Panel` with no background are both invisible to the mouse — so a click on a block's
  words fell straight through it. One transparent `Border`, declared first so it paints nothing, makes
  the whole tile a target, which P4's drag ghost needs too.
- The category hues are turned into theme resources **by iterating `BlockCatalog.Categories`**, not by
  writing forty-four lines of colour. A new category that had no tokens would draw with no fill and read
  as an empty row, which is the same failure as a catalogue row the palette does not know.

---

## 10. Simulator, dry-run tracer and debugger

`DeckForge.Core/Visual/Runtime/` — no WPF dependency, fully testable.

### 10.1 Host abstraction

`IVisualHost` is the surface every runtime call goes through:

```
Deck · Notifications · Variables · Lists · Scripts · Events · Messages · Ui · Http · Clock · Log · Parameters
```

- **Real path:** the generated C# calls the SDK directly; `IVisualHost` exists for the interpreter
  and for tests.
- **`SimulatedHost`:** an in-memory implementation — a clickable folder/profile graph, a
  notification stack with levels and replace-by-key, a variable table including host-shared values,
  a script registry, an event bus with a log, a canned-dialog responder, a manually advanceable
  clock, and an HTTP layer that is **offline by default** (canned responses editable in a table)
  behind an explicit *allow real network* toggle.

### 10.2 Interpreter

`ScriptInterpreter` walks the tree and yields `ExecutionStep` records:

- `BlockEntered` / `BlockExited`
- `ValueComputed` — block, expression text, coerced value
- `HostCall` — surface, member, arguments, result
- `LogEmitted`, `WaitElapsed`, `BreakpointHit`, `Error`

Deterministic, cancellation-aware, with a seeded RNG so `random` is reproducible in tests.

### 10.3 Stage panel

- A mocked deck skin: folders as tiles, buttons with state, the pressing client highlighted.
- Notification area, an event log, and a variables/lists **watch table** fed by `watch`/`unwatch`
  (our adaptation of Scratch's *show variable*).
- A trace timeline of executed steps with values.
- Transport: `Run`, `Step`, `Step into`, `Pause`, `Stop`, `Reset`, a speed slider, and a hat
  selector — "trigger: when action runs / when event received / when widget pressed …".
- **Breakpoints:** click a tile's gutter to toggle; the interpreter pauses there and the tile
  pulses. Scratch has no equivalent; this is the marquee "better than Scratch" feature.

### 10.4 Dry-run tracer

The same interpreter with no fake stage: a form for parameter values, host variables and the clock,
then a step trace with computed values. This answers "what will my blocks actually do" without
building the plugin.

### 10.5 Honesty statement (appears in the UI, not only here)

The interpreter and the compiler are two engines. Parity is enforced by:

1. a shared `BlockSemantics` table that both interpreter tests and emitter tests assert against;
2. golden-file C# snapshots per sample program;
3. end-to-end compile tests that build the generated plugin.

The SDK remains the definition of behaviour. The interpreter is a preview, and the UI says so
rather than implying a guarantee it cannot give.

---

## 11. Persistence and workspace integration

```
<plugin>/.deckforge/visual/
├─ project.visual.json      targets, scripts, variables, lists, procedures
├─ stage.json               breakpoints, canned HTTP responses, watch list
└─ legacy/*.blocks.json     pre-migration sidecars, moved here untouched on first load
```

- `project.visual.json` is the source of truth. The plugin contains only real C#.
- On first load of a workspace that has legacy sidecars, they are migrated, the originals are copied
  to `legacy/` untouched, and a diagnostic says so.
- **Save is order-safe:** the runtime file, each target's code write, and the sidecar are separate
  steps; each is reported; success is never claimed before every step lands. This directly answers
  the old view model's defect of writing the `.cs` first and leaving a half-written action behind.
- Writes are atomic where the file system allows (write-temp, then replace), so an interrupted save
  cannot truncate a user's action.

---

## 12. Testing

Pure core means most of this is ordinary unit testing.

| Area | Tests |
|---|---|
| **Catalog** | unique ids; non-empty name and summary; every block has a palette row, an emitter and an interpreter step; every `IsVerified` backed by an Appendix A entry; legacy aliases resolve |
| **Model** | JSON round-trip per block type; unknown kind → placeholder plus diagnostic; v0 → v1 migration per legacy kind; property test over randomly generated documents |
| **Emitters** | coercion table (§5.3); loop and branch shapes; cancellation coverage; `Cap`/unreachable detection; determinism (compile twice, byte-equal); warning-free output |
| **Procedures** | local functions emitted and callable before declaration; recursion warned; name collisions refused |
| **Compile** | extend `GeneratedCodeCompilesTests`: a corpus of ~40 sample documents written into `src/<Project>/`, built, error-free — writing inside the project directory, because writing outside makes the assertion vacuous |
| **Geometry** | `StackLayout` heights and notch positions; `DropResolver` scoring including shape compatibility, stability bonus, magnet radius at zoom, detach, re-parent, wrap, unwrap, and replace-on-top |
| **Interpreter** | step sequences per block; coercion parity with the runtime library; breakpoints; cancellation; seeded randomness; watch semantics; disabled blocks skipped |
| **Markup** | `XamlMarkupTests` keeps passing: real icon members (named literally *and* bound from the catalogue), no method bindings, no grid cell beyond the definitions, fixed-column budget, no attribute value split across lines |
| **Real window** | the `%TEMP%\uidriver` harness drives the actual window: drag a block from the palette into a loop body, split a stack, wrap, undo, save — screenshotting each step for visual review. Necessary because `Process.Responding` cannot tell whether a window appeared, and WebView2 surfaces need screen capture rather than `GetWindowDC`. Phase 3 ran it against the Visual page before writing any drag code, which is how the diagnostics pane drawn off the right-hand edge of a 960-pixel window was found (§9.10) — neither the markup tests nor a screenshot of a maximized window would have shown it |
| **Performance** | 500-tile document: drag frame budget, save timing, render timing, with a regression threshold |

Commands (unchanged from `SESSION-STATE.md`):

```bash
dotnet build DeckForge.slnx -v q --nologo
dotnet test tests/DeckForge.Tests --nologo
```

Plus, for codegen phases, a scaffolded plugin:

```bash
macrodeck-plugin build --source src/<Plugin> --output artifacts --force
macrodeck-plugin validate --artifact artifacts/<id>-<version>.macroDeckPlugin --level publication
```

---

## 13. Phased implementation plan

Each phase ends with the application building, the suite green, and something demonstrable. Sizes are
relative, not deadlines.

### P0 — Ground truth — ✅ complete

**Done.** Appendix A is authoritative, §7.16 records every verdict, and the baseline is in the
progress tracker at the top of this document. The exit criteria are met: 153 blocks ship, 6 are
deferred to P9, 7 were discovered, and 28 are dropped with a stated reason each.

**Scope.** Write this document. Enumerate the pinned `MacroDeck.Sdk 3.0.0-beta.14` surface from the
real assembly and the offline docs snapshot: `IIntegrationContext` members; `Deck`, `Notifications`,
`Variables`, `Scripts`, `Events`, `Messages`, `Ui` surfaces; music-player actions; widget-handler and
config-flow hook shapes; plugin settings access; host variable write permission. Resolve every `⚠`
in Part 7 to keep / drop / mark-unverified. Record the baseline build and test results.

**Exit criteria.** Appendix A is an authoritative API map. Every `⚠` is resolved and reflected in
Part 7. `dotnet build` clean; `dotnet test` green at 344 passing.

### P1 — Model and catalog (no UI)

**Scope.** `VisualProject`, the block tree, `BlockValue`, `VisualProjectJson` plus the v0 migration,
`BlockCatalog` for all twelve categories, `VisualValidator`, `BlockMetrics`, `StackLayout`,
`DropResolver`. Tests for all of it.

**Exit criteria.** Model, catalog, validator and geometry fully tested. Nothing user-visible changed,
and the existing suite still passes.

### P2 — Emitters

**Scope.** `ExpressionEmitter`, `StatementEmitter`, `ProcedureEmitter`, the runtime-library
contributor, `BlockCompiler` as a facade, legacy aliases.

**Exit criteria.** A hand-written sample program compiles; golden C# for ~40 sample documents;
determinism test green; the generated plugin builds with `macrodeck-plugin validate` clean.

### P3 — Palette and rendering — ✅ complete

**Done.** Every shipping block renders, nested, on a canvas beside a searchable palette, in dark and
light. 49 new tests; full suite 561 green; the window driven at 1400×850 and at 960×640, in both themes,
with every category clicked, a search typed, blocks selected from both surfaces and the no-match state
reached. §9.10 records what moved into Core and what the window found.

**Scope, as built.** `BlockOutline` / `BlockLabel` / `BlockFactory` / `VisualSampleProject` in Core;
`BlockShapeGeometry`, `BlockTheme`, `BlockTile`, `InputSlotView`, `CategoryRail`, `PaletteList`,
`ScriptStrip`, `BlockWorkspace`, `LabelPartTemplateSelector` and `BlockSelectedEventArgs` in the App;
five view models; the page; `Liquid.Block*` tokens; one markup assertion for bound icon names. A block's
shape is chosen by a `DataTemplateSelector` over the catalogue row rather than a template per shape —
see §9.10.

**Exit criteria, checked.** Every catalog block renders ✔ (the sample holds every one of them and a test
fails when it does not; the palette draws all 156 including the hats). Nested ✔ (the recursive template
is the tile itself). Dark and light ✔ (both reviewed in a screenshot). Markup tests green ✔.

**Not in this phase, and why.** Dragging is P4 — the palette says so rather than offering a drag that
does nothing. Field editing, the inspector's editors and the live C# pane are P5; the inspector here
shows what the block is and which SDK member it was verified against, which is the read-only half of
§9.6. Clicking a diagnostic to select its block is P5 with the pane's editors. Zoom, pan and the
minimap are P10, and the canvas is a column of scripts rather than a free-positioned one until then;
script positions are already in the document (§6.1) and shown, so P10 adds the transform rather than the
data.

### P4a — Document editing and undo/redo — ✅ complete

**Done.** The document can now be edited by machine rather than by hand: insert, delete, move, wrap,
unwrap, edit a field, bind a slot, toggle disabled, add/delete/move scripts — each as a reversible
command behind one `DocumentEditor` with a linear undo stack. 26 new tests, including a 1000-gesture
random property test that undoes everything and compares the serialized document byte for byte with
where it started. Full suite 587 green.

**Scope, as built.** `DocumentCommands.cs` (`BodyRef`, the command types, `DocumentTransaction`,
`DocumentLists`) and `DocumentEditor.cs` (`DocumentEditResult`, `Execute`, `Transaction`, `Undo`,
``Redo`, `ClearHistory`) in Core; `DropTargetKind.OntoStatement` and index-carrying gap candidates in
`DropResolver.cs`. No App code — P4b wires these to the pointer.

**Four things this phase had to get right, and did get wrong first.**

1. *Undo is inverse by construction.* Every command captures the index it will need to put things back,
   because "put it back where it was" cannot be recomputed after the move: by the time you undo, the
   list has already changed. `MoveRun` captures source and destination, `DeleteRun` its index, `WrapRun`
   the anchor's index at the time.
2. *A run is everything from the grabbed block down.* `RunFrom` returns the tail, not one block, so a
   drag that takes the top three statements takes all three. This is why moving a run downward within
   its own stack is a no-op rather than a reorder — there is nothing below it to be above.
3. *A transaction is not atomic unless it rolls itself back.* `Transaction` applies in order and, if a
   later command is refused or throws, reverts the earlier ones. Refusals are ordinary returns, not
   exceptions, so a refused second command has to unwind the first by hand.
4. *Typing is one undo, not one per keystroke.* `EditField` coalesces by `Before`/`After` and
   `DocumentEditor.Execute` merges a new `EditField` into the previous command when the previous one
   still has it on top, keeping the original `Before` and the newest `After`. Without that, undo walks
   backwards through a word one letter at a time — the single most common way an editor like this
   feels broken.

**Refusals, and why each one exists.** Moving a body into itself or into its own descendant (that is a
copy, not a move); dropping a reporter onto a stack zone or a hat anywhere but the top of a script;
deleting the hat; binding a slot to the wrong shape; adding a script under a script. All of them return
`Refused` with a reason instead of throwing, so the drag layer can show one and move on.

**The one place the tests had to be corrected, not the code.** The 1000-gesture property test hit
`ArgumentOutOfRangeException` on an empty stack — reachable, because a drag that takes everything can
empty one. That was a gap in the test's generator, not in the editor: an empty stack is a legal state
the run has to survive. It now re-seeds and keeps going, because a property test that stops half way
passes for the wrong reason.

**Not in this phase, and why.** Pointer capture, the ghost, the indicator, auto-scroll and the keyboard
moves are P4b and P4c — all App. The editor has no idea any of them exist, which is the point: it is the
thing they will all call.

### P4b — the pointer — ✅ complete

**Done.** A block can be dragged out of the palette into a script, or from one stack to another, or into a
loop's mouth, with a ghost that shows the run and an indicator that shows the gap. 14 new tests; full suite
601 green; the real window driven for all four gestures with a screenshot taken mid-drag.

**Scope, as built.** `DropPlan` in Core — the decision half, no pixels in it. `CanvasHitTest` in the App —
the measurement half, which walks the live visual tree. `DragController` — pointer state and the threshold.
`DragAdorner` — the ghost and the indicator. `AutoScroll` — edge scrolling. `BlockWorkspace` — the host,
and the only thing that translates mouse events. The view model owns the `DocumentEditor`.

**The split is the design.** Part 9.5 asks for "the scorer is pure Core code so every rule is unit-testable
with no window", and the natural reading is that the *scorer* is the pure part. It is not sufficient on
its own: what a landing zone *does* to the document — move, insert, wrap, replace, bind, create a script,
refuse — is the half with the interesting failures, and it is a question about the document rather than
about the screen. So `DropPlan` is that half and it has no WPF in it, which is what let the 1000-gesture
round-trip property test be written at all.

**Four defects the window found that reading the code did not.** All four were invisible to 601 passing
tests, and three of them were invisible to *using* the app without screenshots.

1. *A drop put a block in the wrong script.* `CandidatesFor` walked a running `y` down from the hat and
   reported every gap's `x` as an indent from the script's origin — which is zero, for every script in the
   document. The scorer weighs x at half a point per pixel, so with every script claiming `x = 0` it had no
   way to prefer the one under the pointer and picked whichever shared a y coordinate. Every candidate now
   takes its coordinates from the block's own rectangle, which the canvas measures off the live tile.
2. *A loop's mouth opened at its foot.* The same walk reported a container's inner gaps at the container's
   bottom, because that is where the walk started. The indicator for the top of a loop's mouth appeared
   below the loop's contents. Both ends now come from the statements' own rectangles.
3. *A drag released in empty space teleported the block.* `Resolve` always names a nearest candidate,
   because a scorer with no winner cannot distinguish "nothing is near" from "nothing near *that accepts
   this*". A block let go over the inspector landed in whichever script was closest. `Drop` now refuses
   outside the magnet radius, and the header says why.
4. *A drag out of the palette that never crossed the canvas did nothing.* The workspace only captured the
   pointer when the press started over it, so a drag that stayed in the palette or went straight to the
   inspector received no move events at all: no ghost, no error, no drop. Capture is unconditional now.

**Two smaller ones.** The ghost drew every block at its header height, so a C-block in a long run appeared
to be a plain statement — `StackLayout.LayoutRun` lays out a hatless run for it, which the canvas
thumbnails of Part 25 will want anyway. And the ghost's outline was white at full strength, which reads as
a wireframe selection rather than as a block.

**Verification that mattered.** The `uidriver` harness gained `press`, `move`, `release` and `drag`, split
so a screenshot can be taken *while the button is still down*. That is the only way to see a ghost or an
indicator: both exist between the press and the release and neither survives it, so a harness that only
presses-moves-releases-and-looks can never show either, and a canvas that draws no indicator looks exactly
like a screenshot taken too late. With it, defects 1 and 2 were visible in a single frame.

### P4c — the keyboard — ✅ complete

**Done.** Part 9.5's "nothing requires a mouse": ↑/↓ traverse a stack, Ctrl+↑/↓ move a block, Delete,
Ctrl+D duplicates, Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y undo and redo, Tab cycles, Space picks up and drops,
Escape puts it back. 17 new tests; full suite 618 green; every binding driven in the real window.

**Scope, as built.** `KeyboardMoves` in Core holds every decision and no state — each method takes the
selection and returns the next one, which makes the whole keyboard layer a pure function of (document,
selection, key) and therefore exhaustively testable. The page has one `PreviewKeyDown` handler whose only
job is turning keystrokes into method calls.

**Two decisions that go against the drag, deliberately.** A keyboard's Ctrl+↓ moves **one block** and
swaps it with its neighbour; a drag carries the whole tail. And Delete removes **one block**, not the run.
Both were written the drag's way first, and both were wrong for the keyboard: carrying the tail makes
Ctrl+↓ a no-op for every block except the top of the stack, since everything below a block is already at
the bottom; and deleting the tail turns a keystroke meant to remove one block into the loss of everything
after it. A drag carries the tail because grabbing statement *n* and leaving 1..n-1 behind orphans them,
and neither keyboard gesture has that problem to solve — the user has already named which block they mean.

**Four defects the window found and 601 passing tests did not.**

1. *Nothing on the canvas could be reached with the keyboard.* `BlockTile` is a `UserControl`, which is not
   focusable, and the workspace's `PreviewMouseLeftButtonDown` marks the event handled — which stops the
   tile's own bubbling `MouseLeftButtonDown` from running at all. So no click ever gave a tile focus, the
   keyboard went wherever it had been, and selection visibly worked while every arrow key addressed
   something else. Tiles take focus on click, and the workspace does it itself for the same reason.
2. *The keyboard went away after the first edit.* Every edit rebuilds the canvas, which replaces every
   tile, so the focused element no longer exists and WPF drops focus. The symptom was "drag works
   perfectly, then Ctrl+Z does nothing" — one keystroke after a gesture that had just succeeded. The
   workspace re-focuses the selection on every rebuild.
3. *Ctrl+Z did nothing at all* while the Undo button beside it worked. The carry-mode branch returned
   before the undo keys were considered, so a user who picked a block up and then wanted to undo the edit
   from before that had no way to ask. Undo and redo now work while carrying — which is also what makes the
   carry mode safe to explore in, since you can always get back.
4. *The ghost's own pitch was wrong for nested blocks*, found earlier in P4b and fixed there.

**One defect the test driver had, not the app.** A scripted shortcut worked once and then silently stopped
working for the rest of the session. The key-up for a modifier was going to whichever window held focus at
that moment, was sometimes swallowed, and left Ctrl held in the system's view; the next `ctrl+d` then
arrived as "Ctrl already down, and here is another Ctrl" and Windows swallowed the pair as a repeat. The
driver now releases every modifier and button before each keystroke. It is worth recording because the
evidence points squarely at the application — "Ctrl+D duplicated a block once and never again" reads as a
bug report against a working feature, and the fix belongs in the harness.

**Verification that mattered.** `hotkey` accepts key *names* (`down`, `delete`, `ctrl+shift+z`) rather
than the first character of them, because mapping "down" to the letter D sends Ctrl+D for every arrow key
and makes a working binding look broken. And each keystroke needed the driver to release stale modifiers
first. Both were in the harness; neither was in the application, and both would have been filed against the
application.

**Exit criteria.** Resolver tests and user-driver drag scenarios green; 500-tile performance budget
met.

### P5 — Editing, code, save

**Scope.** Field editors, inspector, diagnostics pane, live C# pane, target picker, multi-script
save, sidecar persistence, legacy import.

**Exit criteria.** End to end: build a program, save, regenerate, the plugin builds; reload restores
it exactly; a legacy workspace migrates and still compiles.

### 9.6.1 As built (P5a) — the editors, and what the panel will not claim

`SlotValue` in Core reads a slot's value and turns a typed one back into a `BlockInput`. It is the piece
that decides **which member of the input** carries the value, and that is the decision worth having in a
tested place: a number in `Text` and a name in `Number` is a document that validates, emits, and produces
something other than what the user typed — the one failure here that is completely silent.

`InspectorViewModel` holds the rows. Each row reads through to the block every time it is asked and
writes only through the editor, so the panel cannot disagree with the canvas about what a block contains —
which is the failure mode of an editor that keeps its own copy and writes it back on a button.

**Three things the design asked for, and what they became.**

- *One text field per type, with affordances.* Part 9.6 lists ten editor types. Each editor binding to the
  property it cares about means the type switch appears once, in `SlotValue`, instead of ten times in the
  view — and the tenth one would be the one that was wrong.
- *The row expression is labelled "Row expression", not "Generated".* Emitting one block properly needs a
  whole action around it — a signature, a body, the host guard — so the panel shows the catalogue row's
  own text and says so. The pane at the bottom of the panel is the one that may claim to be what Save
  writes, because it goes through `VisualEmitter.CompileTarget` and carries the compile problems with it.
- *Clicking a hole selects the slot, not just the block.* Part 9.6 wants selection to work both ways
  between canvas and inspector, and a click on a hole names something more specific than its block.

**Two refusals worth having.** A number slot rejects a word and a boolean rejects anything that is not
true or false, before the keystroke reaches the document — so there is nothing to undo, and the field
keeps what was typed for the user to correct. And Clear is hidden for a boolean, because emptying a
required boolean produces a document the validator turns into an error: a button whose only possible
result is an unsaveable document is worse than no button.

**One defect the window found, which no test could.** Typing `7` into a count of `10` produced `710`. The
row committed the first keystroke, the canvas rebuilt, and the rebuild handed the binding the freshly-read
value back — so the next character appended to it rather than replacing it. The row now reads through to
the block whenever its draft matches the block, and only prefers its own copy while the two differ, which
is the one rule that stops the panel and the canvas arguing about who is right.

### P5b — save, load, dirty state

**Scope.** Target picker, save to the workspace, dirty tracking, reload restores the document exactly,
legacy import. The exit criterion is P5's: build a program, save, regenerate, the plugin builds; reload
restores it exactly.

### 9.6.2 As built (P5b) — a canvas that survives the window closing

`VisualStore` in Core owns the file. The document is a sidecar at
`<workspace>/.deckforge/canvas.json`, and the C# is a projection of it rather than the other way round.
That direction is the one that matters: the action source is generated, so anything hand-edited inside it
is lost on the next write, and there would then be two copies of the truth with nothing saying which one
the next save would overwrite. It lives in `.deckforge` rather than beside `manifest.json` because that
directory is already DeckForge's own, and private state sitting next to the plugin's files invites a user
to edit it by hand and then wonder why the canvas came back different.

**Three decisions that are refusals in disguise.**

- *Writes go beside the file and then replace it.* A write in place that a crash cuts short leaves a file
  that cannot be parsed, and a canvas that cannot be parsed is a canvas that is gone. The C# can always be
  regenerated from the document; the document cannot be regenerated from anything.
- *Every save keeps the previous file as `canvas.previous.json`.* On for the same reason, and it is what
  makes the next decision possible.
- *An unreadable canvas falls back to that backup and says so.* A canvas that cannot be parsed but whose
  previous version can is not lost work — it is a save that went wrong, and the honest answer is the last
  good state with a warning. The unreadable file is left alone: it is the only copy of whatever happened,
  and overwriting it with the file that worked destroys the evidence. The warning names that consequence,
  because "canvas.json has been left as it was — saving will replace it" is actionable and "something
  went wrong" is not.

**Dirty state is computed, not remembered.** `IsDirty` serialises the document and compares it with what is
on disk, rather than a flag that every edit sets. A flag is wrong in both directions that matter: it
survives an undo that took the document back to the saved state, and it is set by edits that were then
reverted, so the header claims unsaved work when there is none — and an indicator that is always on is
indistinguishable from one that is never on. The cost is a serialisation per rebuild, and the rebuild
already walks every block to project view models.

**`DocumentEditor.ReplaceDocument` drops the history, and had to.** Every command on the undo stack carries
the `Block` and `VisualScript` objects it was built from. After a load those objects are not in the
document, so replaying one would edit a document nobody can see and report success. Saving clears the
history for the same reason from the other end: the question after a save is "what did I change since",
not "what could I have undone before I saved".

**One defect the window found, which no test could.** With a corrupt `canvas.json` and a good backup, the
page drew the previous save and said nothing at all. The header read "Unsaved changes · canvas.json" —
which is *true*, and tells the user nothing about the fact that what is on screen is not what is on disk.
They would have concluded they had unsaved work, rather than that the application had recovered from a bad
file, and the recovery would have looked like the app losing their changes. `VisualStoreResult` now
carries `Recovered`, and anything that is not a plain read of the expected file is announced.

**What was checked, and how.** Nine store tests, including a full save/load round trip asserted for byte
equality after re-serialisation, plus the window: open a generated workspace, read "This workspace has no
canvas yet", Save writes 46 KB and says "Saved 11 scripts to canvas.json", an inspector stepper turns
500 ms into 501 and the header flips to "Unsaved changes", Save puts it back and leaves the old value in
the backup, a restart redraws 501 from the file, a truncated file recovers to 500 with the warning, and
Save after that leaves valid JSON. The keystroke path is the one thing not re-checked here; `postkey`
ctrl+down on the top block of a stack is a refusal, and a refusal showing nothing in the header is the
documented behaviour rather than a missing edit.

### P6 — Shell integration, retire Blocks

**Scope.** Nav item in the old Blocks slot, DI, `PageRegistry`, delete `BlockActionPage` and
`BlockActionViewModel`, update `README.md`, `ROADMAP.md`, `SESSION-STATE.md`, `EXTENDING.md`.

**Exit criteria.** Every other page's Ctrl+digit shortcut unchanged (checked by
`NavigationShortcutTests`); suite green; no `blocks` tag remains; the sidebar reads correctly.

### 9.6.3 As built (P6) — deleting a page, and the one thing that renumbers itself

`BlockActionPage.xaml`, its code-behind and `BlockActionViewModel.cs` are deleted, along with the
`"blocks"` entry in `PageRegistry.Create` and the two DI registrations. Nothing else referenced them;
`BlockCompiler` and `BlockProgramWriter` stay, because §7.4 keeps the compiler's public surface as the
engine the new emitters sit behind and `BlockProgramWriterTests` still exercises the writer's marker
round-trip. `BlockProgramWriter` now has no production caller at all, which is worth saying out loud —
it is kept deliberately, not overlooked, and the day the visual writer takes over that role is the day
it goes.

**The move was the risky part, and it was positional.** `MainWindow` builds `_shortcutTags` by reading
the sidebar in order, so Ctrl+1 is the first item and Ctrl+9 the ninth. Visual was tenth, behind Blocks;
deleting Blocks and leaving Visual where it was would have given Widget Designer the number Blocks used
to have and taken one from everything below. So Visual went into Blocks' exact position, which the
design calls for and which no test was checking.

**Two tests that could not see it.** `NavigationShortcutTests` asserted that every item carries a tag and
that no tag is used twice — both of which stay true through an arbitrary reordering. Nothing recorded
which digit meant which page, so a renumbering would have passed the suite and surfaced as a user's
"Ctrl+9 opens the wrong thing". The order is now pinned as an explicit list, and there is a second
assertion that at least ten items exist, because ten digits is a requirement the markup never states.

The result is worth stating precisely rather than as "nothing moved": Ctrl+1–8 are unchanged, Ctrl+9 now
opens Visual (it opened Blocks), and Ctrl+0 now opens Widget Designer — which had **no shortcut at all**
before, being the eleventh item. Nothing lost a number.

**Checked in the window, after a wait long enough to matter.** Ctrl+9 opens the Visual page and
highlights it in the ninth slot; Ctrl+8 still opens Setup Flow; the sidebar reads Home, New Plugin,
Capabilities, Explorer, Manifest, Actions, Events, Setup Flow, **Visual**, Widget Designer, Icon Studio,
Icon Packs, Localization, Build & Run, Ship, Publish, Terminal — no Blocks. Two harness lessons are now in
`SESSION-STATE.md`: a keystroke fired seconds after launch goes nowhere and looks exactly like a dead
handler, and `SetForegroundWindow` is refused outright for a tool launched from another window unless the
input queues are attached first.

### P7 — Procedures and multi-script UX

**Scope.** Script strip management, hats per target, define and call procedures, parameter slots,
validation feedback for calls and returns.

**Exit criteria.** A program with two scripts and a shared procedure compiles into a plugin that
builds and passes conformance.

### 9.6.4 As built (P7a) — a procedure is a body, not a declaration

Before this phase a procedure was something the emitter read and nothing could edit. Three pieces made
it a thing a user owns, and the order matters: the model first, then the rules that describe what a call
to it may look like, and only then the panel.

**A procedure needs an id, and it is an id rather than its name.** `BodyRef` addresses bodies by id
because a path of indices goes stale the moment anything above it moves — and undo replays those moves in
reverse. A procedure's body had no address at all, so a block inside one was *findable* and uneditable:
selectable, and then refused by every command, with a message about a body that did not exist.
`BodyRef.ProcedureBody(id)` is the fix, and the id belongs to the declaration, so **renaming a procedure
does not move its body** — a name-derived address would leave every undo entry holding a reference that
resolves to nothing the moment the name changed.

Documents written before ids existed deserialise with an empty string and are given one on load, rather
than refused: a canvas that saved perfectly well should not stop opening because a field was added.

**The call rules exist because the alternative is a compile error in the user's own file.** `vis-call-arity`
for a wrong argument count, `vis-call-value-void` for using a procedure that returns nothing as a value,
`vis-call-value-unused` (a warning) for throwing away a value, and the two return rules —
`vis-return-value-void` and `vis-return-no-value` — because `return value;` from a `Task` and `return;`
from a `Task<object?>` are both illegal and neither is catchable from the editor. Recursion is a **warning**,
transitively: §7.14 says so, and refusing it would be a claim the editor cannot keep, since mutually
recursive procedures with no base case are legal C#. The interesting case is not A calling A — two
procedures calling each other produce nothing from a direct check and hang exactly the same way.

**Two bugs the tests found, both from capturing "before" state at the wrong moment.** Both commands edit
the very document they snapshot, so reading the current value *when about to apply* records the value from
the previous keystroke. A merged rename therefore restored the middle of the word rather than what came
before it, and a delete-then-undo restored the name from before the last keystroke. Both now snapshot in
the constructor, and `Merge` carries the oldest name forward explicitly.

**The sample had to become legal.** One declaration cannot serve all three call shapes: a bare `call`
passes nothing, so its procedure takes nothing, and the reporter form needs one that returns a value. The
sample now declares three procedures, one per call shape, and points each call block at the right one —
the same move Phase 3 made for `break` and `continue` inside loops. The alternative, loosening the rules
until the sample passed, would have left every real document with the compile error they exist to name.

### 9.6.5 As built (P7b) — the strip, the panel, and the column

**A procedure is a column, not a form.** `ColumnViewModel` gives scripts and procedures one template, one
items source and one hit test; `HasHat` is the entire difference. The alternative — a procedure edited in
a panel — is a stack of blocks edited through a form, which is a worse editor than the one beside it. And
because procedures are in `Columns`, the arrow keys reach their bodies: `AllNodes()` walks columns, so a
procedure that were not in it would be a place on the canvas the keyboard cannot get to.

**My Blocks goes above the scripts, which inverts §9.2's sketch, on purpose.** With one or two scripts the
sketched order is right. The sample has eleven, and the result was that a document's own procedures — and
the only button that creates one — sat below the fold of every window. The rarer thing goes first.

**Three defects the window found, none of which a test could have.**

- *`IsExpanded="{Binding HasSelectedProcedure}"` threw on every load.* An `Expander` binds `IsExpanded`
  two-way by default, and pointing it at a read-only property is an `InvalidOperationException` — which
  the crash handler swallows, so the page came up looking almost right with an empty canvas and a banner
  nobody reads.
- *The parameter list did not update.* The document holds plain `List<T>`, which raises no notification,
  so the panel showed the parameters a procedure had when the panel was built and a row added by the
  editor existed in the document and nowhere on screen. Projected over `ObservableCollection` now, holding
  the declaration's own instances so rows can be matched back by reference.
- *The parameter delete button did nothing, and said nothing.* It named the procedure by `Tag`, resolved
  through the panel's `DataContext` — from inside a `DataTemplate`, which cannot reach it by `ElementName`
  or by an ancestor search. Both were tried; both produced a button that looked wired. It now reads the
  selected procedure from the editor, which is the only procedure the panel can be showing anyway.

**One refusal the window showed was simply wrong.** Un-ticking "returns a value" was refused only when a
call *discarded* a result, and allowed while a call was *waiting* for one — so the checkbox produced two
red diagnostics instead of an explanation. Both directions are now refused with a sentence naming the calls
that have to change first.

**The drop path needed one more thing, and finding it was the useful part.** `DropCandidate` carried a
parent id and a body name, and `DropPlan` rebuilt a `BodyRef` from them. A procedure gap looks identical to
a script gap until you try to write into it: the rebuilt reference was a *script* reference to a hat that
does not exist, so a legal drop was reported as a refusal about a missing body. `DropCandidate` now carries
`ProcedureBody` and exposes `Where`, and the flag is set from the reference the candidate was collected
from.

**Verified in the window, against a saved workspace:** procedures listed and selectable in the strip,
selecting one scrolling its column into view (the canvas is no longer capped at 1600px, which made the far
end of the document unreachable), the panel renaming and adding and removing parameters, the returns
refusal, a palette block dropped into a procedure body and landing under its `return`, an added procedure
named `myProcedure2` rather than colliding, Save, and a restart restoring all four procedures exactly.

### P8 — Simulator, tracer, debugger

**Scope.** `IVisualHost`, `SimulatedHost`, `ScriptInterpreter`, the stage panel, watch table,
breakpoints, the dry-run tracer, and the `BlockSemantics` table.

**Exit criteria.** Interpreter tests green; per-block semantics parity asserted; a script visibly
runs in the stage; the honesty statement is visible in the UI.

### P9 — Multi-target codegen

**Scope.** `WidgetHandlerTarget`, `ConfigFlowHookTarget`, `LifecycleHookTarget`, each behind its P0
verification.

**Exit criteria.** Scripts in a widget node handler and a config workflow build inside a scaffolded
plugin.

**As built (P9).** One writer, not three: `VisualTargetWriter` resolves an anchor per
`(TargetKind, AnchorId)` and hands the rest to `BlockCompiler.Splice`, because the difference between the
four targets is the anchor and nothing else — and the part that is easy to get wrong, finding the anchor
and replacing it on the *second* save, is the part that method already got right for the action.

Three defects the compile test found and no string assertion would have:

**The stock widget handler did not compile.** `RenderHandler` rendered
`On("press", () => { _ = 0; // TODO: react to the event. })` onto one line, and the `//` comment swallowed
the closing brace — so a generated widget provider with any untouched event was invalid C#, and nothing
in this project builds a generated widget provider by default, so it shipped. Handlers now render
multi-line, which also gives the writer a brace to splice into and leaves room for a comment.

**A widget handler has no `context` and no `_logger`.** The SDK calls it with no parameter; P0 did not
establish an anchor for one, and inventing a context parameter would be inventing API. The writer
therefore *refuses* a widget handler (or a config-flow hook) whose blocks reach for those names, and it
names the blocks — "something needs a context" is a puzzle, "`sensing.tap-velocity` does" is a sentence
the user can act on before they press save. Emitting them produced CS0103 on the first build of a real
plugin.

**`VisualRuntimeTemplate` had no namespace substitution.** `namespace __NAMESPACE__;` was written out
verbatim by every caller, so no region that referenced `VisualRuntime` could ever have compiled in a
plugin nobody had hand-fixed. `Render(rootNamespace)` is now the only way to get the file, and
`VisualTargetWriter.RuntimeFile` hands it to the caller that writes the blocks.

`Splice` grew two cases as a result. The stock `ShutdownAsync` is an expression body, and anchoring on
it dropped the region after a method whose signature had no body — inside the class, as sibling
statements, which compiles if the blocks happen to be class-member-legal and produces a file no user
could read; it opens the block instead. And the closing brace is now re-indented rather than indented
again on top of what is already in front of it, which had been drifting the region one level deeper on
**every** save.

Deferred, and why: the six C5 event hats (`when event received`, `when key pressed`, `when client
connected` / `disconnected`, `when profile changed`, `when folder opened`, `when message received`).
They are implementable now — this is the phase that gives their subscriptions an owner — but each needs
a subscription stored and disposed rather than a statement written, which is a body of work in its own
right and not something to add as an afterthought to a milestone about anchors.

### P10 — Polish and hardening

**Scope.** Zoom, pan and minimap; comments; PNG/SVG export; command palette; keyboard-shortcut
sheet; high contrast; palette recents and favourites; block-label localization; per-block docs deep
links; performance tuning; documentation sweep.

**Exit criteria.** Suite green, performance budget held, docs updated, screenshots recorded for every
theme and category.

---

## 14. Risks and mitigations

| Risk | Mitigation |
|---|---|
| The SDK surface differs from the catalog's assumptions | P0 gates every row; `IsVerified` is test-enforced; unverifiable blocks are dropped or shown unverified and unsaveable |
| Rewriting emitters breaks the proven engine | `BlockCompiler`'s public surface is preserved; the 344 existing tests must pass unchanged at every phase boundary |
| Generated code fails to compile in a user's plugin | Compile tests write into `src/<Project>/`; `VisualRuntime` references are covered by a surface test; the generated plugin is built and validated |
| Interpreter and compiler drift | Shared `BlockSemantics` table, golden C# snapshots, end-to-end compile tests, and a documented honesty statement |
| Magnetic snapping feels wrong | The scorer is pure and testable; magnet radius and stability are documented and tunable in settings; user-driver screenshots for tuning |
| WPF recursion and performance on large documents | Cached metrics, collapsible bodies, virtualised panels, a measured 500-tile budget |
| Enormous scope | Phase order keeps the application shippable at every boundary; the old page is retired only in P6, after the new one can save |
| Two editors competing for one marker region | The old page is removed, not merely hidden; the region has exactly one writer |

---

## 15. File manifest

**Core — new**

```
src/DeckForge.Core/Visual/BlockCatalog.cs
src/DeckForge.Core/Visual/BlockCategory.cs
src/DeckForge.Core/Visual/BlockShapes.cs
src/DeckForge.Core/Visual/BlockValue.cs
src/DeckForge.Core/Visual/VisualBlocks.cs
src/DeckForge.Core/Visual/VisualProject.cs
src/DeckForge.Core/Visual/VisualProjectJson.cs
src/DeckForge.Core/Visual/Migrations/BlocksV1Migration.cs
src/DeckForge.Core/Visual/VisualValidator.cs
src/DeckForge.Core/Visual/BlockMetrics.cs
src/DeckForge.Core/Visual/StackLayout.cs
src/DeckForge.Core/Visual/DropResolver.cs
src/DeckForge.Core/Visual/BlockSemantics.cs
src/DeckForge.Core/Visual/Runtime/IVisualHost.cs
src/DeckForge.Core/Visual/Runtime/SimulatedHost.cs
src/DeckForge.Core/Visual/Runtime/ScriptInterpreter.cs
src/DeckForge.Core/Visual/Runtime/VisualList.cs
```

**Core — new in P3** (see §9.10 for why each is here rather than in the App)

```
src/DeckForge.Core/Visual/BlockOutline.cs        the silhouettes, as numbers
src/DeckForge.Core/Visual/BlockLabel.cs          the label split, words and holes
src/DeckForge.Core/Visual/BlockFactory.cs        a fresh block of any row, filled in
src/DeckForge.Core/Visual/VisualSampleProject.cs the document the page draws
```

`BlockValue.cs` and `VisualBlocks.cs` were §6.2's class-per-block hierarchy, which P1 replaced with one
generic node (§6.2.1) and are not written. `DropResolver.cs` and `StackLayout.cs` landed in P1c.

**Core — new in P8–P10**, and every one of them here for the same reason: `tests/DeckForge.Tests` cannot
reference WPF, so a rule written in a control is a rule nothing can check.

```
src/DeckForge.Core/Visual/Runtime/Values.cs            coercion that matches the generated runtime
src/DeckForge.Core/Visual/Runtime/BlockSemantics.cs    what each block means, in one table
src/DeckForge.Core/Visual/Runtime/ExecutionStep.cs     one traced step
src/DeckForge.Core/Visual/Runtime/StageSession.cs     the transport, the watch table, the trace
src/DeckForge.Core/Visual/Commands/VisualCommands.cs   the palette's list, the sheet's rows, the keys
src/DeckForge.Core/Visual/CanvasView.cs                zoom, pan and the minimap's arithmetic
src/DeckForge.Core/Visual/BlockContrastRules.cs        §9.8's contrast, as a decision about design
src/DeckForge.Core/Visual/BlockLabelKeys.cs            §21.1's keys, and the fallback that makes them safe
src/DeckForge.Core/Visual/ResxTextLookup.cs            those keys, read out of the workspace's own resx
src/DeckForge.Core/Visual/PaletteMemory.cs             recents and pins, and their rules
src/DeckForge.Core/Visual/PaletteStore.cs              where one workspace's memory is written
src/DeckForge.Core/Visual/CanvasPerformanceBudget.cs   §9.9's budget, and what it measured
src/DeckForge.Core/Visual/SvgRenderer.cs               the vector export, from the layout model
```

`Runtime/VisualList.cs` (listed above as planned) is not written: the interpreter works over the document's
own lists, and a parallel copy would be a second thing to keep in step for no gain.

**CodeGen — new**

```
src/DeckForge.CodeGen/Generation/VisualEmitter.cs
src/DeckForge.CodeGen/Generation/VisualProgramWriter.cs   (the action executor's writer)
src/DeckForge.CodeGen/Generation/VisualTargetWriter.cs   (the other three targets, behind one class)
src/DeckForge.CodeGen/Generation/VisualRuntimeTemplate.cs
```

`ExpressionEmitter.cs`, `StatementEmitter.cs`, `ProcedureEmitter.cs` and `Targets/ActionExecutorTarget.cs`
were sketched separately and collapsed: P2 built one data-driven `VisualEmitter` (see §8.6) and P9's writer
covers every target kind, so a class per emitter or per target would have been four and four places for the
same two ideas.

**App — new**

```
src/DeckForge.App/Pages/VisualEditorPage.xaml(.cs)
src/DeckForge.App/ViewModels/Visual/VisualEditorViewModel.cs
src/DeckForge.App/ViewModels/Visual/ScriptViewModel.cs
src/DeckForge.App/ViewModels/Visual/ColumnViewModel.cs                   (P7, one template for both kinds)
src/DeckForge.App/ViewModels/Visual/ProcedureViewModel.cs                (P7)
src/DeckForge.App/ViewModels/Visual/BlockNodeViewModel.cs
src/DeckForge.App/ViewModels/Visual/SlotViewModel.cs
src/DeckForge.App/ViewModels/Visual/PaletteViewModel.cs                 (P10b, recents and pins)
src/DeckForge.App/ViewModels/Visual/CanvasViewModel.cs                  (P10c, zoom and the minimap)
src/DeckForge.App/ViewModels/Visual/StageViewModel.cs                    (P8, a projection of StageSession)
src/DeckForge.App/ViewModels/Visual/CommandPaletteViewModel.cs          (P10a)
src/DeckForge.App/ViewModels/Visual/ShortcutSheetViewModel.cs           (P10a)
src/DeckForge.App/ViewModels/Visual/InspectorViewModel.cs               (P5, with the editors)
src/DeckForge.App/Controls/Blocks/BlockWorkspace.xaml(.cs)
src/DeckForge.App/Controls/Blocks/ScriptStrip.xaml(.cs)
src/DeckForge.App/Controls/Blocks/CategoryRail.xaml(.cs)
src/DeckForge.App/Controls/Blocks/PaletteList.xaml(.cs)
src/DeckForge.App/Controls/Blocks/BlockTile.xaml(.cs)
src/DeckForge.App/Controls/Blocks/InputSlotView.xaml(.cs)
src/DeckForge.App/Controls/Blocks/StagePanel.xaml(.cs)                  (P8)
src/DeckForge.App/Controls/Blocks/CanvasMinimap.xaml(.cs)                (P10c)
src/DeckForge.App/Controls/Blocks/CommandPalette.xaml(.cs)               (P10a)
src/DeckForge.App/Controls/Blocks/ShortcutSheet.xaml(.cs)                (P10a)
src/DeckForge.App/Controls/Blocks/BlockShapeGeometry.cs
src/DeckForge.App/Controls/Blocks/BlockTheme.cs
src/DeckForge.App/Controls/Blocks/LabelPartTemplateSelector.cs          (P3)
src/DeckForge.App/Controls/Blocks/BlockSelectedEventArgs.cs            (P3)
src/DeckForge.App/Controls/Blocks/AutoScroll.cs
src/DeckForge.App/Controls/Blocks/CanvasHitTest.cs
src/DeckForge.App/Controls/Blocks/DragController.cs                     (P4b)
src/DeckForge.App/Controls/Blocks/DragAdorner.cs                        (P4b)
```

`Services/BlockDragService.cs` and `Services/VisualDocumentService.cs` are not written: the drag lives in
`DragController` beside the workspace that owns it, and the document is the view model's own. P8's
`Services/SimulationService.cs` is also not written - the simulation is `StageSession` in Core, and a service
in the App would have been a wrapper over it. P10h's `Services/BlockText.cs` *is* written, and is the one
service here that is a static: thirty places build a block view model, and none of them should have to be
handed a lookup to say a word.

**App — changed**

```
src/DeckForge.App/MainWindow.xaml               (+ Visual nav item, − Blocks in P6)
src/DeckForge.App/MainWindow.xaml.cs            (PageRegistry: + visual, − blocks in P6)
src/DeckForge.App/App.xaml.cs                   (DI registrations; − old page/VM in P6)
src/DeckForge.App/Themes/LiquidTheme.cs         (+ Liquid.Block* tokens, generated from the catalogue)
```

**App — deleted (P6)**

```
src/DeckForge.App/Pages/BlockActionPage.xaml(.cs)
src/DeckForge.App/ViewModels/BlockActionViewModel.cs
```

**CodeGen — changed**

```
src/DeckForge.CodeGen/Generation/BlockCompiler.cs        (facade over the emitters, and the splice)
src/DeckForge.CodeGen/Generation/BlockProgramWriter.cs   (target-agnostic)
src/DeckForge.CodeGen/Generation/WidgetGenerator.cs      (P9's fix: multi-line event handlers)
```

`Splice` grew its empty-body case and stopped re-indenting the closing brace in P9, both because a
multi-target writer needed an anchor whose method had no body and because the closing brace was drifting one
level deeper on every save.

**Tests — new**

```
tests/DeckForge.Tests/Visual/BlockCatalogTests.cs
tests/DeckForge.Tests/Visual/VisualModelTests.cs
tests/DeckForge.Tests/Visual/VisualMigrationTests.cs
tests/DeckForge.Tests/Visual/ExpressionEmitterTests.cs
tests/DeckForge.Tests/Visual/StatementEmitterTests.cs
tests/DeckForge.Tests/Visual/ProcedureEmitterTests.cs
tests/DeckForge.Tests/Visual/VisualCodegenCompilesTests.cs
tests/DeckForge.Tests/Visual/StackLayoutTests.cs
tests/DeckForge.Tests/Visual/DropResolverTests.cs
tests/DeckForge.Tests/Visual/ScriptInterpreterTests.cs
tests/DeckForge.Tests/Visual/VisualRuntimeSurfaceTests.cs
tests/DeckForge.Tests/Visual/BlockOutlineTests.cs               (P3)
tests/DeckForge.Tests/Visual/BlockLabelTests.cs                 (P3)
tests/DeckForge.Tests/Visual/VisualSampleProjectTests.cs       (P3)
tests/DeckForge.Tests/Visual/StageSessionTests.cs              (P8, the transport without a window)
tests/DeckForge.Tests/Visual/VisualTargetWriterTests.cs        (P9, the anchors and the refusals)
tests/DeckForge.Tests/Visual/MultiTargetCodegenTests.cs        (P9, three targets built in a real plugin)
tests/DeckForge.Tests/Visual/ProcedureTests.cs                 (P7)
tests/DeckForge.Tests/Visual/DocumentEditorTests.cs             (P4a)
tests/DeckForge.Tests/Visual/KeyboardMovesTests.cs             (P4c)
tests/DeckForge.Tests/Visual/SlotValueTests.cs                 (P5a)
tests/DeckForge.Tests/Visual/VisualStoreTests.cs               (P5b)
tests/DeckForge.Tests/Visual/VisualValidatorTests.cs           (P1c)
tests/DeckForge.Tests/Visual/CommentTests.cs                   (P10a)
tests/DeckForge.Tests/Visual/VisualCommandsTests.cs            (P10a)
tests/DeckForge.Tests/Visual/PaletteMemoryTests.cs             (P10b)
tests/DeckForge.Tests/Visual/PaletteStoreTests.cs              (P10b)
tests/DeckForge.Tests/Visual/CanvasViewTests.cs                (P10c)
tests/DeckForge.Tests/Visual/CanvasPerformanceBudgetTests.cs   (P10d)
tests/DeckForge.Tests/Visual/SvgRendererTests.cs               (P10e)
tests/DeckForge.Tests/Visual/BlockContrastTests.cs             (P10f)
tests/DeckForge.Tests/Visual/BlockLabelKeysTests.cs            (P10g)
tests/DeckForge.Tests/Visual/ResxTextLookupTests.cs             (P10h)
```

`ExpressionEmitterTests.cs` and `StatementEmitterTests.cs` became the one `VisualEmitterTests.cs` (§8.6);
the drop tests are inside `StackLayoutTests.cs` and `DropPlanTests.cs` rather than a `DropResolverTests.cs` of
their own, and `VisualRuntimeSurfaceTests.cs` became `ScriptInterpreterTests.cs`, because "the runtime" and
"the interpreter" turned out to be one thing with two names. `VisualModelTests.cs` kept its name: it covers
the document model, which is a thing that exists whatever the file that walks it is called. P3 also added one
assertion to `tests/DeckForge.Tests/XamlMarkupTests.cs`: that every category's glyph, which is held in
the catalogue rather than in markup, is a real `SymbolRegular` member — the same defect the icon check
above exists for, reached a different way, and a bad one renders as a missing glyph with no error.

**Docs**

```
visual.md          (this document)
README.md          (status line, nav list)
ROADMAP.md         (Blocks → Visual)
SESSION-STATE.md   (new hard constraints and architecture notes)
EXTENDING.md       (how a block is added: catalog row + emitter + interpreter step + test)
```

---

## 16. Open questions for P0

Each answer keeps a block, drops it, or moves it to a later phase — and P0 writes the answer beside
the row in Part 7 so the catalog never lies.

1. Does Music Players expose a **callable action surface** (play / pause / next / volume), or only
   metadata for the Music Player widget? If metadata only, C4 shrinks drastically.
2. Do widget node handlers and config-flow hooks have **stable anchor text** to splice into? If not,
   P9 waits until they do.
3. Are plugin **settings** readable and writable at runtime through a documented API, or is that
   config-flow only?
4. Is `IIntegrationContext.Messages` **publish-only** or request/response?
5. Are **host variable writes** permitted from an action, or is the variable provider read-only for
   plugins?
6. Do deck hosts expose **per-client** folder/profile queries, or only the origin client?
7. Are event **bindings** available to plugin code, or only declarations plus publish?
8. Are action classes ever generated **`partial`**? If that changes, class-level procedures become
   possible in addition to local functions (Part 4.2), and multi-action sharing becomes a design
   option rather than a non-goal.
9. Is there a documented **WebSocket** client surface, or must network blocks stay HTTP-only?
10. Does the host expose a **notification dismissal** API, or is replace-by-key the only lifecycle?

### 16.1 Answers from P0

| Question | Answer | Effect |
|---|---|---|
| 1. Music player action surface | It is **provider-side**: the plugin implements `IMusicPlayer`; `IIntegrationContext` has no music member. `MacroDeck.Sdk.MusicPlayer.Actions` supplies ready-made action definitions instead | **C4 drops all 17 blocks**, replaced by a capability panel that wires the SDK's own music actions — Actions-editor work |
| 2. Stable anchors for widget and config-flow handlers | Not established in P0; the generated widget provider and `IConfigFlow` shapes were inspected (`ConfigFlowStep`, `ConfigFlowResult`, `ConfigFlowResultKind`) but no anchor text was confirmed | **P9 stays gated** on its own verification |
| 3. Runtime settings access | Yes: `IIntegrationConfig.GetStringAsync` / `SetStringAsync` / `GetSecretAsync` / `SetSecretAsync`, keyed by config **entry id** | `get setting` / `set setting` ship with an entry picker; secrets are readable at runtime, which is exactly why Part 27.2 keeps them out of the document |
| 4. `Messages` publish-only or request/response | **Both**: `PublishAsync`, `SendAsync`, `RequestAsync` (→ `JsonElement?`), `SubscribeAsync`, `HandleCommandsAsync`, `HandleRequestsAsync` | `send message` ships, and a request/reply block plus a result reporter are discovered |
| 5. Host variable writes from an action | Yes, through `IUserVariableApi.ApplyAsync` with `UserVariableOperation` = Set / Add / Toggle / Append. Reading is a **different** API: `IVariableApi.GetByNameAsync` → `VariableHandle.Value` | both ship; Toggle and Append are discovered |
| 6. Per-client deck queries | Yes, and they are **synchronous**: `GetClients()`, `GetFolders()`, `GetProfiles()` return `DeckClient{ClientId, DeviceId, FolderId, ProfileId}`, `DeckFolder{Id, Label}`, `DeckProfile{Id, Label}` | all five C2 sensing ⚠ rows verified |
| 7. Event bindings available to plugin code | `GetBindings()` and a `BindingsChanged` event exist, but `Publish` is void and there is no inbound callback into an action | `publish and wait`, `broadcast to self` and the three last-occurrence reporters drop; the six event hats are deferred to P9 targets |
| 8. Actions generated `partial` | Still `sealed`, unchanged | Procedures stay local functions (Part 4.2) |
| 9. Documented WebSocket client surface | None in the SDK | two websocket blocks drop; network stays HTTP via plain .NET |
| 10. Notification dismissal | Yes: `IUserNotifier.Dismiss(key)` | `clear notification {key}` verified |

**Also answered, without being asked:** modal results are typed (`ShowModalAsync<T>` → `ModalResult<T>`), so
"ask and wait" is real; `ActionErrorCodes` is a static class of **string constants**, not an enum;
`LocalizedText` accepts a raw string by implicit conversion; and the repository's "29 editor types"
claim should read **27** (`ActionParameterType`).

---

## 17. Gap analysis: Visual versus Scratch

Every claim in Part 3 is checked here, row by row, including the rows where we lose.
Verdicts are `Equal`, `Better`, `Reduced`, or `N/A` with the reason.

### 17.1 Authoring capabilities

| Capability | Scratch | Visual | Verdict |
|---|---|---|---|
| Category palette | 9 colour-coded categories | 12 categories, colour-coded, collapsible | Equal |
| Palette search | absent | full-text search, recents, favourites | **Better** |
| Drag from palette | yes | yes, with a rendered bitmap drag image | Equal |
| Magnetic stack snapping | yes | yes, 40px radius scaled by zoom | Equal |
| Dragging carries everything below | yes | yes; Alt narrows to the single block | Equal + explicit affordance |
| Pulling a stack out of a C-block | yes | yes | Equal |
| Wrap a block in a loop | implicit, by dropping on the mouth | same, plus a context-menu "wrap in repeat" | Better |
| Unwrap a wrapped body | possible but undiscoverable | explicit Alt+click on the shoulder | **Better** |
| Drop feedback | shadow + snapping | ghost at the snapped notch, accent-glow gap, caret | Better |
| Nesting depth | unlimited | unlimited | Equal |
| `if / else if / else` chains | one `else if` pair per nesting | `CChain` with unlimited middle bars | Better |
| Reporters in slots | yes | yes | Equal |
| Boolean hexagons | yes | yes | Equal |
| Custom blocks with parameters | yes | yes, with typed slots | Equal |
| Boolean parameters | yes | yes | Equal |
| Procedures that return a value | no | yes, reporter form | **Better** |
| Multiple scripts per sprite | yes | yes; a target holds many scripts | Equal |
| Sprites | with costumes and sounds | Targets; no visual identity | Reduced, by design |
| Variables | local and global, show/hide | local and host-shared, watch/unwatch | Better (watch is debugger-bound) |
| Lists | yes, per-sprite and global | yes | Equal |
| Operators and maths | ~30 | 32, including JSON and encodings | Equal |
| Sound blocks | yes, bundled sounds | Media category, gated on the SDK | Reduced until verified |
| Broadcast and receive | yes | publish plus hats | Equal |
| Comments | yes, resizable | yes, attached as modifiers | Equal |
| Undo/redo | shallow, per sprite | transactional, one gesture is one undo | **Better** |
| Keyboard-only operation | partial | complete, including a keyboard drag mode | **Better** |
| Zoom and pan | no | yes, with a minimap | **Better** |
| Stage / running | real VM, sprites animate | mock-host simulator, no sprite visuals | Reduced for fidelity, better for debugging |
| Single-stepping | no | yes, with step-in and a trace | **Better** |
| Breakpoints | no | yes, per block | **Better** |
| Variable watch table | by showing the variable on stage | a real table, driven by watch blocks | Better |
| Runtime error reporting | a red outline on the block | same, plus a static diagnostics list | **Better** |
| Live generated code | no | yes, per document and per block | **Better** |
| Per-block documentation | hover hints | docs deep links, offline snapshot | Better |
| Block label localization | many languages | resx-driven, feeds the existing Localization manager | Equal |
| Export and share | `.sb3`, project page, remixing | `.dfblock`, PNG, SVG; no community in v1 | Reduced |
| Guided tutorials | extensive, curriculum-grade | planned, not in v1 | Reduced |
| Import existing code as blocks | n/a | round-trip for DeckForge-generated regions | **Better** (unique) |

### 17.2 Where Visual deliberately exceeds Scratch

1. **Breakpoints, step-in, and a trace timeline.** Scratch offers "run slowly" and a red outline on
   error. A plugin author debugging a retry loop needs more.
2. **Search.** A 187-block palette without search is a haystack. Scratch has no search at all.
3. **Live C#.** The canvas and the real plugin code are the same edit seen two ways, so there is no
   gap between the picture and what ships.
4. **Static diagnostics.** Shape violations, dangling references and unreachable code are caught while
   editing rather than at runtime inside Macro Deck.
5. **Transactional undo.** One gesture, one undo step. Half-finished drags never appear in history.
6. **Keyboard completeness.** Every operation, including structural moves, is reachable without a
   pointer.
7. **Zoom, pan and a minimap.** A real plugin script can be long; Scratch assumes it will not be.
8. **Procedures that return values**, with typed slots, at both call sites and definitions.
9. **A per-block C# preview and docs deep link** in the inspector.
10. **Round-trip**: a region of generated code can be reopened as blocks (Part 24).

### 17.3 Where Visual honestly trails Scratch

These are accepted, not hidden.

| Shortfall | Why it is accepted |
|---|---|
| No paint editor, costumes or sounds | There is no sprite to paint. Icon Studio already owns plugin iconography. |
| No coordinate motion | A plugin has no stage, no `x`/`y`, no rotation. Blocks with no referent are dropped (Part 5.1). |
| The simulator is not the real host | The SDK is authoritative; the interpreter is a preview, and the UI says so (Part 10.5). A mock host will never reproduce every host quirk. |
| No community or remixing | DeckForge is an offline tool by design; `.dfblock` packs (Part 25) are the local substitute. |
| No `.sb3` export | Import is designed (Part 26); export is not, because a Macro Deck plugin is not a Scratch project. |
| No curriculum-grade tutorials at launch | The editor ships with an empty-state walkthrough and a shortcut sheet; full guided lessons are Phase 10 and beyond. |

---

## 18. Interaction specification

Every interaction is specified so that two implementations of this document behave identically.
Where a rule depends on focus, that is stated, because the commonest block-editor defects are
focus-dependent: `Space` inserting a character instead of picking up a block, or a shortcut firing
while a field has focus.

### 18.1 Pointer

| Gesture | Target | Result |
|---|---|---|
| Left click | block tile | selects the block; selects its script; inspector follows |
| Left click | field inside a tile | begins inline editing; caret at the click position |
| Left click | empty canvas | clears selection; begins rubber-band selection |
| Left click | script strip row | scrolls that script into view and selects it |
| Left click | category rail row | filters the palette to that category |
| Left click | diagnostic row | selects the block, centres it, and flashes it once |
| Left drag (tile) | anywhere on a tile except a field or a menu | begins a stack drag per Part 9.5 |
| Left drag (field) | a numeric field | scrub-adjusts the value by the field's step |
| Alt + left drag (tile) | middle of a stack | drags only that block, leaving the rest in place |
| Left drag (canvas) | empty canvas | rubber-band; selects every script intersecting the band |
| Middle drag | anywhere on the canvas | pans |
| Right click | block tile | tile context menu |
| Right click | empty canvas | canvas context menu at the pointer |
| Right click | palette block | "add to selected script" and "open docs" |
| Double click | script name in the strip | inline rename |
| Double click | a variable's name in the inspector | inline rename (refuses a duplicate) |
| Double click | the canvas background | creates a new empty script with a default hat at that point |
| Click | a tile's gutter (stage mode) | toggles a breakpoint |
| Hover 400ms | a collapsed body during a drag | expands the body |
| Hover | any tile | shows a tooltip: block name, summary, docs hint, and the emitted C# |

### 18.2 Wheel and zoom

| Input | Result |
|---|---|
| Wheel | vertical scroll |
| Shift + wheel | horizontal scroll |
| Ctrl + wheel | zoom about the pointer, clamped 25%–200%, in 10% steps |
| Ctrl + 0 | reset zoom to 100% |
| Ctrl + `+` / `-` | zoom in / out about the viewport centre |

### 18.3 Keyboard

The map is deliberately small in surface area and deep in reachability.

| Key | Context | Action |
|---|---|---|
| `↑` / `↓` | canvas, tile focused | move focus to the previous / next statement in the same body |
| `←` / `→` | canvas, tile focused | move focus out to the parent body / in to the first nested statement |
| `Ctrl + ↑` / `Ctrl + ↓` | tile focused | move the block (or the focused run) within its body |
| `Ctrl + ←` / `Ctrl + →` | tile focused | outdent / indent into the previous sibling's body if it is a C-block |
| `Tab` / `Shift + Tab` | tile focused | cycle forward / back through the tile's slots |
| `Enter` | field focused | commit the value; on a menu, open it |
| `Enter` | tile focused | edit the tile's first slot |
| `Esc` | field focused | revert the field to the value it had before editing began |
| `Esc` | canvas | clear selection; on a second press, exit stage mode |
| `Delete` / `Backspace` | tile focused | delete the run starting at the focused block (refused for a hat) |
| `Ctrl + D` | tile focused | duplicate the focused run directly below itself |
| `Space` | tile focused | pick up the focused run; move with arrows; `Space` again drops it, `Esc` cancels |
| `Space` | field focused | **types a space**, does not pick up (focus precedence) |
| `Ctrl + Z` / `Ctrl + Y` | canvas | undo / redo one transaction |
| `Ctrl + C` / `Ctrl + V` / `Ctrl + X` | tile focused | copy / paste / cut the focused run as Visual JSON |
| `Ctrl + K` | canvas | command palette: "add block…", "go to script…", "insert hat…" |
| `Ctrl + F` | canvas | palette search focus |
| `Ctrl + S` | anywhere | save into the plugin |
| `F5` | stage | run |
| `F10` / `F11` | stage | step over / step into |
| `F9` | stage | toggle a breakpoint on the focused block |
| `Shift + F5` | stage | stop |
| `Ctrl + 1..0` | application | the sidebar shortcuts, unchanged (Part 9.1) |
| `F1` | application | docs, unchanged |

Rules that resolve conflicts:

1. A focused text field wins over every single-key shortcut. Modified shortcuts still fire.
2. `Esc` first cancels the innermost active thing (a field edit, then a pick-up, then the selection,
   then stage mode).
3. A modal (a menu, the command palette, a dialog) swallows canvas keys entirely.
4. Inline drag mode and pointer drag share one transaction, so undo behaves identically either way.

### 18.4 Field editing

| Rule | Behaviour |
|---|---|
| First keystroke in a literal placeholder | replaces the placeholder rather than appending |
| Committing an invalid menu key | reverts and shows a diagnostic |
| Pasting a multi-line value into a single-line field | newlines become `\n` escapes, never a broken layout |
| `{name}` holes in log templates | highlighted; a hint lists the parameters currently in scope |
| Renaming a variable | refused when the name is taken; offers the next free name in the diagnostic |
| Editing a list or variable literal | opens a small multi-line editor for one-item-per-line entry |

### 18.5 Context menus

**Tile menu:** duplicate, delete, add comment, disable, enable, wrap in `repeat`, wrap in `if`,
wrap in `if / else`, unwrap, extract as procedure, copy, cut, paste below, open docs, show emitted C#.

**Canvas menu:** new script, new script with a chosen hat, paste, select all, arrange scripts in a
column, tidy stacks (removes visual overlap only, never reorders), zoom to fit, export PNG, export SVG,
add variable, add list, define procedure.

**Stage menu:** run, step, step into, pause, stop, reset, clear trace, clear breakpoints, advance the
clock by 1s / 10s, allow real network (with confirmation).

---

## 19. Motion and animation

Motion exists to explain what happened, never to entertain. Everything below is skipped or
collapsed when the system asks for reduced motion (Part 20), and nothing blocks input while playing.

| Interaction | Animated property | Duration | Easing |
|---|---|---|---|
| Tile hover | shadow depth and fill lighten | 90ms | ease-out |
| Tile press | scale 1.00 → 0.985 | 70ms | ease-out |
| Drag start | ghost opacity 0 → 0.9, lift shadow | 110ms | ease-out |
| Magnetic snap | ghost position → snapped notch | 120ms | cubic ease-out |
| Drop into a gap | gap opens 0 → block height, siblings slide down | 160ms | cubic ease-in-out |
| Drop rejected | ghost springs back to origin | 180ms | ease-out with a small overshoot |
| Wrap in a C-block | body slides right and down into the mouth | 200ms | ease-in-out |
| Unwrap | the reverse of the above | 200ms | ease-in-out |
| Script add / delete | tile group fades and scales 0.96 → 1.00 | 150ms | ease-out |
| Panel collapse | width and opacity | 180ms | ease-in-out |
| Diagnostics appear | the offending tile outlines once, then settles | 300ms + 200ms | ease-out |
| Stage: running block | accent pulse | 700ms, looped | sine in-out |
| Stage: breakpoint hit | pause flash on the gutter | 240ms, four times | ease-out |
| Undo / redo | the affected region flashes its previous position | 220ms | ease-out |
| Toast (save succeeded) | slide and fade | 160ms in, 2s hold, 200ms out | ease-in-out |

Rules:

1. No animation delays a state change: the document is mutated first, the animation is decoration.
2. Animations are cancelable. Starting a new gesture mid-animation snaps the previous one to its end
   state rather than queueing.
3. Reduced motion collapses every duration to 0 except the drop indicator, which becomes an instant
   colour change — a drag with no feedback at all is unusable.
4. Durations are constants in `BlockTheme.cs`, not literals at call sites, so they can be tuned in
   one place and asserted in a test.

---

## 20. Settings

New keys on the existing `AppSettings` (`Services/SettingsService.cs`), which persists to
`%LOCALAPPDATA%/DeckForge/settings.json`. Each is added with a default that preserves today's
behaviour, so `AppSettings.RepairNulls` and the existing load path need no special handling.

| Key | Default | Range | Applies to | Settings page section |
|---|---|---|---|---|
| `VisualSnapEnabled` | `true` | on/off | magnetic snapping | Visual |
| `VisualSnapRadius` | `40` | 16–96 px at 100% zoom | the magnet radius | Visual |
| `VisualSnapToGrid` | `false` | on/off | free-canvas script placement | Visual |
| `VisualGridSize` | `16` | 4–64 px | the grid used when the above is on | Visual |
| `VisualDefaultZoom` | `100` | 25–200 % | the zoom a document opens at | Visual |
| `VisualMinimapVisible` | `true` | on/off | the canvas minimap | Visual |
| `VisualPaletteShowDeprecated` | `false` | on/off | whether deprecated blocks appear | Visual |
| `VisualAutosaveSeconds` | `0` | 0 (off), 15–600 s | sidecar autosave. Never writes to the plugin | Visual |
| `VisualReduceMotion` | system | system/always/never | animation (Part 19) | Accessibility |
| `VisualBlockTextSize` | `13.5` | 12–18 px | tile labels | Visual |
| `VisualStageAllowNetwork` | `false` | on/off | the simulator's HTTP layer | Simulator |
| `VisualStageStepDelayMs` | `120` | 0–1000 ms | delay between interpreteted steps | Simulator |
| `VisualBreakpoints` | `[]` | block ids per workspace | persisted breakpoints | internal, in `stage.json` |
| `VisualCannedResponses` | `{}` | url pattern → response | the simulator's canned HTTP table | internal, in `stage.json` |
| `VisualShowBlockCode` | `false` | on/off | a one-block C# preview in the inspector | Visual |
| `VisualOnboardingSeen` | `false` | on/off | the empty-state walkthrough | Visual |

Rules:

1. Settings that affect generated code (`VisualSnap*`, colours, text size) are **never** read by the
   emitter. Code generation stays a pure function of the document.
2. `VisualAutosaveSeconds` writes only the sidecar under `.deckforge/visual/`. The plugin is written
   only by an explicit Save, because a background write into a user's source tree is not acceptable.
3. `VisualStageAllowNetwork` defaults to off and is per-session promptable, but the persisted value is
   what the simulator starts with (Part 27).
4. Every key is added to the Settings page under a `Visual` section, and the theme tokens for block
   colours follow the existing accent setting rather than introducing a second colour preference.

---

## 21. Localization of block labels

Block labels are user-facing strings and must go through the plugin's own localization, which
DeckForge already has a manager and a validator for. The catalog is the single source of both the
English text and the resx key, so a block cannot exist without a translatable label.

### 21.1 Keys

| Thing | Key | Example |
|---|---|---|
| Block label | `Blocks.<Category>.<BlockId>` | `Blocks.Deck.OpenFolder` |
| Slot label | `Blocks.<Category>.<BlockId>.Slot.<slot>` | `Blocks.Deck.OpenFolder.Slot.Folder` |
| Menu option | `Blocks.<Category>.<BlockId>.Menu.<option>` | `Blocks.Ui.Log.Menu.Level.Warning` |
| Category name | `Blocks.Category.<Category>` | `Blocks.Category.Deck` |
| Hat description | `Blocks.Hat.<HatId>` | `Blocks.Hat.ActionRuns` |

Rules, inherited from the existing resx pipeline and restated because they bite:

1. A resx key segment becomes a nested C# type, so a segment that is a C# keyword is escaped and a
   contextual keyword that cannot be a type name (`file`) is prefixed. `CSharpCode.ResxSegment`
   already implements this; the catalog must route segments through it rather than doing `ToPascal`
   by hand.
2. Key segments are Pascal-cased from the **wire** id. The catalog's `Id` is the wire form
   (`deck.open-folder`); the emitted member is `OpenFolder`.
3. Label templates keep their slot markers in the resx value, so a translator can reorder words and
   move a slot. A translation that drops a marker is reported by the existing localization
   diagnostics as a missing placeholder.
4. **User data is never localized**: variable names, list names, folder and profile ids, URLs, file
   paths, JSON paths, log templates and menu keys that are user-chosen.

### 21.2 What the label template means

A catalog label like `open folder {folder} on client {client}` is a template whose markers name
slots. Rendering in the canvas substitutes the slot's rendered content, which may itself be another
block, and the substitution is width-unaware: a tile grows, it never truncates. Long values are
elided with a tooltip carrying the full value, never silently clipped.

### 21.3 Right-to-left

The canvas mirrors in RTL:

- The notch, indent rail and palette sit on the mirrored side; block geometry is computed from the
  flow direction rather than from `Left`/`Right` constants.
- Drop scoring is direction-aware: the horizontal overlap test and the notch comparison use logical
  coordinates, so a mirrored canvas does not prefer the wrong gap.
- Palette category ordering stays LTR (categories are an index, not prose).
- Log templates and code previews stay LTR: they are code, not UI text.

### 21.4 Fallback

`current culture → English default`. A missing translation renders the English label and is listed as
`(missing)` by the existing Localization manager, rather than rendering an empty tile — the failure
mode that made a previous canvas look empty is not repeated.

---

## 22. Versioning and deprecation policy

The catalog is 187 blocks and will grow. Without a stated policy, that surface becomes a compatibility
liability faster than it becomes an asset.

### 22.1 Document schema

| Version | Meaning | Read | Write |
|---|---|---|---|
| `0` | a legacy `BlockProgram` / `<action>.blocks.json` | migrated to 1 on load, originals copied to `legacy/` | never written |
| `1` | the current `VisualProject` | yes | yes |
| `> 1` | written by a newer DeckForge | **read-only** | refused, with an explanation |

A document from a newer version opens **read-only** with a banner naming the version and telling the
user to update DeckForge. It is never silently downgraded, never partially loaded, and never saved
back — silent data loss on open is the one failure a document format may not have.

### 22.2 Block id guarantees

1. **An id is permanent.** It is never renamed, never reused, and never repurposed.
2. **An id's meaning never changes.** If behaviour must change, a new id is added and the old one is
   deprecated; the old one keeps emitting its old semantics until it is removed.
3. **Deprecation lifecycle:**
   `Active → Deprecated → Hidden → Removed`, with at least one schema version between each step and
   no step skip.

| State | In the palette | Loads | Emits | Notes |
|---|---|---|---|---|
| Active | yes | yes | yes | |
| Deprecated | behind "show deprecated" | yes | yes | tooltip names its successor and offers "migrate this use" |
| Hidden | no | yes | yes | still fully functional for existing documents |
| Removed | no | only via the migration that rewrote it | no | requires a schema bump and a rewriting migration |

4. **Emitters support Hidden blocks indefinitely.** A block may only leave the emitter when a
   migration exists that rewrites every document containing it — and that migration must be tested
   against a fixture captured from a real document before removal.
5. **The legacy alias table (Part 7.15) is permanent.** Those ids are the retired page's, and old
   workspaces will exist for as long as someone has a `.blocks.json`.

### 22.3 The generated runtime library

`VisualRuntime.cs` is fully auto-generated, so regeneration replaces it wholesale. That has one
consequence worth stating: a helper may be removed between DeckForge versions, and an old plugin is
repaired simply by saving again from the canvas. The runtime is therefore versioned by the block
catalog that produced it, and its header records the catalog revision.

### 22.4 Compatibility matrix (maintained as versions ship)

| DeckForge | Schema read | Schema write | SDK pin | Visual runtime revision |
|---|---|---|---|---|
| 0.1.x (current) | 0, 1 | 1 | 3.0.0-beta.14 | none (no Visual page) |
| next | 0, 1 | 1 | 3.0.0-beta.14 | 1 |

---

## 23. Extension contract: third-party blocks

DeckForge is built for additive extension (`EXTENDING.md`): a capability is a catalog entry, a
contributor and a page registration. Blocks deserve the same seam, because a plugin author who
wraps a third-party SDK will want blocks for it.

### 23.1 The seam

```csharp
public interface IVisualBlockProvider
{
    string Id { get; }
    int Order { get; }

    /// Categories this provider adds. May be empty when it only extends existing ones.
    IReadOnlyList<BlockCategoryDescriptor> Categories { get; }

    /// Block descriptors, in palette order.
    IReadOnlyList<BlockDescriptor> Blocks { get; }

    /// Source for one block instance. Returning null means "not mine".
    string? EmitExpression(BlockEmitContext context, Block block);
    void EmitStatement(BlockEmitContext context, Block block, IndentedCodeWriter writer);

    /// A step for the interpreter, so the simulator can run contributed blocks too.
    IAsyncEnumerable<ExecutionStep> ExecuteAsync(InterpretContext context, Block block, CancellationToken ct);
}
```

- Registered exactly like `IProjectContentContributor`:
  `services.AddSingleton<IVisualBlockProvider, MyBlocks>();`
- Discovered from extensions in the same way, with the same opt-in: extensions are **off by default**.
- A provider's blocks are namespaced by the provider id (`myext.motion.thrust`), so a contributed id
  can never collide with a catalog id, and a document that uses one records the provider id so a
  machine without the extension reports a missing-provider diagnostic rather than corrupting the file.

### 23.2 Why all three parts are required

A contributed block must supply a palette row, an emitter **and** an interpreter step. A provider that
supplies only an emitter produces blocks that cannot be previewed, which breaks the central promise
that the canvas shows what will run. The interface makes that impossible to satisfy accidentally by
making the three members non-optional; a provider that genuinely cannot be simulated implements the
interpreter step as a single `Unsupported` step, and the stage marks the run as "partially simulated"
rather than pretending.

### 23.3 Trust position

Stated plainly, matching the existing Extensions page: loading an extension runs third-party code in
this process with this process's permissions, and a .NET assembly cannot be sandboxed. A block
provider is therefore a **higher-trust** contribution than a page, because its emitter writes into a
user's source tree. The Visual page's extension list shows which providers contributed blocks, and
which blocks in the current document came from a provider.

### 23.4 Verification

A contributed block declares its SDK requirement the same way catalog blocks do, and it is marked
`IsVerified: false` until the user (or the provider's own tests) confirms it. Unverified blocks are
unsaveable, exactly like unverified catalog blocks, so the `IsVerified` policy stays honest for
everyone.

### 23.5 Tests a provider must bring

1. Every declared block emits compilable C# for a sample document.
2. Every declared block has an interpreter step.
3. Ids are namespaced and unique.
4. The `extensions/DeckForge.SampleExtension` fixture is extended with one contributed block, so the
   seam is exercised from a real assembly outside `src/`, which is how the existing contributor seam
   is proven reachable.

---

## 24. Round-trip: blocks from code already in the file

A plugin author who hand-edited a block region, or who has an action from an older DeckForge, should
be able to open it on the canvas instead of losing it. This is the one capability no Scratch offers,
and it only works because DeckForge owns the region.

### 24.1 The mechanism

1. `BlockCompiler.ExtractRegion` already pulls the text between the markers. Round-trip starts there.
2. A **structural header comment** is emitted into every region:

```csharp
// <macrodeck-blocks>
// deckforge-visual: schema=1 doc=c41f7a2 region=sha256:9a1c…
```

   The `doc` field is the sidecar's document id and the hash covers the emitted body. A region whose
   hash matches its sidecar loads as blocks directly, with no parsing at all.
3. When the hash does not match (hand edits, a foreign file, an older format), the region is parsed by
   a **subset parser** that accepts exactly the constructs the emitters produce: the guard, `await`
   calls on `_integration`/`VisualRuntime`, loops, branches, local declarations of the four inferred
   types, procedure local functions, and comments. It is not a C# parser and does not try to be.
4. **Anything it cannot recognise is preserved, never lost.** Unknown statements become an
   `OpaqueBlock` that renders as a grey tile showing the original lines, is emitted back verbatim at
   the same position, and is reported as a diagnostic ("3 lines could not be opened as blocks; they
   are preserved and will be written back unchanged").
5. A round-trip test asserts the property that matters: **parse-then-emit on a recognized region
   reproduces the region byte-for-byte**, and on a region with unknown lines reproduces it once the
   opaque blocks are re-emitted.

### 24.2 Limits, stated up front

| Limit | Consequence |
|---|---|
| Only constructs the emitters produce are recognised | hand-rolled LINQ, `try`/`catch`, or a custom helper becomes an opaque block |
| Names are matched by call shape, not semantics | a hand-written `_integration.Deck.GoBackAsync(…)` becomes a `go back` block, which is correct, while a wrapper method becomes opaque |
| Local functions are recognised only by the shape the emitter produces | a hand-written helper becomes opaque |
| Regions written by a much older DeckForge may not parse | they degrade to opaque blocks with a full diagnostic, never to a refusal |
| Round-trip never rewrites a file on load | opening is read-only until the user saves |

### 24.3 Why it is safe

The region is exactly the code DeckForge owns. Parsing it is not an inference about the user's
intent; it is reading back a file we wrote, with a hash to confirm it. When the hash matches, the
sidecar is authoritative and nothing is parsed. When it does not, the fallback is preservation, which
means the worst case is "some lines show as a grey tile", never "some lines are gone".

---

## 25. Sharing, export and clipboard

Three different jobs get three different formats. Conflating them is how a format ends up both
lossy and hard to read.

| Job | Format | Contents | Fidelity |
|---|---|---|---|
| Move a script between plugins | `.dfblock` (a zip) | one script, its target kind, the variables/lists it uses, the procedures it calls, plus a manifest | full for the script; unresolved references reported on import |
| Paste between windows | Visual JSON on the clipboard | a script or a run of blocks with the same payload as `.dfblock` minus the manifest | full |
| Show somebody the picture | PNG | a raster render of a script or the whole canvas | visual only |
| Embed or print it | SVG | a vector render produced from the layout model | visual only |

### 25.1 `.dfblock`

```
my-script.dfblock           (zip)
  manifest.json             format version, title, author, created-with, required capability ids
  script.json               the script (schema 1 subset: one script, its dependencies)
  preview.png               optional thumbnail rendered by the export path
```

Import resolution, in order:

1. **Ids are regenerated** on import. A copied script never collides with an existing one.
2. **Variables and lists** are matched by name: an existing one is reused, a missing one is created,
   and a type clash is reported rather than silently coerced.
3. **Procedures** are imported with the script. A call to a procedure that is *not* in the package is
   reported as a diagnostic and shown as a placeholder call, which cannot be saved until it is
   resolved.
4. **Target kind mismatch** (the package holds a widget handler script and the current target is an
   action) is refused with an explanation, never silently retargeted.
5. **Required capabilities** in the manifest are checked against the plugin; missing ones produce a
   warning with a link to the Capabilities page.

### 25.2 Clipboard

`Ctrl+C` writes a stable text payload (a `VisualClipboard` envelope with a format version and the
same subset as `script.json`) and also a bitmap for pasting into a chat app. Paste validates the
envelope and refuses a version it does not understand with an explanation, rather than importing a
garbled block tree.

### 25.3 PNG render

Rendered from the same geometry the canvas uses (`StackLayout` plus `BlockMetrics`), drawn into a
`DrawingVisual` at 2× scale on a transparent background, with an optional padding and a caption
line naming the target. Because it is drawn from the layout model rather than by screenshotting the
window, it works for a script that is scrolled off screen and it is deterministic, so a test can
compare hashes.

### 25.4 SVG render

Emitted by walking the same layout model, so the two renders cannot disagree. Shapes are drawn as
`path` elements with the category fills inlined as literals (an exported file cannot reference the
application's resources) and text as `text` elements in the system font stack. Slot values are
escaped with the same helper the code generator uses for XML, since an escaped `&` was a past defect
in this repository.

---

## 26. Scratch `.sb3` import (design only)

Not in the first scope, but designed here because it is the most likely follow-up request and because
its constraints shape the model. It is an **import** only: a Macro Deck plugin is not a Scratch
project, so there is nothing honest to export back.

### 26.1 Shape of the work

1. `.sb3` is a zip containing `project.json`, costumes, sounds and sprites.
2. `project.json` gives **sprites**, each with **blocks** as a flat id map (`next`, `parent`,
   `inputs`, `fields`), plus **variables** and **lists** with per-target scoping.
3. Flat map → tree: a deterministic walk from each hat along `next`, with inputs resolved through the
   referenced block ids. This is the one place the flat representation is the input rather than the
   output, and it is a bounded, well-specified transformation.
4. Sprite → Target; a sprite's scripts become that target's scripts; hats are mapped by opcode.
5. Stage-level variables become plugin-scope variables; sprite-local variables become locals in the
   nearest target.

### 26.2 Opcode mapping (excerpt; the full table is a deliverable if this proceeds)

| Scratch opcode | Visual block | Notes |
|---|---|---|
| `event_whenflagclicked` | `when action runs` | one per target; extra ones become separate scripts on the same target |
| `event_whenbroadcastreceived` | `when message received` | Scratch broadcasts map onto topics |
| `event_broadcast` / `_andwait` | `send message` / `… and wait` | |
| `control_wait` | `wait {n} seconds` | |
| `control_repeat` / `_forever` / `_until` / `_while` | the matching Control block | |
| `control_if` / `_else` | `if` / `if … else` | |
| `data_setvariableto` / `_changevariableby` | `set` / `change by` | |
| `data_addtolist` and friends | the Lists category | |
| `operator_add`, `_subtract`, `_multiply`, `_divide`, `_mod`, `_random` | the Operators category | |
| `operator_join`, `_letter_of`, `_length`, `_contains` | the text operators | |
| `operator_equals`, `_gt`, `_lt`, `_and`, `_or`, `_not` | comparison and logic | |
| `sensing_askandwait`, `sensing_answer` | `show modal` plus a response slot | the closest honest analogue |
| `procedures_definition` / `_call` / `_prototype` | procedures | argument reporters become parameter slots |
| `looks_sayforsecs`, `looks_say` | `notify` | a speech bubble becomes a notification |
| `motion_*`, `pen_*`, `looks_switchcostume*`, `sound_play*`, `sensing_mouse*`, `sensing_touching*` | **dropped** | no referent; counted in the import report |
| `control_create_clone_of`, `control_start_as_clone` | **dropped** | no clone concept |

### 26.3 Degradation rules

1. **Nothing is dropped silently.** Every unmapped block, unsupported hat and dropped variable
   produces a row in an **import report** shown before anything is written: opcode, sprite, script,
   and the reason.
2. **A script with an unmapped block keeps its shape**: the block becomes an `OpaqueBlock` carrying a
   `// TODO` comment and the original opcode, and the surrounding structure is preserved, so the user
   can finish the port by hand.
3. **Coordinates are discarded**, and an imported script is laid out automatically in a column, one
   script per row, because Scratch positions blocks in sprite-space and there is no equivalent.
4. **Import is a new document**, never a merge into an existing one, until the user explicitly
   imports a script through the `.dfblock` path (Part 25).
5. **Costumes and sounds are ignored** and named in the report; Icon Studio is where plugin imagery
   belongs.

### 26.4 Why it is worth doing at all

Because Scratch is where the target user learned this interface. An author with a working Scratch
sequence can arrive with the control flow already expressed, then replace the blocks with no referent
one by one, guided by the report. That is a materially better onboarding than an empty canvas.

---

## 27. Safety, trust and threat model

A visual editor that emits network calls and host actions into a user's source tree has a small but
real safety surface. It is stated here rather than assumed.

### 27.1 What the tool can do, and to what

| Surface | Risk | Position |
|---|---|---|
| Generated code in the plugin | writes into the user's source tree | only an explicit Save writes to the plugin; autosave writes only the sidecar; the region is the only thing touched on the second and later saves |
| The simulator's HTTP layer | makes real network requests | **off by default** (Part 20); enabling it is an explicit, per-session confirmation in the stage menu, and the toggle is visible while a run is live |
| Canned responses | could be confused with real ones | the stage labels the HTTP panel "simulated" whenever the network toggle is off, and every request in the trace is tagged simulated or real |
| Round-trip parsing | reads a file | read-only on open; a document is never rewritten without a Save |
| Extension block providers | arbitrary code in-process | same trust position as existing extensions: off by default, disclosed on the Extensions page, and disclosed again in the palette for blocks they contribute (Part 23.3) |
| `.dfblock` import | a file from elsewhere | never executes anything; it produces a document, and its manifest's capability claims are checked, not trusted |
| `.sb3` import | a file from elsewhere | never executes anything; unmapped blocks become inert placeholders |
| Exported PNG / SVG | could carry data the user did not mean to share | exports contain only block labels and the user's literals; §27.2 applies |

### 27.2 Secrets

**A design rule, not a convention: secrets never enter the document.**

- The bearer-token block takes a **parameter name**, never a token literal (this is already true of
  the existing `HttpRequestBlock.BearerTokenParameter`). There is no slot for pasting a token into a
  URL.
- URLs that appear to contain credentials (`user:pass@`, `?token=`, `?key=`, `?api_key=`) raise a
  warning on the block, naming the field and suggesting a parameter, because the document is
  committed to git.
- The same check runs on export: PNG, SVG and `.dfblock` exports warn when a script contains a
  literal that looks like a secret, and the export dialog offers to replace it with a parameter
  reference.
- The simulator never persists a real response body into `stage.json` when the network toggle is on;
  only its size and status are recorded.

### 27.3 Host-impact honesty

The UI cannot sandbox the host. A script that opens folders, switches profiles, writes host variables
or shows modals does those things for real once the plugin runs. The simulator's job is to make that
visible **before** it happens, which is why the stage shows a deck and a notification stack rather
than a console log, and why every host call in the trace names the surface it hit.

### 27.4 Failure containment

| Failure | Containment |
|---|---|
| A block provider throws while emitting | the provider is contained, the block emits as an opaque placeholder, and a diagnostic names the provider |
| A sidecar is corrupt | reading returns null with a message and the file is left untouched (the existing `BlockProgramJson.TryDeserialize` stance) |
| A migration throws | the document opens read-only with the migration named, and the original file is untouched |
| The interpreter loops forever | the stage enforces a step budget and pauses with a message naming the script |
| The canvas throws while rendering a malformed block | the block renders as an error tile with its id; the page stays usable, and the shell's crash banner is not triggered by a document |

---

## 28. Acceptance criteria and metrics

The feature is done when all of the following are true. This is the checklist a reviewer uses, and it
is deliberately phrased so each line can be demonstrated rather than argued.

### 28.1 Functional acceptance

1. A new sidebar item **Visual** exists, in the old Blocks slot, and every other page keeps its
   Ctrl+digit shortcut.
2. The palette offers every verified catalog block, in the right category, searchable by name and by
   summary, with the four live in §28.2 present and working.
3. Blocks can be dragged from the palette, reordered by dragging, nested inside `repeat`, `forever`,
   `wait until`, `if`, `if / else` and `if / else if / else` to at least five levels, pulled back out,
   wrapped and unwrapped.
4. Reporters and booleans plug into slots; menus and fields edit inline; a value can be replaced by a
   block and a block by a value.
5. Dragging a middle block carries everything below it; Alt narrows to the single block.
6. Undo and redo work for every mutation, one gesture per step.
7. The document survives navigation, reload, a solved-and-reopened workspace, and a DeckForge
   restart.
8. Save writes real C# into the chosen action inside the markers, the generated plugin builds with
   zero errors, and `macrodeck-plugin validate --level publication` reports no new warnings.
9. Hand-written code outside the markers survives every save, byte-for-byte.
10. A legacy `.blocks.json` workspace migrates, opens, and still compiles — with the originals kept
    under `legacy/`.
11. The simulator runs a script with parameters the user types in, shows the deck and notification
    effects, supports step, step-into, breakpoints and reset, and labels its output as simulated.
12. Diagnostics appear for each class in Appendix F, and clicking one selects the offending block.
13. A script can be exported to PNG, exported to SVG, copied and pasted, and exported and re-imported
    as a `.dfblock` without loss.
14. Every existing test still passes, and the new tests listed in Part 12 pass.

### 28.2 Quality acceptance

| Criterion | Target | How it is checked |
|---|---|---|
| Two themes | light and dark render every block legibly | screenshots for every category in both themes |
| Contrast | label text ≥ 4.5:1 on its tile fill, both themes | computed in a test from the token values, not eyeballed |
| Keyboard completeness | every operation in Part 18.3 works | a user-driver scenario exercising each key path |
| Zero-warning generated code | no analyzer warnings in the user's build | a compile test with warnings-as-errors |
| Determinism | the same document emits byte-identical C# | compile twice and compare, per sample document |
| No data loss | no path loses a block, a line or a script | migration tests, round-trip tests, a corrupt-file test |
| Reduced motion | the UI is fully usable with motion off | a manual pass with the setting forced |

### 28.3 Metrics (measured, not asserted)

| Metric | Target | Why this number |
|---|---|---|
| Time to first generated block for a new user | under 3 minutes from an empty canvas | the tutorial state and empty state are judged by this |
| Palette search latency, 187 blocks | under 30ms keystroke-to-render | the palette is the primary navigation surface |
| Drag frame budget | 60fps with 500 visible tiles | Part 9.9 |
| Save latency, 200 blocks | under 250ms wall clock | `Ctrl+S` must feel immediate |
| Open latency, a 5-script document | under 400ms | page navigation is instant today; Visual must not be the exception |
| Migration success on the fixture set | 100%, no manual fixes | a migration that needs hand repair is not a migration |
| Save round-trip loss | zero lines, ever | the one metric that is absolute |

### 28.4 Deliberate non-goals as acceptance criteria

1. The simulator is not required to reproduce host bugs, timing or quirks, and no test asserts that it
   does.
2. No block may exist whose only implementation is visual: every shipped block emits C#.
3. No `.sb3` export, no community features, no art tools — all named in Part 17.3 as accepted
   shortfalls rather than treated as incomplete work.

---

## Appendix A — SDK inventory (filled in P0)

**Status: filled (P0).** Every row was read from the pinned assemblies with `tools/SdkInventory`, which
is re-runnable on every SDK bump:

```bash
dotnet run --project tools/SdkInventory -- \
  ~/.nuget/packages/macrodeck.sdk/3.0.0-beta.14/lib/net10.0/MacroDeck.Sdk.dll
```

The raw dumps are committed beside the tool at `tools/SdkInventory/surface/`:
`MacroDeck.Sdk-3.0.0-beta.14.md` (253 exported types) and
`MacroDeck.Localization-3.0.0-beta.14.md`. A block may only be marked `IsVerified: true` in Part 7
when its row below cites the member that was inspected.

### A.1 The integration context — the whole host surface

`IIntegrationContext` has exactly ten members. Anything not reachable through one of them is not
callable from an action, which is how every "dropped" verdict in §7.16 was decided.

| Member | Type | Feeds blocks |
|---|---|---|
| `Config` | `IIntegrationConfig` | C6 settings |
| `Deck` | `IDeckNavigator` | C2 |
| `Events` | `IEventPublisher` | C5 |
| `Messages` | `IMessageChannel` | C5 |
| `Notifications` | `IUserNotifier` | C3 |
| `Scripts` | `IScriptApi` | C6 |
| `UiResources` | `IUiResourceRegistry` | C3 |
| `UserVariables` | `IUserVariableApi` | C6, C8 |
| `Variables` | `IVariableApi` | C6 |
| `Widgets` | `IWidgetApi` | C2, C3 |

### A.2 Members per surface

| Surface | Members (verified) | Feeds |
|---|---|---|
| `IDeckNavigator` | `ChangeFolderAsync(folderId, originClientId, ct)`, `ChangeProfileAsync(profileId, originClientId, ct)`, `GoToParentAsync(originClientId, ct)`, `GoBackAsync(originClientId, ct)`, `GetClients()`, `GetFolders()`, `GetProfiles()`, event `ClientChanged` | C2 |
| `DeckClient` / `DeckFolder` / `DeckProfile` | `ClientId`, `DeviceId`, `FolderId`, `ProfileId` / `Id`, `Label` / `Id`, `Label` | C2 sensing |
| `IUserNotifier` | `Notify(UserNotificationRequest)` (Title, Message, Level, Key), `Dismiss(key)` | C3 |
| `UserNotificationLevel` | `Info`, `Warning`, `Error` | C3 menus |
| `IUiInteractions` ∈ `ActionExecutionContext.Ui` | `ShowModalAsync(originClientId, ModalDefinition, ct)` → `Task<bool>` and `Task<ModalResult<T>>` | C3 modals |
| `ModalDefinition` | `ViewId`, `Title` (`LocalizedText?`), `Data` (`Dictionary<string, JsonElement>`) | C3 |
| `IActionInteractions` ∈ `ActionExecutionContext.Interactions` | `RequestItemPicker(...)`, `RequestDevicePicker(...)` | C3 pickers |
| `IScriptApi` | `GetScripts()`, `RunAsync(scriptId, inputs, originClientId, ownerWidgetId, ct)` | C6 |
| `Script` / `ScriptInputType` | `Id`, `Name`, `Description`, `Inputs`, `RunsOnWidget` / `Text`, `Numeric`, `Boolean` | C6 pickers |
| `IEventPublisher` | `Publish(eventId, parameters)` (**void**), `GetBindings()`, event `BindingsChanged` | C5 |
| `IMessageChannel` | `PublishAsync`, `SendAsync`, `RequestAsync` → `JsonElement?`, `SubscribeAsync`, `HandleCommandsAsync`, `HandleRequestsAsync` | C5 |
| `IVariableApi` | `CreateAsync`, `DeleteAsync`, `GetAllAsync`, `GetByNameAsync`, `SetValueAsync` | C6 read |
| `VariableHandle` | `Id`, `Name`, `Type`, `Value`, `DecimalPlaces`, `DefinitionId` | C6 read |
| `IUserVariableApi` | `ApplyAsync(name, ownerWidgetId, operation, value, ct)`, `CreateAsync(name, ownerWidgetId, type, initial, decimalPlaces, ct)` | C6 write |
| `UserVariableOperation` | `Set`, `Add`, `Toggle`, `Append` | C6 write menus |
| `VariableType` | `Text`, `Numeric`, `Boolean` | C6, C8 menus |
| `IWidgetApi` | `SetStateAsync(widgetId, stateId, ct)`, `AdvanceStateAsync`, `ApplyAsync(WidgetAppearanceRequest, ct)`, `Exists(widgetId)`, `GetWidgets()`, `InvalidateIconAsync(actionId, ct)` | C2, C3 |
| `IIntegrationConfig` | `GetEntriesAsync(ct)`, `GetStringAsync(entryId, key, ct)`, `SetStringAsync`, `GetSecretAsync`, `SetSecretAsync` | C6 settings |
| `IUiResourceRegistry` | `RegisterAsync(name, bytes, mediaType, ct)`, `GetPluginIconAsync(key, name, ct)`, `RemoveAsync(name, ct)` | C3 assets |
| `ActionExecutionContext` | `Parameters`, `CancellationToken`, `OriginClientId`, `OwnerWidgetId`, `Ui`, `Interactions`, `CallDepth` | C1, C6, C10 |
| `ActionResult` | `Success()`, `Success(expectedStateId)`, `Failed(code, LocalizedText)`, `Accepted(...)`, `SucceededTask` | C1 |
| `ActionErrorCodes` | static class, nine `const string`: `NOT_CONFIGURED`, `NOT_CONNECTED`, `PERMISSION_DENIED`, `PROVIDER_ERROR`, `PROVIDER_REJECTED`, `INVALID_PARAMETER`, `NOT_FOUND`, `TIMEOUT`, `UNAVAILABLE` | C1 menus |
| `ActionParameterType` | **27** values (String … WidgetTarget) | C10 |
| `LocalizedText` | `FromLiteral`, `FromLocalized`, `Literal`, `IsLocalized`, `op_Implicit(string)`, `op_Implicit(LocalizedString)` | C1, C3 messages |

### A.3 Provider-side surfaces — deliberately not blocks

A plugin *implements* these so Macro Deck can drive **it**. They have no host-side caller, which is why
the blocks that would need them were dropped (§7.16).

| Surface | Implemented by the plugin to | Consequence |
|---|---|---|
| `IMusicPlayerProvider` / `IMusicPlayer` | supply a music player (play, pause, next, seek, volume, shuffle, repeat, `GetStateAsync`, `GetArtworkAsync`) | C4 cannot exist as blocks. `MacroDeck.Sdk.MusicPlayer.Actions` (`MusicPlayerActions`, `MusicPlayerActionDefinition`, `MusicPlayerItemActionDefinition`, `MusicPlayerStateActionDefinition`, `MusicPlayerDeviceActionDefinition`, `MusicPlayerResolver`) is the intended path — Actions-editor work |
| `IIntegrationIssueProvider` | be polled for issues (`GetIssuesAsync`, `ResolveIssueAsync(issueId, ct)`) | `report issue` / `clear issue` dropped |
| `IIconProviderActionDefinition` | supply an action's icon imagery | `set icon from {asset}` dropped |
| `IEventProvider` / `EventDefinition` | declare events (`Id`, `Name`, `Description`, `Category`, `IconName`, `ConfigurationParameters`, `PayloadParameters`, `DeliveryKind`) | event hats belong to Phase 9 targets |
| `IConfigFlowProvider` / `IUiConfigFlowProvider` / `IConfigFlow` | serve setup steps (`ConfigFlowStep`, `ConfigFlowResult`, `ConfigFlowResultKind` = Step/Error/Complete/External, `IOAuthSession`, `IIntegrationConfig`) | the Setup Flow editor owns this; Phase 9 hooks only |
| `IUiProvider` / `IUiSession` / `IUiInteractions` | serve views and dialogs | the Widget Designer owns this |
| `IVariableProvider` / `IVariableSink` | declare and sink variables (`VariableDefinition`, `VariableCatalogPage`, `VariableWriteResult`) | the reason setting a host variable goes through `UserVariables`, not `Variables` |

### A.4 Also inspected, and what they are for

| Surface | Finding |
|---|---|
| `MacroDeck.Sdk.Identity` | `MacroDeckId`, `DeclaredIdValidator`, `QualifiedId`, `LocalIdKind` (Declared, Resource), `OwnerIdKind`, `CapabilityIdConflict`. The SDK has its own id grammar and validator; a future pass could align DeckForge's id rules with `MacroDeckId` instead of maintaining a parallel implementation |
| `MacroDeck.Sdk.Deprecation` | `MacroDeckDeprecatedAttribute`, `MacroDeckSdkUsageAttribute`, `SdkDeprecation`, `SdkDeprecations`. Relevant to Part 22: an SDK bump can deprecate a member a block depends on, and that is the signal to deprecate the block |
| `MacroDeck.Sdk.Logging` | `IntegrationLog.For(integrationId)` → `ILogger`; `MacroDeckIntegrationId` is the property name. The existing injected `_logger` matches |
| `MacroDeck.Sdk.Weather` | `TemperatureUnit`, `WeatherCondition` — provider-side, no blocks |
| `MacroDeck.Sdk.Layouts`, `.Devices`, `.Android`, `.FolderViews`, `.ScreenSavers` | provider-side surfaces; no action callers, so no blocks |
| `MacroDeck.Sdk.Migration` | `IMigrationProvider` — provider-side; the roadmap already lists an MD2 migration editor |
| `MacroDeck.Localization` | `LocalizedString` = `LocalizationKey` + `Arguments`; `MacroDeckStrings.*` static families (`Appearance`, `Common`, `ConfigFlow`, `Connection`, `Settings`, `States`) exist for reuse — §21 should prefer them over new keys, matching the repo's "reuse `MacroDeckStrings.Common.*`" rule |

---

## Appendix B — Semantics table (excerpt; the full table is built in P2)

Both the interpreter and the emitter tests assert against these rows, which is what keeps the two
engines aligned.

| Block | Interpreter | Generated C# |
|---|---|---|
| `repeat {n}` | evaluates `n` once, runs the body `max(0, n)` times, yields per iteration | `for (var i = 0; i < ToNumber(n); i++)` with a cancellation check per iteration |
| `repeat until <c>` | runs the body, then tests; stops when `c` is true | `do { … } while (!ToBool(c));` |
| `while <c>` | tests first; stops when false | `while (ToBool(c)) { … }` |
| `if <c> else` | evaluates `c` once | `if (ToBool(c)) { … } else { … }` |
| `set {v} to {x}` | coerces `x` to the variable's type; on first set adopts the inferred type | `double? v = ToNumber(x);` (or text/bool/list) |
| `change {v} by {n}` | numeric add; text appends when the variable holds text | `v = ToNumber(v) + n;` or `v = ToText(v) + ToText(n);` |
| `wait {n} seconds` | advances the simulated clock by `n`, yields | `await Task.Delay(ms, ct);` |
| `wait until <c>` | polls the condition on each simulated clock tick | `await VisualRuntime.WaitUntilAsync(() => ToBool(c), 50, ct);` |
| `stop this script` | ends the script, no further steps | `return ActionResult.Success();` |
| `throw {m}` | records an error step and ends the trace | `throw new InvalidOperationException(m);` |
| `notify …` | pushes onto the simulated notification stack | `_integration.Notifications.Notify(new UserNotificationRequest { … });` |
| `disable` (modifier) | the block and its body produce no steps | nothing is emitted for the block or its body |
| `call {p}` | pushes a frame, runs the procedure, pops it | `await PAsync(args);` |

---

## Appendix C — Glossary

- **Hat** — a script's entry block; flat top.
- **Target** — the sprite analogue: one artefact that holds scripts.
- **Reporter** — a value-producing oval block.
- **Boolean reporter** — a hexagon predicate.
- **C-block** — wraps a body; `CIf` wraps two; `CChain` wraps many.
- **Cap** — a block that ends a flow.
- **Modifier** — attaches to a block without occupying a stack slot.
- **Gap** — a valid insertion point in a stack; the unit the drop resolver scores.
- **Mouth** — the gap at the top of a C-block's body.
- **Magnet radius** — the pixel distance within which a dragged stack snaps to a gap.
- **Notch / tab** — the connecting geometry that makes a stack read as connected.
- **Region** — the `// <macrodeck-blocks>` span DeckForge owns inside a user's file.
- **Marker contract** — code outside the region belongs to the user and survives regeneration.
- **Verified** — a block whose SDK mapping Appendix A has confirmed.
- **Watch** — a variable surfaced in the debugger; our adaptation of Scratch's *show variable*.
- **BlockSemantics** — the single table both the interpreter and the emitter are tested against.
- **Local function** — the emission strategy for procedures, forced by the sealed action class.

---

## Appendix D — Worked example: fetch and notify

A complete, small program: the canvas, the sidecar, the emitted C#, and the interpreter trace. This
is the fastest way to understand the emission model, and it doubles as the shape of the golden-file
tests in Part 12.

### D.1 The canvas

**Target:** the action `fetch-status` → `FetchStatusAction.cs`.
**Script:** `main`, hat `when action runs`.

```
when action runs
  HTTP GET (parameter url) as response
  if <response is success of response>
    notify Info "Fetched" (response body of response)
  else
    notify Error "Fetch failed" (status code of response)
```

### D.2 The sidecar

Design note: `inputs` is an object **keyed by slot name**, while the in-memory model holds an ordered
array. The order is a property of the block's catalog descriptor, so the array is rebuilt from the
catalog on load. That keeps the JSON readable and diff-stable even when the catalog adds a slot.

```json
{
  "version": 1,
  "targets": [{
    "id": "action:fetch-status",
    "name": "Fetch status",
    "targetKind": "Action",
    "targetFile": "FetchStatusAction.cs",
    "anchorId": "ExecuteAsync",
    "scripts": [{
      "id": "s1", "name": "main", "x": 60, "y": 40,
      "hat": { "kind": "hat.action-runs", "id": "b1" },
      "body": [
        { "kind": "http.get", "id": "b2",
          "fields": { "into": "response" },
          "inputs": { "url": { "slot": { "kind": "parameter.get", "id": "b3",
            "fields": { "name": "url" } } } } },
        { "kind": "control.if-else", "id": "b4",
          "inputs": { "condition": { "slot": { "kind": "http.is-success", "id": "b5",
            "inputs": { "request": { "var": "response" } } } } },
          "bodies": {
            "then": [
              { "kind": "ui.notify", "id": "b6",
                "fields": { "level": "Info", "title": "Fetched" },
                "inputs": { "message": { "slot": { "kind": "http.response-body", "id": "b7",
                  "inputs": { "request": { "var": "response" } } } } } }
            ],
            "else": [
              { "kind": "ui.notify", "id": "b8",
                "fields": { "level": "Error", "title": "Fetch failed" },
                "inputs": { "message": { "slot": { "kind": "http.status-code", "id": "b9",
                  "inputs": { "request": { "var": "response" } } } } } }
            ]
          } }
      ]
    }]
  }],
  "variables": [],
  "lists": [],
  "procedures": []
}
```

### D.3 The generated C#

Splicted into `FetchStatusAction.cs` between the markers, before the method's final top-level
`return`, in the file's own indentation style, by `BlockProgramWriter`'s machinery.

```csharp
// <macrodeck-blocks>
// deckforge-visual: schema=1 doc=c41f7a2 region=sha256:9a1c...
// Generated by DeckForge's Visual canvas. Edit on the canvas; anything outside
// these markers is yours and is preserved across regeneration.

if (_integration is null)
{
    return ActionResult.Failed(ActionErrorCodes.NotConnected, "No host session is established yet.");
}

var _arg0 = context.Parameters.TryGetValue("url", out var _p0) && _p0 is not null ? _p0.ToString() : null;
var response = await VisualRuntime.HttpTextAsync(VisualRuntime.ToText(_arg0), "GET", null, null, context.CancellationToken);

if (VisualRuntime.ToBool(response?.IsSuccess))
{
    _integration.Notifications.Notify(new MacroDeck.Sdk.Notifications.UserNotificationRequest
    {
        Title = "Fetched",
        Message = VisualRuntime.ToText(response?.Body),
    });
}
else
{
    _integration.Notifications.Notify(new MacroDeck.Sdk.Notifications.UserNotificationRequest
    {
        Level = MacroDeck.Sdk.Notifications.UserNotificationLevel.Error,
        Title = "Fetch failed",
        Message = VisualRuntime.ToText(response?.StatusCode),
    });
}
// </macrodeck-blocks>
```

Things to notice, because they are the rules in Part 8.2 restated as output:

1. The host guard appears **once**, not before each call.
2. `parameter.get` follows the existing `_argN`/`_pN` lookup shape the current compiler uses, so the
   generated code stays recognisable to anyone who has read the old engine.
3. Every value passes through `VisualRuntime`, so a text parameter in a status-code position still
   compiles.
4. The notification type is **fully qualified**, because a `using` directive is illegal in the middle
   of a method.
5. Nothing is written outside the markers. The user's own code, above and below, is untouched.

### D.4 The interpreter trace (dry-run tracer)

The user types `url = https://api.example.com/health` into the tracer form and presses Run.

| # | Step | Detail |
|---|---|---|
| 1 | `BlockEntered` | `hat.action-runs` |
| 2 | `ValueComputed` | `parameter url` = `https://api.example.com/health` |
| 3 | `HostCall` | `Http GET https://api.example.com/health` → 200, body 16 bytes — **simulated** |
| 4 | `ValueComputed` | `http.is-success` = `true` |
| 5 | `BlockEntered` | `ui.notify` (then branch) |
| 6 | `HostCall` | `Notifications.Notify` — a toast appears on the simulated deck |
| 7 | `BlockExited` | `control.if-else`; script ends, no explicit finish block |

Re-run with a canned 500 response and step 4 becomes `false`, step 5 becomes the else branch, and the
toast is an error-level notification reading `500`.

---

## Appendix E — Worked example: loop and procedure

Same document shape; only the new parts are shown.

### E.1 The canvas

```
when action runs
  set total to 0
  repeat 3
    change total by 2
  call announce with (total)

define announce (who)
  notify Info "Total" (who)
```

### E.2 The new sidecar parts

```json
"variables": [ { "name": "total", "type": "number", "initial": "0", "scope": "local" } ],

"procedures": [{
  "name": "announce",
  "parameters": [ { "name": "who", "type": "any" } ],
  "returns": null,
  "body": [
    { "kind": "ui.notify", "id": "b20",
      "fields": { "level": "Info", "title": "Total" },
      "inputs": { "message": { "slot": { "kind": "variable.get", "id": "b21",
        "fields": { "name": "who" } } } } }
  ]
}]
```

The body of the script refers to the procedure by name:

```json
{ "kind": "proc.call", "id": "b14",
  "fields": { "name": "announce" },
  "inputs": { "args": [ { "slot": { "kind": "variable.get", "id": "b15",
    "fields": { "name": "total" } } } ] } }
```

### E.3 The generated C#

```csharp
// <macrodeck-blocks>
// deckforge-visual: schema=1 doc=7b90e11 region=sha256:...
// Generated by DeckForge's Visual canvas. Edit on the canvas; anything outside
// these markers is yours and is preserved across regeneration.

if (_integration is null)
{
    return ActionResult.Failed(ActionErrorCodes.NotConnected, "No host session is established yet.");
}

double? total = 0d;
for (var i = 0; i < VisualRuntime.ToNumber(3d); i++)
{
    context.CancellationToken.ThrowIfCancellationRequested();
    total = VisualRuntime.ToNumber(total) + VisualRuntime.ToNumber(2d);
}

await AnnounceAsync(total);

// ---- procedures ----
async Task AnnounceAsync(object? who)
{
    _integration.Notifications.Notify(new MacroDeck.Sdk.Notifications.UserNotificationRequest
    {
        Title = "Total",
        Message = VisualRuntime.ToText(who),
    });
}
// </macrodeck-blocks>
```

Why this shape:

1. **The procedure is a local function**, not a class member, because the action class is `sealed`
   (Part 4.2). It may be declared after its call site — legal C#, and it reads better than a
   forward-declared region.
2. **It is not `static`**, so it closes over `_integration` and `context` without threading them
   through parameters.
3. **The inferred local type is `double?`**, from `set total to 0` followed by `change total by 2`.
   Changing it to text anywhere would flip the type to `string?` and the emitter would switch to
   concatenation, which is exactly the Scratch behaviour.
4. **Cancellation is per iteration**, which is what makes `repeat` safe inside a long action.

### E.4 The interpreter trace, with the watch table

| # | Step | `total` | Note |
|---|---|---|---|
| 1 | `BlockEntered` set | 0 | variable created |
| 2 | `ValueComputed` repeat = 3 | 0 | |
| 3 | iteration 1: change by 2 | 2 | cancellation checked |
| 4 | iteration 2: change by 2 | 4 | |
| 5 | iteration 3: change by 2 | 6 | |
| 6 | `proc.call announce` | 6 | frame pushed |
| 7 | `HostCall` Notify | 6 | simulated toast reading "Total 6" |
| 8 | frame popped | 6 | |

The watch table is populated because `total` was written by a `set` block; a variable only appears in
it when a `watch` block names it or the user adds it, matching Part 7.10.

---

## Appendix F — Diagnostics catalogue

Codes are kebab-case, mirroring the convention the manifest and localization validators already use
(`ManifestValidator` mirrors the CLI's kebab-case ids). Every diagnostic names the block it belongs
to, so the pane can select and centre it.

| Code | Severity | Trigger | Hint shown next to the message |
|---|---|---|---|
| `vis-shape-mismatch` | Error | a statement in a value slot, or a reporter in a stack gap | "Reporter blocks go in the round slots; stack blocks go between blocks." |
| `vis-type-mismatch` | Warning | text in a numeric slot, non-boolean in a predicate | "This will be converted at runtime; the value may not be what you expect." |
| `vis-unbound-slot` | Error | a required slot with no value | "Drop a value or block here, or open the block's fields in the inspector." |
| `vis-menu-key-unknown` | Error | a menu selection not in the block's option list | lists the valid keys |
| `vis-name-duplicate` | Error | two variables, lists or procedures share a name | "Names must be unique; the other one is at <block>." |
| `vis-local-collision` | Error | a local collides with `context`, `_logger`, `_integration`, or a hand-written local | "Rename it, or rename the existing local in your code." |
| `vis-param-unknown` | Warning | `parameter {x}` not declared by the action | "Add it on the Actions page, or pick an existing parameter." |
| `vis-host-var-unknown` | Warning | a host variable name the plugin never declares | links the Variables capability |
| `vis-loop-control-outside-loop` | Error | `break` / `continue` not inside a loop | "Move it inside a repeat, forever, while or repeat until block." |
| `vis-return-outside-procedure` | Error | `return {value}` in a script that is not a procedure | "Use a finish block instead." |
| `vis-forever-no-wait` | Warning | `forever` containing neither a wait nor a host call | "The loop will run as fast as the host allows; add a wait." |
| `vis-cap-unreachable` | Warning | statements after a cap or a `forever` | "It will never run." |
| `vis-capability-missing` | Warning | a block whose capability the plugin does not declare | deep link to the Capabilities page |
| `vis-target-file-missing` | Error | the target file is not in the workspace | "Create the action on the Actions page, or choose another target." |
| `vis-target-anchor-missing` | Error | the splice anchor is not in the file | "This action was edited in a way DeckForge does not recognise; the region cannot be written." |
| `vis-region-conflict` | Error | more than one marker region, or a region owned by another document | "Remove the extra markers, or open this file's other canvas." |
| `vis-procedure-missing` | Error | a `call` naming no procedure | "Define it, or fix the name." |
| `vis-provider-missing` | Error | a block contributed by an extension that is not loaded | "Enable <extension> on the Extensions page." |
| `vis-unverified-block` | Info | a block whose SDK mapping Appendix A has not confirmed | "This block is still being verified; it cannot be saved yet." |
| `vis-newer-schema` | Info | the document is from a newer DeckForge | "This project was made with a newer DeckForge and is open read-only." |
| `vis-opaque-lines` | Warning | round-trip could not recognise some lines | "They are preserved and written back unchanged." |
| `vis-secret-literal` | Warning | a URL or field that looks like it carries a credential | "Use a parameter instead, so the value is not committed to your repository." |
| `vis-step-budget` | Error | the interpreter hit its step budget | "This script may never finish; it was paused." |
| `vis-migration-originals` | Info | legacy sidecars were migrated | "The originals were kept under `.deckforge/visual/legacy/`." |

---

## Appendix G — Undo/redo command catalogue

One gesture is one transaction. The inverse is a real command, not a snapshot, so undo of a large
edit is cheap and a redo restores exactly what the inverse removed.

| Command | Inverse | Coalescing | Produced by |
|---|---|---|---|
| `InsertBlock(body, index, run)` | `DeleteRun(body, index, run.Length)` | none | palette drop, paste, wrap |
| `DeleteRun(body, index, count)` | `InsertBlock(body, index, run)` | none | Delete, cut, drag out |
| `MoveRun(from, fromIndex, to, toIndex, count)` | the same command with the endpoints swapped | none | drag within a stack, `Ctrl+↑`/`↓` |
| `Wrap(blockId, wrapper)` | `Unwrap(blockId)` | none | drop on a mouth, context menu |
| `Unwrap(blockId)` | `Wrap(blockId, wrapper)` | none | Alt+click, context menu |
| `EditField(blockId, name, before, after)` | the same command with an empty `before` and the inverse set | per field, 800ms | typing in a slot |
| `EditMenu(blockId, name, before, after)` | swapped | none | choosing a menu option |
| `BindSlot(blockId, slot, before, after)` | swapped | none | dropping a reporter, clearing a slot |
| `AddScript(target, script)` | `DeleteScript(target, script.Id)` | none | double-click canvas, script strip |
| `DeleteScript(target, scriptId)` | `AddScript(target, script)` | none | script strip context menu |
| `RenameScript(scriptId, before, after)` | swapped | none | inline rename |
| `MoveScript(scriptId, from, to)` | swapped | per drag | dragging on free canvas |
| `AddVariable(name, scope)` | `DeleteVariable(name)` | none | inspector, canvas menu |
| `RenameVariable(before, after)` | swapped, plus every `variable.get` and `variable.set` that named it | none | inline rename |
| `DeleteVariable(name)` | `AddVariable` plus the removed uses | none | inspector |
| `DefineProcedure(name, params)` | `DeleteProcedure(name)` | none | define hat |
| `EditProcedureSignature(before, after)` | swapped, plus call-site arity fixes | none | the define hat's slots |
| `SetHat(scriptId, before, after)` | swapped | none | the hat's menu |
| `ToggleDisable(blockId)` | the same command | none | gutter click, context menu |
| `SetBreakpoint(blockId)` | `ClearBreakpoint(blockId)` | none | stage gutter — **not** part of document undo |

Rules:

1. Breakpoints, watch entries, zoom and scroll are **not** undoable document state; undoing a drag must
   not move the viewport.
2. `RenameVariable` is the only command that touches more than its own subtree, and it is tested
   explicitly because a rename that misses a use is the exact defect the current engine refuses to
   risk by renaming at all.
3. A rejected command (a cyclic move, a duplicate name) is not pushed onto the stack and reports a
   diagnostic instead.
4. The stack is per document and cleared on workspace change, not shared across documents.

---

## Appendix H — Performance budget

| Operation | Target | How it is measured |
|---|---|---|
| Drag a stack, 500 visible tiles | 60fps, no frame over 20ms | a frame-time log from the user-driver harness, p99 reported |
| Drop resolution | under 2ms per pointer move | a micro-benchmark over the resolver with a worst-case candidate set |
| Layout of one script, 200 blocks | under 8ms cold, under 0.5ms cached | a stopwatch test around `StackLayout` |
| Live C# preview regeneration, 200 blocks | under 60ms, debounced | a stopwatch test around the emitter |
| Save, 200 blocks across 3 targets | under 250ms wall clock | an end-to-end test against a scaffolded plugin |
| Open a 5-script document | under 400ms | a stopwatch test around load plus layout |
| Palette filter, 187 blocks | under 30ms per keystroke | a micro-benchmark with a three-character query |
| Memory, one document open | under 60MB above the idle application | a working-set measurement in the harness |
| Export PNG at 2× | under 400ms for a 200-block script | a stopwatch test |

Regression policy: a phase that misses a budget does not ship that phase's exit criteria until the
number is either met or the budget is amended here with the reason. Amending the budget silently is
how a "fast" editor becomes a slow one.

---

## Appendix I — Contributor review checklist

What a reviewer checks on any pull request that touches Visual.

**Model and catalog**

1. A new block arrived as: a catalog row, an emitter, an interpreter step, a palette category, a docs
   path, and a test. A block missing any of the six is incomplete.
2. The block's `IsVerified` flag matches Appendix A. A row marked verified with no inventory entry is
   a blocking review comment.
3. A block id is new, permanent, namespaced and never a rename of an existing id.
4. No existing block's meaning changed. A behaviour change is a new id plus a deprecation entry
   (Part 22.2).

**Code generation**

5. Generated text is warning-free, deterministic, and inside the markers.
6. Any new runtime helper is referenced by an emitter and covered by the runtime surface test.
7. A new block has a compile test that writes into `src/<Project>/` and asserts the build saw the file
   — not merely that the text looks right.
8. Nothing in the emitter reads a setting, a clock, or the file system. Emission is a pure function of
   the document.

**UI**

9. Markup passes `XamlMarkupTests`: real `SymbolRegular` members, no method bindings, no grid cell
   beyond the definitions, fixed columns within the 900px budget, no attribute value split across
   lines.
10. Every colour is a `Liquid.*` token; no literals in markup or code-behind.
11. New interactions appear in Part 18, and focus precedence rules are respected (a focused field wins
   over single-key shortcuts).
12. Anything animated is skipped or collapsed under reduced motion (Part 19).
13. New strings that a user reads are localizable, and any new resx key segment passes through
   `CSharpCode.ResxSegment`.

**Durability**

14. Nothing can lose data: no save path reports success before every step lands, opening never
   rewrites, an unknown kind becomes a placeholder with a diagnostic, and a newer schema opens
   read-only.
15. A migration step has a fixture captured from a real pre-migration document, and a test asserting
   the fixture converts and still compiles.
16. The change is reflected in `visual.md` in the same pull request: catalog counts, the diagnostics
   table, the settings table or the budget, whichever the change touched.

**Honesty**

17. Any limitation the change introduces is stated in the document, not discovered later. This feature's
   credibility rests on the document being right about what it does not do.

---

## Change log

| Date | Change |
|---|---|
| 2026-09-29 | Initial design: analysis, decisions, model, 187-block catalog, codegen, UI, simulator, testing, ten-phase plan, file manifest, SDK-inventory gate. |
| 2026-09-29 | **P1a.** Added the Core document model (§6.2.1 records what changed and why): `BlockShapes.cs`, `Block.cs`, `VisualProject.cs`, `VisualProjectJson.cs`, `Migrations/BlocksV1Migration.cs`, and the two test files. Design changes recorded: an unknown kind needs no placeholder type; the document's variable vocabulary is the SDK's; menu keys are identifiers. Two defects found by the tests are written down in §6.2.1. |
| 2026-09-29 | **P0 complete.** Added the progress tracker and baseline. Added `tools/SdkInventory` and committed the assembly dumps under `tools/SdkInventory/surface/`. Filled Appendix A (§A.1 the ten members of `IIntegrationContext`, §A.2 per-surface members, §A.3 provider-side surfaces deliberately not blocks, §A.4 also-inspected). Added §7.16 with verdicts on every ⚠: **153 ship, 6 deferred, 7 discovered, 28 dropped**. Added §16.1 answers. Recorded into the design: C4 media cannot be blocks at all, issue reporting and event acknowledgement do not exist, host variable read and write are different APIs, `ActionErrorCodes` is a static class of string constants, `LocalizedText` converts from a raw string, and the "29 editor types" claim should read 27. |
| 2026-09-29 | Extended: table of contents and reading order; Part 17 gap analysis against Scratch; Part 18 full interaction and keyboard specification; Part 19 motion and animation; Part 20 settings; Part 21 block-label localization and RTL; Part 22 versioning and deprecation policy; Part 23 the third-party block-provider contract; Part 24 round-trip from existing code; Part 25 `.dfblock`, clipboard, PNG and SVG export; Part 26 Scratch `.sb3` import design; Part 27 safety, secrets and threat model; Part 28 acceptance criteria and metrics; Appendices D–E worked examples with sidecar JSON, generated C# and interpreter traces; Appendix F diagnostics catalogue; Appendix G undo/redo command catalogue; Appendix H performance budget; Appendix I contributor review checklist. |
| 2026-10-03 | **P10g and the documentation sweep.** `BlockLabelKeys` gives §21.1's keys a home in Core with an English fallback, so a build with no localization loaded is unremarkable and a rename cannot orphan a translation. §15 brought in line with what shipped, including the files that were planned and deliberately not written. |
| 2026-10-03 | **P10g–P10h and the documentation sweep.** `BlockLabelKeys` gives §21.1's keys a home in Core and `ResxTextLookup` reads them out of the workspace's own `Strings.resx`, so a translator fills in one row and the block says it — with the catalogue's English as the default, because a build with no localization loaded is the ordinary case. §15 brought in line with what shipped, including the planned files that were deliberately not written and the two duplicate table headers this document had grown. |