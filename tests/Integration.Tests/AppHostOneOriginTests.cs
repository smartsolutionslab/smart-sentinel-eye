using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Spec 491 (#3245), ADR-0192: in the run-mode stack the console is reached at one origin, the gateway's. The AppHost gives the
/// gateway a fixed dev address, turns UI hosting on and points it at the three Vite dev servers; the shell is handed relative
/// `/api` and `/remotes/*` values. In the integration fixture (no web apps) none of that exists, so the gateway keeps its
/// dynamic port and its "no accidental catch-all" 404.
///
/// <para>
/// Builds the application model only — <c>CreateAsync</c> starts nothing — and reads environment entries raw, never resolved
/// (an endpoint-valued entry needs allocation this model-only test never performs), like <see cref="AppHostCamerasRemoteTests"/>,
/// whose helpers are copied rather than shared (ADR-0109, disjoint files).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostOneOriginTests
{
    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
        "ScenarioSimulator=false",
    ];

    private static readonly string[] IntegrationFixtureArguments = [.. RunModeArguments, "E2ETests=true"];

    [Fact]
    public async Task The_gateway_listens_on_the_fixed_one_origin_dev_port_in_run_mode()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");

        var endpoint = gateway.Annotations.OfType<EndpointAnnotation>().Single(annotation => annotation.Name == "http");

        endpoint.Port.ShouldBe(5180);
    }

    [Fact]
    public async Task The_gateway_hosts_the_ui_in_proxy_mode_and_is_told_where_the_three_dev_servers_are()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");

        var environment = await RawEnvironmentAsync(gateway);

        environment.ShouldContainKeyAndValue("Ui__Mode", "Proxy");
        DestinationOf(environment, "Ui__Proxy__Shell").ShouldBe("management-web");
        DestinationOf(environment, "Ui__Proxy__Cameras").ShouldBe("management-cameras");
        DestinationOf(environment, "Ui__Proxy__EventIngestion").ShouldBe("management-event-ingestion");
    }

    [Fact]
    public async Task The_shell_is_handed_relative_one_origin_values_and_the_gateway_origin_for_its_proxy()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var shell = builder.Resources.Single(resource => resource.Name == "management-web");

        var environment = await RawEnvironmentAsync(shell);

        environment.ShouldContainKeyAndValue("VITE_API_GATEWAY_URL", "/api");
        environment.ShouldContainKeyAndValue("VITE_CAMERAS_REMOTE_URL", "/remotes/cameras");
        environment.ShouldContainKeyAndValue("VITE_EVENT_INGESTION_REMOTE_URL", "/remotes/event-ingestion");
        environment.Keys.ShouldContain("VITE_GATEWAY_PROXY_TARGET");
    }

    [Fact]
    public async Task The_console_reaches_the_hub_on_layout_composition_directly()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var shell = builder.Resources.Single(resource => resource.Name == "management-web");

        var environment = await RawEnvironmentAsync(shell);

        environment.ShouldContainKey("VITE_LAYOUT_HUB_URL");
        var hubUrl = environment["VITE_LAYOUT_HUB_URL"].ShouldBeOfType<ReferenceExpression>();
        hubUrl.Format.ShouldBe("{0}" + LayoutLifecycleHub.Path);
        var endpoint = hubUrl.ValueProviders.Single().ShouldBeOfType<EndpointReference>();
        endpoint.Resource.Name.ShouldBe("layout-composition");
        environment.Keys.ShouldNotContain("VITE_LAYOUT_HUB_ORIGIN");
    }

    [Fact]
    public async Task The_integration_fixture_s_gateway_keeps_a_dynamic_port_and_hosts_no_ui()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(IntegrationFixtureArguments);
        var gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");

        var endpoint = gateway.Annotations.OfType<EndpointAnnotation>().Single(annotation => annotation.Name == "http");
        var environment = await RawEnvironmentAsync(gateway);

        endpoint.Port.ShouldBeNull();
        environment.Keys.ShouldNotContain("Ui__Mode");
    }

    // The entry is an unresolved endpoint reference; its resource name is readable without allocating the endpoint, so a
    // destination wired to the wrong dev server (a copy-paste between the three) is caught here.
    private static string DestinationOf(Dictionary<string, object> environment, string key)
    {
        environment.ShouldContainKey(key);
        return ((EndpointReference)environment[key]).Resource.Name;
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
}
