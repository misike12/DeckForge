# Fix plan

Every defect, flaw and visual problem found by the five-lane audit of 2026-10-03, and what was done about
each. Lanes reported; every fix here was written by hand afterwards, so each finding and each fix are two
separate acts and can be checked against each other. Status is updated as work lands.

## Rules of the audit

- Five lanes ran in parallel. **Only Lane D opened or drove the application**; the other four read code and
  ran headless commands, so two agents never competed for the window or the mouse.
- **No lane edited anything.**
- A finding is only closed when the fix is in the tree, the build is clean and the full suite is green.
  Anything checked in the window is recorded as checked in the window.
- Lane D's own crash-log entry turned out to pre-date the fix; and Lane D reported that the harness's
  `GetCurrentThreadId` P/Invoke was wrong (`user32.dll` instead of `kernel32.dll`), which had been making
  clicks and hotkeys look dead. That was the harness, not the product — see "Closed without a change".

## Status key

| Mark | Meaning |
|---|---|
| **F** | Fixed, with a test or by construction |
| **F+V** | Fixed and verified by driving the real window |
| **X** | Documentation only — the code is fine, the document was wrong |
| **N** | Not reproduced when checked by hand; no code change |
| **D** | Deferred, with the reason recorded |

## Baseline

| | |
|---|---|
| Build | 0 errors, 9 unique warnings (5 introduced by the last three commits) |
| Suite | 848 passed, 0 failed, 1 never executed; 849 discovered |
| Wall clock | ~68 s |

---

## 1. Blockers

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| C1 | blocker | Renaming a procedure onto another procedure's name is accepted, and the next rebuild throws `ArgumentException` out of a WPF handler — the canvas then cannot be opened, edited or saved without hand-editing `canvas.json` | `Runtime/ScriptInterpreter.cs:65`, `DocumentEditor.cs:383`, `VisualEditorViewModel.cs:491` | Refuse the rename in `DocumentEditor.EditProcedure`; make the interpreter's procedure projection tolerant so a file with duplicates opens and is *reported* | F |
| B1 | blocker | A malformed `Strings*.resx` still throws `XmlException` out of the Localization page; the crash handler exits the app after three faults. Three layers, three policies, none reporting anything | `LocalizationManagerViewModel.cs:141`, `ResxMerger.cs:134`, `ValidatorParityTests.cs:96`, `OpenFindingsTests.cs:138` | Make an unreadable resx a reported value, not an exception, at all three layers; rename the two tests to what they assert | F |
| D1 | blocker | Below ~1200×800 the whole Stage panel is clipped by the window bottom and the page does not scroll — you cannot run anything | `Pages/VisualEditorPage.xaml` | Let the editor+stage rows scroll vertically | F+V |
| D6 | blocker (likely) | Drag and drop never changes the document: the ghost and the indicator render, but nothing is inserted, moved or removed, and Undo stays disabled | `Controls/Blocks/BlockWorkspace.xaml.cs` | Make the indicator and the commit share one coordinate mapping | F+V |

## 2. Bugs

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| C2 | major | `TileAt` returns the *outermost* tile under the pointer, not the innermost — clicking a nested reporter selects the statement that owns it | `Controls/Blocks/CanvasHitTest.cs:45` | Return the first match walking up | F |
| C3 | major | Step and Step-into re-arm the transport timer, so one press runs the whole script while the outcome still says "Paused" | `Runtime/StageSession.cs:468` | Do not re-arm after an interactive tick | F |
| C4 | major | Step/Step-into run the *first* script, ignoring the picker; `Run` does it correctly | `Runtime/StageSession.cs:449` | Pass the resolved script into the interpreter's reset | F |
| C5 | major | The drag ghost is drawn at the canvas origin; the accumulated `top` and the resolution's `SnapX` are computed and never used | `Controls/Blocks/DragAdorner.cs:97` | Push a translate for the snap point and drop `top` | F |
| C6 | major | `Minimap.For` flips the pan sign, applies the zoom twice and clamps in the wrong unit, so the viewport rectangle leaves the box | `Core/Visual/CanvasView.cs:197` | Correct the algebra; clamp the emitted rect; test a wide document and a non-zero pan | F |
| C7 | major | The stage's parameter boxes bind two-way to an immutable record, so the form can never supply a value and `SetParameter` has no caller in the app | `Controls/Blocks/StagePanel.xaml:210`, `Runtime/StageSession.cs:155` | Make the row a mutable view model whose setter calls `SetParameter` | F |
| C8 | major | Every keystroke in a slot field rebuilds the whole inspector row collection, destroying the focused `TextBox` — the field eats every character | `ViewModels/Visual/InspectorViewModel.cs:238` | Rebuild rows only when the set of slot names changes; update in place otherwise | F |
| B5 | major | `BlockProgramWriter` cannot write the same action twice — its anchor stops matching its own output — and the two tests claiming to cover a double save cannot fail | `CodeGen/Generation/BlockProgramWriter.cs:42`, `BlockCompiler.cs:375` | Use the same anchor as `VisualProgramWriter` (one shared constant); assert `Success` on the second write | F |
| B9 | major | A new procedure's id is `proc{N+1}`, which collides after any deletion; a block dropped into the visible procedure lands in the other one | `ViewModels/Visual/VisualEditorViewModel.cs:691`, `Core/Visual/VisualProject.cs:279` | Add `NextProcedureId()` beside `NextBlockId` and use it in both places | F |
| B6 | major | The palette is bound to whichever workspace was open when the singleton was built; after switching, pins and recents belong to the old workspace | `VisualEditorViewModel.cs:46`, `PaletteViewModel.cs:64` | Rebind the palette on a workspace change | F |
| B2 | major | `Validation` is permanently the *sample project's* context, so parameter and host-variable diagnostics are wrong on every other document | `VisualEditorViewModel.cs:42,64` | Build it from the workspace; default to `Empty` until then | F |
| B3 | major | Pan and zoom are four `public static event`s subscribed with lambdas that can never be removed — every re-created page doubles the zoom | `BlockWorkspace.xaml.cs:272`, `VisualEditorPage.xaml.cs:71` | Make them instance events; `+=` on Loaded, `-=` on Unloaded | F |
| B4/E2 | major | The performance budget's "cached layout" half measures a hand-written loop no product code runs, and `repeats` divides a result `Time` already averaged | `Core/Visual/CanvasPerformanceBudget.cs:91`, tests `:116` | Measure the real path (a measured-height dictionary fed to `StackLayout.LayoutRun`), or report the number honestly; make `repeats` loop rather than divide | F |
| E1 | major | `ProcessResult.Combined` is documented as stdout and stderr "in the order the child wrote them"; two pipes and two threads cannot do that. The test fails **55%** of isolated runs — a real defect the green suite hides | `CliAdapter/Processes/ProcessRunner.cs:8,120`, `ProcessRunnerTests.cs:70` | Keep the ordering that is real (per stream), say so, and test that | F |
| C9 | minor | `Label` null-forgives an unknown nested kind, so a newer `canvas.json` throws out of the timer callback and takes the app down | `Runtime/ScriptInterpreter.cs:978` | Fall back to the kind, as three sibling readers already do | F |
| C10 | minor | Label holes are filled from `Inputs` only, so a menu hole renders as `{level}` in the trace | `Runtime/ScriptInterpreter.cs:975` | Fill from `Fields` through `BlockLabel.MenuText` | F |
| C11 | minor | `ui.log` writes every entry at `Information` while the trace reports the level the block chose | `Runtime/ScriptInterpreter.cs:752` | `Host.Log.Write(LevelOf(level), line)` | F |
| C13 | minor | Single-script SVG export draws rects at `+Margin` but labels at `+0`, so every label sits in the gutter | `Core/Visual/SvgRenderer.cs:53` | Pass `Margin` to the label writer too, as the document path does | F |
| C14 | minor | The zoom buttons change the view without redrawing the minimap, so the thumbnail goes stale | `Pages/VisualEditorPage.xaml.cs:681` | Redraw on every zoom command | F |
| C15 | minor | `SlotValue.From` writes both `Variable` and `Text` into one `BlockInput`, which Core's own loader classifies as malformed | `Core/Visual/SlotValue.cs:114` | Set one member | F |
| C16 | minor | `ScriptStrip.Name_KeyDown` focuses a `TextBox` the rename has already detached, so the keyboard goes nowhere after Enter | `Controls/Blocks/ScriptStrip.xaml.cs:67` | Re-resolve the row after the rebuild | D |
| C17 | minor | A canvas from a newer schema is reported "will not be overwritten", then the page shows the sample with Save enabled — and Save overwrites it | `Core/Visual/VisualProjectJson.cs:145`, `VisualStore.cs:255` | Carry a read-only outcome through the store; refuse to save | D |
| C19 | minor | Filtering the palette rebuilds every row without re-stating the pin stars, so a pinned block's star goes dark | `PaletteViewModel.cs:252`, `PaletteList.xaml.cs:60` | Refresh the stars after a refilter | F |
| C20 | minor | The speed slider starts at 0 while the session runs at 8, so the panel and the run disagree until the slider is touched | `ViewModels/Visual/StageViewModel.cs:33` | Initialise from the session | F |
| C23 | nit | `Reset` does not reseed the `Random`, so two runs of one document produce different traces — against the class's stated determinism | `Runtime/ScriptInterpreter.cs:226` | Reseed in `Reset` | F |
| C25 | nit | The migration message names a backup file nothing writes | `Core/Visual/VisualStore.cs:253` | Name the file that is actually kept | F |
| C22 | nit | The benchmark reports the requested block count, which its own synthetic document does not always produce | `CanvasPerformanceBudget.cs:115` | Count the blocks it built | F |
| C24 | nit | `BlockCatalog.Category` throws for `Media`, which has no row, and `BlockContrastRules.For` calls it unconditionally | `BlockCatalog.cs:255` | Return a neutral descriptor | N |

## 3. Visual and interaction, found only by driving the window

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| D2 | major | The palette list does not scroll: ~4 of 28 Control blocks are reachable, the wheel does nothing, no scrollbar. Most blocks are unreachable by mouse | `Pages/VisualEditorPage.xaml`, palette region | Put the palette in a scroll viewer; find whatever is swallowing the wheel | F+V |
| D5 | major | Zooming out blanks the canvas, and "Fit" reports a zoom it does not apply — content is pinned to the right edge and sliced | `VisualEditorPage.xaml.cs` zoom handlers | Scale about the viewport centre; centre the fitted bounds | F+V |
| D4 | major | The minimap draws over the canvas; clicking it jumps zoom 100%→25% and paints over the scripts panel | `Controls/Blocks/CanvasMinimap.*` | Scale labels with the map, keep it in its cell, and let a click set pan only | F+V |
| D7 | major | The inspector's numeric stepper +/− do nothing | inspector editor template | Re-checked by clicking it with a block selected: it is wired (`Slot_Increment` -> `Number` -> `Text` -> commit), and the earlier run had selected nothing, so the click landed on the diagnostics pane. No change needed | N |
| D8 | major | The command palette and the shortcut sheet have no mouse entry point at all | `Pages/VisualEditorPage.xaml:248` | A header button and a rail entry | F+V |
| D3 | minor | Palette previews clip their last input ("tex" for "text") | palette tile sizing | Size the preview to the block | N |
| D9 | minor | A spurious horizontal scrollbar in the scripts list; its last entry is clipped | `ScriptStrip.xaml` | Wrap item text, drop the horizontal scroller | X |
| D11 | minor | The trace pane does not scroll and slices its last line — the line naming the block that stopped the run | `StagePanel.xaml` | A scroll viewer with auto-scroll to end | N |
| D12 | minor | The speed slider does not respond to dragging | `StagePanel.xaml` | Bind it properly | N |
| D13 | minor | With no workspace open, Save / Revert / Export SVG give no feedback at all — indistinguishable from a broken button | header | Disable with a reason | F |
| D14 | minor | A rejected drop's hint permanently replaces the document summary line and reflows the page | `VisualEditorPage.xaml` header row | Make it transient | N |
| D15 | minor | A nav rail item is clipped at 960×700 | `MainWindow.xaml` | Scroll or collapse the rail earlier | N |
| D16 | minor | The category rail and the inspector slice their last line at rest | rail, inspector | Bottom padding or a fade | N |
| D17 | nit | A brand-new canvas opens with 15 identical `"False" is not a condition` warnings | `Core/Visual/VisualSampleProject.cs` | Give the sample real boolean conditions | D |
| D18 | nit | The header hint paragraph is crushed into five lines when width is scarce | header | Truncate or move it | D |

## 4. Poor code

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| B7/A5 | major | Appendix F's diagnostics table and the test enforcing it have both drifted: 10 listed codes are emitted nowhere, 14 emitted codes are unlisted, and a comment claims two live in CodeGen (which builds no diagnostics) | `visual.md` Appendix F, `VisualValidatorTests.cs:502` | Reconcile the table both ways; make the test read the table | D |
| A1 | major | §28 is the "definition of done" and lists capabilities that do not exist (PNG, `.dfblock`, clipboard, legacy import, round-trip) while the tracker says all ten phases are complete | `visual.md §28` | A §28.5 "criteria not met" table | X |
| A3 | major | The manifest is wrong in both directions: 4 listed paths do not exist, 10 shipped files are unlisted | `visual.md §15` | Reconcile | X |
| A6 | major | The document says the catalogue is 187 blocks in six places; it is 160, and §7 has no as-built table although a test's failure message points the reader to one | `visual.md §7, §22` | An as-built table; fix the present-tense numbers | X |
| A4 | major | §20 specifies 16 settings keys; none exist. §19's motion table is fiction — there are no durations and no animations | `visual.md §19, §20` | Mark as design, unassigned | X |
| A10 | major | A cluster of promised affordances has no implementation: clipboard chords, all three context menus, rubber-band selection, the header target picker, tile automation peers, the transport's Pause | `visual.md §18` | An as-built §18.6 | X |
| A7 | major | §11 specifies three files; the implementation writes one plus a backup and a palette file, and a dead constant still carries the old name | `visual.md §11`, `VisualProjectJson.cs:47` | Rewrite §11 as built; delete the dead constant | X |
| A8 | major | Three acceptance criteria — 60fps drag, 4.5:1 computed contrast, reduced motion — have no measurement, no test, and for motion no implementation | `visual.md §9.9, §28.2, Appendix H` | State the scope of what was measured | X |
| A9 | major | §27.1 says real network is available behind a per-session confirmation in the stage menu; no control calls `AllowRealNetwork` | `visual.md §27.1` | As-built note | X |
| A11 | major | The PNG deferral is honest in the tracker and presented as delivered in four other places | `visual.md §9.7, §18.5, §25, §28.1` | Mark each | X |
| A12 | major | The promised ~40-document compile corpus and the golden-file snapshots do not exist; the test file named to hold them does not exist | `visual.md §12, §13 P2, §15` | Say what shipped | X |
| A2 | blocker | The header says "pre-implementation", the "current position" line says P10 is mid-flight at 820 green, and the tracker says complete at 848 | `visual.md:3, :72, :91` | Make the top of the file agree with the table | X |
| B8 | major | `CapabilityId` is on every row, read by two tests, and justified by a warning nothing emits | `BlockCatalog.cs:27` | Emit `vis-capability-missing` or delete the field | D |
| B10 | minor | `heightOf` is honoured for leaves and ignored for every container, though the design credits it with the warm/cold budget | `Core/Visual/StackLayout.cs:164` | Thread it through, or delete it and its promise | D |
| B11 | minor | "Where every block is" is implemented twice and they disagree about `NotchY`; three members have no caller | `CanvasHitTest.cs:88,120,148`, `BlockWorkspace.xaml.cs:202` | One traversal; delete the dead members | D |
| B12 | minor | The facade whose stated job is to stop view models reaching past it omits `RemoveKey`, the one destructive mutation | `App/Services/ResxMergerService.cs` | Add it, or say why not | F |
| B13 | minor | `BlockWorkspace`'s class doc says zoom and pan are "not here yet" on a class that implements both | `BlockWorkspace.xaml.cs:15` | Rewrite the header | D |
| B14 | minor | Two XML doc blocks stacked on one member, so `Refuse` and `Refresh` are undocumented | `DocumentEditor.cs:464`, `BlockWorkspace.xaml.cs:492` | Move them | D |
| B15 | minor | Six public members have no caller and four of them document one; `ResxTextLookup.Reload`'s promise (a saved translation reaches the canvas) is not kept | several | Wire the one that is a real behaviour; delete the rest | D |
| B16 | minor | `ValidateReachability` checks no reachability; `vis-name-duplicate` is used for an undeclared name; `StackWalk.Push` takes a parameter it never reads | `VisualValidator.cs:733,844,964` | Rename, split the code, drop the parameter | D |
| B17 | minor | `DropPlan` returns six refusal codes in a namespace nothing documents, and its undo label disagrees with the keyboard path's | `Core/Visual/DropPlan.cs` | Prefix and document; share the label | D |
| B18 | minor | Three generators catch bare `Exception`, reduce it to a status line and log nothing, making a product bug indistinguishable from bad input | `ActionsEditorViewModel.cs:310` and two others | Narrow, and log | D |
| B19 | nit | `ReapplyTheme` opens with a null guard on a non-nullable parameter | `App.xaml.cs:185` | Delete the guard | D |
| B20 | nit | The save-failure message says nothing on disk changed, but the backup has already been overwritten | `Core/Visual/VisualStore.cs:105` | Reorder, or name the backup | D |

## 5. Tests

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| E3 | major | `Every_glyph_is_one_the_icon_library_actually_defines` calls `Assembly.Load("DeckForge.App")`, which the test project cannot load, so it is skipped forever — and the summary line reports 848/848 with zero skips while 849 were discovered | `CapabilityCatalogTests.cs:21` | Load the WPF-UI assembly the project already references; delete the `Assume` | F |
| E4 | major | Two `XamlEventWiringTests` pass vacuously when the source tree is not found | `XamlEventWiringTests.cs:53` | Assert the tree was found | F |
| E8 | minor | `PacedTrace : List<ExecutionStep>` hides `Add` without `new`, and `AddRange`/`Insert` bypass the observer — against its own remark. Zero test references | `ScriptInterpreter.cs:1000` | Stop inheriting; add a test | F |
| E10 | minor | Nine unique warnings, five introduced by the last three commits — including a possibly-null dereference and an unused field in the App, and two null-forgiving `.First()` calls in the new tests | several | Fix all of them | F |
| E5 | minor | An SVG test asserts two independent substrings, so an `x` from one rect and a `y` from another satisfy it | `SvgRendererTests.cs:86` | Parse and compare per element | D |
| E6 | minor | `BlockContrastRules.StrokeWidthFor` has zero coverage | `BlockContrastRules.cs:53` | A three-case test, or make it private | D |
| E7 | minor | `BlockLabel.MenuText`'s default-value branch is never executed | `BlockLabel.cs:185` | Two assertions | D |
| E9 | minor | Ten duplicated temp-directory fixtures, three teardown behaviours, six of which throw on a leaked handle | ten files | One `TempDirectory` helper | D |
| E11 | nit | An invariant culture set in `OneTimeSetUp` is never restored | `SvgRendererTests.cs:20` | Restore it | D |
| E12 | nit | `BlockLabel.Plan(descriptor, lookup)` recurses into itself and terminates only because key derivation happens not to depend on the label | `BlockLabel.cs:134` | An `if` | F |
| E13 | nit | A dead `result < 0` arm in the benchmark; `SelectionChanged` listed twice in the new test | `CanvasPerformanceBudget.cs:136`, `XamlEventWiringTests.cs:42` | Delete | F |
| E9b | minor | `PaletteStore.Save`'s failure return is never exercised | `PaletteStore.cs:87` | A read-only-directory test | D |

## 6. Closed without a change

| ID | Finding | Why it stays |
|---|---|---|
| D-x | "Nav clicks are dead in some sessions" | The harness's `GetCurrentThreadId` P/Invoke named `user32.dll` instead of `kernel32.dll`, so no click ever took the foreground. Fixed in the harness; the symptom went with it |
| A-x | `crash.log` `ViewRequested` entry | Predates the fix; the defect class now has a test |
| E-x | The known-flaky transcript test | Not a flake: a real contract that cannot be kept (E1). It passes in full runs and fails 55% in isolation, which is worse than either |
| — | Keyboard-only editing, palette search, text entry | Unverifiable from the harness — synthesised characters do not arrive. Recorded, not fixed, not guessed at |