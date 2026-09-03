using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SmartSentinelEye.ServiceDefaults.Resilience;

namespace SmartSentinelEye.ServiceDefaults.Tests.Resilience;

/// <summary>
/// ADR-0143, observed by counting the attempts a real pipeline makes rather than
/// by reading the predicate back.
///
/// <para>
/// The retry defaults are 3 retries with a 2 s exponential base, so a suite that
/// let the real delays run would take minutes. The delay is set to zero here and
/// nothing else is: the attempt <b>count</b> is what these tests are about, and
/// it is the one thing the backoff does not change.
/// </para>
/// </summary>
public class IdempotentRetryTests
{
    private const string Client = "probe";

    [Fact]
    public async Task A_failing_GET_is_still_retried()
    {
        (HttpClient client, CountingHandler server) = Build();

        using HttpResponseMessage response = await client.GetAsync("https://x.test/thing", CancellationToken.None);

        server.Attempts.ShouldBe(4, "one attempt plus the standard handler's three retries.");
    }

    /// <summary>
    /// The whole point. A POST that reached the server and lost its response is
    /// indistinguishable from one that never arrived, so the retry could apply
    /// the effect twice — which is exactly what produced #2039's conflict.
    /// </summary>
    [Fact]
    public async Task A_failing_POST_is_not_retried()
    {
        (HttpClient client, CountingHandler server) = Build();

        using HttpResponseMessage response = await client.PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(1, "a POST gets one attempt; retrying it can duplicate the effect.");
    }

    [Fact]
    public async Task A_failing_PATCH_is_not_retried()
    {
        (HttpClient client, CountingHandler server) = Build();

        using HttpResponseMessage response = await client.PatchAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(1, "RFC 9110 does not make PATCH idempotent, whatever a given endpoint does.");
    }

    [Fact]
    public async Task A_failing_PUT_is_retried_because_PUT_is_idempotent()
    {
        (HttpClient client, CountingHandler server) = Build();

        using HttpResponseMessage response = await client.PutAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(4);
    }

    /// <summary>
    /// A transport failure has no response to read the method off, so the
    /// predicate falls back to the resilience context. If that fallback did not
    /// work, this would still pass for a POST and silently stop retrying GETs —
    /// which is why the GET case is asserted on the exception path too.
    /// </summary>
    [Fact]
    public async Task The_method_is_still_known_when_the_transport_throws()
    {
        (HttpClient client, CountingHandler server) = Build(throwInstead: true);

        await Should.ThrowAsync<HttpRequestException>(
            () => client.GetAsync("https://x.test/thing", CancellationToken.None));

        server.Attempts.ShouldBe(4, "a thrown GET is transient and must still be retried.");
    }

    [Fact]
    public async Task A_thrown_POST_is_not_retried_either()
    {
        (HttpClient client, CountingHandler server) = Build(throwInstead: true);

        await Should.ThrowAsync<HttpRequestException>(
            () => client.PostAsync("https://x.test/thing", new StringContent("{}"), CancellationToken.None));

        server.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// The opt-out, for a client whose POSTs are idempotent in fact — a token
    /// mint, where a second token supersedes the first.
    /// </summary>
    [Fact]
    public async Task A_client_that_opts_back_in_retries_its_POSTs_again()
    {
        (HttpClient client, CountingHandler server) = Build(optBackIn: true);

        using HttpResponseMessage response = await client.PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(4);
    }

    private static (HttpClient Client, CountingHandler Server) Build(
        bool throwInstead = false, bool optBackIn = false)
    {
        CountingHandler server = new(throwInstead);
        ServiceCollection services = new();

        IHttpClientBuilder builder = services
            .AddHttpClient(Client)
            .ConfigurePrimaryHttpMessageHandler(() => server);

        // The order ConfigureHttpClientDefaults produces: the default first, the
        // per-client opt-in after, so the later Configure wins.
        builder.AddStandardResilienceHandler(options =>
        {
            IdempotentRetry.RetryIdempotentMethodsOnly(options);
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });

        if (optBackIn)
        {
            builder.RetryEveryMethod();
        }

        return (services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(Client), server);
    }

    private sealed class CountingHandler(bool throwInstead) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;

            return throwInstead
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("transport failed"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
