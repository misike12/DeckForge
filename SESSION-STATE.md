# SESSION STATE — read this first if context was lost

## Standing instructions from the user (verbatim intent)

- "implement everything" — every unimplemented feature.
- "implement everything from macrodeck that is not already implemented, i mean all stuff for plugin creation"
- "make a gui or a scratch page where you can build with actual blocks"
- "finish everything from fixplan too"
- "after you are completely done ... put on agent i mean subagents you know to find bugs (ALSO FIX THEM) and test every corner, and make a random test extension"
- "i want to work until at least 7 PM so if you are done just check the time yourself and if its before 7pm then continue. you can go over 7pm, but you cant finish before 7pm"
- "dont ask questions and dont stop"

Non-negotiables:
- Keep SDK pin `3.0.0-beta.14` exactly. User considers it newest; previews are older. Do not argue.
- Real `macrodeck-plugin` CLI is installed and authoritative — verify with it, not assumptions.
- Independent tool: do NOT copy Macro Deck source/branding/licenses. Call the published SDK only.
- Every commit message explains the user-visible symptom it fixes.
- Working tree must be left clean; commit after each milestone.

## Project

Root: `C:\Users\Misu\Desktop\Macronator` (.NET 10, WPF, "DeckForge").
Solutions: `DeckForge.slnx`. Tests: `tests/DeckForge.Tests` (NUnit).

Projects: `DeckForge.Core` (models, catalog, manifest, validators, docs snapshot),
`DeckForge.CodeGen` (templates + generators), `DeckForge.CliAdapter` (process/CLI),
`DeckForge.Validators`, `DeckForge.App` (WPF).

## Ground truth available

- Real CLI: `macrodeck-plugin` (3.0.0-beta.14+088befbf...). Use `macrodeck-plugin <verb> --help`
  to check every flag rather than assuming.
- Official template scaffolded at
  `C:\Users\Misu\AppData\Local\Temp\opencode\official-template` — this is the AUTHORITATIVE
  reference for the generated project. Its `tests/Ref.Tests/PluginIntegrationTests.cs` has 7
  tests across 2 fixtures and is what DeckForge's generated tests must match.
- NOTE: `C:\Users\Misu\AppData\Local\Temp\opencode\macrodeck\*` git working trees are GONE
  (only .git/objects remain). Use the scaffolded template above + the installed CLI instead.
  Re-clone if SDK source is needed: SDK nuget package or `macrodeck-plugin` itself.

## Test suite

`dotnet build DeckForge.slnx -v q --nologo` then
`dotnet test tests\DeckForge.Tests --nologo`.

Last known: **237 passed, 0 failed**. Baseline at session start was 94.

## Git log (this session's work, newest first)

- (uncommitted) docs snapshot path safety — 229
- (uncommitted) settings wiring + theme + workspace opening — 204
- fc6536c Collapse four copies of the source-patcher into one, delete stale App scaffolder — 196
- 3128c6c Stop a remote sitemap choosing where DeckForge writes files — 229 (see below; order may differ)
- be0711f Wire every setting to something, and stop the crash handler being silent — 204
- (earlier: 4a8ce12, d2606e4, 51127df, e32655d, 75f08a2, 85b32a7, 94c06bc, 057a6fc, 81822d5,
  2ed601c, 590dbe3, 078ea14, 9cf18f0, aab4ab7, 698b878, 0fbe1fa)

Always run `git log --oneline -12` to get the real current order.

## What is DONE

### Generators (P0) — all done
- `ActionGenerator`: all 29 parameter editor types emit real SDK signatures, names always emitted,
  optional args named, resx keys, duplicate detection.
- Config Flow: `switch (stepId)`, `ValidateAsync`, closing brace, all editor types, secret handling.
- `WidgetGenerator`: 24 `UiNodeCatalog` types, namespaces, init-only Children/Events, IUiSession.
- `BlockCompiler`: 18 block kinds, `IIntegrationContext` host APIs, async executor rewriting.
- `MacroDeckCli` + `ShipViewModel`: 4 wrong invocations fixed (positional paths), `test` selectors,
  key lookup `.public`/`.private`, cert requirements.

### Validator (P1) — done
- `Development`/`Package`/`Publication` levels, real CLI issue codes, dead icon rule removed,
  invented RID/placeholder rules removed.

### Capabilities — done (this was the session's main thread)
- `CapabilityDefinitions` (23) + `CapabilityScaffolder` in `DeckForge.CodeGen/Capabilities`.
  Every signature read out of the real SDK. Explicit interface implementations so
  `ProviderName` and `GetInstances` can coexist on one class.
- Fixed 4 real `IntegrationPatcher` bugs: `AddUsing` deleted an import per call;
  `FirstUsingDirective` failed on CRLF; `AddInterface` appended after the newline;
  `CapabilityScaffolder.AddPermissions` had inverted LINQ.
- `CapabilityPresetContributor` turns wizard presets into code; `ApplyContributorState` re-renders
  files after contributors run (stock files are written first so contributors can patch them).
- `CapabilityCatalog` gained `logging` (23 total, was 22).
- `Scaffolding_every_capability_produces_a_plugin_that_compiles` scaffolds all 23 and builds.

### Process/CLI — done
- `ProcessRunner`: drains pipes (EOF-signalled, 5s bound), interleaved transcript, per-call
  callbacks, subscriber exceptions contained, stdin writer, caller-supplied encoding,
  CommandLineToArgvW `SplitCommandLine`, `ProcessStartFailedException`, Kill catches Win32Exception.
- `VelopackPackagingService`: ArgumentList via shared runner, no `vpk.exe` on PATH, no leak.
- `EnvironmentDoctor` + `DotNetCli`: version parsing that survives a `global.json` diagnostic,
  one probe not two, both spellings of the install folder, PATH.PathSeparator, severity,
  CLI version gate vs user setting.

### UI bugs — done
- Manifest page: `Click` not `Checked`/`Unchecked` (counter drifted +1/visit), dirty tracking,
  `RawJson` surfaced, stale-state clearing, `PermissionRow`/`PermissionGroupVM` moved to Core.
- ShellMessenger: handler stored+detached, `BeginInvoke` not blocking `Invoke`.
- `MainWindow.NavigateTo`: 13-branch chain → `IRefreshOnNavigate` / `INavigateWithin`.
- 17 commands with unreachable `catch (OperationCanceledException)` → `CancellableOperation`
  + Cancel buttons on 6 pages.
- Localization: 2500 parses → one per culture, one write, `Custom.Key{n}` collision, rename added,
  `(missing)` marker conditional, `ResxMerger.RenameKey`.
- Icon Studio: refuses to write an empty `<svg>`, template selected on load, GeneratedRegex,
  `HasWorkspace` bound.
- Icon pack: 32 MiB limit enforced, duplicate zip entries refused, `ProcessStartFailedException`.
- Settings: 5 dead settings wired (DefaultProjectsDirectory, MacroDeckCliVersion, GitHubAccount,
  CheckForUpdatesOnStart, VelopackUpdateUrl), `TelemetryEnabled` removed on purpose with a reason
  in the About panel, atomic save, `SettingsViewModel`, all settings have UI.
- `MainViewModel` deleted; Home page had two view models and the whole Environment section bound
  to the wrong one. Checks now on `WorkspaceViewModel` + Re-check button.
- `WorkspaceManager.TryOpenSolution`: no longer picks the first `src/` dir; reports multi-plugin.
- Crash log rotates; handled crashes show a banner; `AppTheme.System` follows the OS live.
- LiquidTheme: frozen read-only `NamedColor.ColorBrush`, accent-derived gradient, light-mode
  elevation, light-mode semantic brushes.
- 4 copies of the source-patcher collapsed to 1 (Events editor, Config Flow editor, stale App
  `CapabilityScaffolder` deleted, CodeGen).

### Docs snapshot — done
- One sanitiser for all remote paths, `ResolveInside` containment check, same-origin filter,
  force/Refresh button, `meta.json` read and shown, case-insensitive manifest, 3 snapshots kept,
  asset scan widened, empty catches explained, `GeneratedRegex`. Moved to `DeckForge.Core/Services`
  with `RootOverride` so tests use their own store.

### Template fidelity — mostly done
- LICENSE (MIT/Apache full text, labelled placeholder otherwise), `local-feed/.gitkeep`,
  `.gitignore` keeps the placeholder, README `build --source` (was nonexistent `--project`),
  permissions array, `--self-contained` honoured, JSON escaping for control chars.
- Token layer: every value flows through `Tokens()`; no dead tokens; `NewProjectOptions.Version`
  added; project-name derivation in one place; resx key attribute escaped.

## What is IN PROGRESS / REMAINING

Work through these in order. Each ends with a commit.

1. **Generated test project parity.** DeckForge emits 2 weak tests. The official emits 7 across
   2 fixtures with `UseLocalization(Strings.LocalizationCatalog)`:
   - `PluginIntegrationTests`: builds+initializes, action writes to log, action fails on blank
   - `LocalizationTests`: catalog scope == `plugin:<id>`, default culture, keys come from catalog,
     every declared key resolves to non-empty text
   Copy that shape into `MacroDeckTemplateFactory.IntegrationTestsCs`, parameterised by plugin id.
   Then diff the whole generated tree against
   `C:\Users\Misu\AppData\Local\Temp\opencode\official-template` and close the rest
   (`.slnx` solution folders, AGENTS.md, CLAUDE.md, etc.).

2. **`scratch/` is a temp project** — delete it before committing anything.
   It exists only to generate a reference tree for diffing.

3. **Block Programmer GUI.** `BlockCompiler` supports 18 kinds including `IfBlock` and
   `NavigateBlock`, but `BlockActionPage.xaml` has no UI for them. Build the real visual
   block-graph editor: add/remove/reorder blocks, edit each block's fields, live preview of the
   generated C#, drag to reorder, validation. This is a headline user request.

4. **`IDeckForgeExtension`** — zero implementations, no discovery, no page. Add the interface,
   an assembly-scanning loader, a registration point, and at least one real extension.

5. **Remaining FIXPLAN P2 items:**
   - Converters: `BoolToInverseVisibilityConverter` and `InverseBoolToVisibilityConverter` are
     identical; `BoolToStatusColorConverter` hardcodes colours; `ResourceKeyToBrushConverter`
     degrades a typo to grey silently.
   - `StaticResource` for Liquid brushes on HomePage/NewProjectPage → those pages never re-theme.
   - `LiquidCard` CornerRadius 12 vs hand-rolled 10.
   - `app.manifest` has no `requestedExecutionLevel`.
   - `DeckForge.App.csproj`: unused `Microsoft.Extensions.Hosting`; explicit
     `<Resource Include="Assets\app-icon.png" />` risks NETSDK1022.
   - No `.editorconfig`; no `TreatWarningsAsErrors`.
   - `Assets/generate-icon.ps1` not wired into MSBuild.
   - `NewProjectViewModel`: double validation per keystroke, `Directory.Exists` on every keystroke.
   - `App.xaml`: no `ValidateOnBuild`/`ValidateScopes`; accent applied twice.
   - `AppSettings`/`SettingsPage` leftovers.
   - `EventsEditorViewModel` — re-check for remaining issues after the patcher merge.
   - `IconPackDesignerViewModel` — done this session; verify.
   - `ROADMAP.md` claims a deck-navigation block that has no UI (covered by item 3).
   - `ROADMAP.md` claims `.deckforge/` holds state; only an empty dir is created.
   - Generated `AGENTS.md` / `CLAUDE.md` from the official template (DeckForge emits neither).

6. **Any Macro Deck plugin-creation feature not yet in DeckForge.** Compare
   `macrodeck-plugin --help` verbs against what DeckForge exposes, and the official template's
   files against DeckForge's. Every verb should be reachable from the UI.

7. **Subagent bug hunt** (after everything above): use the `explore` and `general` subagents to
   find bugs, fix everything they report, test every corner.

8. **A random test extension** — write a real, working sample extension that uses several
   capabilities, to prove the generated project is genuinely usable.

9. **Time gate: do not finish before 19:00.** Check `Get-Date`. Keep going until then even if
   the list is exhausted — look for more bugs, more tests, more parity.

## Gotchas learned

- `Set-Content` writes CRLF, so multi-line `.Replace()` patterns often fail silently. Prefer the
  `edit` tool for exact string replacement. Always verify with `Select-String` afterwards.
- The test project cannot reference `DeckForge.App` (WPF). Testable logic must live in
  `DeckForge.Core`. This has already bitten `PermissionRow` and `DocsSnapshotService`.
- `Get-Process DeckForge | Stop-Process -Force` if the build fails with MSB3026 file locks.
- PowerShell here is pwsh 7; `Select-String -Pattern` with `|` and quotes is fussy — prefer
  `grep` tool or simple patterns.
