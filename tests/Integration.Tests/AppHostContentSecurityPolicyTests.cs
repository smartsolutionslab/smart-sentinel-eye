using Aspire.Hosting;
using Aspire.Hosting.JavaScript;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// The gateway's Content-Security-Policy names every origin a browser app reaches directly. Builds the application model only and reads
/// environment entries raw, never resolved, like <see cref="AppHostKioskOneOriginTests"/>, whose helpers are copied rather than shared.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostContentSecurityPolicyTests
{
    private const string PolicyPrefix = "Ui__ContentSecurityPolicy__";

    private static readonly string[] RunModeArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
        "ScenarioSimulator=false",
    ];

    [Fact]
    public async Task Every_origin_a_browser_app_reaches_directly_is_wired_into_the_gateways_policy()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);

        var reachedByBrowsers = await BrowserReachedEndpointsAsync(builder);
        var wired = await WiredEndpointsAsync(builder);

        var missing = reachedByBrowsers.Except(wired).Order(StringComparer.Ordinal).ToList();
        missing.ShouldBeEmpty($"the gateway's {PolicyPrefix}* entries lack: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task The_three_policy_slots_are_wired_to_keycloak_the_layout_hub_and_the_media_server()
    {
        using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(RunModeArguments);
        var gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");

        var environment = await RawEnvironmentAsync(gateway);

        EndpointOf(environment, PolicyPrefix + "IdentityProvider").ShouldBe("keycloak/http");
        EndpointOf(environment, PolicyPrefix + "LayoutHub").ShouldBe("layout-composition/http");
        EndpointOf(environment, PolicyPrefix + "Media").ShouldBe("mediamtx/whep");
    }

    private static string EndpointOf(Dictionary<string, object> environment, string key)
    {
        environment.ShouldContainKey(key);
        var endpoint = environment[key].ShouldBeOfType<EndpointReference>();
        return $"{endpoint.Resource.Name}/{endpoint.EndpointName}";
    }

    private static async Task<HashSet<string>> BrowserReachedEndpointsAsync(IDistributedApplicationTestingBuilder builder)
    {
        HashSet<string> reached = new(StringComparer.Ordinal);

        foreach (var app in builder.Resources.OfType<JavaScriptAppResource>())
        {
            var environment = await RawEnvironmentAsync(app);
            foreach (var entry in environment.Where(entry => entry.Key.StartsWith("VITE_", StringComparison.Ordinal)))
            {
                foreach (var endpoint in EndpointsIn(entry.Value).Where(endpoint => endpoint.Resource.Name != "api-gateway"))
                {
                    reached.Add($"{endpoint.Resource.Name}/{endpoint.EndpointName}");
                }
            }
        }

        // The one origin a browser learns from an API body rather than from its own configuration.
        var streamDistribution = builder.Resources.Single(resource => resource.Name == "stream-distribution");
        var streamEnvironment = await RawEnvironmentAsync(streamDistribution);
        streamEnvironment.ShouldContainKey("services__mediamtx__whep__0");
        foreach (var endpoint in EndpointsIn(streamEnvironment["services__mediamtx__whep__0"]))
        {
            reached.Add($"{endpoint.Resource.Name}/{endpoint.EndpointName}");
        }

        return reached;
    }

    private static async Task<HashSet<string>> WiredEndpointsAsync(IDistributedApplicationTestingBuilder builder)
    {
        var gateway = builder.Resources.Single(resource => resource.Name == "api-gateway");
        var environment = await RawEnvironmentAsync(gateway);

        return environment
            .Where(entry => entry.Key.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            .SelectMany(entry => EndpointsIn(entry.Value))
            .Select(endpoint => $"{endpoint.Resource.Name}/{endpoint.EndpointName}")
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<EndpointReference> EndpointsIn(object value) => value switch
    {
        EndpointReference endpoint => [endpoint],
        ReferenceExpression expression => expression.ValueProviders.OfType<EndpointReference>(),
        _ => [],
    };

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
