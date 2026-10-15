using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using SmartSentinelEye.ServiceDefaults.Resilience;

namespace SmartSentinelEye.ServiceDefaults.Tests.Resilience;

/// <summary>
/// The guard against <c>Extensions.cs</c> drifting from what
/// <see cref="IdempotentRetryTests"/> models: this goes through the real
/// <c>AddServiceDefaults()</c> and the <c>ConfigureHttpClientDefaults</c> pipeline
/// it builds, not a copy of it.
/// </summary>
public class RetryEveryMethodCompositionTests
{
    [Fact]
    public async Task An_opted_in_client_retries_its_POSTs_under_AddServiceDefaults()
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.AddServiceDefaults();

        CountingHandler server = new();
        builder.Services
            .AddHttpClient("probe")
            .ConfigurePrimaryHttpMessageHandler(() => server)
            .RetryEveryMethod();

        // The name the defaults pipeline reads its options under.
        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });

        using var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

        using var response = await client.PostAsync(
            "https://x.test/thing", new StringContent("{}"), CancellationToken.None);

        server.Attempts.ShouldBe(4, "one attempt plus the standard handler's three retries.");
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
