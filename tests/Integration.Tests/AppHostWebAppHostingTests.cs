using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Characterises the three Aspire JS resources (<c>management-web</c>,
/// <c>kiosk-web</c>, <c>kiosk-wall</c>) exactly as <c>Aspire.Hosting.NodeJs</c>
/// 9.5.2's <c>AddNpmApp</c> composes them today, ahead of the swap to
/// <c>Aspire.Hosting.JavaScript</c> 13.5.4 (issue #2643, spec 283).
///
/// <para>
/// <b>Characterisation, not red</b> (constitution §Testing, ADR-0139/ADR-0144,
/// plan.md §4): the package swap is behaviour-preserving (spec 283
/// FR-002/FR-003/FR-004), so this file is observed green here, against
/// <c>develop</c>'s pinned 9.5.2 package, and must pass <b>unmodified</b> once
/// the engineer swaps the package. An assertion that needs editing to stay
/// green is evidence the swap moved behaviour, and blocks the refactor rather
/// than being "fixed" here.
/// </para>
///
/// <para>
/// <b>Never names <c>NodeAppResource</c> or its 13.5.4 replacement</b> — that
/// type identity is exactly what the swap changes. Every assertion below goes
/// through base <c>Aspire.Hosting.ApplicationModel</c> surface both packages
/// expose: <see cref="ExecutableResource"/>,
/// <see cref="CommandLineArgsCallbackAnnotation"/>, <see cref="EndpointAnnotation"/>,
/// <see cref="WaitAnnotation"/>, <see cref="ResourceRelationshipAnnotation"/>,
/// <see cref="EnvironmentCallbackAnnotation"/> and
/// <see cref="SmartSentinelEye.AppHost.StackStatusReport"/>.
/// </para>
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no resources,
/// so this costs no containers and is safe beside a live <c>aspire run</c> —
/// the same footing as <see cref="AppHostE2ESwitchTests"/> and
/// <see cref="AppHostMediaMtxImageTests"/>, whose conventions this mirrors.
/// </para>
///
/// <para>
/// <b>Arguments and environment are read raw, never resolved.</b> This is the
/// same reasoning <see cref="AppHostGatewayRateBudgetTests"/>'s own doc
/// comment gives (verified directly there: the full-resolve version of that
/// file hung past ten minutes on an unallocated <c>EndpointReference</c> and
/// was killed). The three apps' <c>npm run dev</c> arguments are plain
/// strings with no endpoint reference, so the gather step alone already
/// equals the final value; the environment carries several endpoint-valued
/// entries (<c>VITE_API_GATEWAY_URL</c>, <c>VITE_KEYCLOAK_URL</c>,
/// <c>VITE_LAYOUT_HUB_ORIGIN</c>), so only key presence is asserted for
/// those, per plan.md §4 fact 6.
/// </para>
///
/// <para>
/// The public, non-<c>[Experimental]</c> surface used here was decompiled
/// directly from the pinned 13.5.4 and 9.5.2 assemblies with
/// <c>ilspycmd</c>, not recalled from memory (memory: <i>a plan's SDK claim
/// needs checking against the pinned version</i>).
/// <c>ResourceExtensions.TryGetAnnotationsOfType</c> and
/// <c>ResourceExtensions.TryGetEnvironmentVariables</c> are public; the
/// obsolete <c>ResourceExtensions.GetArgumentValuesAsync</c> resolves every
/// entry (the same hang risk the environment's full resolve carries) and
/// would fail this repository's warnings-as-errors Release build besides
/// (ADR-0034), so this file replicates the internal
/// <c>ArgumentsExecutionConfigurationGatherer</c>'s gather step against the
/// public <see cref="CommandLineArgsCallbackAnnotation.Callback"/> instead —
/// exactly as <see cref="AppHostGatewayRateBudgetTests"/> already does for
/// <see cref="EnvironmentCallbackAnnotation"/>.
/// </para>
///
/// <para>
/// <c>EndpointAnnotation.TargetPortEnvironmentVariable</c> — the field
/// <c>WithHttpEndpoint(env: "PORT", …)</c> sets — is <c>internal</c> to
/// <c>Aspire.Hosting</c>, so plan.md §4 fact 4's port-environment-variable-name
/// check is folded into the raw environment gather instead: the
/// <c>"PORT"</c> key's presence is asserted there, not read off the
/// annotation directly.
/// </para>
///
/// <para>
/// <b>No counterfactual run accompanies this commit.</b> Plan.md §4 asks for
/// one against <c>.WithNpm(install: false)</c> and the fixed ports, but that
/// call does not exist until the engineer's own commit adds it, and this
/// task's brief is tests only — <c>AppHost.cs</c> stays untouched here. The
/// counterfactual belongs to phase 5, once the swap exists for it to break
/// against (memory: <i>prove a guard by counterfactual</i>).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostWebAppHostingTests
{
    /// <summary>
    /// Run mode with no <c>E2ETests</c>. Not the shape a developer's
    /// <c>aspire run</c> or CI's end-to-end job see on their own — neither
    /// passes a <c>Parameters:</c> argument by itself.
    /// </summary>
    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
    ];

    /// <summary>
    /// CI's end-to-end job's own shape (mirrors
    /// <c>AppHostStackStatusTests.E2EJobArguments</c> and
    /// <c>AppHostE2ESwitchTests.SimulatorDisabledArguments</c>; copied rather
    /// than shared — ADR-0109 disjoint files, neither of those classes is
    /// touched by this slice). Run mode, no <c>E2ETests</c>, simulator off:
    /// the boot path the three web apps actually compose under in the lane
    /// that proves them end to end.
    /// </summary>
    private static readonly string[] SimulatorDisabledArguments =
    [
        .. RunModeArguments,
        "ScenarioSimulator=false",
    ];

    /// <summary>
    /// The three resources spec 283 names (FR-002). Written out rather than
    /// derived: a resource silently dropped from the composition is the
    /// defect a derived list would hide. <see cref="ExpectedShape"/> is the
    /// single source for each one's app-directory suffix and fixed port, so
    /// per-theory-row values below stay in this one table.
    /// </summary>
    public static TheoryData<string> WebApps => ["management-web", "kiosk-web", "kiosk-wall"];

    /// <summary>
    /// The two resources that reference <c>layout-composition</c> and carry
    /// its hub-origin alias — <c>management-web</c> does not.
    /// </summary>
    public static TheoryData<string> KioskApps => ["kiosk-web", "kiosk-wall"];

    /// <summary>
    /// The app-directory suffix and fixed port spec 283 FR-002 names for each
    /// resource. Kept as a lookup rather than a wider <c>TheoryData</c> row so
    /// every theory method below only declares the parameter it actually
    /// asserts on (xUnit1026).
    /// </summary>
    private static (string WorkingDirectorySuffix, int Port) ExpectedShape(string name) => name switch
    {
        "management-web" => ("apps/management-web", 5173),
        "kiosk-web" => ("apps/kiosk-web", 5174),
        "kiosk-wall" => ("apps/kiosk-web", 5175),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not one of the three web apps spec 283 names"),
    };

    [Theory]
    [MemberData(nameof(WebApps))]
    public async Task The_app_runs_npm_run_dev(string name)
    {
        IResource resource = await ResourceAsync(name);
        ExecutableResource executable = (ExecutableResource)resource;

        executable.Command.ShouldBe("npm");

        IReadOnlyList<string> arguments = await ArgumentValuesAsync(executable);

        arguments.ShouldBe(["run", "dev"]);
    }

    [Theory]
    [MemberData(nameof(WebApps))]
    public async Task The_app_s_working_directory_is_its_app_folder(string name)
    {
        (string workingDirectorySuffix, _) = ExpectedShape(name);

        IResource resource = await ResourceAsync(name);
        ExecutableResource executable = (ExecutableResource)resource;

        NormalizeSeparators(executable.WorkingDirectory).ShouldEndWith(
            NormalizeSeparators(workingDirectorySuffix),
            Case.Sensitive,
            $"'{name}' is expected to run out of '.../{workingDirectorySuffix}', "
            + $"found working directory '{executable.WorkingDirectory}'.");
    }

    /// <summary>
    /// Plan.md §4 fact 3, first half — 9.5.2 composes no <c>-installer</c>
    /// resource at all, so this holds vacuously today and must go on holding
    /// once 13.5.4's default install step is switched off.
    /// </summary>
    [Theory]
    [MemberData(nameof(WebApps))]
    public async Task Nothing_waits_on_an_installer_resource(string name)
    {
        IResource resource = await ResourceAsync(name);

        resource.Annotations.OfType<WaitAnnotation>().ShouldNotContain(
            wait => wait.Resource.Name.EndsWith("-installer", StringComparison.Ordinal),
            $"'{name}' waits on a resource named like an installer — 13.5.4's default "
            + "install step must stay off (spec 283 FR-003).");
    }

    /// <summary>
    /// Plan.md §4 fact 3, second half — the readiness gate's own resource set
    /// must never grow an installer, holding vacuously on 9.5.2 (no
    /// <c>-installer</c> resource exists yet) and substantively once 13.5.4's
    /// installer children are composed but marked
    /// <c>ExplicitStartupAnnotation</c>, which
    /// <see cref="SmartSentinelEye.AppHost.StackStatusReport.ExpectedResourceNames"/>
    /// already excludes.
    /// </summary>
    [Fact]
    public async Task The_expected_resource_set_names_no_installer()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(SimulatorDisabledArguments);

        IReadOnlyList<string> names = SmartSentinelEye.AppHost.StackStatusReport.ExpectedResourceNames(builder.Resources);

        names.ShouldNotContain(
            name => name.EndsWith("-installer", StringComparison.Ordinal),
            "the e2e readiness gate must not wait on an installer resource (spec 283 FR-003/FR-004).");
    }

    [Theory]
    [MemberData(nameof(WebApps))]
    public async Task The_app_exposes_exactly_one_fixed_unproxied_http_endpoint(string name)
    {
        (_, int port) = ExpectedShape(name);

        IResource resource = await ResourceAsync(name);

        EndpointAnnotation[] endpoints = [.. resource.Annotations.OfType<EndpointAnnotation>()];

        endpoints.ShouldHaveSingleItem(
            $"'{name}' is expected to carry exactly one endpoint annotation, found {endpoints.Length}.");

        endpoints[0].Name.ShouldBe("http");
        endpoints[0].Port.ShouldBe(port);
        endpoints[0].IsProxied.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(WebApps))]
    public async Task The_app_is_a_child_of_the_api_gateway(string name)
    {
        IResource resource = await ResourceAsync(name);

        ResourceRelationshipAnnotation[] parents =
        [
            .. resource.Annotations
                .OfType<ResourceRelationshipAnnotation>()
                .Where(relationship => relationship.Type == "Parent"),
        ];

        parents.ShouldHaveSingleItem(
            $"'{name}' is expected to carry exactly one parent relationship, found {parents.Length}.");

        parents[0].Resource.Name.ShouldBe("api-gateway");
    }

    /// <summary>
    /// Plan.md §4 fact 6's minimum key set, common to all three apps. Values
    /// are not asserted for the endpoint-valued entries
    /// (<c>VITE_API_GATEWAY_URL</c>, <c>VITE_KEYCLOAK_URL</c>) — resolving
    /// them needs endpoint allocation this model-only test never performs
    /// (class doc). <c>PORT</c> stands in for
    /// <c>EndpointAnnotation.TargetPortEnvironmentVariable</c>, which is
    /// internal (class doc).
    /// </summary>
    [Theory]
    [MemberData(nameof(WebApps))]
    public async Task The_app_s_environment_carries_the_common_keys(string name)
    {
        IResource resource = await ResourceAsync(name);

        Dictionary<string, object> environment = await RawEnvironmentAsync(resource);

        environment.Keys.ShouldContain("VITE_API_GATEWAY_URL");
        environment.Keys.ShouldContain("VITE_KEYCLOAK_URL");
        environment.Keys.ShouldContain("NODE_ENV");
        environment.Keys.ShouldContain("PORT");
    }

    [Theory]
    [MemberData(nameof(KioskApps))]
    public async Task The_kiosk_app_s_environment_carries_the_layout_hub_origin(string name)
    {
        IResource resource = await ResourceAsync(name);

        Dictionary<string, object> environment = await RawEnvironmentAsync(resource);

        environment.Keys.ShouldContain("VITE_LAYOUT_HUB_ORIGIN");
    }

    /// <summary>
    /// Unlike the endpoint-valued entries above, <c>VITE_KIOSK_MODE</c> is a
    /// plain literal (<c>WithEnvironment("VITE_KIOSK_MODE", "wall")</c>), so
    /// the gathered, unresolved value already equals the final one — no
    /// allocation risk, so the value itself is asserted rather than only the
    /// key.
    /// </summary>
    [Fact]
    public async Task The_wall_app_s_environment_sets_kiosk_mode_to_wall()
    {
        IResource resource = await ResourceAsync("kiosk-wall");

        Dictionary<string, object> environment = await RawEnvironmentAsync(resource);

        environment.ShouldContainKeyAndValue("VITE_KIOSK_MODE", "wall");
    }

    private static async Task<IResource> ResourceAsync(string name)
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(SimulatorDisabledArguments);

        return builder.Resources.Single(resource => resource.Name == name);
    }

    /// <summary>
    /// The gather step only, replicating
    /// <c>ArgumentsExecutionConfigurationGatherer.GatherAsync</c> (decompiled
    /// from the pinned 13.5.4 assembly; that type is <c>internal</c>) against
    /// the public <see cref="CommandLineArgsCallbackAnnotation.Callback"/>.
    /// The three apps' arguments are plain strings ("run", "dev"), so no
    /// resolve step is needed or attempted.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ArgumentValuesAsync(IResource resource)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));

        if (!resource.TryGetAnnotationsOfType(out IEnumerable<CommandLineArgsCallbackAnnotation>? annotations))
        {
            return [];
        }

        List<object> gathered = [];
        CommandLineArgsCallbackContext context = new(gathered, resource, deadline.Token)
        {
            Logger = NullLogger.Instance,
        };

        foreach (CommandLineArgsCallbackAnnotation annotation in annotations)
        {
            await annotation.Callback(context);
        }

        return [.. gathered.Select(value => value?.ToString() ?? string.Empty)];
    }

    /// <summary>
    /// The gather step only — every <see cref="EnvironmentCallbackAnnotation"/>
    /// on <paramref name="resource"/> is invoked once, exactly as
    /// <see cref="AppHostGatewayRateBudgetTests"/>'s own
    /// <c>RawGatheredEnvironmentAsync</c> does, and for the same reason
    /// (class doc): the resolve step hangs on an unallocated
    /// <c>EndpointReference</c>, which several of these entries carry.
    /// </summary>
    private static async Task<Dictionary<string, object>> RawEnvironmentAsync(IResource resource)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));

        if (!resource.TryGetEnvironmentVariables(out IEnumerable<EnvironmentCallbackAnnotation>? annotations))
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
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

    /// <summary>
    /// A backslash separator is green on Windows and red on Linux CI (memory:
    /// <i>source-scanning tests need slash normalising</i>).
    /// </summary>
    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');
}
