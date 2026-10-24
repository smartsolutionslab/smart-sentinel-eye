using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using SmartSentinelEye.ServiceDefaults.Authentication;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.ServiceDefaults.Tests.Fakes;

namespace SmartSentinelEye.ServiceDefaults.Tests.Resilience;

/// <summary>
/// The adversarial pass on selecting the defaults pipeline by request authority (#2959), composed
/// through the real <c>AddServiceDefaults()</c> as <see cref="PerAuthorityCircuitBreakerTests"/> is.
///
/// <para>
/// Three kinds of fact live here. <b>Guards</b> are green before the selection and must stay green
/// after it: they catch a selector that splits one upstream into several breakers (by path, by the
/// spelling of the authority, by a race on a cold key), or that loses the concurrency limit or the
/// timeouts. <b>Coupling</b> facts are red until the selection lands: the shared pipeline couples
/// authorities through more than the breaker's open state — the concurrency limiter, the half-open
/// probe, a mint nested inside another authority's call — and each of these must come apart with it.
/// <b>Hazard</b> facts record behaviour that exists today and that the selection does not change; if
/// one goes red the hazard moved, which is a finding, not a regression to patch.
/// </para>
/// </summary>
public class PerAuthorityPipelineRiskTests
{
    // ---- Guards: green before and after ----

    /// <summary>
    /// The key must be the authority, not its spelling. A selector keyed on <c>OriginalString</c>
    /// or <c>Uri.Authority</c> would split these, and one upstream would get two breakers that
    /// each see half its failures.
    /// </summary>
    [Theory]
    [InlineData("https://same.test/thing", "https://same.test:443/thing")]
    [InlineData("http://same.test/thing", "http://same.test:80/thing")]
    [InlineData("https://SAME.test/thing", "https://same.test/thing")]
    [InlineData("http://[::1]:8080/thing", "http://[0:0:0:0:0:0:0:1]:8080/thing")]
    [InlineData("https://same.test/thing", "https://user:password@same.test/thing")]
    public async Task Equivalent_spellings_of_one_authority_share_one_breaker(string failingUrl, string siblingUrl)
    {
        CountingServer failingServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer siblingServer = new(HttpStatusCode.OK);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => failingServer);
            services.AddHttpClient("sibling").ConfigurePrimaryHttpMessageHandler(() => siblingServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("failing").GetAsync(failingUrl, CancellationToken.None));

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("sibling").GetAsync(siblingUrl, CancellationToken.None));
        siblingServer.Attempts.ShouldBe(0);
    }

    /// <summary>
    /// MediaMTX is ~250 paths on one authority. Keyed per path, an outage would need 250 breakers
    /// to open before provisioning stopped hammering it.
    /// </summary>
    [Fact]
    public async Task Path_and_query_do_not_split_an_authority()
    {
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services =>
            services.AddHttpClient("gateway").ConfigurePrimaryHttpMessageHandler(() => server));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("gateway");

        await Should.ThrowAsync<BrokenCircuitException>(
            () => client.GetAsync("https://mediamtx.test:9997/v3/config/paths/get/camera-1?x=1", CancellationToken.None));
        var attemptsWhenOpened = server.Attempts;

        await Should.ThrowAsync<BrokenCircuitException>(
            () => client.GetAsync("https://mediamtx.test:9997/v3/config/paths/get/camera-2?y=2", CancellationToken.None));
        server.Attempts.ShouldBe(attemptsWhenOpened);
    }

    /// <summary>
    /// Concurrent first requests to a cold authority race the pipeline's creation. If two
    /// instances were handed out, each would see part of the failures and neither would reach
    /// the throughput that opens it.
    /// </summary>
    [Fact]
    public async Task Concurrent_first_requests_to_a_cold_authority_feed_one_breaker()
    {
        const int Burst = 16;
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(
            services => services.AddHttpClient("cold").ConfigurePrimaryHttpMessageHandler(() => server),
            tune: options =>
            {
                options.CircuitBreaker.MinimumThroughput = Burst;
                options.CircuitBreaker.FailureRatio = 1.0;
            });
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using ManualResetEventSlim start = new(initialState: false);

        // A POST with no opt-in: one attempt each, so the breaker sees exactly Burst outcomes.
        var burst = Enumerable.Range(0, Burst)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                using var response = await factory.CreateClient("cold")
                    .PostAsync("https://cold.test/thing", new StringContent("{}"), CancellationToken.None);
            }))
            .ToArray();
        start.Set();
        await Task.WhenAll(burst);

        server.Attempts.ShouldBe(Burst);
        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("cold").PostAsync("https://cold.test/thing", new StringContent("{}"), CancellationToken.None));
        server.Attempts.ShouldBe(Burst, "one breaker saw every failure of the burst and opened.");
    }

    /// <summary>
    /// The limiter splits with the breaker; it must not disappear. One authority still has a cap.
    /// </summary>
    [Fact]
    public async Task One_authority_is_still_capped_by_the_concurrency_limiter()
    {
        BlockingServer server = new();
        using var provider = Compose(
            services => services.AddHttpClient("busy").ConfigurePrimaryHttpMessageHandler(() => server),
            tune: LimitConcurrencyToOne);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var holding = factory.CreateClient("busy").GetAsync("https://busy.test/first", CancellationToken.None);
        await server.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Should.ThrowAsync<RateLimiterRejectedException>(
            () => factory.CreateClient("busy").GetAsync("https://busy.test/second", CancellationToken.None));

        server.Release();
        using var released = await holding;
        released.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Per attempt, not per authority: a hanging upstream still gets four bounded attempts. The clock is
    /// virtual and advances only once the server has seen the attempt, so a starved runner cannot reorder
    /// the attempt timer and the request.
    /// </summary>
    [Fact]
    public async Task The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority()
    {
        var attemptTimeout = TimeSpan.FromMilliseconds(50);
        SchedulingTimeProvider clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        HangingServer server = new();
        using var provider = Compose(
            services => services.AddHttpClient("slow").ConfigurePrimaryHttpMessageHandler(() => server),
            tune: options =>
            {
                options.CircuitBreaker.MinimumThroughput = 100;
                options.AttemptTimeout.Timeout = attemptTimeout;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            },
            clock: clock);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("slow");

        using CancellationTokenSource abandon = new();
        var call = client.GetAsync("https://slow.test/thing", abandon.Token);

        try
        {
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                (await server.AttemptArrivedAsync(HangGuard)).ShouldBeTrue($"attempt {attempt} never reached the server");

                clock.Advance(attemptTimeout - TimeSpan.FromTicks(1));
                call.IsCompleted.ShouldBeFalse($"attempt {attempt} was cut short one tick before its timeout");
                server.Attempts.ShouldBe(attempt);

                clock.Advance(TimeSpan.FromTicks(1));
            }

            await Should.ThrowAsync<TimeoutRejectedException>(() => call.WaitAsync(HangGuard));

            server.Attempts.ShouldBe(4);
        }
        finally
        {
            // A failed assertion leaves the call pending; disposing the provider before it settles never returns.
            await abandon.CancelAsync();
            await Task.WhenAny(call, Task.Delay(HangGuard));
        }
    }

    [Fact]
    public async Task The_total_timeout_still_bounds_the_whole_request_to_a_new_authority()
    {
        HangingServer server = new();
        using var provider = Compose(
            services => services.AddHttpClient("slow").ConfigurePrimaryHttpMessageHandler(() => server),
            tune: options =>
            {
                options.CircuitBreaker.MinimumThroughput = 100;
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(150);
            });
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("slow");

        await Should.ThrowAsync<TimeoutRejectedException>(
            () => client.GetAsync("https://slow.test/thing", CancellationToken.None));

        server.Attempts.ShouldBeLessThan(4, "the total budget expired before the retries were spent.");
    }

    // ---- Coupling: red until the pipeline is selected by authority ----

    /// <summary>
    /// The first strategy of the standard pipeline is a concurrency limiter with no queue. Shared,
    /// a hung upstream holding its permits refuses calls to every other upstream — the same blast
    /// radius as the shared breaker, reached by a hang instead of by errors.
    /// </summary>
    [Fact]
    public async Task A_saturated_authority_does_not_use_up_the_concurrency_limit_of_another()
    {
        BlockingServer hungServer = new();
        CountingServer healthyServer = new(HttpStatusCode.OK);
        using var provider = Compose(
            services =>
            {
                services.AddHttpClient("hung").ConfigurePrimaryHttpMessageHandler(() => hungServer);
                services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(() => healthyServer);
            },
            tune: LimitConcurrencyToOne);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var holding = factory.CreateClient("hung").GetAsync("https://hung.test/thing", CancellationToken.None);
        await hungServer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            using var response = await factory.CreateClient("healthy")
                .GetAsync("https://healthy.test/thing", CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            healthyServer.Attempts.ShouldBe(1);
        }
        finally
        {
            hungServer.Release();
            using var released = await holding;
        }
    }

    /// <summary>
    /// Shared, the breaker's single half-open probe goes to whichever call arrives first. A
    /// healthy upstream's success then closes the breaker for the failing one, which is let back
    /// in with a full sampling window to fail through instead of one probe.
    /// </summary>
    [Fact]
    public async Task A_healthy_authority_does_not_take_the_half_open_probe_of_a_failing_one()
    {
        ManualClock clock = new();
        CountingServer failingServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer healthyServer = new(HttpStatusCode.OK);
        using var provider = Compose(
            services =>
            {
                services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => failingServer);
                services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(() => healthyServer);
            },
            clock: clock);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("failing").GetAsync("https://failing.test/thing", CancellationToken.None));
        var attemptsWhenOpened = failingServer.Attempts;
        clock.Advance(BreakDuration + TimeSpan.FromSeconds(1));

        using var healthy = await factory.CreateClient("healthy")
            .GetAsync("https://healthy.test/thing", CancellationToken.None);
        healthy.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Its own probe: one attempt, which fails and re-opens at once.
        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("failing").GetAsync("https://failing.test/thing", CancellationToken.None));
        (failingServer.Attempts - attemptsWhenOpened).ShouldBe(
            1,
            "the failing authority's breaker was still open, so its next call was a single half-open probe.");
    }

    /// <summary>
    /// A token mint nested in another authority's call (the revocation fetch, camera-catalog,
    /// overlay-designer). Shared, the outer call becomes the half-open probe and the mint behind
    /// it is refused by the very breaker it was meant to test: both upstreams healthy, the call
    /// fails, and the breaker closes on a probe that never reached anything.
    /// </summary>
    [Fact]
    public async Task A_mint_nested_in_another_authoritys_call_is_not_refused_by_that_calls_half_open_probe()
    {
        ManualClock clock = new();
        SwitchableServer tokenServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer fetchServer = new(HttpStatusCode.OK);
        using var provider = ComposeNested(tokenServer, fetchServer, clock);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient(TokenClient)
                .PostAsync(TokenAuthority + "/realms/r/protocol/openid-connect/token", new StringContent("{}"), CancellationToken.None));
        tokenServer.Status = HttpStatusCode.OK;
        clock.Advance(BreakDuration + TimeSpan.FromSeconds(1));

        using var response = await factory.CreateClient(FetchClient)
            .GetAsync("http://identity.test/registered-clients/revoked", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fetchServer.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// <c>HttpClient.Send</c> enters <c>ResilienceHandler.Send</c>, a separate override from
    /// <c>SendAsync</c>; it must select by authority too.
    /// </summary>
    [Fact]
    public void A_synchronous_send_to_a_healthy_authority_is_not_refused_by_another_authoritys_breaker()
    {
        CountingServer failingServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer healthyServer = new(HttpStatusCode.OK);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => failingServer);
            services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(() => healthyServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpRequestMessage failing = new(HttpMethod.Get, "https://failing.test/thing");
        Should.Throw<BrokenCircuitException>(() => factory.CreateClient("failing").Send(failing, CancellationToken.None));

        using HttpRequestMessage healthy = new(HttpMethod.Get, "https://healthy.test/thing");
        using var response = factory.CreateClient("healthy").Send(healthy, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        healthyServer.Attempts.ShouldBe(1);
    }

    // ---- Hazards: today's behaviour, unchanged by the selection ----

    /// <summary>
    /// "400/401 are not retried" holds for the mint's own pipeline only. The mint turns the 401 into
    /// an <see cref="HttpRequestException"/> (<c>EnsureSuccessStatusCode</c>), which the outer
    /// call's predicate treats as transient — so the outer GET retries, and each outer attempt
    /// mints again. A rejected credential costs four token POSTs per outer call, not one.
    /// </summary>
    [Fact]
    public async Task Hazard_a_credential_the_token_authority_rejects_is_retried_by_the_outer_call()
    {
        SwitchableServer tokenServer = new(HttpStatusCode.Unauthorized);
        CountingServer fetchServer = new(HttpStatusCode.OK);
        using var provider = ComposeNested(tokenServer, fetchServer, new ManualClock(), minimumThroughput: 100);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<HttpRequestException>(
            () => factory.CreateClient(FetchClient)
                .GetAsync("http://identity.test/registered-clients/revoked", CancellationToken.None));

        tokenServer.Attempts.ShouldBe(4, "the outer call's retry, not the mint's, re-sends the rejected credential.");
        fetchServer.Attempts.ShouldBe(0);
    }

    /// <summary>
    /// Identity's admin client calls Keycloak and mints from the same Keycloak, so both share one
    /// breaker by design (FR-003). In half-open the outer call takes the only probe, the nested
    /// mint is refused, and the refusal is not an outcome the breaker counts — so the breaker
    /// closes without Keycloak having been asked anything. Shared by every nested pair today;
    /// after the selection, only by this one.
    /// </summary>
    [Fact]
    public async Task Hazard_a_same_authority_nested_mint_lets_the_breaker_close_without_a_probe()
    {
        ManualClock clock = new();
        SwitchableServer tokenServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer adminServer = new(HttpStatusCode.OK);
        using var provider = ComposeNested(tokenServer, adminServer, clock);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient(TokenClient)
                .PostAsync(TokenAuthority + "/realms/r/protocol/openid-connect/token", new StringContent("{}"), CancellationToken.None));
        var tokenAttemptsWhenOpened = tokenServer.Attempts;
        clock.Advance(BreakDuration + TimeSpan.FromSeconds(1));

        // Keycloak is still down. The outer call to the same authority takes the probe.
        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient(FetchClient).GetAsync(TokenAuthority + "/admin/realms/r/clients", CancellationToken.None));
        tokenServer.Attempts.ShouldBe(tokenAttemptsWhenOpened, "the nested mint was refused by the half-open breaker.");
        adminServer.Attempts.ShouldBe(0);

        // ...and the breaker is closed: the next mint reaches the still-failing Keycloak.
        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient(TokenClient)
                .PostAsync(TokenAuthority + "/realms/r/protocol/openid-connect/token", new StringContent("{}"), CancellationToken.None));
        tokenServer.Attempts.ShouldBeGreaterThan(
            tokenAttemptsWhenOpened,
            "the breaker closed on a probe that never reached Keycloak.");
    }

    // ---- Harness ----

    private const string TokenClient = "token";
    private const string FetchClient = "fetch";
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(60);
    private const string TokenAuthority = "https://keycloak.test:8443";
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(5);

    private static void LimitConcurrencyToOne(HttpStandardResilienceOptions options)
    {
        options.RateLimiter.DefaultRateLimiterOptions.PermitLimit = 1;
        options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 0;
    }

    private static ServiceProvider Compose(
        Action<IServiceCollection> registerClients,
        Action<HttpStandardResilienceOptions>? tune = null,
        TimeProvider? clock = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.AddServiceDefaults();
        registerClients(builder.Services);

        if (clock is not null)
        {
            // Polly takes the container's TimeProvider for the breaker's break duration.
            builder.Services.AddSingleton<TimeProvider>(clock);
        }

        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
            options.CircuitBreaker.MinimumThroughput = 2;
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.BreakDuration = BreakDuration;
            tune?.Invoke(options);
        });

        return builder.Services.BuildServiceProvider();
    }

    /// <summary>
    /// The production shape of a nested mint: an opted-in token client (as every
    /// <c>client_credentials</c> mint is) and an outer client whose <see cref="AuthorizingHandler"/>
    /// mints through it per attempt. A zero <c>expires_in</c> makes every outer attempt mint.
    /// </summary>
    private static ServiceProvider ComposeNested(
        SwitchableServer tokenServer,
        CountingServer outerServer,
        ManualClock clock,
        int minimumThroughput = 2) =>
        Compose(
            services =>
            {
                services.AddHttpClient(TokenClient).ConfigurePrimaryHttpMessageHandler(() => tokenServer).RetryEveryMethod();
                services.AddSingleton(provider => new ClientCredentialsTokenProvider(
                    provider.GetRequiredService<IHttpClientFactory>(),
                    TokenClient,
                    () => new ClientCredentials(TokenAuthority, "r", "client", "secret"),
                    clock,
                    NullLogger.Instance));
                services.AddTransient<MintingHandler>();
                services.AddHttpClient(FetchClient)
                    .ConfigurePrimaryHttpMessageHandler(() => outerServer)
                    .AddHttpMessageHandler<MintingHandler>();
            },
            tune: options => options.CircuitBreaker.MinimumThroughput = minimumThroughput,
            clock: clock);

    private sealed class MintingHandler(ClientCredentialsTokenProvider tokens) : AuthorizingHandler
    {
        protected override async Task<AuthenticationHeaderValue?> AuthorizationAsync(CancellationToken cancellationToken) =>
            new("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class CountingServer(HttpStatusCode status) : HttpMessageHandler
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            return Task.FromResult(new HttpResponseMessage(status));
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            return new HttpResponseMessage(status);
        }
    }

    private sealed class SwitchableServer(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = status;

        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            HttpResponseMessage response = new(Status);
            if (Status == HttpStatusCode.OK)
            {
                response.Content = new StringContent(
                    """{"access_token":"minted","expires_in":0,"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class BlockingServer : HttpMessageHandler
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class HangingServer : HttpMessageHandler
    {
        private readonly SemaphoreSlim arrivals = new(0);
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public Task<bool> AttemptArrivedAsync(TimeSpan guard) => arrivals.WaitAsync(guard);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            arrivals.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
