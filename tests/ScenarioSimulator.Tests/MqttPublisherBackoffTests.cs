using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 250 (#2449) — the publisher's backoff exercised through the loop it
/// actually drives, porting the three <c>ResetIfHeld</c> cases from
/// EventIngestion's <c>MqttConnectionLoopTests</c>. <c>MqttBackoff</c> is
/// byte-identical between the two loops (diffed with doc comments stripped), so
/// these are the same three scenarios run against the twin's own copy — until
/// now nothing in this suite reached <c>ResetIfHeld</c> at all.
///
/// <para>
/// <b>Its own harness</b>, not a reuse of
/// <c>MqttPublisherDropAccountingTests</c>'s <c>PublisherUnderTest</c>: that
/// type is private to a characterisation file this spec leaves unmodified
/// (ADR-0139 / ADR-0144), and it has no way to pass a backoff in.
/// </para>
/// </summary>
public class MqttPublisherBackoffTests
{
    /// <summary>
    /// Attempts a 100 ms/400 ms backoff cannot reach in <see cref="SpinWindow"/>:
    /// the delays run 0, 100, 200, 400, 400 … so about five are due, and even a
    /// fix that only ever waited the first 100 ms could manage a dozen. Twenty
    /// sits comfortably above any loop that waits and far below one that does
    /// not — a spin makes thousands. Carried over from EventIngestion's
    /// <c>MqttConnectionLoopTests</c> unchanged, and re-measured below rather
    /// than trusted (plan.md §4.2).
    /// </summary>
    private const int SpinCeiling = 20;

    /// <summary>
    /// Connects a 100 ms/400 ms backoff cannot reach in <see cref="FlapWindow"/>
    /// once it engages: the delays run 0, 100, 200, 400, 400 … and each cycle
    /// also spends <see cref="JustPastTheFloor"/> connected, so eight or so are
    /// due in three seconds. A backoff that resets every cycle instead runs at
    /// the hold period alone — about 27. Sixteen sits clear of both. Same
    /// source and re-measurement basis as <see cref="SpinCeiling"/>.
    /// </summary>
    private const int FlapCeiling = 16;

    private static readonly TimeSpan SpinWindow = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan FlapWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A hold that clears <see cref="PublisherUnderTest.Patient"/>'s 100 ms
    /// floor and nothing more — the margin is the whole point. Anywhere above
    /// the floor used to count as a connection that held, so a peer flapping at
    /// <c>first + ε</c> reset the backoff on every cycle and reconnected at full
    /// speed. <c>ResetIfHeld</c> now measures against twice the wait actually
    /// served, so this hold no longer clears it.
    /// </summary>
    private static readonly TimeSpan JustPastTheFloor = TimeSpan.FromMilliseconds(110);

    /// <summary>
    /// How long to hold the connecting attempt before dropping it, so the
    /// connection <b>held</b> rather than merely arrived. <c>ResetIfHeld</c>'s
    /// yardstick is twice the delay <c>Patient()</c> actually served on the
    /// attempt that connects: two refusals put that attempt at
    /// <c>100 x 2^1</c> jittered [0.8, 1.2] = 160-240 ms, so twice that tops out
    /// at 480 ms. 900 ms clears the worst case by 1.9x.
    /// </summary>
    private static readonly TimeSpan HeldConnectionDuration = TimeSpan.FromMilliseconds(900);

    /// <summary>
    /// How long to wait for the reconnect that follows a held connection. Above
    /// it sits nothing the correct loop produces; below it sits nothing the
    /// broken loop produces — with the backoff inherited, the next attempt is
    /// <c>Patient()</c>'s <c>100 x 2^2</c> capped at 400 ms, jittered
    /// [0.8, 1.2] = 320-480 ms. 150 ms sits clear of both.
    /// </summary>
    private static readonly TimeSpan PromptReconnectWindow = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task A_connection_that_dies_on_arrival_is_retried_with_a_delay_rather_than_a_spin()
    {
        await using PublisherUnderTest publisher = PublisherUnderTest.Create(PublisherUnderTest.Patient());
        publisher.Client.DropEveryConnectionImmediately = true;

        await publisher.Publisher.StartAsync(CancellationToken.None);

        bool spun = await PublisherUnderTest.WaitUntilAsync(
            () => publisher.Client.ConnectAttempts >= SpinCeiling, SpinWindow);

        Console.WriteLine(
            $"[dies-on-arrival] {publisher.Client.ConnectAttempts} attempts inside a "
            + $"{SpinWindow.TotalSeconds:F0}s window");

        spun.ShouldBeFalse(
            $"the loop reached {publisher.Client.ConnectAttempts} attempts inside a "
            + $"{SpinWindow.TotalSeconds:F0}s window against a 100 ms backoff, which allows about five. "
            + "Resetting the backoff on a connection that has not lasted a single instant means a "
            + "takeover loop reconnects with no delay at all, for as long as the other client is "
            + "there.");
    }

    /// <summary>
    /// CF2's target (plan.md §5) — the architect's flagged-most-important
    /// counterfactual, because a hardcoded constant backoff bypassing the new
    /// injectable parameter would still pass the case above (a hold of zero is
    /// caught by any yardstick) but must fail this one: the doubled yardstick
    /// exists precisely to tell a peer sitting a hair above the floor apart from
    /// a connection that genuinely held.
    /// </summary>
    [Fact]
    public async Task A_connection_held_just_past_the_floor_still_backs_off()
    {
        await using PublisherUnderTest publisher = PublisherUnderTest.Create(PublisherUnderTest.Patient());
        publisher.Client.HoldEveryConnectionFor(JustPastTheFloor);

        await publisher.Publisher.StartAsync(CancellationToken.None);

        await Task.Delay(FlapWindow);

        Console.WriteLine(
            $"[held-just-past-the-floor] {publisher.Client.ConnectAttempts} attempts inside a "
            + $"{FlapWindow.TotalSeconds:F0}s window, holding each connection "
            + $"{JustPastTheFloor.TotalMilliseconds:F0} ms");

        publisher.Client.ConnectAttempts.ShouldBeLessThan(
            FlapCeiling,
            $"connects made in {FlapWindow.TotalSeconds:F0}s against a peer holding each one for "
            + $"{JustPastTheFloor.TotalMilliseconds:F0} ms — a 100 ms floor exceeded by a hair. "
            + "ResetIfHeld clears the backoff on every one of them, so there is never a delay left to "
            + "wait: the takeover the guard was written for reconnects at full speed anyway, one cycle "
            + "per peer period, for as long as the other client is there.");
    }

    [Fact]
    public async Task A_reconnect_after_a_held_connection_does_not_inherit_the_previous_backoff()
    {
        await using PublisherUnderTest publisher = PublisherUnderTest.Create(PublisherUnderTest.Patient());
        publisher.Client.RefuseNextConnects(2);

        await publisher.Publisher.StartAsync(CancellationToken.None);

        (await PublisherUnderTest.WaitUntilAsync(() => publisher.Client.IsConnected)).ShouldBeTrue(
            "the loop never connected");

        int before = publisher.Client.ConnectAttempts;

        // Hold the connection well past ResetIfHeld's own yardstick (twice the
        // 160-240 ms this attempt served, so at most 480 ms) before dropping it
        // — a connection that held, not merely arrived.
        await Task.Delay(HeldConnectionDuration);
        await publisher.Client.DropAsync();

        Stopwatch stopwatch = Stopwatch.StartNew();
        bool reconnected = await PublisherUnderTest.WaitUntilAsync(
            () => publisher.Client.ConnectAttempts > before, PromptReconnectWindow);
        long elapsedMs = stopwatch.ElapsedMilliseconds;

        Console.WriteLine(
            $"[reconnect-after-held] reconnect observed at {elapsedMs} ms against a "
            + $"{PromptReconnectWindow.TotalMilliseconds:F0} ms window, after a "
            + $"{HeldConnectionDuration.TotalMilliseconds:F0} ms hold");

        reconnected.ShouldBeTrue(
            $"the reconnect took {elapsedMs} ms against a {PromptReconnectWindow.TotalMilliseconds:F0} ms "
            + "window — still waiting out the backoff the outage had grown. A delay is for repeated "
            + "failures, not for the first attempt after a connection that held.");
    }

    private sealed class PublisherUnderTest : IAsyncDisposable
    {
        private readonly KeycloakTokenProvider tokens;

        private PublisherUnderTest(
            FakeMqttClient client,
            RecordingLogger<MqttPublisher> logger,
            KeycloakTokenProvider tokens,
            MqttBackoff backoff)
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
                backoff);
        }

        public FakeMqttClient Client { get; }

        public RecordingLogger<MqttPublisher> Logger { get; }

        public MqttPublisher Publisher { get; }

        /// <summary>
        /// Slow enough that a delay is distinguishable from none, and small
        /// enough to keep the suite quick — carried over from EventIngestion's
        /// <c>LoopUnderTest.Patient()</c>, the same figures this file's
        /// ceilings were derived against.
        /// </summary>
        public static MqttBackoff Patient() =>
            new(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400));

        public static PublisherUnderTest Create(MqttBackoff backoff)
        {
            FakeMqttClient client = new();
            KeycloakTokenProvider tokens = new(
                new FakeHttpClientFactory(new HttpClient(new StubKeycloak())),
                Options.Create(new SimulatorOptions { KeycloakUrl = "https://keycloak.test" }),
                TimeProvider.System,
                NullLogger<KeycloakTokenProvider>.Instance);

            return new PublisherUnderTest(client, new RecordingLogger<MqttPublisher>(), tokens, backoff);
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

    private sealed class StubKeycloak : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"a-token","expires_in":300,"token_type":"Bearer"}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
