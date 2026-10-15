using System.Net;
using Microsoft.Extensions.DependencyInjection;
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
        (var client, var server) = Build();

        using var response = await client.GetAsync("https://x.test/thing", CancellationToken.None);

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
        (var client, var server) = Build();

        using var response = await client.PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(1, "a POST gets one attempt; retrying it can duplicate the effect.");
    }

    [Fact]
    public async Task A_failing_PATCH_is_not_retried()
    {
        (var client, var server) = Build();

        using var response = await client.PatchAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(1, "RFC 9110 does not make PATCH idempotent, whatever a given endpoint does.");
    }

    [Fact]
    public async Task A_failing_PUT_is_retried_because_PUT_is_idempotent()
    {
        (var client, var server) = Build();

        using var response = await client.PutAsync(
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
        (var client, var server) = Build(throwInstead: true);

        await Should.ThrowAsync<HttpRequestException>(
            () => client.GetAsync("https://x.test/thing", CancellationToken.None));

        server.Attempts.ShouldBe(4, "a thrown GET is transient and must still be retried.");
    }

    [Fact]
    public async Task A_thrown_POST_is_not_retried_either()
    {
        (var client, var server) = Build(throwInstead: true);

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
        (var client, var server) = Build(optBackIn: true);

        using var response = await client.PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(4);
    }

    [Fact]
    public async Task A_client_that_opts_back_in_retries_its_PATCHes_again()
    {
        (var client, var server) = Build(optBackIn: true);

        using var response = await client.PatchAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(4);
    }

    [Fact]
    public async Task A_client_that_opts_back_in_retries_a_thrown_POST_again()
    {
        (var client, var server) = Build(throwInstead: true, optBackIn: true);

        await Should.ThrowAsync<HttpRequestException>(
            () => client.PostAsync("https://x.test/thing", new StringContent("{}"), CancellationToken.None));

        server.Attempts.ShouldBe(4);
    }

    /// <summary>
    /// Opting in restores the library's transient predicate, not "retry anything".
    /// </summary>
    [Fact]
    public async Task A_client_that_opts_back_in_does_not_retry_a_POST_answered_with_a_bad_request()
    {
        (var client, var server) = Build(optBackIn: true, status: HttpStatusCode.BadRequest);

        using var response = await client.PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(1);
    }

    /// <summary>
    /// The opt-in is per client: another client in the same container, sending
    /// after the opted-in one, must still get one attempt for a POST.
    /// </summary>
    [Fact]
    public async Task A_sibling_client_without_the_opt_in_still_gets_one_attempt_for_a_POST()
    {
        CountingHandler optedInServer = new(false);
        CountingHandler siblingServer = new(false);
        ServiceCollection services = new();
        AddDefaults(services);
        services.AddHttpClient(Client).ConfigurePrimaryHttpMessageHandler(() => optedInServer).RetryEveryMethod();
        services.AddHttpClient("sibling").ConfigurePrimaryHttpMessageHandler(() => siblingServer);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        using var first = await factory.CreateClient(Client).PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);
        using var second = await factory.CreateClient("sibling").PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        siblingServer.Attempts.ShouldBe(1, "the opt-in belongs to the client that asked, not to the shared pipeline.");
    }

    // The shape AddServiceDefaults produces: the standard handler arrives through
    // ConfigureHttpClientDefaults, shared by every client, so a per-client opt-in has
    // to reach it from outside.
    private static void AddDefaults(IServiceCollection services) =>
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(options =>
        {
            IdempotentRetry.RetryIdempotentMethodsOnly(options);
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        }));

    private static (HttpClient Client, CountingHandler Server) Build(
        bool throwInstead = false,
        bool optBackIn = false,
        HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
    {
        CountingHandler server = new(throwInstead, status);
        ServiceCollection services = new();
        AddDefaults(services);

        var builder = services
            .AddHttpClient(Client)
            .ConfigurePrimaryHttpMessageHandler(() => server);

        if (optBackIn)
        {
            builder.RetryEveryMethod();
        }

        return (services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(Client), server);
    }

    private sealed class CountingHandler(bool throwInstead, HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
        : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;

            return throwInstead
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("transport failed"))
                : Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
