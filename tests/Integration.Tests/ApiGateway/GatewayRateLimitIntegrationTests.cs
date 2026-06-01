using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// ADR-0106 (#1004): the gateway rate-limits per fab at the edge. Booting the
/// real stack, this asserts that one fab exhausting its window is rejected with
/// 429, while a different fab — a separate partition — still gets through,
/// proving the limits are isolated per fab. Only the proxied routes carry the
/// "per-fab" policy, so the gateway's own health endpoints are never throttled.
/// </summary>
[Collection(AspireCollection.Name)]
public class GatewayRateLimitIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task Gateway_returns_429_for_a_fab_over_its_limit_and_isolates_other_fabs()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        HttpStatusCode busyFab = await SendUntilAsync(gateway, "rl-fab-a", HttpStatusCode.TooManyRequests);
        busyFab.ShouldBe(HttpStatusCode.TooManyRequests);

        // A different fab is a separate partition, so its window is untouched.
        HttpStatusCode freshFab = await SendOnceAsync(gateway, "rl-fab-b");
        freshFab.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpStatusCode> SendUntilAsync(HttpClient gateway, string fab, HttpStatusCode target)
    {
        HttpStatusCode status = HttpStatusCode.OK;
        for (int attempt = 0; attempt < 250 && status != target; attempt++)
        {
            status = await SendOnceAsync(gateway, fab);
        }

        return status;
    }

    private static async Task<HttpStatusCode> SendOnceAsync(HttpClient gateway, string fab)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/camera-catalog/health");
        request.Headers.Add("X-Fab", fab);
        using HttpResponseMessage response = await gateway.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<HttpClient> CreateGatewayClientAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("api-gateway", KnownResourceStates.Running, cts.Token);
        return aspire.App.CreateHttpClient("api-gateway");
    }
}
