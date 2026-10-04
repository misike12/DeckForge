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
| C16 | minor | `ScriptStrip.Name_KeyDown` focuses a `TextBox` the rename has already detached, so the keyboard goes nowhere after Enter | `Controls/Blocks/ScriptStrip.xaml.cs:67` | Re-resolve the row after the rebuild | F |
| C17 | minor | A canvas from a newer schema is reported "will not be overwritten", then the page shows the sample with Save enabled — and Save overwrites it | `Core/Visual/VisualProjectJson.cs:145`, `VisualStore.cs:255` | Carry a read-only outcome through the store; refuse to save | F |
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
| D9 | minor | A spurious horizontal scrollbar in the scripts list; its last entry is clipped | `ScriptStrip.xaml` | Wrap item text, drop the horizontal scroller | N |
| D11 | minor | The trace pane does not scroll and slices its last line — the line naming the block that stopped the run | `StagePanel.xaml` | A scroll viewer with auto-scroll to end | N |
| D12 | minor | The speed slider does not respond to dragging | `StagePanel.xaml` | Bind it properly | N |
| D13 | minor | With no workspace open, Save / Revert / Export SVG give no feedback at all — indistinguishable from a broken button | header | Disable with a reason | F |
| D14 | minor | A rejected drop's hint permanently replaces the document summary line and reflows the page | `VisualEditorPage.xaml` header row | The dedicated refusal line already fixed the reflow; refusals now also auto-clear after six seconds | F |
| D15 | minor | A nav rail item is clipped at 960x700 | `MainWindow.xaml` | Not a defect: the pane scrolls, with a visible scrollbar | N |
| D16 | minor | The category rail and the inspector slice their last line at rest | rail, inspector | Not a defect: both scroll, and both were seen to scroll | N |
| D17 | nit | A brand-new canvas opens with 15 identical `"False" is not a condition` warnings | `Core/Visual/VisualValidator.cs:481` | A boolean *literal* is a condition, and `ScriptInterpreter` reads it as one. Warn only when the slot holds text or a number | F |
| D18 | nit | The header hint paragraph is crushed when width is scarce | `VisualEditorPage.xaml`, `.xaml.cs` | Two faults, not one: the header wrapped instead of trimming, and the narrow layout had no trailing star column, so everything spanning the grid was 610px inside a 930px viewport | F |

## 4. Poor code

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| B7/A5 | major | Appendix F's diagnostics table and the test enforcing it have both drifted: 10 listed codes are emitted nowhere, 14 emitted codes are unlisted, and a comment claims two live in CodeGen (which builds no diagnostics) | `visual.md` Appendix F, `VisualValidatorTests.cs:502` | Reconcile the table both ways; make the test read the table | F |
| A1 | major | §28 is the "definition of done" and lists capabilities that do not exist (PNG, `.dfblock`, clipboard, legacy import, round-trip) while the tracker says all ten phases are complete | `visual.md §28` | A §28.5 "criteria not met" table | F |
| A3 | major | The manifest is wrong in both directions: 4 listed paths do not exist, 10 shipped files are unlisted | `visual.md §15` | Reconcile | F |
| A6 | major | The document says the catalogue is 187 blocks in six places; it is 160, and §7 has no as-built table although a test's failure message points the reader to one | `visual.md §7, §22` | An as-built table; fix the present-tense numbers | F |
| A4 | major | §20 specifies 16 settings keys; none exist. §19's motion table is fiction — there are no durations and no animations | `visual.md §19, §20` | Mark as design, unassigned | F |
| A10 | major | A cluster of promised affordances has no implementation: clipboard chords, all three context menus, rubber-band selection, the header target picker, tile automation peers, the transport's Pause | `visual.md §18` | An as-built §18.6 | F |
| A7 | major | §11 specifies three files; the implementation writes one plus a backup and a palette file, and a dead constant still carries the old name | `visual.md §11`, `VisualProjectJson.cs:47` | Rewrite §11 as built; delete the dead constant | F |
| A8 | major | Three acceptance criteria — 60fps drag, 4.5:1 computed contrast, reduced motion — have no measurement, no test, and for motion no implementation | `visual.md §9.9, §28.2, Appendix H` | State the scope of what was measured | F |
| A9 | major | §27.1 says real network is available behind a per-session confirmation in the stage menu; no control calls `AllowRealNetwork` | `visual.md §27.1` | As-built note | F |
| A11 | major | The PNG deferral is honest in the tracker and presented as delivered in four other places | `visual.md §9.7, §18.5, §25, §28.1` | Mark each | F |
| A12 | major | The promised ~40-document compile corpus and the golden-file snapshots do not exist; the test file named to hold them does not exist | `visual.md §12, §13 P2, §15` | Say what shipped | F |
| A2 | blocker | The header says "pre-implementation", the "current position" line says P10 is mid-flight at 820 green, and the tracker says complete at 848 | `visual.md:3, :72, :91` | Make the top of the file agree with the table | F |
| B8 | major | `CapabilityId` is on every row, read by two tests, and justified by a warning nothing emits | `BlockCatalog.cs:27` | Emit `vis-capability-missing` or delete the field | F |
| B10 | minor | `heightOf` is honoured for leaves and ignored for every container, though the design credits it with the warm/cold budget | `Core/Visual/StackLayout.cs:164` | Thread it through, or delete it and its promise | F |
| B11 | minor | "Where every block is" is implemented twice and they disagree about `NotchY`; three members have no caller | `CanvasHitTest.cs:88,120,148`, `BlockWorkspace.xaml.cs:202` | One traversal; delete the dead members | F |
| B12 | minor | The facade whose stated job is to stop view models reaching past it omits `RemoveKey`, the one destructive mutation | `App/Services/ResxMergerService.cs` | Add it, or say why not | F |
| B13 | minor | `BlockWorkspace`'s class doc says zoom and pan are "not here yet" on a class that implements both | `BlockWorkspace.xaml.cs:15` | Rewrite the header | F |
| B14 | minor | Two XML doc blocks stacked on one member, so `Refuse` and `Refresh` are undocumented | `DocumentEditor.cs:464`, `BlockWorkspace.xaml.cs:492` | Move them | F |
| B15 | minor | Six public members have no caller and four of them document one; `ResxTextLookup.Reload`'s promise (a saved translation reaches the canvas) is not kept | several | Wire the one that is a real behaviour; delete the rest | X |
| B16 | minor | `ValidateReachability` checks no reachability; `vis-name-duplicate` is used for an undeclared name; `StackWalk.Push` takes a parameter it never reads | `VisualValidator.cs:733,844,964` | Rename, split the code, drop the parameter | F |
| B17 | minor | `DropPlan` returns six refusal codes in a namespace nothing documents, and its undo label disagrees with the keyboard path's | `Core/Visual/DropPlan.cs` | Prefix and document; share the label | F |
| B18 | minor | Three generators catch bare `Exception`, reduce it to a status line and log nothing, making a product bug indistinguishable from bad input | `ActionsEditorViewModel.cs:310` and two others | Narrow, and log | F |
| B19 | nit | `ReapplyTheme` opens with a null guard on a non-nullable parameter | `App.xaml.cs:185` | Delete the guard | F |
| B20 | nit | The save-failure message says nothing on disk changed, but the backup has already been overwritten | `Core/Visual/VisualStore.cs:105` | Reorder, or name the backup | F |

## 5. Tests

| ID | Severity | Finding | Where | Fix | Status |
|---|---|---|---|---|---|
| E3 | major | `Every_glyph_is_one_the_icon_library_actually_defines` calls `Assembly.Load("DeckForge.App")`, which the test project cannot load, so it is skipped forever — and the summary line reports 848/848 with zero skips while 849 were discovered | `CapabilityCatalogTests.cs:21` | Load the WPF-UI assembly the project already references; delete the `Assume` | F |
| E4 | major | Two `XamlEventWiringTests` pass vacuously when the source tree is not found | `XamlEventWiringTests.cs:53` | Assert the tree was found | F |
| E8 | minor | `PacedTrace : List<ExecutionStep>` hides `Add` without `new`, and `AddRange`/`Insert` bypass the observer — against its own remark. Zero test references | `ScriptInterpreter.cs:1000` | Stop inheriting; add a test | F |
| E10 | minor | Nine unique warnings, five introduced by the last three commits — including a possibly-null dereference and an unused field in the App, and two null-forgiving `.First()` calls in the new tests | several | Fix all of them | F |
| E5 | minor | An SVG test asserts two independent substrings, so an `x` from one rect and a `y` from another satisfy it | `SvgRendererTests.cs:86` | Parse and compare per element | F |
| E6 | minor | `BlockContrastRules.StrokeWidthFor` has zero coverage | `BlockContrastRules.cs:53` | A three-case test, or make it private | F |
| E7 | minor | `BlockLabel.MenuText`'s default-value branch is never executed | `BlockLabel.cs:185` | Two assertions | F |
| E9 | minor | Ten duplicated temp-directory fixtures, three teardown behaviours, six of which throw on a leaked handle | ten files | One `TempDirectory` helper | D |
| E11 | nit | An invariant culture set in `OneTimeSetUp` is never restored | `SvgRendererTests.cs:20` | Restore it | F |
| E12 | nit | `BlockLabel.Plan(descriptor, lookup)` recurses into itself and terminates only because key derivation happens not to depend on the label | `BlockLabel.cs:134` | An `if` | F |
| E13 | nit | A dead `result < 0` arm in the benchmark; `SelectionChanged` listed twice in the new test | `CanvasPerformanceBudget.cs:136`, `XamlEventWiringTests.cs:42` | Delete | F |
| E9b | minor | `PaletteStore.Save`'s failure return is never exercised | `PaletteStore.cs:87` | A read-only-directory test | F |

## 6. Closed without a change

| ID | Finding | Why it stays |
|---|---|---|
| D-x | "Nav clicks are dead in some sessions" | The harness's `GetCurrentThreadId` P/Invoke named `user32.dll` instead of `kernel32.dll`, so no click ever took the foreground. Fixed in the harness; the symptom went with it |
| A-x | `crash.log` `ViewRequested` entry | Predates the fix; the defect class now has a test |
| E-x | The known-flaky transcript test | Not a flake: a real contract that cannot be kept (E1). It passes in full runs and fails 55% in isolation, which is worse than either |
| — | Keyboard-only editing, palette search, text entry | Unverifiable from the harness — synthesised characters do not arrive. Recorded, not fixed, not guessed at |

## What is still open, and why

Nothing below is a known defect with a known fix that was skipped for time. Each is a judgement call,
recorded so the next person does not have to rediscover it.

| Item | Why it is still open |
|---|---|
| D14 - a rejected drop's hint stays on screen until something else changes | Fixed in code, and **not verified in the running window** - which is the honest status, not a hedge. `ReportProblem` sets the sentence and arms a six-second `DispatcherTimer` that clears it, and only if the text is still the one that timer was armed for; the confirmations ("saved plugin.json", "recovered from autosave") deliberately stay, because a timer on those would hide a recovery notice from anyone who looked away. Every easily reachable refusal turned out to be unreachable for this purpose: Save and Revert are `IsEnabled`-bound and correctly greyed with no workspace, and Export SVG opens a modal `SaveFileDialog` on its own window handle before it can report anything. A rejected drop would show it, and the driver has no way to cancel a modal child window. |
| D15, D16 - a rail row and two panel bottoms clipped at small window sizes | Not defects, and the wheel is what proved it. At 960x700 the app's own nav pane scrolls with a visible scrollbar and every item is reachable; the Visual page's category rail and its inspector both scroll, and both were seen to scroll. What the audit photographed was a scroll viewport's edge, which clips mid-item in every scrolling list on earth. This is the mistake the `wheel` command in the harness exists to prevent, and the lane that reported it had not scrolled anything. |
| D17 - a new canvas opens with 15 identical `"False" is not a condition` warnings | Fixed, and in the validator rather than in the sample, which was the wrong diagnosis at first. `BlockFactory` fills every boolean slot with `Of(false)`, and the validator listed `Boolean` among the kinds that needed converting before they could be read as a condition. Nothing is converted: `ScriptInterpreter` reads `BlockInputKind.Boolean => input.Boolean ?? false`. A fresh canvas therefore warned fifteen times about a value it had just invented. The sample's `wait until false` blocks are legal and stay. |
| D18 - the header hint paragraph is crushed when width is scarce | Fixed, and there were two faults rather than one. The header's four lines wrapped, so a sentence became forty lines and pushed the panels a screen down; they now trim. Underneath that, the narrow layout defined three fixed columns and no star, while the header, the canvas and the stage all span the grid's columns - so at 1200x800 they were 610 pixels wide inside a 930-pixel viewport, the header's text column was left with 96, and 320 pixels of the window were empty. A trailing star column fixes the width, and the inspector now spans it so the third row has no hole beside it. |
| E9 - ten temp-directory fixtures with three teardown behaviours | One shared `TempDirectory` helper is an hour of mechanical work across ten files, and nothing is broken while they stand. |

## Verified, not inferred

| Claim | How it was checked |
|---|---|
| Drag and drop moves a block | A left-drag in the real window, before and after, with the block visibly changing place |
| The palette scrolls | The wheel over the palette reaches `when plugin initializes`, which is the eighth Control block |
| The overlays are readable | The command palette opens from its header button and its rows are legible over a dimmed page |
| The Stage is reachable in a short window | A page-level scrollbar appears at 1200x800 |
| Zoom out keeps the document on screen | 75% shows every block, after the viewport was measured from the scroller rather than the surface |
| Category switching | Clicking Sensing in the rail repopulated the palette with 23 sensing blocks |
| The whole suite | `dotnet test tests/DeckForge.Tests` - 857 passed, 0 failed, and no test that skips itself |

**Not verifiable from here.** Synthesised keystrokes do not reach the window, so the keyboard paths - the
command palette's own search, the canvas's arrow-key and carry-mode editing, the shortcut sheet's rows as
pressed rather than as listed - rest on `VisualCommandsTests` and on the fact that one table feeds the
palette, the sheet and the dispatcher. The harness's own bug was found on the way: its
`GetCurrentThreadId` P/Invoke named `user32.dll` instead of `kernel32.dll`, which is why three lanes
agreed that clicks had stopped working.
## Second audit round, closed

| Item | Outcome |
|---|---|
| D17 - fifteen `"False" is not a condition` warnings on a fresh canvas | Fixed in the validator, not the sample. `BlockFactory` fills every boolean slot with `Of(false)` and the validator counted a boolean literal as needing conversion; `ScriptInterpreter` reads it directly. The window now shows 3 honest warnings |
| D18 - the header hint crushed | Two faults: the header wrapped instead of trimming, and the narrow layout had no trailing star column, so the header, the canvas and the stage were 610px inside a 930px viewport |
| D14 - a refusal stayed in the message line | `ReportProblem` with a six-second timer. **Not verified in the window** - every reachable refusal is either a disabled button or behind a modal `SaveFileDialog` |
| D15, D16 - rail and panel "clipping" | Not defects. Both scroll, with a scrollbar. The lane had not used the wheel |
| Build warnings | NU1701 x2, named and explained rather than left as the only warning in the build |

## The context-menu defect the window found

Driving the window after the third audit round found a **right-click on a block doing nothing at all** -
no menu, and the block not even selected, so `ContextMenuOpening` had plainly never run. The cause is
that `BlockWorkspace` puts *its* menu on three elements that are all ancestors of every tile, and which of
two `ContextMenu`s on one route wins is not something the tile controls. No test can see this: a menu that
does not open fails silently and identically to a menu with nothing in it.

The tile now handles the right button itself and opens its own menu, so nothing depends on which ancestor
wins. **That fix is not confirmed end to end.** The harness reports `GetForegroundWindow()` returning
zero on this machine, so `SetForegroundWindow` is refused and the click that navigates to the Visual page
is swallowed - roughly one run in three reaches the page. The canvas and stage menus are unverified for
the same reason, and the palette's right-click is not built at all (Part 18.5 does not ask for it).

## What the harness still cannot do

- Hold the foreground window, so the first navigation click is unreliable. This is the single biggest
  gap left and it has now cost one unverifiable fix.
- Send keystrokes the application receives. Every shortcut is covered by `VisualCommandsTests` instead.
- Photograph a WPF `Popup`. `ContextMenu` renders into a separate top-level window, so the window-DC
  capture cannot see one even when it is open; only a full-desktop capture can, and that one is too wide
  to read without knowing where the window landed.
## The crash the user hit, and what it was

`crash.log`, 44 entries, latest 2026-10-04 18:18:35:

```
System.Windows.Markup.XamlParseException: A TwoWay or OneWayToSource binding cannot
work on the read-only property 'Selected' of type 'DeckForge.App.ViewModels.Visual.MenuEditor'.
```

Selecting any block with a dropdown took the application down. `ComboBox.SelectedItem` binds
TwoWay by default; the inspector's menu row bound it to `MenuEditor.Selected`, which is computed
from the block (`SlotValue.ReadMenu`) and had no setter. WPF throws while the *template loads*,
so the crash happened on layout rather than on the selection that caused it.

**It was never working.** `git log -S` puts the binding and the read-only property in the same
commit, `34ccb83` "Edit a block's fields, and see the code it makes" - the commit that introduced
the inspector's editors. The dropdown has been inert since the day it was written, and the day
became a crash the first time somebody selected a block that has one.

**The smaller half.** Adding a setter stops the crash and would have left a combo box that shows
the current option and cannot change it. The setter routes through the existing guarded `Choose`,
so the write is the same one an explicit choose performs: refused unless the option is one the
menu declares, applied as one undoable `EditField`. `Selected` still caches nothing in either
direction - the getter reads the block every time, so undo and rebuild cannot leave the row
showing a value the document does not have.

**What no test can catch, and why.** A read-only property under an implicit two-way binding is a
crash, so it is only ever found by running the window. A guard would have to resolve each binding
against the App assembly, and the test project may not reference the App - which is the right rule
and the reason this class of defect stays manual. There are **35 more bindings** in the App's XAML
that lean on the same implicit mode (`IsChecked="{Binding ...}"`, `SelectedItem="{Binding ...}"`
without `Mode=`). Every one checked has a setter, so none is presently a crash; none is guarded
either. The honest recommendation is a sweep that makes each of them state its mode, which is
mechanical and about an hour, and this entry is the argument for doing it.
## The second crash: moving a MenuItem between menus

`crash.log`, entry 45, 2026-10-04 18:42:37:

```
System.InvalidOperationException: Element already has a logical parent. It must be
detached from the old parent before it is attached to a new one.
   at MS.Internal.Controls.InnerItemCollectionView.Add(Object item)
   at DeckForge.App.Pages.VisualEditorPage.Tile_MenuRequested(...) line 456
   at DeckForge.App.Controls.Blocks.BlockTile.OnPreviewMouseRightButtonDown(...) line 122
```

**This one was mine.** `Tile_MenuRequested` built a menu with `VisualMenus.ForTile` and then copied its
`Items` into the tile's own menu. A `MenuItem` carries its logical parent, so adding one that is still in
another menu throws from inside a WPF internal - with a message that names neither a menu nor a context
menu, on a line that reads like copying a list.

`VisualMenus.Menu` became `Fill(menu, ...)`, and a new `FillTileMenu` fills a menu the caller already
owns. Nothing is moved between parents anywhere now. `ForCanvas` and `ForStage` create their own menu and
fill it, which is why they never hit this.

**Two things worth keeping from this.**

The crash is what proved the first fix worked. The right-click reaching `Tile_MenuRequested` at all is
the evidence that the tile's own right-button handler runs, which was the thing I could not verify last
round - so a defect I introduced is what finally confirmed the repair for the defect before it.

The lesson is about the fix's shape, not the bug. The handler that fixed the *first* crash took the right
button off `ContextMenuService` and opened the menu itself, and the page filled a menu the tile owned.
That was right. What was wrong was the page then filling it by *transferring* rows from a menu it had
built for the purpose. Building into the target, rather than building and moving, is the whole difference
between the two versions.

**Still unverified:** that the menu paints. A WPF `ContextMenu` renders into its own top-level window,
which the window-DC capture cannot see; the full-desktop capture is too wide to read without knowing where
the window landed. Right-clicking a block now selects it and does not throw - both verified - but whether
the popup is visible on screen is not.