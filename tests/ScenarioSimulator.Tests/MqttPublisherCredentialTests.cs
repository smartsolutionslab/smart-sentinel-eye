using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 254 (#2450) — a port of EventIngestion's
/// <c>MqttConnectionLoopTests.Each_attempt_presents_a_freshly_minted_credential</c>
/// onto <see cref="MqttPublisher"/>, now that <c>FakeMqttClient</c> keeps a
/// credential history to assert against.
///
/// <para>
/// <b>Its own harness</b>, not a reuse of
/// <c>MqttPublisherBackoffTests.PublisherUnderTest</c>: that type is private to a
/// sibling file, and this one needs its own stub Keycloak — every existing stub
/// in this suite answers a constant token with <c>expires_in: 300</c>, which
/// <c>ClientCredentialsTokenProvider</c> caches, so every attempt would present
/// the same string whether the publisher mints fresh tokens or reuses one
/// (spec.md §1, "blind twice over"). <see cref="CountingKeycloak"/> mints
/// <c>token-1</c>, <c>token-2</c>, … with no caching, so a reused credential is
/// visible rather than merely uncounted.
/// </para>
/// </summary>
public class MqttPublisherCredentialTests
{
    [Fact]
    public async Task Each_attempt_presents_a_freshly_minted_credential()
    {
        await using PublisherUnderTest publisher = PublisherUnderTest.Create();
        publisher.Client.RefuseNextConnects(2);

        await publisher.Publisher.StartAsync(CancellationToken.None);

        (await PublisherUnderTest.WaitUntilAsync(() => publisher.Client.ConnectAttempts >= 3)).ShouldBeTrue(
            "the loop gave up before the third attempt");

        List<string> presented = [.. publisher.Client.PresentedCredentials.Take(3)];

        presented.ShouldBe(
            ["token-1", "token-2", "token-3"],
            "each attempt must present a freshly minted credential. Reusing one is exactly #2038: a "
            + "publisher that re-presents the same dead JWT forever, recoverable only by a restart.");
    }

    private sealed class PublisherUnderTest : IAsyncDisposable
    {
        private readonly KeycloakTokenProvider tokens;

        private PublisherUnderTest(FakeMqttClient client, RecordingLogger<MqttPublisher> logger, KeycloakTokenProvider tokens)
        {
            Client = client;
            Logger = logger;
            this.tokens = tokens;

            Publisher = new MqttPublisher(
                Options.Create(new SimulatorOptions
                {
                    MqttHost = "mosquitto.test:1883",
                    KeycloakUrl = "https://keycloak.test",
                    ClientSecret = "a-secret",
                }),
                tokens,
                logger,
                client,
                new MqttBackoff(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(4)));
        }

        public FakeMqttClient Client { get; }

        public RecordingLogger<MqttPublisher> Logger { get; }

        public MqttPublisher Publisher { get; }

        public static PublisherUnderTest Create()
        {
            FakeMqttClient client = new();
            KeycloakTokenProvider tokens = new(
                new FakeHttpClientFactory(new HttpClient(new CountingKeycloak())),
                Options.Create(new SimulatorOptions { KeycloakUrl = "https://keycloak.test" }),
                TimeProvider.System,
                NullLogger<KeycloakTokenProvider>.Instance);

            return new PublisherUnderTest(client, new RecordingLogger<MqttPublisher>(), tokens);
        }

        public async ValueTask DisposeAsync()
        {
            await Publisher.DisposeAsync();
            tokens.Dispose();
        }

        public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? within = null)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(10));
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
            }

            return condition();
        }
    }

    /// <summary>
    /// Hands back <c>token-1</c>, <c>token-2</c>, … so a reused credential is
    /// visible rather than merely uncounted. <c>expires_in: 0</c> defeats
    /// <c>ClientCredentialsTokenProvider</c>'s 80%-of-lifetime cache, which would
    /// otherwise answer every attempt from the first mint and make this test
    /// unable to tell minting from remembering. Carried over from
    /// EventIngestion's <c>MqttConnectionLoopTests.CountingKeycloak</c>.
    /// </summary>
    private sealed class CountingKeycloak : HttpMessageHandler
    {
        private int minted;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int number = Interlocked.Increment(ref minted);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"token-{{number}}","expires_in":0,"token_type":"Bearer"}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
