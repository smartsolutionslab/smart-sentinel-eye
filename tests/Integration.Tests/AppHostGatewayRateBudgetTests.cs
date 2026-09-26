using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Spec 232 (#2221) US2 / FR-002-003: a four-tile live kiosk wall's own routine
/// telemetry — polling, latency and skew POSTs, none of it an operator write —
/// spends roughly 246 gateway requests/min at rest (spec.md §2's table), over
/// the api-gateway's 100/min-per-replica production default
/// (<c>src/ApiGateway/appsettings.json</c>) by itself, because no client sends
/// <c>X-Fab</c> so every browser in a local or CI run shares one
/// source-IP partition. The dev/CI stack widens the bucket for its own
/// topology; the production default, and the partition key, are untouched
/// (plan.md §2).
///
/// <para>
/// <b>Builds the application model only</b>: <c>CreateAsync</c> starts no
/// resources, so this costs no containers and is safe beside a live
/// <c>aspire run</c> — the same footing as <see cref="AppHostReplicaCountTests"/>
/// and <see cref="AppHostE2ESwitchTests"/>.
/// </para>
///
/// <para>
/// Reads the <b>composed, but unresolved, environment</b> — the raw values
/// each <c>EnvironmentCallbackAnnotation.Callback</c> writes — rather than
/// <c>AppHost.cs</c>'s source text: the same disproved-by-counterfactual
/// reasoning <see cref="AppHostReplicaCountTests"/>'s own doc comment gives
/// for reading <c>GetReplicaCount()</c> instead of grepping for
/// <c>WithReplicas</c>. <b>Deliberately not</b>
/// <c>ExecutionConfigurationBuilder.BuildAsync</c> (nor the
/// <c>[Obsolete]</c> <c>ResourceExtensions.GetEnvironmentVariableValuesAsync</c>
/// it replaces at the pinned 13.5.3, decompiled from the pinned assembly —
/// memory: a plan's SDK claim needs checking against the pinned DLL): both
/// resolve <i>every</i> environment entry to a string, including the eight of
/// api-gateway's own nine <c>WithReference(...)</c> calls whose value is an
/// unallocated <c>EndpointReference</c> — resolving one of those hangs
/// indefinitely on a <c>CreateAsync</c>-only model (verified directly: the
/// full-resolve version of this file hung past ten minutes and was killed).
/// This guard only ever needs one literal key it knows is a plain string, so
/// it stops after the gather step and reads that one raw value straight —
/// never touching the eight it cannot safely resolve.
/// </para>
///
/// <para>
/// <c>RunModeArguments</c> / <c>FixtureArguments</c> below are copied from
/// <see cref="AppHostReplicaCountTests"/>, not extracted into a shared helper
/// — ADR-0109 (disjoint files): that class is untouched by this slice, and
/// extracting would touch it.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostGatewayRateBudgetTests
{
    private const string PermitLimitKey = "RateLimiting__PermitLimit";

    private const string ExpectedRunModeValue = "6000";

    /// <summary>
    /// The derivation, quoted in every failure message so a later change is
    /// told where 6 000 came from (plan.md §2), the way
    /// <c>WhepAuthorizeCeilingTests</c> quotes its own ceiling's derivation.
    /// </summary>
    private const string Derivation =
        "Derivation (plan.md §2, spec 232/#2221): one four-tile live wall spends ~246 gateway "
        + "requests/min at rest doing nothing but showing video (spec.md §2's table) — already over "
        + "the 100/min-per-replica production default by itself. A local run at the default worker "
        + "count budgets for 12 concurrent four-tile walls (8 workers, some specs holding two pages) "
        + "≈ 2 950/min, doubled for margin ≈ 6 000/min per replica, without relying on the "
        + "two api-gateway replicas' round-robin halving it. The window stays the 1-minute default: "
        + "the fix is the size of the bucket, not the shape of the window.";

    /// <summary>
    /// Run mode with no <c>E2ETests</c>. Not the shape a developer's
    /// <c>aspire run</c> or the end-to-end job boot — neither passes a
    /// <c>Parameters:</c> argument. Mirrors <see cref="AppHostReplicaCountTests"/>.
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
    /// US2 happy path / FR-002. <b>Expected red on the unfixed tree</b>: no
    /// <c>WithEnvironment("RateLimiting__PermitLimit", ...)</c> call exists in
    /// <c>AppHost.cs</c> yet, so the key is absent from the composed
    /// environment rather than carrying <c>"6000"</c>.
    /// </summary>
    [Fact]
    public async Task A_run_mode_stack_widens_the_gateways_rate_budget_for_its_own_simulated_screens()
    {
        Dictionary<string, string> environment = await GatewayEnvironmentAsync(RunModeArguments);

        environment.ShouldContainKey(
            PermitLimitKey,
            $"api-gateway's composed environment carried no '{PermitLimitKey}' in run mode without "
            + $"E2ETests. It must be set to '{ExpectedRunModeValue}' so the dev/CI stack's gateway "
            + $"budget covers its own simulated screens instead of the 100/min production default "
            + $"(src/ApiGateway/appsettings.json) that a single four-tile wall alone already exceeds. "
            + Derivation);

        environment[PermitLimitKey].ShouldBe(
            ExpectedRunModeValue,
            $"api-gateway's '{PermitLimitKey}' resolved to '{environment[PermitLimitKey]}' in run mode "
            + $"without E2ETests; it must be '{ExpectedRunModeValue}'. " + Derivation);
    }

    /// <summary>
    /// US2 conflict guard / FR-003. <b>Expected green already and must stay
    /// green</b>: nothing in the unfixed tree sets this key under any gate, so
    /// it is absent here for the same reason it is absent in run mode — this
    /// fact only starts proving the guard once T004 lands the gate as
    /// <c>isRunMode &amp;&amp; !isE2ETests</c> rather than <c>isRunMode</c> alone.
    /// <c>GatewayRateLimitIntegrationTests</c> exhausts exactly the 100/min
    /// production default this guards; if it were overridden here, that test
    /// would fail without ever changing.
    /// </summary>
    [Fact]
    public async Task The_integration_fixture_keeps_the_production_rate_budget_unoverridden()
    {
        Dictionary<string, string> environment = await GatewayEnvironmentAsync(FixtureArguments);

        environment.ShouldNotContainKey(
            PermitLimitKey,
            $"api-gateway's composed environment carried '{PermitLimitKey}'='"
            + $"{(environment.TryGetValue(PermitLimitKey, out string? observed) ? observed : string.Empty)}' "
            + "under E2ETests=true. The integration fixture must keep the 100/min production default "
            + "(src/ApiGateway/appsettings.json) unoverridden — GatewayRateLimitIntegrationTests "
            + "exhausts exactly that window, and a widened budget here would silently break it. "
            + Derivation);
    }

    /// <summary>
    /// The single raw environment entry this guard cares about, keyed the
    /// same way a run-mode boot would compose it:
    /// <see cref="DistributedApplicationOperation.Run"/>, matching every
    /// other resource-model fact in this file's neighbourhood
    /// (<see cref="AppHostReplicaCountTests"/>, <see cref="AppHostParameterOverrideTests"/>)
    /// which never passes <c>Publish</c>.
    /// </summary>
    private static async Task<Dictionary<string, string>> GatewayEnvironmentAsync(string[] arguments)
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(arguments);

        IResource gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");

        Dictionary<string, object> raw = await RawGatheredEnvironmentAsync(gateway);

        // Only PermitLimitKey is converted to a string. Every other entry may
        // carry an unresolved ReferenceExpression/EndpointReference (see the
        // class doc's "deliberately not BuildAsync" paragraph) that this
        // guard has no business — and no safe way — to resolve.
        return raw.TryGetValue(PermitLimitKey, out object? value)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PermitLimitKey] = value?.ToString() ?? string.Empty,
            }
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The gather step only — every <see cref="EnvironmentCallbackAnnotation"/>
    /// on <paramref name="resource"/> is invoked once, exactly as
    /// <c>Aspire.Hosting.ApplicationModel.EnvironmentVariablesExecutionConfigurationGatherer.GatherAsync</c>
    /// does internally (decompiled from the pinned 13.5.3 assembly; that type
    /// is <c>internal</c>, so this reproduces its five lines against the
    /// public <see cref="EnvironmentCallbackAnnotation.Callback"/> instead of
    /// calling it). Unlike <c>ExecutionConfigurationBuilder.BuildAsync</c>,
    /// this never calls the resolve step that turns every raw value into a
    /// string — the step that hangs on an unallocated <c>EndpointReference</c>
    /// (class doc). A bounded token is still passed through as a last-resort
    /// safety net, in case some other callback's own work is not as purely
    /// synchronous as api-gateway's <c>WithReference</c>/<c>WithEnvironment</c>
    /// calls observed to be.
    /// </summary>
    private static async Task<Dictionary<string, object>> RawGatheredEnvironmentAsync(IResource resource)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));

        if (!resource.TryGetEnvironmentVariables(out IEnumerable<EnvironmentCallbackAnnotation>? annotations))
        {
            return [];
        }

        Dictionary<string, object> environmentVariables = [];
        EnvironmentCallbackContext context = new(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            resource,
            environmentVariables,
            deadline.Token)
        {
            Logger = NullLogger.Instance,
        };

        foreach (EnvironmentCallbackAnnotation annotation in annotations)
        {
            await annotation.Callback(context);
        }

        return environmentVariables;
    }
}
