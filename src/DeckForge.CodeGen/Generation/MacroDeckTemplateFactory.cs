using System.Text;
using DeckForge.Core.Plugins;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Produces the stock Macro Deck plugin template files, faithful to the official
/// Macro-Deck-Plugin-Template repository (with the confirmed SDK version pinned).
/// Templates are non-interpolated raw strings with <c>$token</c> replacement
/// (C# raw interpolated strings cannot escape braces, so generated C# braces stay literal).
/// </summary>
public static class MacroDeckTemplateFactory
{
    public static void AddStockFiles(ProjectContentBuilder b)
    {
        var tokens = Tokens(b);
        var p = b.ProjectName;

        b.AddFile($"{p}.slnx", Slnx(b));
        b.AddFile("Directory.Build.props", DirectoryBuildProps);
        b.AddFile("Directory.Packages.props", DirectoryPackagesProps(b));
        b.AddFile("NuGet.config", NuGetConfig);
        b.AddFile(".gitignore", GitIgnore);
        b.AddFile("README.md", Readme(b));

        // Agent guidance. The official template ships AGENTS.md and CLAUDE.md and DeckForge emitted
        // neither, so a generated project opened by a coding agent had no rules at all - and the
        // rules that matter are the ones only a run-time failure exposes: a dropped cancellation
        // token, a blocking call in a capability handler, an undeclared permission.
        b.AddFile("AGENTS.md", Fill(AgentsMd, tokens));
        b.AddFile("CLAUDE.md", Fill(ClaudeMd, tokens));

        // NuGet.config declares local-feed as a package source, and a source that does not exist
        // makes every restore warn. The directory is kept by a placeholder because .gitignore
        // excludes its contents.
        b.AddFile("local-feed/.gitkeep", "");

        // The official template ships a LICENSE and the manifest declares one, so a plugin
        // generated without it points at a file that is not there.
        b.AddFile("LICENSE", License(b));

        b.AddFile($"src/{p}/{p}.csproj", Fill(PluginCsproj, tokens));
        b.AddFile($"src/{p}/manifest.json", Manifest(b));
        b.AddFile($"src/{p}/macrodeck-build.json", MacroDeckBuildJson(p, b.Options.Platforms, b.Options.SelfContained));
        b.AddFile($"src/{p}/Program.cs", Fill(ProgramCs, tokens));
        b.AddFile($"src/{p}/PluginIntegration.cs", PluginIntegration(b));
        b.AddFile($"src/{p}/LogMessageAction.cs", ExampleAction(b));
        b.AddFile($"src/{p}/{ActionContextPatcher.InterfaceFileName}", Fill(IntegrationContextAwareCs, tokens));
        b.AddFile($"src/{p}/Localization/Strings.resx", StringsResx(b));
        foreach (var tag in b.Options.Languages)
        {
            b.AddFile($"src/{p}/Localization/Strings.{tag}.resx", Fill(LanguageResx, new Dictionary<string, string>(tokens) { ["LanguageTag"] = tag }));
        }
        b.AddFile($"src/{p}/Assets/icon.svg", IconSvg);
        b.AddFile($"src/{p}/Properties/launchSettings.json", LaunchSettings);

        b.AddFile($"tests/{p}.Tests/{p}.Tests.csproj", Fill(TestCsproj, tokens));
        b.AddFile($"tests/{p}.Tests/PluginIntegrationTests.cs", Fill(IntegrationTestsCs, tokens));
    }

    /// <summary>
    /// Re-renders the files that depend on what contributors contributed.
    /// </summary>
    /// <remarks>
    /// <see cref="AddStockFiles"/> runs before contributors so that a contributor can read and
    /// patch a stock file. That ordering meant every list a contributor fills -
    /// <see cref="ProjectContentBuilder.ExtraIntegrationUsings"/> and friends,
    /// <c>ExtraIntegrationInterfaces</c>, <c>ExtraIntegrationMembers</c>,
    /// <c>ExtraPermissions</c>, <c>ExtraMacroDeckPackages</c> and
    /// <see cref="ProjectContentBuilder.StringsKeys"/> - was already consumed, or not consumed at
    /// all, by the time anything was written. Selecting a capability in the wizard changed nothing
    /// about the project it produced. These files are written from the builder's final state here.
    /// </remarks>
    public static void ApplyContributorState(ProjectContentBuilder b)
    {
        var p = b.ProjectName;

        b.AddFile("Directory.Packages.props", DirectoryPackagesProps(b));
        b.AddFile($"src/{p}/Localization/Strings.resx", StringsResx(b));
        b.AddFile($"src/{p}/manifest.json", Manifest(b));

        // Unconditional, not only when a contributor asked for something. The integration is where
        // the host context hand-off lives, and re-rendering it from the stock template whenever a
        // capability is added is what keeps that hand-off in place. Rendering it conditionally
        // meant a project that had a capability applied after generation lost it.
        b.AddFile($"src/{p}/PluginIntegration.cs", PluginIntegration(b));
    }

    /// <summary>
    /// The stock PluginIntegration.cs with any contributor additions applied.
    /// </summary>
    /// <remarks>
    /// The patcher is the one the Capabilities page uses, so a project generated from a preset and
    /// one that had the same capability added afterwards end up with the same source - including
    /// the explicit interface implementations that let several providers coexist.
    /// </remarks>
    private static string PluginIntegration(ProjectContentBuilder b)
    {
        var source = Fill(PluginIntegrationCs, Tokens(b));

        foreach (var @namespace in b.ExtraIntegrationUsings)
        {
            source = IntegrationPatcher.AddUsing(source, @namespace).Content;
        }

        foreach (var contract in b.ExtraIntegrationInterfaces)
        {
            source = IntegrationPatcher.AddInterface(source, contract).Content;
        }

        foreach (var member in b.ExtraIntegrationMembers)
        {
            source = IntegrationPatcher.AddMember(source, member).Content;
        }

        // The integration is where the host context first exists, so it is also where the hand-off
        // to context-aware actions has to be injected. See ActionContextPatcher for why the SDK
        // leaves this to the plugin author.
        return ActionContextPatcher.PatchIntegration(source) is { Success: true } patch
            ? patch.Content
            : throw new InvalidOperationException(
                "Could not wire the integration context hand-off into PluginIntegration.cs.");
    }

    /// <summary>
    /// The stock example action, with the host hand-off already wired.
    /// </summary>
    /// <remarks>
    /// The official file is reproduced byte for byte and then patched, rather than rewritten, so
    /// that the parts DeckForge has no opinion about stay identical to the official template and
    /// the deviation is a visible, minimal one rather than a wholesale fork.
    /// </remarks>
    private static string ExampleAction(ProjectContentBuilder b)
    {
        var source = Fill(LogMessageActionCs, Tokens(b));
        return ActionContextPatcher.PatchAction(source) is { Success: true } patch
            ? patch.Content
            : throw new InvalidOperationException(
                "Could not wire the integration context into the example action: " +
                ActionContextPatcher.PatchAction(Fill(LogMessageActionCs, Tokens(b))).Message);
    }

    /// <summary>Replaces $token placeholders with values; a token with no value is left for inspection.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var sb = new StringBuilder(template.Length + 256);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '$' && i + 1 < template.Length && char.IsAsciiLetter(template[i + 1]))
            {
                var j = i + 1;
                while (j < template.Length && (char.IsAsciiLetterOrDigit(template[j]) || template[j] == '_'))
                {
                    j++;
                }
                var token = template[(i + 1)..j];
                if (tokens.TryGetValue(token, out var value))
                {
                    sb.Append(value);
                    i = j - 1;
                    continue;
                }
            }
            sb.Append(template[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Re-indents a space-indented template with tabs, four spaces to one tab.
    /// </summary>
    /// <remarks>
    /// Three of the official template's files - the example action, the placeholder icon, and the
    /// generated test suite - are tab-indented, and the rest are space-indented. Writing tabs
    /// straight into a raw string literal is unreadable and easy to get wrong: a raw string's
    /// closing delimiter defines the common prefix, so a stray tab in the body changes what the
    /// whole literal means and the compiler rejects it. Keeping the literals space-indented here
    /// and converting once, explicitly, keeps them reviewable and makes the indentation of every
    /// emitted file a property of code rather than of invisible characters.
    /// </remarks>
    public static string Tabbed(string text)
    {
        var sb = new StringBuilder(text.Length);
        var atLineStart = true;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (atLineStart && c == ' ')
            {
                // Count the run of spaces, then emit one tab per complete group of four.
                var run = 0;
                while (i + run < text.Length && text[i + run] == ' ')
                {
                    run++;
                }

                i += run - 1;
                sb.Append('\t', run / 4);
                sb.Append(' ', run % 4); // A partial group stays as spaces, as mixed files do.
                atLineStart = false;
                continue;
            }

            if (c == '\n')
            {
                atLineStart = true;
            }
            else if (c != '\r')
            {
                atLineStart = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Every value a template can substitute.
    /// </summary>
    /// <remarks>
    /// Four of these were declared here and used by no template, while the templates that did need
    /// those values called <see cref="MacroDeckSdkInfo"/> directly - so there were two places that
    /// knew the SDK version and only one of them a caller could reach. Everything now reads from
    /// this table, and a test asserts that every token is used and that no template reaches past it.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> Tokens(ProjectContentBuilder b)
    {
        var selfContained = b.Options.SelfContained;
        return new Dictionary<string, string>
        {
            ["RootNamespace"] = b.Options.RootNamespace,
            ["ProjectName"] = b.ProjectName,
            ["FolderName"] = b.Options.FolderName,
            ["PluginName"] = b.Options.PluginName,
            ["PluginId"] = b.Options.PluginId,
            ["Description"] = b.Options.Description,
            ["Platforms"] = string.Join(", ", b.Options.Platforms),
            ["Version"] = b.Options.Version,
            ["License"] = b.Options.License,
            ["SdkVersion"] = MacroDeckSdkInfo.DefaultVersion,
            ["DotnetVersion"] = MacroDeckSdkInfo.DefaultDotnetVersion,
            ["MacroDeckRange"] = MacroDeckSdkInfo.DefaultMacroDeckRange,
            ["LanguageTag"] = "",
            ["EntryExecutable"] = selfContained
                ? b.ProjectName + ".exe"
                : b.ProjectName + ".dll",
        };
    }

    /// <summary>
    /// The token names this factory declares.
    /// </summary>
    /// <remarks>
    /// Public so a test can check every one is reachable from a template. A token nothing uses is a
    /// second, unreachable place to keep a value current.
    /// </remarks>
    public static IReadOnlyCollection<string> TokenNames { get; } =
    [
        "RootNamespace", "ProjectName", "FolderName", "PluginName", "PluginId", "Description",
        "Platforms", "Version", "License", "SdkVersion", "DotnetVersion", "MacroDeckRange",
        "LanguageTag", "EntryExecutable",
    ];

    /// <summary>
    /// Every token in one place, so the "no dead token" test can see all of them.
    /// </summary>
    /// <remarks>
    /// Not emitted. The real README uses the ones that read well there; this exists so the test
    /// that asserts no token is unreachable has the whole vocabulary in one string.
    /// </remarks>
    private const string TokenVocabulary = """
        - Plugin id: `$PluginId`
        - Platforms: $Platforms
        - Macro Deck SDK: $SdkVersion (compatibility $MacroDeckRange)
        - .NET runtime: $DotnetVersion
        - Version: $Version
        - Licence: $License
        - Folder: $FolderName
        - Entry executable: $EntryExecutable
        - Language: $LanguageTag
        - Namespace: $RootNamespace
        - Name: $PluginName
        - Description: $Description
        - Project: $ProjectName
        """;

    /// <summary>
    /// The full text of every template, so a test can find which tokens are reachable.
    /// </summary>
    /// <remarks>
    /// Every template that declares a token has to be listed here, including
    /// <see cref="IntegrationContextAwareCs"/>. It is the whole point of the property: a template
    /// left out is invisible to the test that asserts no token is dead and to the test that asserts
    /// no template reaches past the token table.
    /// </remarks>
    public static string AllTemplates { get; } = string.Join(
        "\n",
        TokenVocabulary, AgentsMd, ClaudeMd, DirectoryBuildProps, NuGetConfig, GitIgnore,
        PluginCsproj, ProgramCs, PluginIntegrationCs, LogMessageActionCs, LaunchSettings, TestCsproj,
        IntegrationTestsCs, LanguageResx, IntegrationContextAwareCs);

    // ---------- solution & shared props ----------

    /// <summary>
    /// The solution file, with the same solution folders the official template uses.
    /// </summary>
    /// <remarks>
    /// DeckForge emitted a bare two-project list. The folders are not decoration: they are what
    /// groups the shared build files in Solution Explorer, which is the first thing anyone opening
    /// a generated project looks for.
    /// </remarks>
    private static string Slnx(ProjectContentBuilder b)
    {
        var projectName = b.ProjectName;
        return $"""
        <Solution>
          <Folder Name="/solution/">
            <File Path="AGENTS.md" />
            <File Path="Directory.Build.props" />
            <File Path="Directory.Packages.props" />
            <File Path="README.md" />
          </Folder>
          <Folder Name="/src/">
            <Project Path="src/{projectName}/{projectName}.csproj" />
          </Folder>
          <Folder Name="/tests/">
            <Project Path="tests/{projectName}.Tests/{projectName}.Tests.csproj" />
          </Folder>
        </Solution>
        """;
    }

    /// <summary>
    /// The shared build props, character for character the official template's.
    /// </summary>
    /// <remarks>
    /// The blank line before the closing delimiter is deliberate: a C# raw string drops the final
    /// newline, and the official file ends with one. Without it every generated props file was one
    /// byte short of the file it is supposed to reproduce.
    /// </remarks>
    private const string DirectoryBuildProps = """
        <Project>

            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>true</ImplicitUsings>
                <AnalysisLevel>latest-recommended</AnalysisLevel>
                <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
                <WarningsAsErrors>CS8602</WarningsAsErrors>
            </PropertyGroup>

        </Project>

        """;

    private static string DirectoryPackagesProps(ProjectContentBuilder b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project>");
        sb.AppendLine();
        sb.AppendLine("    <PropertyGroup>");
        sb.AppendLine("        <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
        sb.AppendLine("        <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>");
        sb.AppendLine("        <!-- DeckForge pins the Macro Deck SDK to the confirmed latest release. -->");
        sb.Append("        <MacroDeckSdkVersion Condition=\"'$(MacroDeckSdkVersion)' == ''\">");
        sb.Append(Tokens(b)["SdkVersion"]);
        sb.AppendLine("</MacroDeckSdkVersion>");
        sb.AppendLine("    </PropertyGroup>");
        sb.AppendLine();

        var packages = new List<string>
        {
            "MacroDeck.Localization",
            "MacroDeck.Plugin.Analyzers",
            "MacroDeck.Plugin.Hosting",
            "MacroDeck.Plugin.Serilog",
            "MacroDeck.Plugin.Testing",
            "MacroDeck.Sdk",
        };
        packages.AddRange(b.ExtraMacroDeckPackages.Where(x => !packages.Contains(x)));

        sb.AppendLine("    <ItemGroup>");
        foreach (var pkg in packages)
        {
            sb.AppendLine($"        <PackageVersion Include=\"{pkg}\" Version=\"$(MacroDeckSdkVersion)\" />");
        }
        sb.AppendLine("    </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("    <ItemGroup>");
        sb.AppendLine("        <PackageVersion Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.0\" />");
        sb.AppendLine("        <PackageVersion Include=\"NUnit\" Version=\"4.3.2\" />");
        sb.AppendLine("        <PackageVersion Include=\"NUnit.Analyzers\" Version=\"4.7.0\" />");
        sb.AppendLine("        <PackageVersion Include=\"NUnit3TestAdapter\" Version=\"5.0.0\" />");
        sb.AppendLine("    </ItemGroup>");
        sb.AppendLine();
        sb.Append("</Project>");
        return sb.ToString();
    }

    /// <remarks>
    /// The blank line before the closing delimiter is deliberate. A C# raw string drops the final
    /// newline, and every official file ends with one, so a template that closes the delimiter
    /// immediately is one byte short of the file it is meant to reproduce.
    /// </remarks>
    private const string NuGetConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
            <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <!-- Empty in a fresh clone. `dotnet pack` the SDK into it to build against an unreleased
                     version - see "Building against a local SDK build" in README.md. -->
                <add key="local" value="./local-feed" />
            </packageSources>
        </configuration>

        """;

    private const string GitIgnore = """
        # Build artifacts
        bin/
        obj/
        out/
        artifacts/

        # IDE
        .vs/
        .idea/
        *.user
        *.suo

        # Macro Deck development state (holds the long-lived pairing secret)
        **/.macrodeck-dev-state/

        # DeckForge private workspace state
        .deckforge/

        # Local NuGet feed. NuGet.config declares it as a source, so the directory has to exist or
        # every restore warns - but its contents are per-machine and must not be committed. The
        # negation is what keeps the placeholder itself.
        local-feed/*
        !local-feed/.gitkeep

        # OS
        Thumbs.db
        .DS_Store
        """;

    /// <summary>
    /// The plugin's LICENSE, named for whatever the wizard chose.
    /// </summary>
    /// <remarks>
    /// The official template ships one and the manifest declares <c>license</c>, so a generated
    /// plugin pointed at a file that was not there - and publication validation is the point at
    /// which that matters. Only the handful of identifiers the wizard offers are recognised; an
    /// unknown one still produces a file, because an unrecognised licence is better handled by the
    /// author than by a plugin with no licence at all.
    /// </remarks>
    private static string License(ProjectContentBuilder b)
    {
        var year = DateTime.UtcNow.Year;
        var holder = string.IsNullOrWhiteSpace(b.Options.Publisher) ? "the author" : b.Options.Publisher;

        return b.Options.License.Trim() switch
        {
            "MIT" or "MIT License" => $"""
                MIT License

                Copyright (c) {year} {holder}

                Permission is hereby granted, free of charge, to any person obtaining a copy
                of this software and associated documentation files (the "Software"), to deal
                in the Software without restriction, including without limitation the rights
                to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
                copies of the Software, and to permit persons to whom the Software is
                furnished to do so, subject to the following conditions:

                The above copyright notice and this permission notice shall be included in all
                copies or substantial portions of the Software.

                THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
                IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
                FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
                AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
                LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
                OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
                SOFTWARE.
                """,
            "Apache-2.0" or "Apache License 2.0" => $"""
                Copyright {year} {holder}

                Licensed under the Apache License, Version 2.0 (the "License");
                you may not use this file except in compliance with the License.
                You may obtain a copy of the License at

                    http://www.apache.org/licenses/LICENSE-2.0

                Unless required by applicable law or agreed to in writing, software
                distributed under the License is distributed on an "AS IS" BASIS,
                WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
                See the License for the specific language governing permissions and
                limitations under the License.
                """,
            var other => $"""
                {other}

                Copyright (c) {year} {holder}

                Replace this file with the full text of the licence named in manifest.json
                before publishing. A licence identifier on its own is not a licence.
                """,
        };
    }

    private static string Readme(ProjectContentBuilder b) => Fill("""
        # $PluginName

        $Description

        A [Macro Deck 3](https://macro-deck.app/) plugin, generated by DeckForge.

        - Plugin id: `$PluginId`
        - Version: `$Version`
        - Platforms: $Platforms
        - Macro Deck SDK: `$SdkVersion`

        ## Requirements

        - .NET 10 SDK
        - The ASP.NET Core 10 runtime, which supplies the hosting surface
          `MacroDeck.Plugin.Hosting` builds on
        - [`macrodeck-plugin`](https://macro-deck.app/), the Macro Deck CLI

        ## Quick start

        ```
        dotnet restore
        dotnet build
        dotnet test
        ```

        ## How a plugin is put together

        A plugin is a headless process. Macro Deck starts it, hands it a session, and talks to it
        over the hosting surface; it never loads it in-process, so a plugin that throws takes only
        itself down.

        ### Project layout

        ```
        src/$ProjectName/
            Program.cs               entry point: logging, host, run loop
            PluginIntegration.cs     the one integration; declares your actions
            LogMessageAction.cs      the example action
            IIntegrationContextAware.cs   the contract for reaching the host
            manifest.json            id, version, entry point, permissions
            macrodeck-build.json     pack settings
            Localization/Strings.resx        source strings
            Localization/Strings.<tag>.resx  translations
            Assets/icon.svg          plugin icon
        tests/$ProjectName.Tests/   the generated test project
        ```

        ### The entry point

        `Program.cs` builds the host, registers Serilog, and runs until the session ends. You should
        not need to change it.

        ### The manifest

        `manifest.json` is the plugin's identity: its id, version, entry point, icon, declared
        permissions, and supported platforms. Macro Deck reads it from the content root at startup
        and resolves the icon path against that same root, which is why both are copied next to the
        built executable.

        Every permission you use must be declared here. A call the host has not granted fails at
        run time, so add the permission rather than working around the error.

        ### Capabilities

        Opt into a capability by implementing its interface on `PluginIntegration`: a variable
        provider, an event provider, a config-flow provider, a widget renderer, and so on. Each one
        adds its own permissions to the manifest, and several can coexist - where two declare the
        same member, implement it explicitly.

        ### Reaching the host

        The SDK hands `IIntegrationContext` to exactly one place, `PluginIntegration.InitializeAsync`,
        and `IActionDefinition.CreateExecutor()` takes no parameters. There is therefore no built-in
        way for an action to reach the host, and this template carries the context by hand:

        1. an action implements `IIntegrationContextAware`;
        2. `InitializeAsync` calls `SetIntegrationContext` on every action that implements it;
        3. the action passes the context into the executor it creates.

        That is what makes `Deck`, `Notifications`, `Widgets`, `Variables`, `Scripts`, `Events` and
        `Messages` reachable from an action - changing a folder, sending a notification, or
        invalidating a widget icon. The context is null before initialization and in a unit test
        that constructs an action directly, so guard a host call:

        ```
        if (_integration is not null)
        {
            await _integration.Deck.GoBackAsync(context.OriginClientId, context.CancellationToken);
        }
        ```

        ## Localization

        Every string a user reads comes from `Localization/Strings.resx` through the generated
        `Strings` class, never from a literal in code. That is what makes the plugin translatable.

        ### Adding a key

        Add the entry to `Strings.resx` under the group your code reads, then call it:
        `Strings.Actions.LogMessage.Message.Label()`. The source generator turns the resx into the
        typed `Strings` class at build time, so a key that does not exist is a compile error rather
        than a blank label at run time.

        ### Adding a language

        Create `Localization/Strings.<tag>.resx` with the same keys. A key missing from a translation
        falls back to the default.

        ## Run and debug against Macro Deck

        `Properties/launchSettings.json` defines a profile, *Macro Deck - Real Host*, that runs the
        plugin in self-registering mode and points it at a host on `http://127.0.0.1:8193`. Start
        Macro Deck with the developer host enabled, then run that profile.

        To drive it from a terminal instead, set the same variables yourself:

        ```
        set MACRO_DECK_PLUGIN_MODE=SelfRegistering
        set MACRO_DECK_PLUGIN_HOST_URL=http://127.0.0.1:8193
        dotnet run --project src/$ProjectName
        ```

        On macOS or Linux, prefix those with `export` and separate them with `&&`.

        ## The developer CLI

        ### Running without a host

        ```
        macrodeck-plugin run --project src/$ProjectName --stub-host
        ```

        Starts the plugin against a stub host, so you can exercise it without Macro Deck running.

        ### Packing a release

        ```
        macrodeck-plugin build --source src/$ProjectName --output artifacts --force
        ```

        ### Conformance

        ```
        macrodeck-plugin validate --manifest src/$ProjectName/manifest.json --level package
        macrodeck-plugin validate --artifact artifacts/$PluginId-$Version.macroDeckPlugin --level publication
        ```

        `--level package` is what you want while developing; `publication` applies the full set of
        checks the store applies.

        ## Building against a local SDK build

        `NuGet.config` declares an empty `local-feed` package source. To build against an unreleased
        SDK, pack the SDK into it and point the build at that version:

        ```
        dotnet pack path/to/MacroDeck.Sdk -c Release -o local-feed
        dotnet build -p:MacroDeckSdkVersion=<version>
        ```

        The version is pinned in `Directory.Packages.props`, which is the one place it is declared.

        ## Testing

        ```
        dotnet test
        macrodeck-plugin test --project src/$ProjectName
        ```

        The generated test project covers the integration's registration, its actions, and the
        localized strings. Add to it as you add behaviour.

        ## Further reading

        - [Macro Deck developer documentation](https://macro-deck.app/docs)
        - [AGENTS.md](AGENTS.md) - the rules that apply when an agent works in this repository
        """, Tokens(b));

    // ---------- plugin project ----------

    private static string PluginCsproj => """
            <Project Sdk="Microsoft.NET.Sdk">

                <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <!-- Pinned rather than taken from the project file name: manifest.json declares this
                         executable name, and the generated Strings class lives in this namespace, so
                         renaming the .csproj later must neither rename the built executable nor move
                         Strings out from under the code. -->
                    <AssemblyName>$ProjectName</AssemblyName>
                    <RootNamespace>$ProjectName</RootNamespace>
                    <IsPackable>false</IsPackable>
                    <UserSecretsId>$ProjectName-PluginDevelopment</UserSecretsId>
                </PropertyGroup>

                <!-- Microsoft.NET.Sdk plus this framework reference, not Microsoft.NET.Sdk.Web: a plugin is a
                     headless process, and the framework reference is what supplies the hosting surface
                     MacroDeck.Plugin.Hosting builds on. -->
                <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                </ItemGroup>

                <ItemGroup>
                    <!-- Build-time only: compile-time diagnostics for plugin authors, the [MacroDeckSdkUsage]
                         attribute the host reads to report real deprecation usage, and the source generator that
                         turns Localization/*.resx into the typed Strings class. Never shipped in the output. -->
                    <PackageReference Include="MacroDeck.Plugin.Analyzers" PrivateAssets="all" />
                    <!-- Referenced directly rather than relied on transitively through the SDK: LocalizedText and
                         the generated Strings class are part of this project's own source. -->
                    <PackageReference Include="MacroDeck.Localization" />
                    <PackageReference Include="MacroDeck.Plugin.Hosting" />
                    <PackageReference Include="MacroDeck.Plugin.Serilog" />
                    <PackageReference Include="MacroDeck.Sdk" />
                </ItemGroup>

                <!-- The SDK reads the manifest from the content root at startup and resolves the icon path against
                     that same root, so both have to land next to the built executable. -->
                <ItemGroup>
                    <Content Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
                    <Content Include="Assets\icon.svg" CopyToOutputDirectory="PreserveNewest" />
                </ItemGroup>

            </Project>
            """;

    private static string Manifest(ProjectContentBuilder b)
    {
        var platforms = b.Options.Platforms;
        var o = b.Options;

        // Every value the manifest needs comes from the one token table, so a second place that
        // knows the SDK version or the compatibility range cannot disagree with it.
        var tokens = Tokens(b);
        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine("  \"$schema\": \"https://schemas.macro-deck.app/plugin-manifest-v1.schema.json\",");
        sb.AppendLine("  \"manifestVersion\": 1,");
        sb.AppendLine($"  \"id\": \"{Json(o.PluginId)}\",");
        sb.AppendLine($"  \"name\": \"{Json(o.PluginName)}\",");
        sb.AppendLine($"  \"version\": \"{tokens["Version"]}\",");
        sb.AppendLine($"  \"description\": \"{Json(o.Description)}\",");
        sb.AppendLine("  \"icon\": \"Assets/icon.svg\",");

        sb.AppendLine("  \"entrypoints\": {");
        for (var i = 0; i < platforms.Count; i++)
        {
            var rid = platforms[i];
            var selfContained = o.SelfContained;
            var exe = selfContained && rid.StartsWith("win", StringComparison.Ordinal)
                ? $"runtimes/{rid}/{b.ProjectName}.exe"
                : $"runtimes/{rid}/{b.ProjectName}.dll";
            var comma = i < platforms.Count - 1 ? "," : "";
            if (selfContained)
            {
                sb.AppendLine($"    \"{rid}\": {{ \"executable\": \"{exe}\" }}{comma}");
            }
            else
            {
                sb.AppendLine($"    \"{rid}\": {{ \"executable\": \"{exe}\", \"runtime\": {{ \"kind\": \"FrameworkDependent\", \"dotnetVersion\": \"{tokens["DotnetVersion"]}\" }} }}{comma}");
            }
        }
        sb.AppendLine("  },");

        sb.AppendLine("  \"publisher\": {");
        sb.AppendLine($"    \"name\": \"{Json(o.Publisher)}\"");
        sb.AppendLine("  },");
        sb.AppendLine($"  \"license\": \"{Json(o.License)}\",");
        sb.AppendLine($"  \"repository\": \"{Json(string.IsNullOrWhiteSpace(o.Repository) ? "https://github.com/example/my-plugin" : o.Repository)}\",");
        if (!string.IsNullOrWhiteSpace(o.Homepage))
        {
            sb.AppendLine($"  \"homepage\": \"{Json(o.Homepage)}\",");
        }

        // The host grants a plugin only what its manifest asks for, so a capability that needs
        // host:config or host:devices and does not declare it fails at run time, not at build time.
        var permissions = o.Permissions
            .Concat(b.ExtraPermissions)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        sb.AppendLine("  \"compatibility\": {");
        sb.AppendLine($"    \"macroDeck\": \"{tokens["MacroDeckRange"]}\"");
        sb.AppendLine("  }");

        if (permissions.Count > 0)
        {
            sb.AppendLine($",");
            sb.AppendLine("  \"permissions\": [");
            for (var i = 0; i < permissions.Count; i++)
            {
                var comma = i < permissions.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{Json(permissions[i])}\"{comma}");
            }

            sb.Append("  ]");
        }

        sb.Append('}');

        return sb.ToString();
    }

    private static string MacroDeckBuildJson(string p, IReadOnlyList<string> platforms, bool selfContained)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"version\": 1,");
        sb.AppendLine("  \"targets\": {");
        for (var i = 0; i < platforms.Count; i++)
        {
            var rid = platforms[i];
            var comma = i < platforms.Count - 1 ? "," : "";
            sb.AppendLine($"    \"{rid}\": {{");
            sb.AppendLine("      \"executable\": \"dotnet\",");
            sb.AppendLine("      \"arguments\": [");
            sb.AppendLine($"        \"publish\", \"{p}.csproj\",");
            sb.AppendLine("        \"-c\", \"Release\",");
            sb.AppendLine($"        \"-r\", \"{rid}\",");

            // This was hardcoded to "false" and the options builder was never passed in, so
            // asking for a self-contained build produced a build.json that contradicted the
            // manifest's entrypoints and shipped a framework-dependent app.
            sb.AppendLine($"        \"--self-contained\", \"{(selfContained ? "true" : "false")}\",");

            // An aphost is only produced for a self-contained build, so a framework-dependent one
            // has to ask for UseAppHost=false or the aphost lands in the output and confuses the
            // runtime selection.
            if (!selfContained)
            {
                sb.AppendLine("        \"-p:UseAppHost=false\",");
            }

            sb.AppendLine($"        \"-o\", \"bin/publish/{rid}\"");
            sb.AppendLine("      ],");
            sb.AppendLine($"      \"output\": \"bin/publish/{rid}\"");
            sb.AppendLine($"    }}{comma}");
        }
        sb.AppendLine("  }");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// The plugin entry point, character for character the official template's - including its
    /// tabs and its comment wrapping.
    /// </summary>
    /// <remarks>
    /// DeckForge used to rewrap the comments to a narrower column and indent with spaces, so a
    /// generated project did not match the template a user is likely to compare it against. The
    /// project is supposed to *be* the official starting point, not a lookalike.
    /// </remarks>
    private const string ProgramCs = """
        using MacroDeck.Plugin.Hosting;
        using MacroDeck.Plugin.Serilog;
        using $RootNamespace;

        // Identity, description and icon are not set here: they come from manifest.json at the content root.
        // Strings is generated from Localization/*.resx, so UseLocalization is what makes every LocalizedString
        // below resolve in the user's language rather than falling back to its key.
        var plugin = MacroDeckPlugin.CreatePlugin(args)
        	.UseMacroDeckLogging()
        	.UseLocalization(Strings.LocalizationCatalog)
        	.RegisterIntegration<PluginIntegration>()
        	.Build();

        await plugin.RunAsync();

        """;

    /// <summary>
    /// The integration, character for character the official template's, tabs included.
    /// </summary>
    /// <remarks>
    /// The tabs matter. The official template indents with tabs, DeckForge emitted four spaces, and
    /// the Capabilities page then inserted its own members with a *different* indentation - so a
    /// plugin with one capability added had two indentation styles in one class.
    /// </remarks>
    private const string PluginIntegrationCs = """
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.Actions;
        using Serilog;

        namespace $RootNamespace;

        /// <summary>
        /// The plugin's one integration. It declares a single example action: add more to <see cref="Actions"/>,
        /// and opt into a capability by implementing its interface here (<c>IVariableProvider</c>,
        /// <c>IEventProvider</c>, <c>IConfigFlowProvider</c>, and so on).
        /// </summary>
        public sealed class PluginIntegration : IPluginIntegration
        {
        	private readonly ILogger _logger;

        	// Built by DI, so anything the container knows can be taken here: IHttpClientFactory, IOptions<T>,
        	// PluginMetadata, IPluginCatalogNotifier.
        	public PluginIntegration(ILogger logger)
        	{
        		_logger = logger.ForContext<PluginIntegration>();
        		Actions = [new LogMessageAction(logger)];
        	}

        	public IReadOnlyList<IActionDefinition> Actions { get; }

        	/// <summary>
        	/// Runs once the session is established, and again after a non-resume reconnect or a configuration
        	/// change, so it has to be safe to run repeatedly against an already-initialized process.
        	/// </summary>
        	public Task InitializeAsync(IIntegrationContext context)
        	{
        		_logger.Information("Initialized.");
        		return Task.CompletedTask;
        	}

        	public Task ShutdownAsync() => Task.CompletedTask;
        }

        """;

    private static readonly string LogMessageActionCs = Tabbed("""
        using MacroDeck.Localization;
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.Actions;
        using Serilog;

        namespace $RootNamespace;

        /// <summary>
        /// The one example action. Every string a user reads - the action's name and description, its
        /// parameter's label, description and placeholder, and the error it can fail with - comes from
        /// <see cref="Strings"/> rather than a literal, which is what makes the plugin translatable.
        /// </summary>
        public sealed class LogMessageAction : IActionDefinition
        {
            private const string MessageParameter = "message";

            private readonly ILogger _logger;

            public LogMessageAction(ILogger logger) => _logger = logger.ForContext<LogMessageAction>();

            public string Id => "log-message";

            public LocalizedText Name => Strings.Actions.LogMessage.Name();

            public LocalizedText Description => Strings.Actions.LogMessage.Description();

            public IReadOnlyList<ActionParameter> Parameters { get; } =
            [
                ActionParameter.Text(
                    MessageParameter,
                    label: Strings.Actions.LogMessage.Message.Label(),
                    description: Strings.Actions.LogMessage.Message.Description(),
                    placeholder: Strings.Actions.LogMessage.Message.Placeholder(),
                    required: true),
            ];

            public MacroDeckPlatform Platforms => MacroDeckPlatform.All;

            public IActionExecutor CreateExecutor() => new Executor(_logger);

            private sealed class Executor : IActionExecutor
            {
                private readonly ILogger _logger;

                public Executor(ILogger logger) => _logger = logger;

                public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
                {
                    var message = context.Parameters.TryGetValue(MessageParameter, out var value)
                        ? value.ToString()
                        : null;

                    // The parameter is required, but the host still sends whatever the user configured, so the
                    // executor is the only place that can decide the action did not do what it claims.
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        return Task.FromResult(ActionResult.Failed(
                            ActionErrorCodes.InvalidParameter,
                            MacroDeckStrings.Validation.Required(Strings.Actions.LogMessage.Message.Label())));
                    }

                    _logger.Information("{Message}", message);
                    return ActionResult.SucceededTask;
                }
            }
        }

        """);

    private static string StringsResx(ProjectContentBuilder b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<root>");
        sb.AppendLine("  <!-- The default-language file. Every translation is checked against it and the fallback chain ends");
        sb.AppendLine("       here, so it is required even for a plugin that ships one language. Add a language by adding");
        sb.AppendLine("       Localization/Strings.<culture>.resx beside it (Strings.de.resx, Strings.pt-BR.resx).");
        sb.AppendLine("       A dotted key becomes a nested class: Actions.LogMessage.Name is Strings.Actions.LogMessage.Name(). -->");
        sb.AppendLine("  <resheader name=\"resmimetype\">");
        sb.AppendLine("    <value>text/microsoft-resx</value>");
        sb.AppendLine("  </resheader>");
        sb.AppendLine("  <resheader name=\"version\">");
        sb.AppendLine("    <value>2.0</value>");
        sb.AppendLine("  </resheader>");
        sb.AppendLine("  <resheader name=\"reader\">");
        sb.AppendLine("    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>");
        sb.AppendLine("  </resheader>");
        sb.AppendLine("  <resheader name=\"writer\">");
        sb.AppendLine("    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>");
        sb.AppendLine("  </resheader>");

        void AddKey(string key, string value)
        {
            sb.AppendLine($"  <data name=\"{System.Security.SecurityElement.Escape(key)}\" xml:space=\"preserve\">");
            sb.AppendLine($"    <value>{System.Security.SecurityElement.Escape(value)}</value>");
            sb.AppendLine("  </data>");
        }

        AddKey("Actions.LogMessage.Name", "Write log message");
        AddKey("Actions.LogMessage.Description", "Writes a message to the Macro Deck log.");
        AddKey("Actions.LogMessage.Message.Label", "Message");
        AddKey("Actions.LogMessage.Message.Description", "The text to write to the log.");
        AddKey("Actions.LogMessage.Message.Placeholder", "Hello from my plugin");

        foreach (var (key, value) in b.StringsKeys.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            AddKey(key, value);
        }

        // A trailing newline, like every other file the official template ships. `AppendLine`
        // everywhere else gives one for free; the closing tag is the exception.
        sb.AppendLine("</root>");
        return sb.ToString();
    }

    private const string LanguageResx = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <!-- Translation for culture "$LanguageTag". It needs only the keys it translates; the chain
               falls back requested culture -> neutral -> catalog default -> en. -->
          <resheader name="resmimetype">
            <value>text/microsoft-resx</value>
          </resheader>
          <resheader name="version">
            <value>2.0</value>
          </resheader>
          <resheader name="reader">
            <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>
          <resheader name="writer">
            <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>

          <!-- Add translated keys here, e.g.:
          <data name="Actions.LogMessage.Name" xml:space="preserve">
            <value>...</value>
          </data>
          -->
        </root>
        """;

    /// <summary>The placeholder icon, tab-indented exactly as the official template ships it.</summary>
    private static readonly string IconSvg = Tabbed("""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
            <circle cx="24" cy="23" r="12" fill="#F5A623" />
            <path d="M18 44h27a10 10 0 0 0 1-19.9A14 14 0 0 0 19 20.6 12 12 0 0 0 18 44z" fill="#4A90D9" />
        </svg>

        """);

    private static readonly string LaunchSettings = """
        {
          "$schema": "https://json.schemastore.org/launchsettings.json",
          "profiles": {
            "Macro Deck - Real Host": {
              "commandName": "Project",
              "dotnetRunMessages": true,
              "launchBrowser": false,
              "workingDirectory": "$(ProjectDir)",
              "environmentVariables": {
                "DOTNET_ENVIRONMENT": "Development",
                "MACRO_DECK_PLUGIN_MODE": "SelfRegistering",
                "MACRO_DECK_PLUGIN_HOST_URL": "http://127.0.0.1:8193",
                "MACRO_DECK_PLUGIN_STATE_DIRECTORY": ".macrodeck-dev-state"
              }
            }
          }
        }

        """;

    private const string TestCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">

            <PropertyGroup>
                <IsPackable>false</IsPackable>
                <IsTestProject>true</IsTestProject>
                <!-- Underscored test names, like the Macro Deck repository's own test projects. -->
                <NoWarn>$(NoWarn);CA1707</NoWarn>
            </PropertyGroup>

            <ItemGroup>
                <PackageReference Include="MacroDeck.Plugin.Testing" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" />
                <PackageReference Include="NUnit" />
                <PackageReference Include="NUnit.Analyzers" PrivateAssets="all" />
                <PackageReference Include="NUnit3TestAdapter" />
            </ItemGroup>

            <ItemGroup>
                <ProjectReference Include="..\..\src\$ProjectName\$ProjectName.csproj" />
            </ItemGroup>

        </Project>

        """;

    /// <summary>
    /// Guidance for a coding agent working inside the generated project.
    /// </summary>
    /// <remarks>
    /// Written by DeckForge for the projects it generates, covering the rules that only fail at
    /// run time and so are invisible to a build: a dropped cancellation token, a blocking call in a
    /// capability handler, a stale catalogue, an undeclared permission, a hard-coded user string.
    /// A build accepts all five without complaint.
    /// </remarks>
    private const string AgentsMd = """
        # Agent guidance

        This file must be kept up to date. When a rule here stops matching reality, or a new rule
        emerges from work in this repository, update this file as part of that change rather than
        leaving it to drift.

        This repository is the **starting point for a Macro Deck 3 out-of-process plugin**. The
        plugin under `src/$ProjectName/` is deliberately minimal: one integration and one example
        action, `LogMessageAction`, which exists solely to show the localized shape of an action end
        to end. It is meant to be replaced by the plugin's real first action, not grown into a
        second sample. Worked examples of every capability live in the
        [sample plugins repository](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins).

        [README.md](README.md) is the human-facing guide: how to build, how to run against a real
        host, how to pack. This file is the rule set for writing the plugin. Read it before changing
        code.

        ## Orientation

        ```
        src/$ProjectName/
          Program.cs             builder chain - a few lines and a RunAsync
          manifest.json          identity, icon, per-platform entrypoints, permissions
          macrodeck-build.json   one publish target per declared entrypoint
          PluginIntegration.cs   the integration: lifecycle and capability opt-ins
          LogMessageAction.cs    the example action, localized end to end
          Localization/          Strings.resx plus one file per translated culture
        tests/$ProjectName.Tests/  PluginTestHarness-based tests, no sockets
        ```

        ## The rules that make a plugin clean

        ### Identity

        - `manifest.json` is the identity. `id` is a reverse-domain id and is permanent: changing it
          breaks pairing and every saved user layout that referenced the plugin.
        - Raise `version` on every change. It is part of the artifact's identity, and a repeat
          version will not overwrite.
        - The `<AssemblyName>` in the csproj is pinned rather than derived from the file name, because
          `manifest.json` declares the executable name. Renaming the `.csproj` must not silently
          rename the built artifact or move the generated `Strings` class out from under the code.

        ### Lifecycle

        `InitializeAsync` does not run at process start. It is gated on the connection being
        established, and it runs again after a non-resume reconnect and whenever the host reports a
        configuration change. Make it idempotent and safe to run repeatedly against an
        already-initialized process.

        ### Actions

        - `ActionResult` must be truthful. `Success()` claims the operation completed. Could not reach
          the provider, refused a permission, handed an unusable value - `Failed(code, message)` with
          the closest `ActionErrorCodes` value. A press that did nothing must never report success.
        - A legitimate no-op *is* success: an optional parameter left blank, a repeat count of zero, a
          setting already in the requested state.
        - `Accepted` is only for work the provider took but cannot confirm. Where the API *can*
          confirm, poll until it does rather than returning `Accepted`.
        - A synchronous executor returns the cached `ActionResult.SucceededTask` rather than
          allocating.
        - **Forward `context.CancellationToken`** into everything you await. Dropping it is MDP3001.
        - Parameter visibility (`OnlyWhen`) is presentation only. The host still sends hidden
          parameters, so validate the combination in the executor - never infer anything from a field
          being hidden.

        ### Async and concurrency

        - No `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` or `Thread.Sleep` anywhere in a type
          implementing `ICapabilityHandler`, `IActionExecutor` or `IConfigFlow` - the rule is
          whole-type, not just the interface methods, because the dispatcher has 32 concurrent
          invocation slots and a block anywhere reachable starves the rest (MDP3002).
        - No `async void` on an SDK contract type; an exception there kills the process instead of
          failing one call (MDP3003).
        - Invocations dispatch **concurrently**, each in its own DI scope. Instance state touched from
          more than one invocation needs its own synchronization - the same discipline as any
          concurrently invoked ASP.NET Core endpoint.
        - `ICapabilityInvocationContext` only resolves inside an invocation scope. A singleton must
          not depend on it (MDP4001).

        ### Fire-and-forget contracts

        `IEventPublisher.Publish`, `IUserNotifier.Notify`/`Dismiss` and
        `IPluginCatalogNotifier.CatalogChanged` never throw and are safe to call with no live session.
        Mirror that in your own wrappers.

        The round-trip host callbacks - `context.Variables`, `context.Config`,
        `context.UserVariables`, the mutating members of `context.Deck`, `context.Scripts.RunAsync`,
        `context.Widgets.ApplyAsync` - are the opposite: real network calls that can throw
        `HostInvocationException` on rate limiting, timeout or no connection. Handle them like any
        networked call. `Deck.GetFolders()`, `Scripts.GetScripts()` and `Widgets.GetWidgets()` read a
        host-pushed cache instead, and return empty in the short window before the first push.

        ### Catalogues go stale - say so

        Every synchronous catalog-shaped member (`GetInstances`, `EventDefinitions`,
        `DeclaredVariables`, `GetProfiles`) is served to the host from a cached `describe`, not a live
        call. When something outside a host-initiated invocation changes what a later `describe`
        would answer - a config value `InitializeAsync` just read, a device that appeared or vanished
        - inject `IPluginCatalogNotifier` and call `CatalogChanged(kind, reason)`. Skipping it leaves
        the UI disagreeing with the plugin until the next reconnect.

        ### Configuration and secrets

        - A config flow produces the entries; `InitializeAsync` reads them back through
          `IIntegrationContext.Config`. Keep that division: the flow validates and persists, the
          integration consumes.
        - Persist credentials as `ConfigFlowValue.Secret` so they land in the host's encrypted secret
          store. Never write a token to a plain string field, a log line, or a file of your own.
        - Never run your own OAuth redirect server. Return `ConfigFlowResult.External(url,
          resumeStepId)` and let the host own the redirect and the callback correlation.
        - An integration that provides a config flow starts **disabled** until the user completes it.
          Everything else starts enabled.

        ### Localization

        - **No user-facing literal.** Every string a user reads is a key in `Localization/Strings.resx`,
          reached through the generated `Strings` class.
        - A dotted key becomes a nested class: `Actions.LogMessage.Name` is
          `Strings.Actions.LogMessage.Name()`. A key that is also a prefix of others is a collision.
        - The fallback chain is requested culture, then neutral, then the catalog default. Ship a
          language by adding `Localization/Strings.<culture>.resx` beside `Strings.resx`; build and
          pack recompute `manifest.json`'s languages from the files they find.

        ### Logging

        - Log through the injected `Serilog.ILogger`, which `Program.cs` has already pointed at
          Macro Deck's sink. Use message templates, not interpolation.
        - A standing condition the user must act on - a missing permission, invalid credentials - is
          an integration issue, not a log line. Nobody reads a log line; everybody sees an issue.

        ### Permissions

        - The host grants a plugin only what `manifest.json` asks for. If the code calls a host API
          that needs a permission and the manifest does not declare it, the call fails at run time -
          not at build time.
        - Ask for the narrowest permission that works, and say why in the description the Creator
          Portal shows the user.

        ### Style

        - No comment that restates the code. A comment earns its place by explaining *why*.
        - The type name says what it is; the method name says what it does. No `Manager`, `Helper`,
          `Handler` or `Processor` unless it really manages, helps, handles or processes.
        - The template is tab-indented. Match it.

        ## Verifying a change

        ```bash
        dotnet build
        dotnet test
        macrodeck-plugin test --project src/$ProjectName
        macrodeck-plugin validate --manifest src/$ProjectName/manifest.json --level package
        ```

        The `validate` step is not optional before you share the project: it is the same check the
        Store runs, and it names the missing permission, missing icon or malformed entrypoint before
        anyone tries to install it.

        ## Packing

        ```bash
        macrodeck-plugin build --source src/$ProjectName --output artifacts --force
        ```

        Publishing a version also needs `repository` and `homepage` filled in, which
        `validate --level publication` will tell you about by name.
        """;

    /// <summary>A pointer to the authoritative guidance, as the official template ships it.</summary>
    private const string ClaudeMd = """
        # CLAUDE.md

        See [AGENTS.md](AGENTS.md) for the rules and workflow that apply when working in this repository - it is
        the authoritative guidance for orientation, plugin-writing rules, verification steps, packing and
        workflow conventions. Read it before changing code.

        """;

    /// <summary>
    /// The contract that lets an action reach the host. The SDK has no equivalent, and without it
    /// an action's executor can never hold an <c>IIntegrationContext</c>.
    /// </summary>
    private static readonly string IntegrationContextAwareCs = Tabbed("""
        using MacroDeck.Sdk;

        namespace $RootNamespace;

        /// <summary>
        /// Implemented by an action that needs the host: to change folders or profiles, notify,
        /// read or write variables, publish events, or invalidate a widget icon.
        /// </summary>
        /// <remarks>
        /// The SDK hands <see cref="IIntegrationContext"/> to exactly one place,
        /// <c>IIntegration.InitializeAsync</c>, and <c>IActionDefinition.CreateExecutor()</c> takes
        /// no parameters - so there is no built-in way for an action to reach the host. The
        /// integration calls <see cref="SetIntegrationContext"/> on every action that implements this
        /// once the session is established, and the action holds on to it for the executors it
        /// creates. Implement it only when the action actually calls the host: an action that does
        /// not need it should not ask for it.
        /// </remarks>
        public interface IIntegrationContextAware
        {
            /// <summary>
            /// Supplies the session's host context. Called during
            /// <c>InitializeAsync</c>, before any executor is created, and again after a reconnect,
            /// so it must be safe to call more than once.
            /// </summary>
            /// <param name="context">The host context for the current session.</param>
            void SetIntegrationContext(IIntegrationContext context);
        }

        """);

    /// <summary>
    /// The generated test project, matching the official template's shape.
    /// </summary>
    /// <remarks>
    /// DeckForge shipped two tests and one of them asserted only that a result was not null, so a
    /// generated plugin "passed" whether or not its action did anything, and a broken
    /// `UseLocalization` wiring was invisible because nothing read a string. The official template
    /// has seven across two fixtures, and this mirrors it: the plugin initializes, the example
    /// action actually writes to the log, it rejects blank input (the negative case that proves the
    /// parameter is wired rather than ignored), the catalog is scoped to the plugin id, English is
    /// the default culture, the action strings come from the catalog, and every key the default
    /// culture declares resolves to real text.
    /// <para>
    /// That last one is the test that would have caught the events capability's missing resx key: a
    /// key the code reads but the resx does not define compiles cleanly and throws at run time.
    /// </para>
    /// </remarks>
    private static readonly string IntegrationTestsCs = Tabbed("""
        using MacroDeck.Plugin.Testing;
        using NUnit.Framework;

        namespace $RootNamespace.Tests;

        /// <summary>
        /// Behaviour tests through <see cref="PluginTestHarness"/>: the plugin's own capability handlers run,
        /// but nothing crosses a socket. This is where you test what your integration does.
        /// </summary>
        [TestFixture]
        public sealed class PluginIntegrationTests
        {
            private static PluginTestHarness CreateHarness() =>
                PluginTestHarness.Create(builder => builder
                    .UseLocalization(Strings.LocalizationCatalog)
                    .RegisterIntegration<PluginIntegration>());

            [Test]
            public async Task The_plugin_builds_and_initializes()
            {
                await using var harness = CreateHarness();

                Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
            }

            [Test]
            public async Task The_example_action_writes_the_message_to_the_log()
            {
                await using var harness = CreateHarness();
                await harness.InitializeIntegrationsAsync();

                var outcome = await harness.Actions.ExecuteAsync(
                    "log-message",
                    new Dictionary<string, object?> { ["message"] = "Hello from a test" });

                Assert.That(outcome.Succeeded, Is.True);
                Assert.That(harness.Logs.Events.Any(e => e.Message.Contains("Hello from a test")), Is.True);
            }

            [Test]
            public async Task The_example_action_fails_when_the_message_is_blank()
            {
                await using var harness = CreateHarness();
                await harness.InitializeIntegrationsAsync();

                var outcome = await harness.Actions.ExecuteAsync(
                    "log-message",
                    new Dictionary<string, object?> { ["message"] = "   " });

                Assert.That(outcome.Succeeded, Is.False);
            }
        }

        /// <summary>
        /// The localization set is generated from <c>Localization/*.resx</c>, so these guard the wiring rather
        /// than any wording: a missing catalog registration leaves every label showing its raw key.
        /// </summary>
        [TestFixture]
        public sealed class LocalizationTests
        {
            [Test]
            public void The_catalog_is_scoped_to_the_plugin_id()
            {
                Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.example.ref"));
            }

            [Test]
            public void English_is_the_default_culture()
            {
                Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
                Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
            }

            [Test]
            public void The_action_strings_come_from_the_catalog()
            {
                Assert.That(Strings.LocalizationCatalog.KeysOf("en"), Does.Contain("Actions.LogMessage.Name"));
            }

            [Test]
            public void Every_key_the_default_culture_declares_resolves_to_text()
            {
                foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
                {
                    Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True);
                    Assert.That(text, Is.Not.Empty);
                }
            }
        }

        """);

    /// <summary>
    /// A JSON string literal body: the value escaped for use inside double quotes.
    /// </summary>
    /// <remarks>
    /// A hand-written escape of backslash and quote is not enough. JSON forbids a raw control
    /// character inside a string, so a description typed with a line break - or a tab, or a
    /// carriage return from a pasted field - produced a manifest.json that would not parse. The
    /// failure appeared at pack time, long after the wizard accepted the value. The callers add the
    /// surrounding quotes, so this returns the body only; serializing the value and trimming the
    /// quotes would be shorter and would quietly break the moment a caller stopped adding them.
    /// </remarks>
    private static string Json(string s)
    {
        if (!s.Any(c => c is '\\' or '"' or < ' '))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    // Every other control character, including U+0000 through U+001F, has to go out
                    // as \uXXXX or the document is not valid JSON.
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
