# DeckForge Fix Plan — Round 2 (exhaustive audit)

Scope: every page, control, label, icon, generator, validator, service and realistic user flow,
visually (driving the real app) and non-visually (read-only audits). Findings are recorded here
**before** any fix is made.

Every item is labelled with how it was established, so nothing gets "fixed" on the strength of an
unverified claim:

- **CONFIRMED** — reproduced or established by direct reading of the exact code path, with a
  file:line citation.
- **REPORTED** — surfaced by a read-only subagent audit; plausible and specific, but not yet
  reproduced by me. Must be reproduced (or disproved) before it is fixed.
- **DISPROVED** — investigated and found not to be a defect. Kept here so the same false lead is
  not re-investigated.

## Status summary

| # | Severity | Area | Finding | State |
|---|----------|------|---------|-------|
| 1 | Critical | Shell | Ctrl+digit page shortcuts were dead | **FIXED** |
| 2 | Critical | CodeGen | `BlockCompiler.Splice` reused stale indices after removing a region | **FIXED** |
| 3 | High | Blocks | Canvas was silently wiped by navigation | **FIXED** |
| 4 | High | Blocks | Target action was hardcoded; blocks could only ever target one action | **PARTLY FIXED** |
| 5 | High | Blocks | Status text pointed at a control that does not exist | **FIXED** |
| 6 | Medium | CodeGen | `NumberLiteral` injected non-numeric text raw into generated C# | OPEN |
| 7 | Low | Manifest | Save was a silent no-op with no workspace and no feedback | OPEN |
| 8 | High | Validators | Icon extension rule may reject extensions the real CLI accepts | REPORTED |
| 9 | High | CLI | Environment version probe may throw unhandled | REPORTED |
| 10 | — | Validators | `bundledIconPacks` type confusion | DISPROVED |
| 11 | — | Blocks | Arbitrary file write via sidecar `TargetFile` | DISPROVED |

## Fixed this session

### 1. Ctrl+digit shortcuts (Critical, fixed)

`MainWindow.xaml.cs` now collects the tags with the pattern the same file already uses 100 lines
below it, so the sidebar and the shortcut list cannot drift apart:

```csharp
.. RootNavigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>()
    .Concat(RootNavigation.FooterMenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>())
```

Menu items still come before footer items, because Ctrl+1 is `_shortcutTags[0]` and the digits have
to run down the sidebar in the order the user sees them.

**Verification — and its limit, stated plainly.** The defect and the fix are both certain from the
code: `NavigationViewItem` derives from a WPF control, is not `IEnumerable`, and so
`OfType<System.Collections.IEnumerable>()` discarded every page. A regression test pins all of it:
tags present, tags unique, and the broken flattening absent. The test was confirmed to fail when the
bug is reintroduced.

What I could **not** do is press the key. The driver confirms the window has keyboard focus
(`GetForegroundWindow` returns the app), but neither `keybd_event` nor posted `WM_KEYDOWN` reaches
WPF's `PreviewKeyDown` in this environment, so every screenshot came back on the same page. That is a
harness limitation and is *not* evidence the shortcut is still broken — but it does mean nobody has
yet seen the shortcut work. **A human should press Ctrl+1 through Ctrl+0 once and confirm each digit
lands on the page its sidebar entry names.** Until then this fix is code-proven and test-guarded, not
observed.

### 2. `BlockCompiler.Splice` stale indices (Critical, fixed)

Every position is now resolved *after* the old region is removed, instead of before. The comment on
the code explains why the order is the fix and not a style preference.

**Verification.** A test fixture was written with the executor deliberately **not** the last member
and a top-level `return` in the member after it, because that is the shape that corrupts. The test
was run against the old ordering and **failed**, and the failure was not hypothetical: dumping the
output showed the entire block region spliced into `BuildLabel()` — the wrong method — while the
executor was left with no region at all. The test now also asserts the region sits before the
executor's own return, because marker-count and program-presence assertions both passed while the
region was in entirely the wrong place. With the fix, all 30 tests in the fixture pass.

Worth noting how this was nearly missed: a first attempt at the fixture asserted on a `{name}`
placeholder that the compiler substitutes, and a temporary "reproduction" captured the index at its
already-correct position, so the test looked green while the bug was live. Both were wrong and both
were corrected before the fix was called verified.

### 3. Canvas wiped by navigation (High, fixed)

`RefreshOnNavigate` now reloads only when the workspace behind the canvas is not the one already on
screen, tracked by a new `_canvasWorkspace` field. A genuine workspace switch is still picked up,
because that is what raises `WorkspaceChanged` and calls `Load()` directly. Navigating away and back
keeps an unsaved canvas.

The sidecar is also parsed once per load rather than twice.

### 4 & 5. Block target action (High, partly fixed)

- The target file is now derived from the target action id by the same rule the generator and the
  Actions page use (`CSharpCode.TypeName(CSharpCode.ToPascal(id), "Action") + ".cs"`), instead of
  silently inheriting `BlockProgram`'s `LogMessageAction.cs` default for every program.
- The target is restored from the saved program, so reopening it no longer re-aims the canvas at the
  example action.
- The false instruction is gone. The message now names the missing file and the id that produced it.
- The page header and a new line under **Program** both show the real target, verified on screen.

**Still open, deliberately.** `TargetActionId` is now real state, but there is still **no picker** on
the page, so the user cannot choose a different action in the UI. I did not add one by scraping
`src\*.cs` for action files: the id cannot be recovered reliably from a file name, and a picker that
silently lists the wrong ids would be worse than none. This needs either a supported action
enumeration or the ids carried in the manifest. Until then the Blocks page can only usefully target
the example action, which is the honest state of a feature that is not finished.

### 6. `NumberLiteral` raw injection (Medium, open)

Not yet fixed. Recorded so it is not lost.

### 7. Manifest Save silent no-op (Low, open)

Not yet fixed. Recorded so it is not lost.


---

## 1. Critical — Ctrl+digit page shortcuts are dead

**File:** `src/DeckForge.App/MainWindow.xaml.cs:46-55`

```csharp
_shortcutTags =
[
    .. RootNavigation.MenuItems
        .OfType<System.Collections.IEnumerable>()          // <-- wrong
        .SelectMany(items => items.Cast<Wpf.Ui.Controls.NavigationViewItem>())
        .Concat(RootNavigation.FooterMenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>())
        ...
];
```

`MenuItems` is `IList<object>` whose elements are `NavigationViewItem`. A `NavigationViewItem` is
**not** `IEnumerable`, so `OfType<System.Collections.IEnumerable>()` discards every real item. The
result is that `_shortcutTags` contains only the footer items (Help, Settings, ...). Ctrl+1..9
therefore either navigate somewhere arbitrary or do nothing at all.

The correct pattern already exists 106 lines below in the very same file
(`MainWindow.xaml.cs:162-163`):

```csharp
foreach (var item in RootNavigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>()
             .Concat(RootNavigation.FooterMenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>()))
```

which proves the intent and gives the fix for free. The `.SelectMany` + `IEnumerable` machinery
should be deleted, not patched.

**Fix:** use the `OfType<NavigationViewItem>()` pattern for `_shortcutTags`.
**Test:** a unit test asserting `_shortcutTags` covers every tag in the XAML, so a future control
added without a tag cannot silently drop its shortcut.
**Manual check:** press Ctrl+1..Ctrl+9 and Ctrl+0 in the running app and confirm each lands on the
page its menu item names.

## 2. Critical — `BlockCompiler.Splice` reuses stale indices after removing a region

**File:** `src/DeckForge.CodeGen/Generation/BlockCompiler.cs:387-421`

```csharp
var close = MatchingBrace(source, open);   // 387: index into the CURRENT string
...
if (begin >= 0 && end > begin)
{
    var start = LineStartOf(source, begin);
    var after = end + EndMarker.Length;
    var lineEnd = source.IndexOf('\n', after);
    source = source.Remove(start, (lineEnd < 0 ? after : lineEnd + 1) - start);  // 405: source SHORTENS
}
...
var insertAt = LastTopLevelReturn(source, open, close) ?? close;   // 419: close is now stale
return source.Insert(insertAt, ...);                               // 421
```

`close` (and `open`, and `anchorIndex`) are computed *before* the removal at line 405, but the
removal deletes characters from inside the body, so every index after `start` shifts earlier by the
number of removed characters. `close` is never recomputed. Consequences:

- `LastTopLevelReturn(source, open, close)` scans **past the true end of the method body**, into
  following members of the file, so it can return a `return` belonging to a *different* method.
- When it returns null, `insertAt = close` is a stale index which can be **larger than the new
  string length**, making `source.Insert` throw `ArgumentOutOfRangeException`.
- When it is within range, the region is inserted **outside the executor**, producing a file that
  no longer compiles or that compiles the blocks into the wrong action.

The second save of any block program is the dangerous one, because only then does a region exist to
be removed. The current tests pass only because their fixture happens to contain a top-level
`return` inside the executor, which masks the shifted index.

**Fix:** recompute `open`/`close` (and re-find the anchor) after the removal, or compute the
insertion point as a *relative offset* from a stable reference that survives the removal.
**Test:** a regression test that saves a program, then saves a *different* program over the same
action file, and asserts the output still has exactly one region, inside the executor, and that
the file parses. Add a second fixture where the executor is **not** the last member of the file
and a later member contains a top-level `return` — that is the shape that corrupts today.

## 3. High — the blocks canvas is silently wiped by navigation

**File:** `src/DeckForge.App/ViewModels/BlockActionViewModel.cs:154-181`

```csharp
public void Load()
{
    ...
    Statements.Clear();                 // 162: unconditional
    ...
    foreach (var statement in ReadPersisted()?.Statements ?? [])   // 172
        Statements.Add(statement);
}

public void RefreshOnNavigate() => Load();   // 181: called on every navigation
```

`ReadPersisted()` returns null when the sidecar JSON does not exist yet, i.e. for any canvas the
user has built but not saved. So the sequence *build a canvas → click another page → come back*
empties the canvas with **no warning and no undo**. The user loses their work silently.

This is also an inconsistency in the codebase: `ManifestStudioViewModel.RefreshOnNavigate`
(`ManifestStudioViewModel.cs:233-242`) correctly refuses to discard state and instead says
*"Unsaved changes are still here - press Save, or Reload to discard them."* The blocks canvas
should behave the same way.

**Fix:** do not clear a dirty canvas on navigation; surface an unsaved-changes state, matching the
manifest studio, and only reload when the workspace actually changed.
**Test:** construct the view model, add statements, call `RefreshOnNavigate`, assert the statements
survive.

## 4. High — the block target action is hardcoded, so blocks only work for one action

**File:** `src/DeckForge.App/ViewModels/BlockActionViewModel.cs:25`, `183-187`, `543`

```csharp
private const string TargetActionId = "log-message";   // 25: a constant, not user state
...
private BlockProgram Program() => new()
{
    TargetActionId = TargetActionId,      // the constant
    Statements = [.. Statements],
};                                          // TargetFile is never set
```

`BlockProgram.TargetFile` defaults to `"LogMessageAction.cs"`
(`src/DeckForge.Core/Blocks/BlockModel.cs:39`) and `Program()` never assigns it, so line 543

```csharp
var actionFile = Path.Combine(ws.PluginProjectDirectory, program.TargetFile);
```

**always** resolves to `LogMessageAction.cs`, no matter what the user does. A plugin with six
actions can only ever receive blocks on one of them. The feature that is the entire point of the
Blocks page is effectively non-functional for the rest of the plugin.

**Fix:** persist the chosen target action in the sidecar `BlockProgram` and derive the target file
from it, and restore it on load.
**Test:** round-trip a program whose `TargetFile` is a non-default action, and assert the write
lands in that file.

## 5. High — status text refers to a control that does not exist

**File:** `src/DeckForge.App/ViewModels/BlockActionViewModel.cs:546`

```csharp
StatusText = $"{program.TargetFile} not found. Set the target action on the Blocks page first.";
```

There is no target-action control on the Blocks page. A search of `src/DeckForge.App/Pages/*.xaml`
for `TargetAction` returns nothing, and `TargetActionId` is a `const`. The app instructs the user
to perform an action that is impossible, which sends them looking for a control that does not
exist. This is fixed by the same work as #4, and the message must go away or become true.

## 6. Medium — `NumberLiteral` injects non-numeric text raw into generated C#

**File:** `src/DeckForge.Core/Code/CSharpCode.cs:113-124`

```csharp
return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
    ? d.ToString("R", CultureInfo.InvariantCulture)
    : text;                                  // 123: raw passthrough
```

Reachable from user input on two paths:
`BlockCompiler.cs:943` (`"number" => double? x = {NumberLiteral(set.Literal)}`, from a block
editor field) and `ActionParameterFactory.cs:383` (from a parameter's default value).

Anything that is not a number is emitted verbatim into the generated source, so a typo or a
pasted value produces a compile error whose cause is invisible, at best, and arbitrary statements
in the generated file at worst. This is not a privilege boundary (the user is generating their own
plugin) but it is a correctness and robustness defect, and the stated rationale — "keeps a
malformed range visible" — is achieved badly: it makes the file uncompilable without saying why.

**Fix:** emit a safe literal and an explicit diagnostic comment (or reject the value at the editor
and tell the user), never the raw text.
**Test:** assert that a malicious/malformed numeric value cannot place a statement in the output.

## 7. Low — Manifest Save silently does nothing

**File:** `src/DeckForge.App/ViewModels/ManifestStudioViewModel.cs:244-256`

`Save()` correctly returns when `_document is null`, so it cannot crash. But with no workspace open
it gives the user **no message at all**, so pressing Save appears to work. Should state that there is
nothing to save, consistent with the rest of the page's status reporting.

---

## Reported by the read-only audits, not yet reproduced

These came out of the four subagent passes. They are specific and plausible; I will reproduce each
before changing anything, because at least two other agent claims in this round proved to be false
(see below).

- **[REPORTED, High] Icon extension rule is wrong.** `ManifestValidator` appears to enforce an
  invented extension whitelist, while the installed `macrodeck-plugin 3.0.0-beta.14` accepts
  `.ico`, `.bmp`, `.gif` and no extension at all. If true, valid manifests are rejected.
  *Next:* diff the validator's rule against what the real CLI accepts, and against the official
  template.
- **[REPORTED, High] Environment doctor throws on the machine it is meant to diagnose.** The
  version probe in `MacroDeckCli` / `EnvironmentDoctor` reportedly raises an unhandled exception
  instead of reporting the problem. *Next:* run the command on this machine and capture the exact
  exception.
- **[REPORTED] `IconStudio` SaveAs** has no guard on the destination and can fail obscurely.
- **[REPORTED] `WidgetDesigner`** binds `EventNames` / `SchemaTypes` as static members through a
  path that does not notify, so the pickers can go stale.
- **[REPORTED]** Further medium/low items across generators, validators, CLI and the view models
  that still need to be triaged one at a time.

## Disproved during this pass

Kept so the same lead is not chased twice.

- **[DISPROVED] `bundledIconPacks` type confusion.** `ValidateBundledIconPacks`
  (`ManifestValidator.cs:754`) already checks `packs.ValueKind != JsonValueKind.Array` and returns
  early, and line 771 checks each element is an object. There is no crash path here.
- **[DISPROVED] Arbitrary file write through the sidecar `TargetFile`.** `Program()`
  (`BlockActionViewModel.cs:183`) constructs a *fresh* `BlockProgram` and copies only
  `TargetActionId` and `Statements`. The deserialized `TargetFile` is never read back, so it
  cannot reach `Path.Combine`. The real defect here is different and is tracked as #4: the target
  is always the default.

---

## Still to do

- [ ] **Press Ctrl+1..Ctrl+0 once, by hand.** The only verification of finding 1 that is still
      missing, and the only one the harness cannot do.
- [ ] Add a real target-action picker to the Blocks page (finding 4). Needs a trustworthy source of
      action ids; do not scrape file names.
- [ ] Fix `NumberLiteral` (finding 6) and the Manifest Save message (finding 7).
- [ ] Reproduce and triage every **REPORTED** item above. None has been touched yet.
- [ ] Finish the serial wide/narrow visual pass over every page, every control state (empty,
      loading, error, populated) and both themes. Only the Blocks page has been re-checked this
      session, after its own change.
- [ ] Exercise every remaining flow end to end: all action types, widgets, manifests, icon packs,
      localization, extensions, ship, publish.
- [ ] Re-run the full gate after all of the above: generated-project build, `macrodeck-plugin build`,
      publication validation, conformance, template parity, and a final visual pass.

## Verification state for this session

- Solution builds: 0 errors, 2 pre-existing `NU1701` package warnings in the test project.
- Tests: **349 passed, 0 failed** (was 344; +2 splice regression tests, +3 shortcut guard tests).
- The generated-project compile test passes, which exercises `Splice` against real generated output.
- Blocks page re-checked on screen after the XAML change; header binding and the new target line
  both render, layout intact.
- Template parity, `macrodeck-plugin build`, publication validation and conformance have **not** been
  re-run since these changes and must be, before this is considered done.

