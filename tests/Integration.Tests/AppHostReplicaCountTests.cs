using Aspire.Hosting;

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
/// A purely negative assertion ("nothing above one") passes vacuously against
/// an empty model or a broken accessor. With no real resource above one
/// instance left to witness that the accessor can see a count above one, the
/// witness is a throwaway model holding one resource at two replicas beside
/// one at the default, read through the same <c>ResourcesAboveOneInstance</c>
/// the real-model assertions use (spec 169 §5.4). The real-model assertions
/// also name <c>api-gateway</c> explicitly, so an empty model cannot satisfy
/// them either.
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

    /// <summary>
    /// Run mode with no <c>E2ETests</c>. Not the shape a developer's
    /// <c>aspire run</c> or the end-to-end job boot — neither passes a <c>Parameters:</c>
    /// argument. Mirrors <see cref="AppHostE2ESwitchTests"/>.
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
    public async Task A_run_mode_stack_composes_every_service_at_one_instance()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(RunModeArguments);

        Dictionary<string, int> aboveOneInstance = ResourcesAboveOneInstance(resources);

        aboveOneInstance.ShouldBeEmpty(
            $"observed above-one-instance resources: [{Describe(aboveOneInstance)}]. A run-mode "
            + $"AppHost model must compose every service at one instance, api-gateway included: "
            + $"its in-memory rate limiter is per process, so a second replica multiplies every "
            + $"caller's limit (#2283). {OneInstanceRule}");

        IResource? gateway = resources.SingleOrDefault(resource => resource.Name == "api-gateway");

        gateway.ShouldNotBeNull(
            "api-gateway was absent from the run-mode model — 'nothing above one' is satisfied "
            + "by an empty model, so this guard asserts the gateway's presence explicitly rather "
            + "than trusting the negative check alone.");

        gateway.GetReplicaCount().ShouldBe(
            1,
            "api-gateway must run as one instance in run mode: no shared rate-limiter store "
            + "exists, so each replica would hold its own window (ADR-0153 clause 2, #2283).");
    }

    /// <summary>
    /// The liveness witness. Neither real model has a resource above one
    /// instance, so this proves on a throwaway model that
    /// <c>ResourcesAboveOneInstance</c> still reports a count above one — and
    /// only that one, not the resource left at the default. Builds the model
    /// only; nothing is started, and the project path is never compiled.
    /// </summary>
    [Fact]
    public void The_scan_reports_a_resource_composed_above_one_instance_and_nothing_else()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder([]);
        string projectPath = SyntheticProjectPath();

        builder.AddProject("synthetic-single", projectPath);
        builder.AddProject("synthetic-doubled", projectPath).WithReplicas(2);

        Dictionary<string, int> aboveOneInstance = ResourcesAboveOneInstance([.. builder.Resources]);

        Dictionary<string, int> onlyTheDoubled = new() { ["synthetic-doubled"] = 2 };

        aboveOneInstance.ShouldBeEquivalentTo(
            onlyTheDoubled,
            $"observed above-one-instance resources: [{Describe(aboveOneInstance)}]; expected "
            + $"exactly [{Describe(onlyTheDoubled)}]. The scan the real-model assertions rely on "
            + "cannot see a replica count above one — or cannot tell it from one — so their "
            + "'nothing above one' proves nothing.");
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
            + "passing, because a guard that cannot see the resource it names explicitly "
            + "proves nothing about any other resource either.");
    }

    /// <summary>
    /// A real project file, so the path-string <c>AddProject</c> overload has
    /// something to point at; the witness never builds or starts it. Built
    /// with <see cref="System.IO.Path.Combine(string[])"/>, not a literal
    /// separator — green on Windows and red on Linux CI otherwise.
    /// </summary>
    private static string SyntheticProjectPath()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        string root = candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        return System.IO.Path.Combine(root, "src", "ApiGateway", "SmartSentinelEye.ApiGateway.csproj");
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
