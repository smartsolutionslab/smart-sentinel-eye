using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
using SmartSentinelEye.ServiceDefaults.Resilience;

namespace SmartSentinelEye.ServiceDefaults.Tests.Resilience;

/// <summary>
/// Each upstream authority owns its circuit breaker (#2959): one upstream failing must not
/// refuse calls to another, and calls to the same authority still share one breaker whichever
/// client sends them. Composed through the real <c>AddServiceDefaults()</c>, as
/// <see cref="RetryEveryMethodAdversarialTests"/> does.
///
/// <para>
/// The first three facts are red until the pipeline is selected by authority. The last four
/// are guards, green before and after: they catch a fix that keys the breaker per client,
/// removes it, counts client errors, or loses the <c>RetryEveryMethod()</c> opt-in.
/// </para>
/// </summary>
public class PerAuthorityCircuitBreakerTests
{
    // ---- Red until the pipeline is selected by authority ----

    [Fact]
    public async Task The_authority_includes_the_port()
    {
        CountingServer failingServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer healthyServer = new(HttpStatusCode.OK);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => failingServer);
            services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(() => healthyServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("failing").GetAsync("https://host.test:8443/thing", CancellationToken.None));

        using var response = await factory.CreateClient("healthy")
            .GetAsync("https://host.test:9000/thing", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        healthyServer.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task An_opted_in_client_failing_its_POSTs_does_not_freeze_the_revocation_fetch()
    {
        CountingServer tokenServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer revocationServer = new(HttpStatusCode.OK);
        using var provider = Compose(services =>
        {
            services.AddHttpClient("token").ConfigurePrimaryHttpMessageHandler(() => tokenServer).RetryEveryMethod();
            services.AddHttpClient("revocation").ConfigurePrimaryHttpMessageHandler(() => revocationServer);
        });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("token")
                .PostAsync("https://keycloak.test/token", new StringContent("{}"), CancellationToken.None));

        using var response = await factory.CreateClient("revocation")
            .GetAsync("https://identity.test/revoked-clients", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        revocationServer.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_client_addressed_by_a_logical_service_name_is_isolated_by_that_name()
    {
        // Both logical names resolve to the one endpoint; only the path tells the calls apart.
        PathServer server = new("/failing/");
        using var provider = Compose(
            services =>
            {
                services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => server);
                services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(() => server);
            },
            configuration: new Dictionary<string, string?>
            {
                ["services:alpha:http:0"] = "http://127.0.0.1:5001",
                ["services:beta:http:0"] = "http://127.0.0.1:5001",
            });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("failing").GetAsync("http://alpha/failing/thing", CancellationToken.None));

        using var response = await factory.CreateClient("healthy")
            .GetAsync("http://beta/healthy/thing", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        server.HealthyAttempts.ShouldBe(1);
    }

    // ---- Guards: green before and after ----

    [Fact]
    public async Task Two_clients_to_the_same_authority_share_one_breaker()
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
            () => factory.CreateClient("failing").GetAsync("https://same.test/thing", CancellationToken.None));

        await Should.ThrowAsync<BrokenCircuitException>(
            () => factory.CreateClient("sibling").GetAsync("https://same.test/other", CancellationToken.None));
        siblingServer.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task The_failing_authority_still_opens_its_own_breaker()
    {
        CountingServer server = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(services =>
            services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() => server));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("failing");

        await Should.ThrowAsync<BrokenCircuitException>(
            () => client.GetAsync("https://failing.test/thing", CancellationToken.None));
        var attemptsWhenOpened = server.Attempts;

        await Should.ThrowAsync<BrokenCircuitException>(
            () => client.GetAsync("https://failing.test/thing", CancellationToken.None));

        server.Attempts.ShouldBe(attemptsWhenOpened, "an open breaker must refuse the call without reaching the server.");
    }

    [Fact]
    public async Task A_bad_request_never_opens_a_breaker()
    {
        CountingServer server = new(HttpStatusCode.BadRequest);
        using var provider = Compose(services =>
            services.AddHttpClient("bad").ConfigurePrimaryHttpMessageHandler(() => server));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("bad");

        for (var call = 0; call < 6; call++)
        {
            using var response = await client.GetAsync("https://bad.test/thing", CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        server.Attempts.ShouldBe(6);
    }

    [Fact]
    public async Task The_opt_in_reaches_the_pipeline_of_the_authority_it_calls()
    {
        CountingServer optedServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer plainOtherServer = new(HttpStatusCode.ServiceUnavailable);
        CountingServer plainSameServer = new(HttpStatusCode.ServiceUnavailable);
        using var provider = Compose(
            services =>
            {
                services.AddHttpClient("opted").ConfigurePrimaryHttpMessageHandler(() => optedServer).RetryEveryMethod();
                services.AddHttpClient("plainOther").ConfigurePrimaryHttpMessageHandler(() => plainOtherServer);
                services.AddHttpClient("plainSame").ConfigurePrimaryHttpMessageHandler(() => plainSameServer);
            },
            tune: options => options.CircuitBreaker.MinimumThroughput = 100);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var optedResponse = await factory.CreateClient("opted")
            .PostAsync("https://a.test/thing", new StringContent("{}"), CancellationToken.None);
        using var otherResponse = await factory.CreateClient("plainOther")
            .PostAsync("https://b.test/thing", new StringContent("{}"), CancellationToken.None);
        using var sameResponse = await factory.CreateClient("plainSame")
            .PostAsync("https://a.test/thing", new StringContent("{}"), CancellationToken.None);

        optedServer.Attempts.ShouldBe(4);
        plainOtherServer.Attempts.ShouldBe(1);
        plainSameServer.Attempts.ShouldBe(1);
    }

    private static ServiceProvider Compose(
        Action<IServiceCollection> registerClients,
        Action<HttpStandardResilienceOptions>? tune = null,
        Dictionary<string, string?>? configuration = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        builder.AddServiceDefaults();
        registerClients(builder.Services);

        // The name the defaults pipeline reads its options under.
        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
            options.CircuitBreaker.MinimumThroughput = 2;
            options.CircuitBreaker.FailureRatio = 0.5;
            tune?.Invoke(options);
        });

        return builder.Services.BuildServiceProvider();
    }

    private sealed class CountingServer(HttpStatusCode status) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class PathServer(string failingPathPrefix) : HttpMessageHandler
    {
        public int HealthyAttempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith(failingPathPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            HealthyAttempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
