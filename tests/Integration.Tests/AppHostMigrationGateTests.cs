namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Guards the edge that keeps a context service from starting over a failed
/// migration (#2137, spec 128).
///
/// <para>
/// <c>WaitForCompletion(migrations)</c> was applied only under
/// <c>E2ETests=true</c>. Run mode — a developer's <c>aspire run</c>, and the
/// end-to-end CI job, which also boots run mode — started all nine context
/// services regardless of what <c>migrations</c> exited with, so a stack with
/// a partial schema reported healthy. The integration lane at least went red
/// (#2064); the two lanes a human watches did not.
/// </para>
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no resources,
/// so this costs no containers and is safe beside a live <c>aspire run</c> —
/// the same footing as <see cref="AppHostE2ESwitchTests"/>.
/// </para>
///
/// <para>
/// This asserts the dependency edge, not the runtime refusal. That Aspire
/// honours a completion wait is Aspire's contract, not ours; that the edge
/// exists in run mode is ours, and is the thing that was missing. The refusal
/// itself was observed once, by hand, against a stack booted with a
/// deliberately wrong <c>MigrationRunnerClientSecret</c>
/// (specs/128-a-failed-migration-is-loud/verification.md).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostMigrationGateTests
{
    /// <summary>
    /// The shape a developer's <c>aspire run</c> and the end-to-end job see:
    /// parameters only, no <c>E2ETests</c>.
    /// </summary>
    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
    ];

    private static readonly string[] FixtureArguments =
    [
        .. RunModeArguments,
        "E2ETests=true",
    ];

    /// <summary>
    /// Every service that owns a database and therefore a schema the migration
    /// run creates. Written out rather than discovered, because a service that
    /// silently stopped matching a filter is the defect this guards.
    /// </summary>
    public static TheoryData<string> ContextServices =>
    [
        "camera-catalog",
        "stream-distribution",
        "layout-composition",
        "event-ingestion",
        "overlay-designer",
        "system-variables",
        "automation",
        "identity",
        "audit-observability",
    ];

    [Theory]
    [MemberData(nameof(ContextServices))]
    public async Task A_run_mode_service_waits_for_the_migration_run_to_finish(string serviceName)
    {
        WaitAnnotation wait = await MigrationWaitAsync(RunModeArguments, serviceName);

        wait.WaitType.ShouldBe(
            WaitType.WaitForCompletion,
            $"'{serviceName}' must not start until the migration run has finished — a service "
            + "that boots over an aborted run comes up against a partial schema and reports "
            + "healthy, which is worse than failing (#2137).");
    }

    /// <summary>
    /// A completion wait that accepts any exit code would be satisfied by the
    /// abort it exists to catch, so the expected code is part of the gate.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContextServices))]
    public async Task A_run_mode_service_requires_the_migration_run_to_have_succeeded(
        string serviceName)
    {
        WaitAnnotation wait = await MigrationWaitAsync(RunModeArguments, serviceName);

        wait.ExitCode.ShouldBe(
            0,
            $"'{serviceName}' waits for 'migrations' to finish but would accept a non-zero "
            + "exit, which is the failure this gate exists for (#2137).");
    }

    /// <summary>
    /// The integration fixture's gate predates this one (#2064) and is not
    /// weakened by making run mode match it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContextServices))]
    public async Task The_fixture_lane_keeps_the_gate_it_already_had(string serviceName)
    {
        WaitAnnotation wait = await MigrationWaitAsync(FixtureArguments, serviceName);

        wait.WaitType.ShouldBe(WaitType.WaitForCompletion);
        wait.ExitCode.ShouldBe(0);
    }

    private static async Task<WaitAnnotation> MigrationWaitAsync(
        string[] arguments,
        string serviceName)
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(arguments);

        IResource service = builder.Resources.Single(resource => resource.Name == serviceName);

        WaitAnnotation[] waits =
        [
            .. service.Annotations
                .OfType<WaitAnnotation>()
                .Where(annotation => annotation.Resource.Name == "migrations"),
        ];

        waits.ShouldHaveSingleItem(
            $"expected '{serviceName}' to carry exactly one wait on 'migrations', found "
            + $"{waits.Length} — the service starts without one, or the edge was added twice.");

        return waits[0];
    }
}
