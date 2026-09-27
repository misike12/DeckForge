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

        b.AddFile($"{p}.slnx", Fill(Slnx, tokens));
        b.AddFile("Directory.Build.props", DirectoryBuildProps);
        b.AddFile("Directory.Packages.props", DirectoryPackagesProps(b));
        b.AddFile("NuGet.config", NuGetConfig);
        b.AddFile(".gitignore", GitIgnore);
        b.AddFile("README.md", Readme(b));

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
        b.AddFile($"src/{p}/PluginIntegration.cs", Fill(PluginIntegrationCs, tokens));
        b.AddFile($"src/{p}/LogMessageAction.cs", Fill(LogMessageActionCs, tokens));
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

        if (b.ExtraIntegrationUsings.Count > 0
            || b.ExtraIntegrationInterfaces.Count > 0
            || b.ExtraIntegrationMembers.Count > 0)
        {
            b.AddFile($"src/{p}/PluginIntegration.cs", PluginIntegration(b));
        }
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

        return source;
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

    private static IReadOnlyDictionary<string, string> Tokens(ProjectContentBuilder b)
    {
        var selfContained = b.Options.SelfContained;
        return new Dictionary<string, string>
        {
            ["RootNamespace"] = b.RootNamespace,
            ["ProjectName"] = b.ProjectName,
            ["PluginName"] = b.Options.PluginName,
            ["PluginId"] = b.Options.PluginId,
            ["Description"] = b.Options.Description,
            ["Platforms"] = string.Join(", ", b.Options.Platforms),
            ["SdkVersion"] = MacroDeckSdkInfo.DefaultVersion,
            ["DotnetVersion"] = MacroDeckSdkInfo.DefaultDotnetVersion,
            ["MacroDeckRange"] = MacroDeckSdkInfo.DefaultMacroDeckRange,
            ["LanguageTag"] = "",
            ["EntryExecutable"] = selfContained ? b.ProjectName + ".exe" : b.ProjectName + ".dll",
        };
    }

    // ---------- solution & shared props ----------

    private static string Slnx => """
        <Solution>
          <Project Path="src/$ProjectName/$ProjectName.csproj" />
          <Project Path="tests/$ProjectName.Tests/$ProjectName.Tests.csproj" />
        </Solution>
        """;

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
        sb.Append(MacroDeckSdkInfo.DefaultVersion);
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

    private const string NuGetConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
            <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <!-- Empty in a fresh clone. `dotnet pack` the SDK into it to build against an
                     unreleased version. -->
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
        - Platforms: $Platforms

        ## Build

        ```
        dotnet build
        dotnet test
        ```

        ## Run against a disposable stub host (no Macro Deck needed)

        ```
        macrodeck-plugin run --project src/$ProjectName --stub-host
        ```

        ## Package

        ```
        macrodeck-plugin build --source src/$ProjectName --output artifacts --force
        ```

        ## Check before you publish

        ```
        macrodeck-plugin validate --artifact artifacts/$PluginId-1.0.0.macroDeckPlugin --level publication
        ```

        ## Test

        ```
        macrodeck-plugin test --project src/$ProjectName
        ```
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

                <!-- Microsoft.NET.Sdk plus this framework reference, not Microsoft.NET.Sdk.Web: a
                     plugin is a headless process, and the framework reference is what supplies the
                     hosting surface MacroDeck.Plugin.Hosting builds on. -->
                <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                </ItemGroup>

                <ItemGroup>
                    <!-- Build-time only: compile-time diagnostics for plugin authors, and the source
                         generator that turns Localization/*.resx into the typed Strings class.
                         Never shipped in the output. -->
                    <PackageReference Include="MacroDeck.Plugin.Analyzers" PrivateAssets="all" />
                    <PackageReference Include="MacroDeck.Localization" />
                    <PackageReference Include="MacroDeck.Plugin.Hosting" />
                    <PackageReference Include="MacroDeck.Plugin.Serilog" />
                    <PackageReference Include="MacroDeck.Sdk" />
                </ItemGroup>

                <!-- The SDK reads the manifest from the content root at startup and resolves the icon
                     path against that same root, so both have to land next to the built executable. -->
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
        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine("  \"$schema\": \"https://schemas.macro-deck.app/plugin-manifest-v1.schema.json\",");
        sb.AppendLine("  \"manifestVersion\": 1,");
        sb.AppendLine($"  \"id\": \"{Json(o.PluginId)}\",");
        sb.AppendLine($"  \"name\": \"{Json(o.PluginName)}\",");
        sb.AppendLine("  \"version\": \"1.0.0\",");
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
                sb.AppendLine($"    \"{rid}\": {{ \"executable\": \"{exe}\", \"runtime\": {{ \"kind\": \"FrameworkDependent\", \"dotnetVersion\": \"{MacroDeckSdkInfo.DefaultDotnetVersion}\" }} }}{comma}");
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
        sb.AppendLine($"    \"macroDeck\": \"{MacroDeckSdkInfo.DefaultMacroDeckRange}\"");
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

    private const string ProgramCs = """
        using MacroDeck.Plugin.Hosting;
        using MacroDeck.Plugin.Serilog;
        using $RootNamespace;

        // Identity, description and icon are not set here: they come from manifest.json at the
        // content root. Strings is generated from Localization/*.resx, so UseLocalization is what
        // makes every LocalizedString below resolve in the user's language rather than falling
        // back to its key.
        var plugin = MacroDeckPlugin.CreatePlugin(args)
            .UseMacroDeckLogging()
            .UseLocalization(Strings.LocalizationCatalog)
            .RegisterIntegration<PluginIntegration>()
            .Build();

        await plugin.RunAsync();
        """;

    private const string PluginIntegrationCs = """
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.Actions;
        using Serilog;

        namespace $RootNamespace;

        /// <summary>
        /// The plugin's one integration. It declares a single example action: add more to
        /// <see cref="Actions"/>, and opt into a capability by implementing its interface here
        /// (<c>IVariableProvider</c>, <c>IEventProvider</c>, <c>IConfigFlowProvider</c>, and so on).
        /// </summary>
        public sealed class PluginIntegration : IPluginIntegration
        {
            private readonly ILogger _logger;

            // Built by DI, so anything the container knows can be taken here: IHttpClientFactory,
            // IOptions<T>, PluginMetadata, IPluginCatalogNotifier.
            public PluginIntegration(ILogger logger)
            {
                _logger = logger.ForContext<PluginIntegration>();
                Actions = [new LogMessageAction(logger)];
            }

            public IReadOnlyList<IActionDefinition> Actions { get; }

            /// <summary>
            /// Runs once the session is established, and again after a non-resume reconnect or a
            /// configuration change, so it has to be safe to run repeatedly against an
            /// already-initialized process.
            /// </summary>
            public Task InitializeAsync(IIntegrationContext context)
            {
                _logger.Information("Initialized.");
                return Task.CompletedTask;
            }

            public Task ShutdownAsync() => Task.CompletedTask;
        }
        """;

    private const string LogMessageActionCs = """
        using MacroDeck.Localization;
        using MacroDeck.Sdk;
        using MacroDeck.Sdk.Actions;
        using Serilog;

        namespace $RootNamespace;

        /// <summary>
        /// The one example action. Every string a user reads - the action's name and description,
        /// its parameter's label, description and placeholder, and the error it can fail with -
        /// comes from <see cref="Strings"/> rather than a literal, which is what makes the plugin
        /// translatable.
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

                    // The parameter is required, but the host still sends whatever the user
                    // configured, so the executor is the only place that can decide the action did
                    // not do what it claims.
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
        """;

    private static string StringsResx(ProjectContentBuilder b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<root>");
        sb.AppendLine("  <!-- The default-language file. Every translation is checked against it and the");
        sb.AppendLine("       fallback chain ends here, so it is required even for a plugin that ships one");
        sb.AppendLine("       language. Add a language by adding Localization/Strings.<culture>.resx beside it.");
        sb.AppendLine("       A dotted key becomes a nested class:");
        sb.AppendLine("       Actions.LogMessage.Name is Strings.Actions.LogMessage.Name(). -->");
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
            sb.AppendLine($"  <data name=\"{key}\" xml:space=\"preserve\">");
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

        sb.Append("</root>");
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

    private const string IconSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
            <circle cx="24" cy="23" r="12" fill="#F5A623" />
            <path d="M18 44h27a10 10 0 0 0 1-19.9A14 14 0 0 0 19 20.6 12 12 0 0 0 18 44z" fill="#4A90D9" />
        </svg>
        """;

    private const string LaunchSettings = """
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
                    <ProjectReference Include="../../src/$ProjectName/$ProjectName.csproj" />
                </ItemGroup>

            </Project>
            """;

    private const string IntegrationTestsCs = """
        using MacroDeck.Plugin.Testing;
        using NUnit.Framework;

        namespace $RootNamespace.Tests;

        [TestFixture]
        public class PluginIntegrationTests
        {
            [Test]
            public async Task Plugin_builds_and_initializes()
            {
                await using var harness = PluginTestHarness.Create(b => b.RegisterIntegration<PluginIntegration>());
                await harness.InitializeIntegrationsAsync();
            }

            [Test]
            public async Task Log_message_action_executes()
            {
                await using var harness = PluginTestHarness.Create(b => b.RegisterIntegration<PluginIntegration>());
                await harness.InitializeIntegrationsAsync();

                var outcome = await harness.Actions.ExecuteAsync(
                    "log-message",
                    new Dictionary<string, object?> { ["message"] = "Hello from DeckForge" });
                Assert.That(outcome, Is.Not.Null);
            }
        }
        """;

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
