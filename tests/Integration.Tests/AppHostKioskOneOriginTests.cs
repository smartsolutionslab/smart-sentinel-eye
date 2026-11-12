using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// In the run-mode stack the kiosk and the wall display are paths on the gateway's origin (<c>/kiosk/</c>, <c>/wall/</c>) and reach
/// the layout hub directly on <c>layout-composition</c>, which admits exactly their origins. Builds the application model only and reads
/// environment entries raw, never resolved, like <see cref="AppHostOneOriginTests"/>, whose helpers are copied rather than shared.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostKioskOneOriginTests
{
    private const string AllowedOriginsPrefix = "LayoutHub__Cors__AllowedOrigins__";

    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
        "ScenarioSimulator=false",
    ];

    [Fact]
    public async Task The_gateway_is_told_where_the_kiosk_and_the_wall_dev_servers_are()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");

        var environment = await RawEnvironmentAsync(gateway);

        DestinationOf(environment, "Ui__Proxy__Kiosk").ShouldBe("kiosk-web");
        DestinationOf(environment, "Ui__Proxy__Wall").ShouldBe("kiosk-wall");
    }

    [Theory]
    [InlineData("kiosk-web")]
    [InlineData("kiosk-wall")]
    public async Task Each_kiosk_instance_reaches_the_hub_on_layout_composition_directly(string instance)
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var resource = builder.Resources.Single(candidate => candidate.Name == instance);

        var environment = await RawEnvironmentAsync(resource);

        environment.ShouldContainKey("VITE_LAYOUT_HUB_URL");
        var hubUrl = environment["VITE_LAYOUT_HUB_URL"].ShouldBeOfType<ReferenceExpression>();
        hubUrl.Format.ShouldBe("{0}/hubs/layouts");
        var endpoint = hubUrl.ValueProviders.Single().ShouldBeOfType<EndpointReference>();
        endpoint.Resource.Name.ShouldBe("layout-composition");
        environment.Keys.ShouldContain("VITE_LAYOUT_HUB_ORIGIN");
    }

    [Fact]
    public async Task Layout_composition_admits_the_kiosk_the_wall_and_the_gateway_origins_from_endpoint_references()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var layoutComposition = builder.Resources.Single(resource => resource.Name == "layout-composition");

        var environment = await RawEnvironmentAsync(layoutComposition);

        var origins = environment
            .Where(entry => entry.Key.StartsWith(AllowedOriginsPrefix, StringComparison.Ordinal))
            .Select(entry => entry.Value.ShouldBeOfType<EndpointReference>().Resource.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        origins.ShouldBe(["api-gateway", "kiosk-wall", "kiosk-web"]);
    }

    // The entry is an unresolved endpoint reference; its resource name is readable without allocating the endpoint.
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
