using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Spec 333 (#2297): the end-to-end job's stack boots in run mode with no
/// <c>E2ETests</c> — the same shape a developer's <c>aspire run</c> composes —
/// so it inherits persistent container lifetimes, five named data volumes and
/// pgAdmin, none of which the Playwright suite depends on and all of which
/// outlive the <c>kill</c> that ends the job (spec.md §1-§2).
///
/// <para>
/// The fix is a new, orthogonal <c>PersistentStack</c> switch (default on, the
/// same fail-open shape <c>ScenarioSimulator</c> already uses), evaluated only
/// inside the existing <c>isRunMode &amp;&amp; !isE2ETests</c> gate, so the
/// integration fixture's own composition (<c>E2ETests=true</c>) cannot be
/// reached by it. This class is the model-composition half of that proof: it
/// reads the <em>composed</em> <c>DistributedApplicationTestingBuilder</c>
/// model, the same footing as <see cref="AppHostE2ESwitchTests"/>,
/// <see cref="AppHostReplicaCountTests"/> and
/// <see cref="AppHostGatewayRateBudgetTests"/> — <c>CreateAsync</c> starts no
/// resources, so this costs no containers and is safe beside a live
/// <c>aspire run</c>.
/// </para>
///
/// <para>
/// <b>Witness against vacuous passes</b> (spec 169 §5.4):
/// <see cref="The_developer_lane_keeps_its_persistent_containers_volumes_and_pgadmin"/>
/// names seven resources explicitly as <c>ContainerLifetime.Persistent</c> and
/// five of them as carrying a data volume, which proves the reader this file's
/// e2e-lane facts share can actually see a persistent lifetime and a volume
/// mount at all — so those facts' "none of this" assertions cannot pass
/// because the reader itself is broken.
/// </para>
///
/// <para>
/// <c>DevArguments</c>/<c>FixtureArguments</c> are copied from
/// <see cref="AppHostReplicaCountTests"/>, not extracted into a shared helper —
/// ADR-0109 (disjoint files): that class is untouched by this slice, and
/// extracting would touch it. Likewise the environment/runtime-args gather
/// helpers below reproduce the shape <see cref="AppHostGatewayRateBudgetTests"/>
/// already established (gather-only, never the resolve step that hangs on an
/// unallocated <c>EndpointReference</c> — that class's own doc comment records
/// why) rather than inventing a different reader for the same composed model.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostStackPersistenceTests
{
    private const string Workflow = ".github/workflows/ci.yml";
    private const string BootCommand = "dotnet run --project src/AppHost";
    private const string PersistentStackArgument = "PersistentStack=false";
    private const string PermitLimitKey = "RateLimiting__PermitLimit";
    private const string AdditionalHostsKey = "MTX_WEBRTCADDITIONALHOSTS";

    /// <summary>
    /// The exact resources that still carry <c>ContainerLifetime.Persistent</c>
    /// and a data volume in a developer's bare <c>aspire run</c> — unaffected
    /// by this spec, which only narrows when the e2e lane composes the same
    /// set, never the developer lane's own default.
    /// </summary>
    private static readonly string[] PersistentDevLaneResources =
    [
        "postgres", "rabbitmq", "keycloak", "mosquitto", "storage", "mediamtx", "camera-sim",
    ];

    private static readonly string[] VolumeMountedDevLaneResources =
    [
        "postgres", "rabbitmq", "keycloak", "mosquitto", "storage",
    ];

    /// <summary>
    /// A developer's bare <c>aspire run</c>. Mirrors
    /// <see cref="AppHostReplicaCountTests"/>'s <c>RunModeArguments</c>.
    /// </summary>
    private static readonly string[] DevArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
    ];

    /// <summary>
    /// <c>ci.yml</c>'s end-to-end job boot, once this spec's fix lands:
    /// run mode, the simulator off, and the new switch off.
    /// </summary>
    private static readonly string[] E2EArguments =
    [
        .. DevArguments,
        "ScenarioSimulator=false",
        "PersistentStack=false",
    ];

    /// <summary>
    /// A misspelled or unrecognised value. The switch must fail open to a
    /// developer's stack, the same convention <c>ScenarioSimulator</c> uses —
    /// absent or unparseable is never read as "off".
    /// </summary>
    private static readonly string[] UnparseableArguments =
    [
        .. DevArguments,
        "PersistentStack=nope",
    ];

    private static readonly string[] FixtureArguments =
    [
        .. DevArguments,
        "E2ETests=true",
    ];

    /// <summary>
    /// FR-002 / spec scenario 1. <b>Expected red on the unfixed tree</b>:
    /// <c>AppHost.cs</c> gates every <c>WithLifetime(ContainerLifetime.Persistent)</c>
    /// on <c>isRunMode &amp;&amp; !isE2ETests</c> alone — no <c>PersistentStack</c>
    /// switch exists yet — so the e2e lane's own boot arguments still compose
    /// every container a developer's <c>aspire run</c> does.
    /// </summary>
    [Fact]
    public async Task The_e2e_lane_composes_no_persistent_container()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(E2EArguments);

        IReadOnlyList<string> offenders = PersistentResourceNames(resources);

        offenders.ShouldBeEmpty(
            $"observed persistent resources in the e2e lane: [{string.Join(", ", offenders)}]. "
            + "PersistentStack=false must stop every resource composing with "
            + "ContainerLifetime.Persistent, so a stack killed at the end of the job "
            + "leaves nothing behind (spec.md US1 scenario 1).");
    }

    /// <summary>
    /// FR-002 / spec scenario 1. <b>Expected red on the unfixed tree</b>: the
    /// same unconditional <c>!isE2ETests</c> gate also carries every
    /// <c>WithDataVolume()</c>/<c>WithVolume(...)</c> call.
    /// </summary>
    [Fact]
    public async Task The_e2e_lane_mounts_no_data_volume()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(E2EArguments);

        IReadOnlyList<string> offenders = VolumeMountedResourceNames(resources);

        offenders.ShouldBeEmpty(
            $"observed volume-mounted resources in the e2e lane: [{string.Join(", ", offenders)}]. "
            + "PersistentStack=false must stop every resource mounting a named data volume, so "
            + "the next boot starts from nothing rather than a developer's leftover data "
            + "(spec.md US1 scenario 1).");
    }

    /// <summary>
    /// FR-002 / spec scenario 1. <b>Expected red on the unfixed tree</b>:
    /// <c>postgres.WithPgAdmin()</c> sits inside the same unconditional
    /// <c>isRunMode &amp;&amp; !isE2ETests</c> block.
    /// </summary>
    [Fact]
    public async Task The_e2e_lane_composes_no_pgadmin()
    {
        IReadOnlyList<string> names = await ResourceNamesAsync(E2EArguments);

        names.ShouldNotContain(
            "pgadmin",
            "PersistentStack=false must remove pgAdmin from the e2e lane's composition — "
            + "nothing in the Playwright suite, its support scripts or its wait-for-stack "
            + "readiness gate depends on it (spec.md US1 scenario 1).");
    }

    /// <summary>
    /// FR-003 / spec scenario 2. <b>Expected green already and must stay
    /// green</b>: the four Vite apps, <c>fixture-video</c>, the gateway's
    /// widened rate budget and mediamtx's ICE port map are gated on
    /// <c>isRunMode &amp;&amp; !isE2ETests</c>, or on <c>isRunMode</c> alone —
    /// never on <c>ScenarioSimulator</c> or the new <c>PersistentStack</c>
    /// switch — so losing persistence must not cost any of them.
    /// </summary>
    [Fact]
    public async Task The_e2e_lane_keeps_the_web_apps_the_gateway_budget_and_the_ice_port_map()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(E2EArguments);
        IReadOnlyList<string> names = [.. resources.Select(resource => resource.Name)];

        names.ShouldContain("management-web");
        names.ShouldContain("kiosk-web");
        names.ShouldContain("kiosk-wall");
        names.ShouldContain("management-cameras");
        names.ShouldContain(
            "fixture-video",
            "the e2e lane needs a picture for the wall (spec 056/076); PersistentStack must not "
            + "touch the isRunMode-only gate fixture-video is composed under.");

        IResource gateway = resources.Single(resource => resource.Name == "api-gateway");
        string? permitLimit = await GatheredEnvironmentValueAsync(gateway, PermitLimitKey);

        permitLimit.ShouldBe(
            "6000",
            $"api-gateway's '{PermitLimitKey}' resolved to "
            + $"'{permitLimit ?? "<absent>"}' in the e2e lane; it must stay '6000' "
            + "(spec 232/#2221) — PersistentStack must not narrow the isRunMode && "
            + "!isE2ETests gate that widens the gateway's own budget.");

        IResource mediamtx = resources.Single(resource => resource.Name == "mediamtx");
        IReadOnlyList<string> runtimeArgs = await GatheredContainerRuntimeArgsAsync(mediamtx);

        runtimeArgs.ShouldContain(
            "8189:8189/udp",
            $"observed mediamtx runtime args: [{string.Join(", ", runtimeArgs)}]. The e2e lane's "
            + "browser needs a host-reachable ICE candidate, so the raw docker port map must "
            + "stay even though mediamtx's lifetime no longer does (spec.md edge case).");
        runtimeArgs.ShouldContain("8189:8189/tcp");

        string? additionalHosts = await GatheredEnvironmentValueAsync(mediamtx, AdditionalHostsKey);

        additionalHosts.ShouldBe(
            "127.0.0.1",
            $"mediamtx's '{AdditionalHostsKey}' resolved to '{additionalHosts ?? "<absent>"}' in "
            + "the e2e lane; it must stay '127.0.0.1' so the browser's ICE candidate advertises "
            + "a host it can reach.");
    }

    /// <summary>
    /// FR-002 / spec scenario 3. <b>Expected green already and must stay
    /// green</b> — this is the developer lane's own default, which this spec
    /// does not touch, and it is also this file's witness against a vacuous
    /// pass on every e2e-lane "none of this" fact above (class doc comment).
    /// </summary>
    [Fact]
    public async Task The_developer_lane_keeps_its_persistent_containers_volumes_and_pgadmin()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(DevArguments);

        AssertExactlyPersistent(resources, PersistentDevLaneResources);
        AssertVolumesMounted(resources, VolumeMountedDevLaneResources);

        IReadOnlyList<string> names = [.. resources.Select(resource => resource.Name)];

        names.ShouldContain(
            "pgadmin",
            "a developer's aspire run must keep pgAdmin — PersistentStack defaults on, the same "
            + "fail-open convention ScenarioSimulator already uses.");
    }

    /// <summary>
    /// FR-001 / spec scenario 5's dev half. <b>Expected green already and
    /// must stay green</b>: on the unfixed tree <c>PersistentStack</c> is not
    /// read at all, so the value is inert and the stack is persistent for the
    /// same reason the developer lane always is. After the fix,
    /// <c>bool.TryParse("nope", out _)</c> fails, so
    /// <c>!bool.TryParse(...) || persistent</c> evaluates <c>true</c> —
    /// absent or unparseable both mean "a developer's stack", never "off".
    /// </summary>
    [Fact]
    public async Task An_unparseable_switch_keeps_the_stack_persistent()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(UnparseableArguments);

        AssertExactlyPersistent(resources, PersistentDevLaneResources);

        IReadOnlyList<string> names = [.. resources.Select(resource => resource.Name)];

        names.ShouldContain(
            "pgadmin",
            "PersistentStack=nope must fail open to a developer's stack, not silently disable "
            + "persistence the way a typo in a boolean switch could.");
    }

    /// <summary>
    /// FR-002's untouched half / spec's Edge Cases. <b>Expected green already
    /// and must stay green</b>: the new switch is evaluated only inside the
    /// existing <c>isRunMode &amp;&amp; !isE2ETests</c> gate (plan.md §2.1
    /// step 1), so it cannot reach the integration fixture's own composition.
    /// </summary>
    [Fact]
    public async Task The_integration_fixture_composes_no_persistent_container_and_no_volume()
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(FixtureArguments);

        PersistentResourceNames(resources).ShouldBeEmpty(
            "the integration fixture (E2ETests=true) must keep composing no persistent "
            + "container — PersistentStack is evaluated only inside isRunMode && "
            + "!isE2ETests, so it must never change this lane's shape.");
        VolumeMountedResourceNames(resources).ShouldBeEmpty(
            "the integration fixture (E2ETests=true) must keep mounting no data volume.");

        IReadOnlyList<string> names = [.. resources.Select(resource => resource.Name)];

        names.ShouldNotContain("pgadmin");
    }

    /// <summary>
    /// FR-004 / spec scenario 4. <b>Expected red on the unfixed tree</b>:
    /// <c>ci.yml</c>'s e2e boot line does not pass <c>PersistentStack=false</c>
    /// yet, so the switch's fail-open default leaves the job composing a
    /// developer's persistent stack (mirrors
    /// <see cref="AppHostE2ESwitchTests.The_e2e_boot_line_disables_the_scenario_simulator"/>'s
    /// own reasoning for why a text-reading guard earns its place here: the
    /// switch fails open, so a misspelled or misplaced argument would pass
    /// every model-composition fact above while the workflow silently kept
    /// persistence).
    /// </summary>
    [Fact]
    public void The_e2e_boot_line_disables_persistence()
    {
        string command = BootLine().Split('>', 2)[0];

        command.ShouldContain(
            PersistentStackArgument,
            Case.Sensitive,
            $"{Workflow} boots the end-to-end stack without '{PersistentStackArgument}' ahead of "
            + "the redirection, so the run-mode default applies and the stack composes "
            + "persistent containers, named data volumes and pgAdmin that a restarted job "
            + "should never have inherited (spec.md FR-004).");
    }

    private static void AssertExactlyPersistent(IReadOnlyList<IResource> resources, string[] expected)
    {
        IReadOnlyList<string> persistent = PersistentResourceNames(resources);

        persistent.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            $"observed persistent resources: [{string.Join(", ", persistent)}]; expected exactly "
            + $"[{string.Join(", ", expected)}].");
    }

    private static void AssertVolumesMounted(IReadOnlyList<IResource> resources, string[] expected)
    {
        foreach (string name in expected)
        {
            IResource resource = resources.Single(candidate => candidate.Name == name);

            resource.Annotations.OfType<ContainerMountAnnotation>()
                .Any(mount => mount.Type == ContainerMountType.Volume)
                .ShouldBeTrue($"'{name}' carried no data volume in the developer lane.");
        }
    }

    private static IReadOnlyList<string> PersistentResourceNames(IReadOnlyList<IResource> resources) =>
    [
        .. resources
            .Where(resource => resource.Annotations
                .OfType<ContainerLifetimeAnnotation>()
                .Any(lifetime => lifetime.Lifetime == ContainerLifetime.Persistent))
            .Select(resource => resource.Name),
    ];

    private static IReadOnlyList<string> VolumeMountedResourceNames(IReadOnlyList<IResource> resources) =>
    [
        .. resources
            .Where(resource => resource.Annotations
                .OfType<ContainerMountAnnotation>()
                .Any(mount => mount.Type == ContainerMountType.Volume))
            .Select(resource => resource.Name),
    ];

    private static async Task<IReadOnlyList<IResource>> ComposedResourcesAsync(string[] arguments)
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(arguments);

        return [.. builder.Resources];
    }

    private static async Task<IReadOnlyList<string>> ResourceNamesAsync(string[] arguments)
    {
        IReadOnlyList<IResource> resources = await ComposedResourcesAsync(arguments);

        return [.. resources.Select(resource => resource.Name)];
    }

    /// <summary>
    /// The gather step only, the same shape
    /// <see cref="AppHostGatewayRateBudgetTests"/>'s own
    /// <c>RawGatheredEnvironmentAsync</c> uses and for the same reason:
    /// api-gateway's other environment entries carry unresolved
    /// <c>EndpointReference</c>s that hang if actually resolved, and this
    /// helper never needs to — it only ever reads one literal-valued key.
    /// </summary>
    private static async Task<string?> GatheredEnvironmentValueAsync(IResource resource, string key)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));

        if (!resource.TryGetEnvironmentVariables(out IEnumerable<EnvironmentCallbackAnnotation>? annotations))
        {
            return null;
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

        return environmentVariables.TryGetValue(key, out object? value) ? value?.ToString() : null;
    }

    /// <summary>
    /// mediamtx's raw runtime args — <c>WithContainerRuntimeArgs("--publish", ...)</c>
    /// adds plain strings via <c>context.Args.AddRange(args)</c>, so unlike the
    /// environment gather above there is nothing here that could hang on an
    /// unresolved reference; every callback on this resource is synchronous.
    /// </summary>
    private static async Task<IReadOnlyList<string>> GatheredContainerRuntimeArgsAsync(IResource resource)
    {
        if (!resource.TryGetAnnotationsOfType(out IEnumerable<ContainerRuntimeArgsCallbackAnnotation>? annotations))
        {
            return [];
        }

        List<object> args = [];
        ContainerRuntimeArgsCallbackContext context = new(args);

        foreach (ContainerRuntimeArgsCallbackAnnotation annotation in annotations)
        {
            await annotation.Callback(context);
        }

        return [.. args.Select(arg => arg?.ToString() ?? string.Empty)];
    }

    /// <summary>
    /// A scan that matches nothing passes silently, and a passing guard that
    /// checked nothing is indistinguishable from one that holds — so the
    /// absence of the boot line is itself a failure. Mirrors
    /// <see cref="AppHostE2ESwitchTests"/>'s own <c>BootLine()</c>.
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
    /// literal path: a backslash separator is green on Windows and red on Linux
    /// CI, and this repository has been bitten by exactly that.
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
