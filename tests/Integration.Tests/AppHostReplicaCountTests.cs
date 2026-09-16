namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Guards ADR-0153 clause 1: no service in this system runs more than one
/// instance, because at least five of the nine context services hold
/// per-instance state — in-process SignalR hub state, in-memory caches,
/// per-process dictionaries, a fixed MQTT <c>ClientId</c>, and startup-mutator
/// hosted services — that a second replica corrupts or duplicates.
///
/// <para>
/// Reads the <em>composed application model</em>, not <c>AppHost.cs</c>'s
/// text. A source-text scan over <c>WithReplicas</c> only catches that exact
/// spelling written in that exact file; this repository has disproved guards
/// of that shape by counterfactual before. <c>GetReplicaCount</c> is the same
/// accessor the orchestrator itself consults, so this reads what the runtime
/// reads regardless of how the count got there — a literal call, a helper, a
/// loop, or a value bound from configuration.
/// </para>
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no resources,
/// so this costs no containers and is safe beside a live <c>aspire run</c> —
/// the same footing as <see cref="AppHostE2ESwitchTests"/>.
/// </para>
///
/// <para>
/// <c>api-gateway</c> is the recorded exception (ADR-0153 clause 2,
/// <c>AppHost.cs</c>'s <c>// HA (#1005)</c> comment block): it runs at 2
/// replicas in run mode, gated off under <c>E2ETests=true</c> so the gateway
/// routing/rate-limit integration tests resolve a single endpoint. Its fate —
/// a shared rate-limiter store, or returning to one replica — is #2283's
/// decision, not this guard's; this guard only pins the count as it stands.
/// </para>
///
/// <para>
/// The run-mode assertion below asserts <c>api-gateway</c> is present
/// <em>and</em> at exactly 2, not merely that nothing else exceeds 1. A
/// purely negative assertion ("nothing above one") passes vacuously against
/// an empty model or a broken accessor; the known-broken exception is this
/// guard's own liveness witness — the only resource in the system whose count
/// is above one, so the only available proof that the guard can see a count
/// above one at all. If #2283 returns the gateway to one replica, this
/// witness is lost and whoever closes it must add a synthetic two-replica
/// resource to a throwaway model, or accept the phase-4 counterfactual record
/// as the standing evidence (spec 169 §5.4).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostReplicaCountTests
{
    private const string OneInstanceRule =
        "ADR-0153 clause 1 pins every service to one instance: at least five of the nine "
        + "context services hold per-instance state that a second replica corrupts or "
        + "duplicates. Clause 3 is the only route to an exception — enumerate and resolve "
        + "the context's per-instance state, and add a test that runs two instances and "
        + "fails without the fix.";

    private const string GatewayException =
        "api-gateway's replica count is a recorded ADR-0153 clause-2 exception. #2283 owns "
        + "the choice between a shared rate-limiter store and returning to one replica — it "
        + "must not be changed by an unrelated slice.";

    /// <summary>
    /// The shape a developer's <c>aspire run</c> and the end-to-end job boot:
    /// parameters only, no <c>E2ETests</c>. Mirrors
    /// <see cref="AppHostE2ESwitchTests"/>.
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

    [Fact]
    public async Task A_run_mode_stack_composes_only_the_recorded_exception_above_one_instance()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(RunModeArguments);

        Dictionary<string, int> aboveOneInstance = ResourcesAboveOneInstance(resources);

        Dictionary<string, int> onlyRecordedException = new() { ["api-gateway"] = 2 };

        aboveOneInstance.ShouldBeEquivalentTo(
            onlyRecordedException,
            $"observed above-one-instance resources: [{Describe(aboveOneInstance)}]; expected only "
            + $"[{Describe(onlyRecordedException)}]. A run-mode AppHost model must compose every "
            + $"service at one instance except the recorded api-gateway exception. "
            + $"{OneInstanceRule} {GatewayException}");
    }

    [Fact]
    public async Task The_integration_lane_composes_every_service_at_one_instance()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(FixtureArguments);

        Dictionary<string, int> aboveOneInstance = ResourcesAboveOneInstance(resources);

        aboveOneInstance.ShouldBeEmpty(
            $"observed above-one-instance resources: [{Describe(aboveOneInstance)}]. The "
            + $"integration fixture (E2ETests=true) must compose every service at one "
            + $"instance, including api-gateway, which is gated off under this flag by "
            + $"AppHost.cs's `// HA (#1005)` comment block. {OneInstanceRule}");

        IResource? gateway = resources.SingleOrDefault(resource => resource.Name == "api-gateway");

        gateway.ShouldNotBeNull(
            "api-gateway was absent from the E2ETests=true model — 'nothing above one' is "
            + "satisfied by an empty model, so this guard asserts the gateway's presence "
            + "explicitly rather than trusting the negative check alone.");

        gateway.GetReplicaCount().ShouldBe(
            1,
            "api-gateway must resolve to a single endpoint under E2ETests=true so the "
            + "gateway routing/rate-limit integration tests are not split across replicas.");
    }

    /// <summary>
    /// The population gate. A model that failed to compose, or a filter that
    /// matched nothing, must fail rather than pass silently — the same
    /// reasoning <c>IntegrationTestSelectionTests</c> applies to its own scan.
    /// </summary>
    [Fact]
    public async Task The_composed_model_is_read_before_any_replica_count_is_judged()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(RunModeArguments);

        resources.ShouldNotBeEmpty(
            "the composed AppHost model was empty — the scan is broken, not passing, because "
            + "it observed nothing.");

        resources.ShouldContain(
            resource => resource.Name == "api-gateway",
            "api-gateway was absent from the composed model — the scan is broken, not "
            + "passing, because a guard that cannot see its own liveness witness (spec 169 "
            + "§5.4) proves nothing about any other resource either.");
    }

    /// <summary>
    /// Renders a replica-count set as <c>name=count, name=count</c> so a
    /// failure message names the offending resource and its count in the
    /// message text itself, rather than depending on how much of the actual
    /// value Shouldly's own diff happens to print.
    /// </summary>
    private static string Describe(Dictionary<string, int> replicaCounts) =>
        string.Join(", ", replicaCounts.Select(entry => $"{entry.Key}={entry.Value}"));

    private static Dictionary<string, int> ResourcesAboveOneInstance(IReadOnlyList<IResource> resources)
    {
        Dictionary<string, int> aboveOneInstance = [];

        foreach (IResource resource in resources)
        {
            int count = resource.GetReplicaCount();

            if (count > 1)
            {
                aboveOneInstance[resource.Name] = count;
            }
        }

        return aboveOneInstance;
    }

    private static async Task<IReadOnlyList<IResource>> ComposedResourcesAsync(string[] arguments)
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(arguments);

        return [.. builder.Resources];
    }
}
