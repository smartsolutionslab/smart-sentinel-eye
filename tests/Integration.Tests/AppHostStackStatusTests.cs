using SmartSentinelEye.AppHost;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Guards the two facts #2268 / spec 181 need held together, so the resource
/// gate <c>wait-for-e2e-stack.sh</c> reads (<c>scripts/wait-for-e2e-stack.test.mjs</c>)
/// cannot silently go back to being three port probes:
///
/// <list type="number">
/// <item>the end-to-end job's boot command actually sets the switch that
/// makes the AppHost write a status report;</item>
/// <item>the resource set that report is seeded from is the composition
/// itself — every resource the e2e job's own arguments produce that is not
/// <see cref="IResourceWithoutLifetime"/> — not a hand-written list
/// (spec.md §5's rejected options are all hand-written lists of one shape
/// or another).</item>
/// </list>
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no resources,
/// so this costs no containers and is safe beside a live <c>aspire run</c> —
/// the same footing as <see cref="AppHostE2ESwitchTests"/> and
/// <see cref="AppHostMigrationGateTests"/>, whose conventions this mirrors.
/// </para>
///
/// <para>
/// Fact 2 asserts against resources named directly — <c>audit-observability</c>,
/// <c>minio</c> and <c>migrations</c> present; <c>PostgresUser</c>,
/// <c>PostgresPassword</c>, <c>KeycloakPassword</c> and
/// <c>RabbitMqPassword</c> absent — rather than re-deriving the "expected"
/// set with the same <c>IResourceWithoutLifetime</c> predicate
/// <see cref="StackStatusReport"/> itself is meant to apply. Comparing two
/// derivations of the same filter would pass even if both drifted together;
/// naming the resources is what lets the assertion actually fail
/// (an-assertion-must-not-check-its-own-input).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostStackStatusTests
{
    private const string Workflow = ".github/workflows/ci.yml";
    private const string BootCommand = "dotnet run --project src/AppHost";
    private const string StatusFileArgument = "StackStatusFile=";

    /// <summary>
    /// The shape the end-to-end job boots: parameters, plus the switch that
    /// disables the scenario simulator (<see cref="AppHostE2ESwitchTests"/>).
    /// No <c>E2ETests</c> — that switch is a different lane and removes
    /// resources (the Vite apps) the e2e job needs.
    /// </summary>
    private static readonly string[] E2EJobArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
        "ScenarioSimulator=false",
    ];

    /// <summary>
    /// Proves the workflow file contains the string, and nothing more — the
    /// same limitation
    /// <see cref="AppHostE2ESwitchTests.The_e2e_boot_line_disables_the_scenario_simulator"/>
    /// records for its own sibling assertion. It earns its place because the
    /// switch fails open: an AppHost that never receives it writes no report
    /// at all, and the gate's own "no report" refusal
    /// (<c>scripts/wait-for-e2e-stack.test.mjs</c>) would then fire on every
    /// run for a reason that has nothing to do with the stack — the boot
    /// command quietly stopped asking (#2268).
    /// </summary>
    [Fact]
    public void The_e2e_boot_line_sets_the_stack_status_file_switch()
    {
        string command = BootLine().Split('>', 2)[0];

        command.ShouldContain(
            StatusFileArgument,
            Case.Sensitive,
            $"{Workflow} boots the end-to-end stack without '{StatusFileArgument}' ahead of the "
            + "redirection, so the AppHost never writes a status report and the resource gate this "
            + "spec adds has nothing to read (#2268).");
    }

    /// <summary>
    /// The expected set is what the composition actually contains under the
    /// e2e job's own arguments, filtered to resources that can be
    /// <c>Running</c> at all — never a name list.
    /// </summary>
    [Fact]
    public async Task The_expected_resource_set_is_the_composition_minus_the_lifetime_less_resources()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(E2EJobArguments);

        IReadOnlyList<string> names = StackStatusReport.ExpectedResourceNames(builder.Resources);

        // The resource named in the issue's own evidence (#2264), a container
        // it WaitFors, and the one-shot project resource the migration probe
        // above this gate already depends on being able to tell apart from
        // "started".
        names.ShouldContain("audit-observability");
        names.ShouldContain("minio");
        names.ShouldContain("migrations");

        // Aspire's own IResourceWithoutLifetime marker, not a name this file
        // wrote down: parameters can never reach Running, so a gate that
        // waited on one would wait forever, and a report that printed one
        // would carry the value it forbids (spec.md §3's "no secrets"
        // scenario) if it were ever printed with anything but the name.
        names.ShouldNotContain("PostgresUser");
        names.ShouldNotContain("PostgresPassword");
        names.ShouldNotContain("KeycloakPassword");
        names.ShouldNotContain("RabbitMqPassword");
        names.ShouldNotContain("IdentityAdminClientSecret");
        names.ShouldNotContain("MigrationRunnerClientSecret");
        names.ShouldNotContain("ScenarioSimulatorClientSecret");
        names.ShouldNotContain("EventIngestionMqttClientSecret");
        names.ShouldNotContain("StreamDistributionAttributionClientSecret");
        names.ShouldNotContain("SystemVariablesSeederClientSecret");
    }

    /// <summary>
    /// A scan that matches nothing passes silently, and a passing guard that
    /// checked nothing is indistinguishable from one that holds — so the
    /// absence of the boot line is itself a failure.
    /// </summary>
    private static string BootLine()
    {
        string[] candidates =
        [
            .. System.IO.File
                .ReadAllLines(WorkflowPath())
                .Where(line => line.Contains(BootCommand, StringComparison.Ordinal)),
        ];

        candidates.ShouldHaveSingleItem(
            $"expected exactly one '{BootCommand}' line in {Workflow}, found {candidates.Length} — "
            + "the scan is broken, or the boot step moved.");

        return candidates[0];
    }

    /// <summary>
    /// Built with <see cref="System.IO.Path.Combine(string[])"/> rather than a
    /// literal path: a backslash separator is green on Windows and red on
    /// Linux CI, and this repository has been bitten by exactly that.
    /// </summary>
    private static string WorkflowPath() =>
        System.IO.Path.Combine([RepositoryRoot(), .. Workflow.Split('/')]);

    private static string RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
