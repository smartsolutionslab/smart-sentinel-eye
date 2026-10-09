using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Characterises the <c>management-cameras</c> Aspire resource plan.md §4.4/§4.8
/// and tasks.md T008 describe: the federated cameras remote (spec 316, US3),
/// composed the same way as the three existing JS apps in
/// <see cref="AppHostWebAppHostingTests"/> but parented to the gateway as a
/// fourth, independently-built front end rather than a fourth entry in that
/// file's own <c>WebApps</c> table (plan.md §1: it is <c>management-cameras</c>,
/// not a fourth shell, and it carries no <c>VITE_KEYCLOAK_URL</c>/
/// <c>VITE_API_GATEWAY_URL</c> pair of its own — its only job is to serve
/// <c>remoteEntry.js</c> to the shell).
///
/// <para>
/// <b>Red, not characterisation</b> (tasks.md T006: US3 is new behaviour).
/// Today's <c>AppHost.cs</c> composes no resource named <c>management-cameras</c>
/// at all, so every <c>[Fact]</c> below fails on <c>builder.Resources.Single(...)</c>
/// finding zero matches, or on <c>management-web</c>'s environment missing the
/// one new key — module/script/resource absent, exactly as tasks.md predicts.
/// T008 (infra-engineer) is the task that makes this green.
/// </para>
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no resources,
/// so this costs no containers and is safe beside a live <c>aspire run</c> —
/// the same footing <see cref="AppHostWebAppHostingTests"/>'s own doc comment
/// claims for itself, which this file mirrors directly (same
/// <c>SimulatorDisabledArguments</c> shape, copied rather than shared per
/// ADR-0109's disjoint-files rule — neither file is touched by the other's
/// task).
/// </para>
///
/// <para>
/// <b>Arguments and environment are read raw, never resolved</b>, for the same
/// reason <see cref="AppHostWebAppHostingTests"/> gives: the environment
/// carries an endpoint-valued entry (<c>VITE_CAMERAS_REMOTE_URL</c>), so only
/// key presence is asserted on <c>management-web</c>'s side (plan.md §6 test
/// 10's own "key presence only" rule).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostCamerasRemoteTests
{
    /// <summary>
    /// Run mode with no <c>E2ETests</c> — mirrors
    /// <see cref="AppHostWebAppHostingTests.RunModeArguments"/> exactly (copied,
    /// not shared; see class doc).
    /// </summary>
    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
    ];

    /// <summary>
    /// CI's end-to-end job's own shape — mirrors
    /// <see cref="AppHostWebAppHostingTests.SimulatorDisabledArguments"/>
    /// exactly (copied, not shared; see class doc).
    /// </summary>
    private static readonly string[] SimulatorDisabledArguments =
    [
        .. RunModeArguments,
        "ScenarioSimulator=false",
    ];

    [Fact]
    public async Task The_resource_exists()
    {
        IResource resource = await ResourceAsync();

        resource.Name.ShouldBe("management-cameras");
    }

    [Fact]
    public async Task The_resource_runs_npm_run_dev()
    {
        IResource resource = await ResourceAsync();
        ExecutableResource executable = (ExecutableResource)resource;

        executable.Command.ShouldBe("npm");

        IReadOnlyList<string> arguments = await ArgumentValuesAsync(executable);

        arguments.ShouldBe(["run", "dev"]);
    }

    [Fact]
    public async Task The_resource_s_working_directory_is_apps_management_cameras()
    {
        IResource resource = await ResourceAsync();
        ExecutableResource executable = (ExecutableResource)resource;

        NormalizeSeparators(executable.WorkingDirectory).ShouldEndWith(
            "apps/management-cameras",
            Case.Sensitive,
            "'management-cameras' is expected to run out of '.../apps/management-cameras', "
            + $"found working directory '{executable.WorkingDirectory}'.");
    }

    [Fact]
    public async Task The_resource_exposes_exactly_one_fixed_unproxied_http_endpoint_on_5176()
    {
        IResource resource = await ResourceAsync();

        EndpointAnnotation[] endpoints = [.. resource.Annotations.OfType<EndpointAnnotation>()];

        endpoints.ShouldHaveSingleItem(
            $"'management-cameras' is expected to carry exactly one endpoint annotation, found {endpoints.Length}.");

        endpoints[0].Name.ShouldBe("http");
        endpoints[0].Port.ShouldBe(5176);
        endpoints[0].IsProxied.ShouldBeFalse();
    }

    [Fact]
    public async Task The_resource_is_a_child_of_the_api_gateway()
    {
        IResource resource = await ResourceAsync();

        ResourceRelationshipAnnotation[] parents =
        [
            .. resource.Annotations
                .OfType<ResourceRelationshipAnnotation>()
                .Where(relationship => relationship.Type == "Parent"),
        ];

        parents.ShouldHaveSingleItem(
            $"'management-cameras' is expected to carry exactly one parent relationship, found {parents.Length}.");

        parents[0].Resource.Name.ShouldBe("api-gateway");
    }

    /// <summary>
    /// The shell side of the wiring (plan.md §4.4/§4.8): <c>management-web</c>
    /// must carry the remote's URL so <c>navigation/remotes.ts</c> (T009) has
    /// something to read. Key presence only — resolving the value needs
    /// endpoint allocation this model-only test never performs (class doc).
    /// </summary>
    [Fact]
    public async Task Management_web_s_environment_carries_the_cameras_remote_url_key()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(SimulatorDisabledArguments);

        IResource managementWeb = builder.Resources.Single(resource => resource.Name == "management-web");

        Dictionary<string, object> environment = await RawEnvironmentAsync(managementWeb);

        environment.Keys.ShouldContain("VITE_CAMERAS_REMOTE_URL");
    }

    private static async Task<IResource> ResourceAsync()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(SimulatorDisabledArguments);

        return builder.Resources.Single(resource => resource.Name == "management-cameras");
    }

    /// <summary>
    /// The gather step only — mirrors
    /// <see cref="AppHostWebAppHostingTests.ArgumentValuesAsync"/> exactly
    /// (copied, not shared; see class doc).
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
    /// The gather step only — mirrors
    /// <see cref="AppHostWebAppHostingTests.RawEnvironmentAsync"/> exactly
    /// (copied, not shared; see class doc).
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
    /// source-scanning tests need slash normalising) — mirrors
    /// <see cref="AppHostWebAppHostingTests.NormalizeSeparators"/> exactly.
    /// </summary>
    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');
}
