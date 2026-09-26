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
    /// Spec 258 T002 (plan.md §4.5, corrected premise): the plan's "add a
    /// gateway route <c>/walls/{**catch-all}</c> mirroring the <c>/layouts</c>
    /// route" describes a route that has never existed — every gateway route
    /// is context-prefixed (see the <c>ReverseProxy:Routes</c> section of
    /// <c>appsettings.json</c>), and the frontend already calls layouts as
    /// <c>layout-composition/layouts</c> (<c>apps/shared/src/api/layouts.api.ts</c>).
    /// Wall lives in the same LayoutComposition context as Layout, so once
    /// its endpoints exist they are reachable at
    /// <c>/layout-composition/walls/...</c> through the <b>existing</b>
    /// <c>/layout-composition/{**catch-all}</c> route — no new route entry is
    /// needed, and adding a bare <c>/walls/{**catch-all}</c> one would
    /// duplicate CORS/rate-limiter config and break the one-route-per-context
    /// convention (ADR-0109) for no reason.
    ///
    /// <para>
    /// This test pins that claim against a sub-path that already exists
    /// today, because <c>WallEndpoints</c> (T033) is not built yet on this
    /// branch — a request to a not-yet-implemented <c>/walls/{id}</c> would
    /// 404 whether or not the gateway had a route for it, which proves
    /// nothing. <c>GET /layouts/{layoutIdentifier}</c> is the closest existing
    /// stand-in under the same <c>layout-composition</c> cluster: it
    /// requires <c>sse.layouts.read</c>
    /// (<c>src/LayoutComposition/Api/LayoutEndpoints.cs</c>), so an
    /// unauthenticated caller reaching it answers 401 — "gateway routed it
    /// and the service rejected it" — whereas an unrouted path answers 404
    /// from YARP itself, before the request ever leaves the gateway (see
    /// <see cref="Gateway_returns_404_for_a_path_with_no_configured_route"/>).
    /// That is the same forwarding-plus-prefix-stripping mechanism
    /// <c>/layout-composition/walls/...</c> will use once <c>WallEndpoints</c>
    /// exists, so this is a corrected-premise test observed green today, not
    /// new behaviour.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Gateway_forwards_a_layout_composition_subpath_with_the_prefix_stripped()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        HttpResponseMessage response = await gateway.GetAsync($"/layout-composition/layouts/{Guid.NewGuid()}");

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
