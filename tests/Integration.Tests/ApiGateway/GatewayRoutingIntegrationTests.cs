using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// ADR-0106 (#1001/#1002): the YARP API gateway fronts the nine context REST
/// APIs. Booting the real stack (AspireFixture), these tests assert that the
/// gateway forwards <c>/{context}/...</c> to the matching service — a 200 from
/// each service's unauthenticated <c>/health</c> proves YARP routing plus
/// Aspire service-discovery resolution (<c>http://{context}</c>) end-to-end —
/// and that an unrouted path is rejected with 404 (no accidental catch-all).
/// This is the automated form of the manual curl matrix used to verify the
/// scaffold and the full route table.
/// </summary>
[Collection(AspireCollection.Name)]
public class GatewayRoutingIntegrationTests(AspireFixture aspire)
{
    [Theory]
    [InlineData("camera-catalog")]
    [InlineData("stream-distribution")]
    [InlineData("layout-composition")]
    [InlineData("event-ingestion")]
    [InlineData("overlay-designer")]
    [InlineData("system-variables")]
    [InlineData("audit-observability")]
    [InlineData("automation")]
    [InlineData("identity")]
    public async Task Gateway_forwards_the_health_route_to_each_context_service(string context)
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        HttpResponseMessage response = await gateway.GetAsync($"/{context}/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gateway_returns_404_for_a_path_with_no_configured_route()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        HttpResponseMessage response = await gateway.GetAsync("/not-a-context/health");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// ADR-0106 (#1003): the gateway owns one CORS policy for the browser apps.
    /// A CORS preflight (OPTIONS + Origin + Access-Control-Request-Method) for an
    /// allowed origin is answered at the edge — the gateway echoes the origin in
    /// Access-Control-Allow-Origin rather than forwarding the probe to a service.
    /// </summary>
    [Fact]
    public async Task Gateway_answers_the_cors_preflight_for_an_allowed_origin()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();
        using HttpRequestMessage preflight = new(HttpMethod.Options, "/camera-catalog/health");
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        HttpResponseMessage response = await gateway.SendAsync(preflight);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeTrue();
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain("http://localhost:5173");
    }

    private async Task<HttpClient> CreateGatewayClientAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("api-gateway", KnownResourceStates.Running, cts.Token);
        return aspire.App.CreateHttpClient("api-gateway");
    }
}
