using DeckForge.CliAdapter;
using DeckForge.CliAdapter.Processes;
using DeckForge.CliAdapter.Tools;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the environment probes. These run against the real machine, so what they check is
/// the shape and the wording of a diagnosis rather than a fixed answer - whether this machine has
/// Macro Deck installed is not something a test should assert.
/// </summary>
[TestFixture]
public sealed class EnvironmentDoctorTests
{
    private static (EnvironmentDoctor Doctor, DotNetCli Cli, MacroDeckCli MacroDeck) Build()
    {
        var runner = new ProcessRunner();
        var dotnet = new DotNetCli(runner);
        var cli = new MacroDeckCli(runner);
        return (new EnvironmentDoctor(dotnet, cli), dotnet, cli);
    }

    [Test]
    public async Task The_sdk_version_is_a_version_and_not_a_diagnostic()
    {
        // dotnet --version prints a multi-line "the SDK to resolve ... was not found" message when
        // global.json asks for something missing. The probe used to hand that whole message back as
        // the version, and the page then said "dotnet was not found on PATH".
        var (_, dotnet, _) = Build();
        var probe = await dotnet.ProbeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(probe.Version, Is.Not.Null, $"No version parsed from: {probe.Output}");
            Assert.That(probe.Found, Is.True);
        });
    }

    [Test]
    public async Task Asking_for_the_version_twice_runs_the_command_once()
    {
        // IsAvailableAsync and GetVersionAsync were the same command, so a caller that wanted both
        // paid for two process launches to learn the same thing.
        var (_, dotnet, _) = Build();

        var version = await dotnet.GetVersionAsync();
        var available = await dotnet.IsAvailableAsync();
        var probe = await dotnet.ProbeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(version, Is.Not.Null);
            Assert.That(available, Is.True);
            Assert.That(probe.Found, Is.True);
            Assert.That(probe.Version?.ToString(), Is.EqualTo(version));
        });
    }

    [Test]
    public async Task The_checklist_never_reports_a_version_string_it_could_not_parse()
    {
        var (doctor, _, _) = Build();
        var checks = await doctor.RunAllAsync();
        var dotnet = checks.Single(c => c.Id == "dotnet");

        Assert.Multiple(() =>
        {
            Assert.That(dotnet.Detail, Does.Not.Contain("not found on PATH").Or.Contain("dotnet "));
            Assert.That(
                Version.TryParse(dotnet.Detail?.Replace("dotnet ", string.Empty).Split(' ')[0], out _),
                Is.True,
                $"The .NET check reports an unparseable detail: {dotnet.Detail}");
        });
    }

    [Test]
    public void The_macro_deck_host_is_optional_so_its_absent_state_is_not_an_error()
    {
        var (doctor, _, _) = Build();
        var host = doctor.RunAllAsync().GetAwaiter().GetResult().Single(c => c.Id == "macrodeck-host");

        Assert.That(host.Severity, Is.EqualTo(DoctorSeverity.Optional));
    }

    [Test]
    public void Every_required_check_that_fails_offers_a_way_to_fix_it()
    {
        var (doctor, _, _) = Build();

        var required = doctor.RunAllAsync().GetAwaiter().GetResult()
            .Where(c => c.Severity == DoctorSeverity.Required)
            .ToList();

        Assert.That(required, Is.Not.Empty);
        foreach (var check in required)
        {
            if (check.Ok)
            {
                continue;
            }

            Assert.That(
                check.FixHint,
                Is.Not.Null.And.Not.Empty,
                $"'{check.Title}' failed with no way to fix it.");
        }
    }

    [Test]
    public void The_cli_install_command_names_the_version_deckforge_targets()
    {
        var (doctor, _, _) = Build();

        var command = doctor.CliInstallCommand();

        Assert.Multiple(() =>
        {
            Assert.That(command, Does.Contain(Core.Plugins.MacroDeckSdkInfo.DefaultCliVersion));
            Assert.That(command, Does.Contain("--allow-prerelease"));
        });
    }

    [Test]
    public void A_path_entry_list_is_split_on_the_platform_separator()
    {
        // The old search split PATH on a hardcoded ';', which finds nothing off Windows.
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        Assume.That(pathVariable, Is.Not.Null.And.Not.Empty);

        var fromPath = EnvironmentDoctor.FindOnPath(
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        Assert.That(fromPath, Is.Not.Null, $"Nothing found on PATH: {pathVariable}");
    }

    [Test]
    public void The_macro_deck_search_tries_both_spellings_of_the_product_folder()
    {
        // One candidate said "MacroDeck" and two said "Macro Deck", so an install was only found
        // if the installer happened to have used the spelling that candidate expected.
        var found = EnvironmentDoctor.FindMacroDeckInstall();

        Assert.That(
            found,
            Is.Null.Or.Not.Empty,
            "FindMacroDeckInstall returned an empty string, which reads as a path but is not one.");
    }
}
