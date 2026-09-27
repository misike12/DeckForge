# Extending DeckForge

DeckForge is deliberately built so that **new features are additive**. Almost everything
you will want to add - a capability editor, a new tool button, a docs section, a build
step - is a new class plus one registration, not a change to existing code.

Read this once, then use the checklists below.

---

## Architecture in one screen

```
DeckForge.Core        Domain model: options, manifest, workspace, capability catalog,
                      Macro Deck rules, extension contract (no WPF - testable everywhere)
DeckForge.CodeGen     Generation: stock template factory + IProjectContentContributor pipeline
DeckForge.Validators  Native linters (manifest, localization) mirroring the macrodeck-plugin CLI
DeckForge.CliAdapter  ProcessRunner + typed wrappers: dotnet, macrodeck-plugin, (git/gh later)
DeckForge.App         WPF-UI shell, liquid theme, pages, view models, DI composition root
DeckForge.Tests       Unit tests for all of the above
```

Dependency direction is strictly left-to-right: `App -> {CodeGen, Validators, CliAdapter} -> Core`.

Two extension seams do most of the work:

| Seam | Contract | Where it runs |
|---|---|---|
| Generation contributors | `IProjectContentContributor` | New-project wizard + "add capability" flows |
| Page registry | `PageRegistry.Create(tag)` + DI | MainWindow navigation |

---

## Adding a capability editor (the most common change)

A Macro Deck capability (e.g. Variables, Events, Music Players) needs three pieces:

### 1. Catalog entry (DeckForge.Core)

`src/DeckForge.Core/Capabilities/CapabilityCatalog.cs` - append one descriptor:

```csharp
public static readonly CapabilityDescriptor MyThing = new()
{
    Id = "my-thing",                       // stable machine id
    Name = "My Thing",
    Summary = "One line shown on the gallery card.",
    Category = CapabilityCategory.Data,
    Interface = "IMyThingProvider",        // SDK interface the scaffold implements
    Permissions = ["host:whatever"],       // suggested manifest permissions
    DocsPath = "features/my-thing",        // docs.macro-deck.app page
    Glyph = "\uE945",
};
```

Add it to the `All` list. The gallery, docs links and permission pickers all read from
here - there is no other registry.

### 2. Generator contributor (DeckForge.CodeGen)

```csharp
public sealed class MyThingContributor : IProjectContentContributor
{
    public string Id => "my-thing";
    public int Order => 100;
    public IReadOnlyList<string> PresetIds => ["my-thing"];

    public void Contribute(ProjectContentBuilder builder, NewProjectOptions options)
    {
        var p = builder.ProjectName;

        builder.AddFile($"src/{p}/MyThingProvider.cs", /* generated C# */ """
            using MacroDeck.Sdk;

            namespace $RootNamespace;

            public sealed class MyThingProvider : IMyThingProvider
            {
                // ...
            }
            """);

        builder.ExtraIntegrationInterfaces.Add("IMyThingProvider");
        builder.ExtraIntegrationUsings.Add("MacroDeck.Sdk.MyThings");
        foreach (var key in MyThingStrings())
        {
            builder.AddStringKey(key.Key, key.Value);
        }
    }
}
```

Register it in `App.xaml.cs`: `services.AddSingleton<IProjectContentContributor, MyThingContributor>();`

Notes:
- Templates are **non-interpolated raw strings** with `$Token` placeholders filled by
  `MacroDeckTemplateFactory.Fill` (raw interpolated strings cannot escape braces).
  Use `$RootNamespace` and `$ProjectName`; the builder's `Fill` handles the rest.
- The stock files are order 0; your contributor (order 100) can override any file by
  re-adding it - e.g. rewrite `PluginIntegration.cs` to implement the new interface.
- `builder.AddStringKey(...)` lands in `Localization/Strings.resx` automatically;
  dotted keys become the generated `Strings` class hierarchy.

### 3. UI (DeckForge.App)

Add a page (or a section inside the capability gallery page), then:

- `src/DeckForge.App/Pages/MyThingPage.xaml` + `.cs`
- register in DI: `services.AddTransient<MyThingPage>();`
- add one line in `PageRegistry.Create`: `"my-thing" => ...GetService(typeof(MyThingPage))...`
- add the nav item in `MainWindow.xaml` (or a card on the gallery page)

Everything else - validation, docs links, permission suggestions - comes from the
catalog entry you wrote in step 1.

---

## Adding a page

1. Create `Pages/MyPage.xaml` + code-behind (inherit `Page`, take your VM via ctor).
2. ViewModel in `ViewModels/` using CommunityToolkit `[ObservableProperty]`/`[RelayCommand]`.
3. DI: `services.AddTransient<MyPage>();` (+ the VM if injected).
4. `PageRegistry.Create`: add the tag mapping.
5. `MainWindow.xaml`: add `ui:NavigationViewItem` with `Tag` and `Click="Nav_Click"`.

## Adding a validator

Native validators give instant feedback before the CLI runs:

- Manifest rules -> `src/DeckForge.Validators/ManifestValidator.cs` (codes mirror the
  CLI's kebab-case ids; add a test in `ValidatorsTests.cs`).
- New file kinds get their own static class + `ValidationResult` usage.

The CLI remains authoritative; native validators are the fast path.

## Adding a tool command (Build & Run page)

1. Add a method to `MacroDeckCli` / `DotNetCli` in `DeckForge.CliAdapter` that returns
   `Task<ProcessResult>` (check `macrodeck-plugin <cmd> --help` for real flags - the
   adapter documents only verified flags).
2. Add `[RelayCommand] private async Task MyThingAsync(CancellationToken ct)` to
   `BuildRunViewModel`, calling `RunStreamingAsync(label, () => _cli.MyThingAsync(...))`.
3. Add the button in `BuildRunPage.xaml`.

## Adding settings

`AppSettings` (Services/SettingsService.cs) is the store; add the property, bind it in
SettingsPage, apply it in `App.ApplyTheme` or the relevant service. Settings persist to
`%LOCALAPPDATA%/DeckForge/settings.json`.

## Extending the theme

All pages use `Liquid.*` resources only - never hard-coded colors. Add tokens in
`Themes/LiquidTheme.cs` (dark and light variants + accent-derived washes), then reference
`{StaticResource Liquid.MyToken}`.

---

## Conventions worth keeping

- **Generated projects stay 100% stock Macro Deck template.** DeckForge writes its own
  state only under `.deckforge/` in the workspace.
- **The macrodeck-plugin CLI is authoritative.** Parse its JSON output where available
  (`validate --output Json`); never reimplement protocol behavior.
- **SDK version pin lives in `MacroDeckSdkInfo`** (Core). Bump `DefaultVersion` when
  Macro Deck releases; beta > preview in SemVer order, so pin exact versions.
- **Localization keys are dotted** and named after where they are used; reuse
  `MacroDeckStrings.Common.*` before adding new keys.
- **Tests mirror the docs.** When you encode a documented rule (id grammar, SemVer,
  entrypoint layout), add the test cases next to the implementation.

## Verification checklist for any change

```bash
dotnet build DeckForge.slnx          # clean build, 0 warnings
dotnet test tests/DeckForge.Tests    # all green
```

For generator changes, additionally regenerate a scratch plugin and run the real CLI:

```bash
macrodeck-plugin build --source src/<Plugin> --output artifacts --force
macrodeck-plugin validate --artifact artifacts/<id>-<version>.macroDeckPlugin
macrodeck-plugin test --project src/<Plugin>
```
