using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// ADR-0106 (#1004), as corrected by spec 311 (#2283): the gateway rate-limits
/// each request <em>source</em> at the edge. <c>X-Fab</c> is a header the caller
/// chose — the gateway verifies no token — so it must not select the partition:
/// keyed on it, a caller rotating the value got a fresh 100-request window per
/// value and was never refused at all. Booting the real stack, these assert that
/// one source over its limit is refused with 429 whatever <c>X-Fab</c> it sends,
/// while the gateway's own health endpoint — which carries no rate-limit policy —
/// keeps answering.
///
/// <para>
/// Every caller in the fixture collapses to one source address, so the window
/// these tests exhaust is the one <see cref="GatewayRoutingIntegrationTests"/>
/// also spends. Each test therefore ends by waiting for the window to recover,
/// so a later gateway test in the same process does not start throttled.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class GatewayRateLimitIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// The production default in <c>src/ApiGateway/appsettings.json</c>, which
    /// the fixture keeps (<see cref="AppHostGatewayRateBudgetTests"/>).
    /// </summary>
    private const int PermitLimit = 100;

    private static readonly TimeSpan RecoveryBound = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(15);

    [Fact]
    public async Task A_caller_rotating_X_Fab_is_still_refused_once_over_its_limit()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        try
        {
            int admitted = 0;
            for (int attempt = 0; attempt <= PermitLimit; attempt++)
            {
                HttpStatusCode status = await SendOnceAsync(gateway, $"rot-{attempt}");
                if (status != HttpStatusCode.TooManyRequests)
                {
                    admitted++;
                }
            }

            admitted.ShouldBeLessThanOrEqualTo(
                PermitLimit,
                $"{admitted} of {PermitLimit + 1} requests from one source, each with a different "
                + $"X-Fab value, were admitted. The limit is {PermitLimit} per window per source; a "
                + "caller-chosen header must not buy a fresh window.");
        }
        finally
        {
            await WaitForWindowToRecoverAsync(gateway);
        }
    }

    [Fact]
    public async Task Gateway_refuses_a_source_over_its_limit_whatever_fab_header_it_sends()
    {
        using HttpClient gateway = await CreateGatewayClientAsync();

        try
        {
            HttpStatusCode busySource = await SendUntilAsync(gateway, "rl-fab-a", HttpStatusCode.TooManyRequests);
            busySource.ShouldBe(HttpStatusCode.TooManyRequests);

            HttpStatusCode otherFabHeader = await SendOnceAsync(gateway, "rl-fab-b");
            otherFabHeader.ShouldBe(
                HttpStatusCode.TooManyRequests,
                "the same source, over its limit, sent a different X-Fab value and was admitted — "
                + "the header selected a fresh partition.");

            using HttpResponseMessage health = await gateway.GetAsync("/health");
            health.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "the gateway's own /health carries no rate-limit policy and must answer even while "
                + "the source is throttled on every proxied route.");
        }
        finally
        {
            await WaitForWindowToRecoverAsync(gateway);
        }
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

    private static async Task<HttpStatusCode> SendOnceAsync(HttpClient gateway, string? fab)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/camera-catalog/health");
        if (fab is not null)
        {
            request.Headers.Add("X-Fab", fab);
        }

        using HttpResponseMessage response = await gateway.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// Polls without an <c>X-Fab</c> header — the shape every other gateway test
    /// sends — until a proxied request is admitted. Runs in <c>finally</c>, so on
    /// reaching the bound it returns rather than throws: an exception here would
    /// replace the assertion failure the test exists to report.
    /// </summary>
    private static async Task WaitForWindowToRecoverAsync(HttpClient gateway)
    {
        DateTime deadline = DateTime.UtcNow + RecoveryBound;
        while (DateTime.UtcNow < deadline)
        {
            if (await SendOnceAsync(gateway, fab: null) != HttpStatusCode.TooManyRequests)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// A plain client, not the fixture's factory one: every factory client
    /// shares one standard resilience pipeline, which retries a 429 on a
    /// <c>GET</c> — so a refusal is reported late, or as a later success — and
    /// whose circuit breaker counts a 429 against every other test on that
    /// pipeline. Same reasoning as the fixture's
    /// <c>StreamDistributionThrottleProbe</c>.
    /// </summary>
    private async Task<HttpClient> CreateGatewayClientAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("api-gateway", KnownResourceStates.Running, cts.Token);
        return new HttpClient { BaseAddress = aspire.App.GetEndpoint("api-gateway", "http") };
    }
}
