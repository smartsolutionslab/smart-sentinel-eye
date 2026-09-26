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
    /// Spec 208 (#2284) review, BLOCKER 1: <c>/streams/authorize</c> is
    /// MediaMTX's own anonymous, credential-free external-auth hook. MediaMTX
    /// calls it directly at the service by service DNS (mediamtx.yml), never
    /// through this externally-reachable gateway, and no browser caller does
    /// either. Left proxied, an anonymous off-box caller reaching it this way
    /// would collapse into the same partition MediaMTX's own legitimate calls
    /// share (spec 208 spec.md Assumptions item 5) and could exhaust the
    /// whep-authorize ceiling, denying MediaMTX too — the "stream-distribution"
    /// catch-all must carve this one path out while still forwarding the rest.
    /// </summary>
    [Fact]
    public async Task Gateway_does_not_forward_the_anonymous_whep_authorize_hook()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        HttpResponseMessage response = await gateway.PostAsJsonAsync(
            "/stream-distribution/streams/authorize",
            new { token = (string?)null, path = "cam-anything", action = "read" });

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

    /// <summary>
    /// Spec 258 T002 (plan.md §4.5): a dedicated <c>/walls/{**catch-all}</c>
    /// route to layout-composition, alongside the existing
    /// <c>/layout-composition/{**catch-all}</c> context route (walls are
    /// addressed directly, not under the context prefix).
    ///
    /// <para>
    /// Asserted as "not the gateway's own unrouted-404", not as a specific
    /// success code: an unauthenticated request that reaches the real
    /// layout-composition service's <c>/walls/{id}</c> endpoint should hit
    /// auth middleware and answer 401, whereas today — with no route
    /// configured at all — YARP itself answers 404 before the request ever
    /// leaves the gateway. That is the distinction this test exists to make:
    /// "gateway routed it and the service rejected it" vs. "gateway had no
    /// route for it". Today's 404 is the latter, so this is RED until the
    /// route exists.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Gateway_forwards_a_walls_path_to_layout_composition()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        HttpResponseMessage response = await gateway.GetAsync($"/walls/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> CreateGatewayClientAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("api-gateway", KnownResourceStates.Running, cts.Token);
        return aspire.CreateServiceClient("api-gateway");
    }
}
