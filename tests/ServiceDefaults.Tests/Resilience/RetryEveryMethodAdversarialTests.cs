using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.ServiceDefaults.Tests.Fakes;

namespace SmartSentinelEye.ServiceDefaults.Tests.Resilience;

/// <summary>
/// The edges <see cref="IdempotentRetryTests"/> leaves open, composed through the real
/// <c>AddServiceDefaults()</c> so the pipeline is the one every host runs: one standard
/// handler added by <c>ConfigureHttpClientDefaults</c>, reading options named
/// <c>-standard</c> for every client.
///
/// <para>
/// Three kinds of fact live here. <b>Opt-in contract</b> facts are red until
/// <c>RetryEveryMethod()</c> reaches that pipeline: they pin the paths a partial fix
/// would miss (synchronous <c>Send</c>, the body re-sent intact, 429, an attempt
/// timeout). <b>Leak guards</b> are green today and must stay green: an opt-in that
/// escapes its client fails toward duplicate effects. <b>Isolation</b> facts record
/// that one upstream authority's failures stay its own: the fact that used to record a
/// shared circuit breaker as a hazard now pins the per-authority fix (#2959).
/// </para>
/// </summary>
public class RetryEveryMethodAdversarialTests
{
    private const string Url = "https://x.test/thing";

    // ---- Opt-in contract: red until the opt-in reaches that client's pipeline ----

    /// <summary>
    /// <c>HttpClient.Send</c> walks <c>DelegatingHandler.Send</c>, not <c>SendAsync</c>.
    /// A marker that only overrides <c>SendAsync</c> leaves the synchronous path unmarked.
    /// </summary>
    [Fact]
    public void An_opted_in_client_retries_a_synchronous_POST()
    {
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services => services.AddHttpClient("opted")
            .ConfigurePrimaryHttpMessageHandler(() => server)
            .RetryEveryMethod());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("opted");

        using HttpRequestMessage request = new(HttpMethod.Post, Url) { Content = new StringContent("{}") };
        using var response = client.Send(request, CancellationToken.None);

        server.Attempts.ShouldBe(4, "the synchronous path must carry the opt-in too.");
    }

    /// <summary>
    /// The gateway posts with <c>PostAsJsonAsync</c>. A retry must re-send the same body,
    /// not an empty or truncated one: an empty <c>add/</c> body is a different request.
    /// </summary>
    [Fact]
    public async Task An_opted_in_POST_resends_the_same_JSON_body_on_every_attempt()
    {
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services => services.AddHttpClient("opted")
            .ConfigurePrimaryHttpMessageHandler(() => server)
            .RetryEveryMethod());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("opted");

        using var response = await client.PostAsJsonAsync(
            Url, new { source = "rtsp://camera/stream" }, CancellationToken.None);

        server.Bodies.Count.ShouldBe(4);
        server.Bodies.ShouldAllBe(body => body == """{"source":"rtsp://camera/stream"}""");
    }

    /// <summary>
    /// A form-encoded token mint (<c>ClientCredentialsTokenProvider</c>) must also arrive
    /// whole on every attempt.
    /// </summary>
    [Fact]
    public async Task An_opted_in_POST_resends_the_same_form_body_on_every_attempt()
    {
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services => services.AddHttpClient("opted")
            .ConfigurePrimaryHttpMessageHandler(() => server)
            .RetryEveryMethod());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("opted");

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "probe",
        });
        using var response = await client.PostAsync(Url, form, CancellationToken.None);

        server.Bodies.Count.ShouldBe(4);
        server.Bodies.ShouldAllBe(body => body == "grant_type=client_credentials&client_id=probe");
    }

    /// <summary>
    /// 429 is in the library's transient set, and a rate-limited request was not applied.
    /// </summary>
    [Fact]
    public async Task An_opted_in_POST_answered_too_many_requests_is_retried()
    {
        CountingServer server = new(HttpStatusCode.TooManyRequests);
        using var provider = Compose(services => services.AddHttpClient("opted")
            .ConfigurePrimaryHttpMessageHandler(() => server)
            .RetryEveryMethod());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("opted");

        using var response = await client.PostAsync(Url, new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(4);
    }

    /// <summary>
    /// The duplicate-apply surface the opt-in accepts, pinned so nobody mistakes it for
    /// "only retried when the server said no": an attempt that timed out may have been
    /// applied, and it is re-sent. This is why only idempotent-in-fact clients may opt in.
    /// </summary>
    [Fact]
    public async Task An_opted_in_POST_whose_attempt_timed_out_is_sent_again()
    {
        var attemptTimeout = TimeSpan.FromMilliseconds(100);
        SchedulingTimeProvider clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        HangingServer server = new();
        using var provider = Compose(
            services => services.AddHttpClient("opted")
                .ConfigurePrimaryHttpMessageHandler(() => server)
                .RetryEveryMethod(),
            options => options.AttemptTimeout.Timeout = attemptTimeout,
            clock);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("opted");

        using CancellationTokenSource abandon = new();
        var call = client.PostAsync(Url, new StringContent("{}"), abandon.Token);

        try
        {
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                (await server.AttemptArrivedAsync(HangGuard)).ShouldBeTrue($"attempt {attempt} never reached the server");
                clock.Advance(attemptTimeout);
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

    // ---- Leak guards: green today, must stay green after the fix ----

    /// <summary>
    /// The sequential sibling fact in <see cref="IdempotentRetryTests"/> passes for a fix
    /// that keeps the opt-in in process-wide state and clears it when the opted-in request
    /// finishes. A sibling that fails <i>while</i> the opted-in request is in flight does not.
    /// </summary>
    [Fact]
    public async Task A_sibling_POST_failing_while_an_opted_in_POST_is_in_flight_gets_one_attempt()
    {
        BlockingServer optedInServer = new();
        CountingServer siblingServer = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("opted").ConfigurePrimaryHttpMessageHandler(() => optedInServer).RetryEveryMethod();
            services.AddHttpClient("sibling").ConfigurePrimaryHttpMessageHandler(() => siblingServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var inFlight = factory.CreateClient("opted").PostAsync(Url, new StringContent("{}"), CancellationToken.None);
        await optedInServer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var siblingResponse = await factory.CreateClient("sibling")
            .PostAsync(Url, new StringContent("{}"), CancellationToken.None);
        optedInServer.Release();
        using var optedInResponse = await inFlight;

        siblingServer.Attempts.ShouldBe(1, "the opt-in must live on the request, not in shared state.");
    }

    /// <summary>
    /// The opt-in rides on the request object, so the same-named client resolved twice is
    /// still opted in, and a plain client created from the same factory afterwards is not.
    /// Also the thrown path: an opt-in read only off the response would miss it.
    /// </summary>
    [Fact]
    public async Task A_plain_client_POST_whose_transport_throws_gets_one_attempt_beside_an_opted_in_client()
    {
        CountingServer optedInServer = new(HttpStatusCode.ServiceUnavailable, throwInstead: true);
        CountingServer plainServer = new(HttpStatusCode.ServiceUnavailable, throwInstead: true);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("opted").ConfigurePrimaryHttpMessageHandler(() => optedInServer).RetryEveryMethod();
            services.AddHttpClient("plain").ConfigurePrimaryHttpMessageHandler(() => plainServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<HttpRequestException>(
            () => factory.CreateClient("opted").PostAsync(Url, new StringContent("{}"), CancellationToken.None));
        await Should.ThrowAsync<HttpRequestException>(
            () => factory.CreateClient("plain").PostAsync(Url, new StringContent("{}"), CancellationToken.None));

        optedInServer.Attempts.ShouldBe(4);
        plainServer.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// Through the real <c>AddServiceDefaults()</c>: a fix that put the marker into
    /// <c>ConfigureHttpClientDefaults</c> would pass every opted-in fact and the
    /// handler-chain guards, and retry every POST in every host.
    /// </summary>
    [Fact]
    public async Task A_client_without_the_opt_in_still_gets_one_POST_attempt_under_AddServiceDefaults()
    {
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services => services.AddHttpClient("plain")
            .ConfigurePrimaryHttpMessageHandler(() => server));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("plain");

        using var response = await client.PostAsync(Url, new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// spec §5 R4: the marker must never reach Keycloak or MediaMTX as a header.
    /// </summary>
    [Fact]
    public async Task The_opt_in_puts_nothing_on_the_wire()
    {
        CountingServer optedInServer = new(HttpStatusCode.OK);
        CountingServer plainServer = new(HttpStatusCode.OK);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("opted").ConfigurePrimaryHttpMessageHandler(() => optedInServer).RetryEveryMethod();
            services.AddHttpClient("plain").ConfigurePrimaryHttpMessageHandler(() => plainServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var first = await factory.CreateClient("opted").PostAsync(Url, new StringContent("{}"), CancellationToken.None);
        using var second = await factory.CreateClient("plain").PostAsync(Url, new StringContent("{}"), CancellationToken.None);

        optedInServer.HeaderNames.ShouldBe(plainServer.HeaderNames, ignoreOrder: true);
    }

    /// <summary>
    /// A caller that gives up must not be retried on its behalf, opt-in or not.
    /// </summary>
    [Fact]
    public async Task An_opted_in_POST_whose_caller_cancels_is_not_retried()
    {
        using CancellationTokenSource caller = new();
        CountingServer server = new(HttpStatusCode.ServiceUnavailable, onAttempt: caller.Cancel);
        using var provider = Compose(services => services.AddHttpClient("opted")
            .ConfigurePrimaryHttpMessageHandler(() => server)
            .RetryEveryMethod());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("opted");

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.PostAsync(Url, new StringContent("{}"), caller.Token));

        server.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// Calibration for the fact above: the library honours a cancelled caller for a GET,
    /// which is retried, so the POST fact can fail and is not true by construction.
    /// </summary>
    [Fact]
    public async Task A_GET_whose_caller_cancels_is_not_retried()
    {
        using CancellationTokenSource caller = new();
        CountingServer server = new(HttpStatusCode.ServiceUnavailable, onAttempt: caller.Cancel);
        using var provider = Compose(services => services.AddHttpClient("plain")
            .ConfigurePrimaryHttpMessageHandler(() => server));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("plain");

        await Should.ThrowAsync<OperationCanceledException>(() => client.GetAsync(Url, caller.Token));

        server.Attempts.ShouldBe(1);
    }

    // ---- Isolation: one upstream's breaker is not another's (#2959) ----

    /// <summary>
    /// <b>Inverted by #2959.</b> This fact used to record, as a hazard, that every client in a
    /// host resolved the same <c>-standard</c> pipeline and so shared one circuit breaker: one
    /// upstream failing refused calls to every other upstream. The flip is the expected outcome
    /// of #2959, not a regression: a failing upstream opens the breaker for its own authority
    /// only, and a healthy upstream on another authority is still called.
    /// </summary>
    [Fact]
    public async Task A_failing_upstream_does_not_open_the_circuit_breaker_for_a_healthy_upstream_on_another_authority()
    {
        CountingServer failingServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer healthyServer = new(HttpStatusCode.OK);
        using var provider = Compose(
            services =>
            {
                services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => failingServer);
                services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(() => healthyServer);
            },
            options =>
            {
                options.CircuitBreaker.MinimumThroughput = 2;
                options.CircuitBreaker.FailureRatio = 0.5;
            });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("failing").GetAsync("https://failing.test/thing", CancellationToken.None));

        using var response = await factory.CreateClient("healthy")
            .GetAsync("https://healthy.test/thing", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        healthyServer.Attempts.ShouldBe(1);
    }

    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(60);

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
            // Polly takes the container's TimeProvider for its timeout timers.
            builder.Services.AddSingleton<TimeProvider>(clock);
        }

        // The name the defaults pipeline reads its options under (spec §1).
        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
            tune?.Invoke(options);
        });

        return builder.Services.BuildServiceProvider();
    }

    private sealed class CountingServer(
        HttpStatusCode status,
        bool throwInstead = false,
        Action? onAttempt = null) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        public List<string> Bodies { get; } = [];

        public List<string> HeaderNames { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Record(request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return Answer();
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
            {
                using MemoryStream copy = new();
                request.Content.CopyTo(copy, null, cancellationToken);
                body = Encoding.UTF8.GetString(copy.ToArray());
            }

            Record(request, body);
            return Answer();
        }

        private void Record(HttpRequestMessage request, string? body)
        {
            Attempts++;
            if (body is not null)
            {
                Bodies.Add(body);
            }

            HeaderNames.Clear();
            HeaderNames.AddRange(request.Headers.Select(header => header.Key));
            onAttempt?.Invoke();
        }

        private HttpResponseMessage Answer() =>
            throwInstead ? throw new HttpRequestException("transport failed") : new HttpResponseMessage(status);
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

    private sealed class BlockingServer : HttpMessageHandler
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
