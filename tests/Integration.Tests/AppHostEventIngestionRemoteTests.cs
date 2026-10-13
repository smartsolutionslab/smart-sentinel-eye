using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Characterises the <c>management-event-ingestion</c> Aspire resource: the
/// second federated remote (issue #2780), composed identically to
/// <c>management-cameras</c> (see <see cref="AppHostCamerasRemoteTests"/>,
/// which this file mirrors exactly — copied, not shared, per ADR-0109's
/// disjoint-files rule).
///
/// <para>
/// <b>Red, not characterisation</b> (new behaviour). Today's <c>AppHost.cs</c>
/// composes no resource named <c>management-event-ingestion</c>, so every
/// <see cref="Fact"/> below fails on <c>builder.Resources.Single(...)</c>
/// finding zero matches, or on <c>management-web</c>'s environment missing
/// the new key.
/// </para>
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no
/// resources, so this costs no containers.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostEventIngestionRemoteTests
{
    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
    ];

    private static readonly string[] SimulatorDisabledArguments =
    [
        .. RunModeArguments,
        "ScenarioSimulator=false",
    ];

    [Fact]
    public async Task The_resource_exists()
    {
        var resource = await ResourceAsync();

        resource.Name.ShouldBe("management-event-ingestion");
    }

    [Fact]
    public async Task The_resource_runs_npm_run_dev()
    {
        var resource = await ResourceAsync();
        var executable = (ExecutableResource)resource;

        executable.Command.ShouldBe("npm");

        var arguments = await ArgumentValuesAsync(executable);

        arguments.ShouldBe(["run", "dev"]);
    }

    [Fact]
    public async Task The_resource_s_working_directory_is_apps_management_event_ingestion()
    {
        var resource = await ResourceAsync();
        var executable = (ExecutableResource)resource;

        NormalizeSeparators(executable.WorkingDirectory).ShouldEndWith(
            "apps/management-event-ingestion",
            Case.Sensitive,
            "'management-event-ingestion' is expected to run out of "
            + $"'.../apps/management-event-ingestion', found working directory '{executable.WorkingDirectory}'.");
    }

    [Fact]
    public async Task The_resource_exposes_exactly_one_fixed_unproxied_http_endpoint_on_5177()
    {
        var resource = await ResourceAsync();

        EndpointAnnotation[] endpoints = [.. resource.Annotations.OfType<EndpointAnnotation>()];

        endpoints.ShouldHaveSingleItem(
            "'management-event-ingestion' is expected to carry exactly one endpoint annotation, found "
            + $"{endpoints.Length}.");

        endpoints[0].Name.ShouldBe("http");
        endpoints[0].Port.ShouldBe(5177);
        endpoints[0].IsProxied.ShouldBeFalse();
    }

    [Fact]
    public async Task The_resource_is_a_child_of_the_api_gateway()
    {
        var resource = await ResourceAsync();

        ResourceRelationshipAnnotation[] parents =
        [
            .. resource.Annotations
                .OfType<ResourceRelationshipAnnotation>()
                .Where(relationship => relationship.Type == "Parent"),
        ];

        parents.ShouldHaveSingleItem(
            "'management-event-ingestion' is expected to carry exactly one parent relationship, found "
            + $"{parents.Length}.");

        parents[0].Resource.Name.ShouldBe("api-gateway");
    }

    /// <summary>
    /// The shell side of the wiring: <c>management-web</c> must carry the
    /// remote's URL, and that URL must actually point at
    /// <c>management-event-ingestion</c> — not a copy-paste of the
    /// <c>management-cameras</c> wiring above it — so
    /// <c>navigation/remotes.ts</c> loads the right <c>remoteEntry.js</c>
    /// under the <c>eventIngestion</c> name. The raw environment value is an
    /// <see cref="EndpointReference"/>; its <c>Resource.Name</c> is readable
    /// without allocating the endpoint, so this model-only test can assert on
    /// it directly.
    /// </summary>
    [Fact]
    public async Task Management_web_s_environment_carries_the_event_ingestion_remote_url_key()
    {
        using var builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(SimulatorDisabledArguments);

        var managementWeb = builder.Resources.Single(resource => resource.Name == "management-web");

        var environment = await RawEnvironmentAsync(managementWeb);

        environment.Keys.ShouldContain("VITE_EVENT_INGESTION_REMOTE_URL");

        var endpoint = (EndpointReference)environment["VITE_EVENT_INGESTION_REMOTE_URL"];

        endpoint.Resource.Name.ShouldBe("management-event-ingestion");
        endpoint.EndpointName.ShouldBe("http");
    }

    private static async Task<IResource> ResourceAsync()
    {
        using var builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(SimulatorDisabledArguments);

        return builder.Resources.Single(resource => resource.Name == "management-event-ingestion");
    }

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

        foreach (var annotation in annotations)
        {
            await annotation.Callback(context);
        }

        return [.. gathered.Select(value => value?.ToString() ?? string.Empty)];
    }

    private static async Task<Dictionary<string, object>> RawEnvironmentAsync(IResource resource)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));

        if (!resource.TryGetEnvironmentVariables(out var annotations))
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

        foreach (var annotation in annotations)
        {
            await annotation.Callback(context);
        }

        return environmentVariables;
    }

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');
}
