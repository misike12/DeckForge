# SESSION-STATE.md

Persistent context for work on DeckForge. Read this before changing anything.

## What DeckForge is

A C#/.NET 10 WPF IDE that generates [Macro Deck 3](https://macro-deck.app/) plugins. It is a
separate product: it does not embed, wrap or rebrand Macro Deck, and it ships no Macro Deck
source. The Macro Deck plugin template is the ground truth for what a generated project should
look like, and the SDK is the ground truth for what that code may contain.

## Hard constraints

- **SDK pin is exactly `3.0.0-beta.14`.** The installed CLI reports
  `3.0.0-beta.14+088befbf36036b18c52aaaf5ba9597a413efd0db`. Verify flags with
  `macrodeck-plugin <verb> --help`; never guess a flag.
- **Never copy Macro Deck branding, source or licences.** Match generated output for *fidelity*;
  keep DeckForge's own identity, documentation and licence text.
- **PowerShell `Set-Content` converts multiline replacements to CRLF.** Prefer the `edit` tool and
  verify afterwards. A whole-file `[IO.File]::WriteAllText` round-trip is safe; a
  `$lines[$a..$b] = $lines[$x..$y]` assignment is a PowerShell type error.
- **WPF App logic cannot be referenced from `tests/DeckForge.Tests`.** Move testable logic into
  `DeckForge.Core` or `DeckForge.CodeGen`. The App project is Windows-only and pulls in WPF.
- **A running DeckForge process holds build outputs.** If a build fails with `MSB3021`/`MSB3027`
  "being used by another process", kill `DeckForge.exe` and retry. That is a stale process, not a
  bug.
- **`Process.Responding` does not mean the app started.** A WPF application whose `OnStartup`
  threw still has a live dispatcher, so it reports `Responding = True` with no window on screen.
  It is not a smoke test. To check that a window appeared, enumerate top-level visible windows for
  the process (`EnumWindows` + `IsWindowVisible`), or just look. This cost a real bug: a
  `ui:SymbolIcon` naming a `SymbolRegular` member that does not exist made `MainWindow`'s XAML throw
  during startup, the crash handler swallowed it, and the result was a process in Task Manager and
  nothing else - which was then reported as a passing smoke test.
- **A `ui:SymbolIcon` name is an enum member the compiler cannot check.** `XamlMarkupTests` verifies
  every one against the real `SymbolRegular` enum. If you add an icon, that test has to pass.
- **A UI-thread exception before the window is shown is fatal, not survivable.** There is nowhere to
  show the banner the crash handler would use, so `App` now shows a native message box and exits
  with 71. Swallowing it produces a process with no window and no symptom, which is worse than a
  crash.
- `macrodeck-plugin test`'s conformance check **MDC0604 is flaky** - a 15 s shutdown assertion. The
  same binary passed, failed and passed across three consecutive runs. Do not chase it.

## Verifying

```
dotnet build DeckForge.slnx -v q --nologo
dotnet test tests\DeckForge.Tests --nologo
dotnet run --project tools/TemplateParity -- <official-template-dir> <output-dir>
```

`tools/TemplateParity` generates a reference project and diffs it against a copy of the official
template, listing intentional deviations with their reason and exiting non-zero on anything else.
Scaffold the reference with the real CLI, then pin it to beta.14 for comparison.

For a real end-to-end check of a generated plugin:

```
cd <generated>
dotnet build <name>.slnx
macrodeck-plugin build --source src/<Name> --output artifacts --force
macrodeck-plugin validate --artifact artifacts/<id>-1.0.0.macroDeckPlugin --level publication
macrodeck-plugin test --project src/<Name>
```

Note `validate --manifest` against `src/<Name>/manifest.json` always reports
`entrypoint-missing`: the manifest names a RID-specific `runtimes/win-x64/<Name>.dll` that only
exists after `macrodeck-plugin build`. Validate the artifact, or the build output, not the source
tree.

Current state: **344 tests pass, 0 fail.** Template parity: no unexpected differences. A generated
project builds, packs, validates at publication level, and passes 25/25 conformance checks.

To confirm the application actually opens, start the built exe and check for a visible top-level
window - not `Process.Responding`:

```
src\DeckForge.App\bin\Debug\net10.0-windows\DeckForge.exe
```

If it starts and shows nothing, `%LOCALAPPDATA%\DeckForge\crash.log` has the reason; since the
startup handler was hardened, a failure now also produces a message box and exit code 71.

## Architecture notes

- **Two raw-string whitespace traps.** A raw string's closing delimiter defines the common prefix,
  so a stray tab in the body changes what the whole literal means. And a raw string drops the final
  newline, while every official file ends with one - so a template that closes its delimiter
  immediately is one byte short. Several templates end with a deliberate blank line for this
  reason; do not "tidy" them.
- **Tab-indented templates are converted, not written with tabs.** `MacroDeckTemplateFactory.Tabbed`
  turns space-indented literals into tabs once, explicitly, so each emitted file's indentation is a
  property of code rather than of invisible characters. Three official files are tab-indented
  (`LogMessageAction.cs`, `Assets/icon.svg`, the generated test source); the rest are spaces.
- **`IntegrationPatcher.IndentOf` describes the line *before* the index**, so for a member
  declaration - always at a line start - it returns nothing. `ActionContextPatcher.LineIndent` and
  `BlockCompiler.LineIndentOf` exist for that reason. Use those.
- **The host context is carried by hand.** The SDK gives `IIntegrationContext` to exactly one
  place, `IIntegration.InitializeAsync`, and `IActionDefinition.CreateExecutor()` takes no
  parameters, so an action cannot be given the host by the framework.
  `ActionContextPatcher` wires it: an action implements `IIntegrationContextAware`, the integration
  hands the context to every action that implements it, the action passes it to its executor. It is
  idempotent (regeneration re-applies every patch) and reports the anchor it failed on rather than
  returning null.
- **Compile tests must write into `src/<Project>/`.** Writing to the solution root puts the file
  outside every `.csproj` glob, and the test then passes on a build that never saw the generated
  code. Five assertions were vacuous this way for a long time. They also have to pass the
  generators' resx entries, or every `Strings.*()` accessor is a CS0117 that has nothing to do with
  the code under test.
- **A resx key segment is a type name.** The SDK's localization generator turns a dotted key into
  nested classes, so a segment that is a C# keyword comes out as `@int` and is referenced as
  `_int`; a contextual keyword like `file` cannot be a type at all (CS9056). `CSharpCode.ResxSegment`
  handles both. The catalog's `UiNodeCatalog.Name` values are the *wire* names, and the emitted C#
  member is their Pascal-case form.
- **`UiNodeCatalogTests` verifies the widget node catalog against `MacroDeck.Ui` by reflection.** The
  24 types and 235 properties were transcribed by hand; a name or kind that does not match the SDK
  fails there rather than in a user's plugin.
- **`BlockProgramWriter` owns the canvas save.** It does the three fallible steps - wire the host
  context, splice the region in the file's own indentation, make the executor `async` - and reports
  which one failed. It also refuses a canvas whose variable names collide with names already in
  scope, because renaming the declaration would leave `If` blocks reading the host's variable and
  silently testing the wrong value.
- **Extensions are off by default.** Loading one runs third-party code in this process with this
  process's permissions, and a .NET plugin cannot be sandboxed. `ExtensionService` contains a
  failure per extension so one bad assembly cannot stop the IDE; the Extensions page says the trust
  position plainly rather than implying a boundary that does not exist.

## Gotchas that cost time

- **A C# raw string cannot carry a trailing comma before `)`.** That is a CS1525, and building the
  argument list by appending `,` to each line produces one.
- **A trailing comma before `]` in a collection expression is fine**; before `)` in an argument list
  it is not.
- **`CSharpCode.NumberLiteral` accepts `NaN` and `Infinity`** and emits them as bare identifiers, so
  a range bound carrying one is a CS0103. No live path yet; be careful before wiring one.
- **`macrodeck-plugin test --project` needs a real build**; it reports conformance against the
  compiled subject.
- The official template's `Directory.Packages.props` floats `3.0.0-*` and a fresh restore resolved
  to `preview.10` from the local cache, not `beta.14`. Pin it when comparing, or the comparison is
  against a different SDK than the one DeckForge targets.

## Where things live

| Area | Path |
|---|---|
| Template factory, token table, parity-critical literals | `src/DeckForge.CodeGen/Generation/MacroDeckTemplateFactory.cs` |
| Host hand-off patcher | `src/DeckForge.CodeGen/Generation/ActionContextPatcher.cs` |
| Block compiler and the `locals` tracker | `src/DeckForge.CodeGen/Generation/BlockCompiler.cs` |
| Canvas save | `src/DeckForge.CodeGen/Generation/BlockProgramWriter.cs` |
| Action generator (the maintained one) | `src/DeckForge.CodeGen/Generation/ActionGenerator.cs` |
| Action parameter emitter and the 29-type table | `src/DeckForge.Core/Code/ActionParameterFactory.cs`, `ActionParameterTypes.cs` |
| Widget generator | `src/DeckForge.CodeGen/Generation/WidgetGenerator.cs` |
| Widget node catalog (reflected in tests) | `src/DeckForge.Core/Widgets/UiNodeCatalog.cs` |
| Extension loader and contracts | `src/DeckForge.Core/Extensions/` |
| Validators | `src/DeckForge.Validators/` |
| Real-CLI wrapper, process runner, doctor | `src/DeckForge.CliAdapter/` |
| Block canvas view model and page | `src/DeckForge.App/ViewModels/BlockActionViewModel.cs`, `Pages/BlockActionPage.xaml` |
| Extensions page | `src/DeckForge.App/Pages/ExtensionsPage.xaml` |
| Parity harness | `tools/TemplateParity/` |
| Compile tests that must write into the project | `tests/DeckForge.Tests/GeneratedCodeCompilesTests.cs` |

## Still open

From the second review pass, verified but not yet fixed - all are real, none is a regression:

- `ActionsEditorViewModel.Generate` writes the `.cs` before the resx and the integration patch, so a
  failure in either leaves a half-written action that a retry then refuses to overwrite.
- `ActionsEditorViewModel`'s `ParameterSpec` defaults are duplicated in the test fixture rather than
  shared; if a default changes, the compile test silently stops reproducing it.

Fixed since that pass, each with a test in `OpenFindingsTests`: `WorkspaceManager.FromPluginProject`
now finds the solution by walking up and passes the plugin's real location through, and trims both
separators; `WorkspaceManager.Open` creates the state directory before publishing `Current`;
`ResxMerger.ReadKeys` keeps the first of a duplicate key instead of throwing; the version-range
grammar accepts a bare `3.0.0`; `AppSettings.RepairNulls` replaces a JSON null list;
`CSharpCode.XmlUrl` no longer double-escapes `&`; `CSharpCode.Xml` neutralises newlines;
`ManifestValidator.IsBase64` caps its `stackalloc`; `DocsSnapshotService.CurrentMeta` reads a
non-numeric `pageCount` as 0 and catches what it can throw.

## Testing the parts that are not reachable from a test

Three checks cannot be written as unit tests, and each has been replaced with something that is:

- **Layout.** `XamlMarkupTests` reads the markup rather than loading it: no row or column outside the
  grid's own definitions, no row of fixed columns over 900px, no binding written as a method call, no
  attribute value split across source lines, and every `ui:SymbolIcon` naming a real enum member. The
  rest is done by driving the real window - see below.
- **The window itself.** `Process.Responding` cannot tell whether a WPF window appeared, so a
  temporary driver under `%TEMP%\uidriver` finds the real top-level window, resizes it, types into
  it, clicks it, and screenshots it. `GetWindowDC` + `BitBlt` misses everything DWM composites, so
  WebView2 pages (Icon Studio, Widget Designer, Docs) need its `screen` command, which captures the
  desktop instead. Its coordinates are window-relative and 1:1; the fullscreen capture is DPI-scaled
  and is not.
- **The extension point.** `extensions/DeckForge.SampleExtension` is a real assembly outside `src/`
  referencing only `DeckForge.Core`. A test loads its DLL through `Assembly.LoadFrom` and asserts the
  hook is answered, so a change that made the contract reachable only from inside the app would fail.


## Deliberate deviations from the official template

Listed by `tools/TemplateParity` with the reason for each. In short: DeckForge's own licence,
README, `AGENTS.md` and `CLAUDE.md`; an SDK pin instead of a float; the `IIntegrationContextAware`
file and the hand-off wired into the example action and the integration; `AssemblyName` and
`RootNamespace` pinned in the project file; and the manifest fields the wizard collected.
