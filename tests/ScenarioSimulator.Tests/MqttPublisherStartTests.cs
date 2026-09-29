using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-B, T-B06 (ADR-0144 red). <c>MqttPublisher.StartAsync</c>
/// guards its connect loop with <c>if (loop is not null) return
/// Task.CompletedTask;</c> — a plain check-then-set on two unsynchronised
/// fields (<c>loop</c> and <c>loopCancellation</c>). Until this PR it had
/// exactly one caller (<c>BilletTimelineHostedService</c>), so the race was
/// latent. Plan.md's <c>ClipCueHostedService</c> (T-B15) becomes a second, and
/// both hosted services call <c>StartAsync</c> during startup with no
/// ordering between them, so two threads can now race the same fields on the
/// same publisher instance.
///
/// <para>
/// <b>Why this needs a genuine thread race, not a sequential call.</b> The
/// window between the null check and the field assignments is synchronous C#
/// running on the calling thread up to the first <em>true</em> await
/// suspension point several calls deep (inside
/// <c>FakeMqttClient.ConnectAsync</c>'s <c>await Task.Yield()</c>), so two
/// <em>sequential</em> calls never race — the first always finishes assigning
/// before the second one's check runs. Only two calls actually executing on
/// different CPU cores at the same instant can land both checks before either
/// assignment. This test uses raw <see cref="Thread"/>s released together by
/// a <see cref="Barrier"/> (real OS parallelism — <c>Task.Run</c> is not
/// enough, thread-pool scheduling can serialise it), repeated across many
/// rounds so the race is overwhelmingly likely to be hit at least once — the
/// same technique this test suite already uses for
/// <c>FakeMqttClientConcurrencyTests</c> in EventIngestion, including its
/// <c>WaitAsync(OverallBound)</c> safety net (see below for why that net is
/// not optional here).
/// </para>
///
/// <para>
/// <b>What "hit" looks like, and what it actually does — observed, not
/// merely predicted.</b> Running this test against the current,
/// unsynchronised <c>StartAsync</c> does not just let a second connect loop
/// start (visible as <see cref="FakeMqttClient.ConnectAttempts"/> settling at
/// 2 instead of 1, since <c>FakeMqttClient.ConnectAsync</c> checks
/// <c>IsConnected</c> and increments the counter only after, mirroring the
/// real client's <c>ThrowIfConnected</c>). It goes further: <c>StartAsync</c>
/// writes <c>loopCancellation</c> and <c>loop</c> in <b>two separate
/// statements</b>, each re-reading the field rather than a captured local —
/// <c>loop = RunAsync(clientOptions, loopCancellation.Token)</c> reads
/// <c>loopCancellation</c> again to build the argument. Under a genuine race
/// the field can be overwritten by the other thread <em>between</em> those
/// two statements, so the token a given <c>RunAsync</c> call actually runs
/// under can end up different from whichever <c>CancellationTokenSource</c>
/// the fields settle on afterwards. <c>DisposeAsync</c> then cancels
/// <em>that</em> source and awaits <em>that</em> <c>loop</c> — and if they
/// were paired with different racing calls, the cancellation never reaches
/// the loop being awaited, which then never observes a drop and never
/// returns: <c>DisposeAsync</c> hangs forever. <b>This is not a hypothesised
/// consequence — it is what made the very first run of this test hang for
/// over ten minutes at zero CPU</b> (the process was blocked on exactly this
/// <c>await</c>, not merely slow) until it was killed by hand. Because a hit
/// can make disposal itself unrecoverable, this test never disposes a racing
/// round's publisher — doing so would reintroduce the hang it exists to
/// prove — and the whole run carries a hard <see cref="OverallBound"/> so a
/// repeat of that hang fails the test loudly and quickly rather than wedging
/// the run.
/// </para>
/// </summary>
public sealed class MqttPublisherStartTests
{
    private const int Rounds = 60;
    private const int RacingThreads = 6;

    private static readonly TimeSpan OverallBound = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task Many_concurrent_StartAsync_calls_on_one_publisher_never_start_more_than_one_connect_loop()
    {
        KeycloakTokenProvider tokens = new(
            new FakeHttpClientFactory(new HttpClient(new StubKeycloak())),
            Options.Create(new SimulatorOptions { KeycloakUrl = "https://keycloak.test" }),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);

        try
        {
            await RunRoundsAsync(tokens).WaitAsync(OverallBound);
        }
        finally
        {
            tokens.Dispose();
        }
    }

    private static async Task RunRoundsAsync(KeycloakTokenProvider tokens)
    {
        for (int round = 0; round < Rounds; round++)
        {
            FakeMqttClient client = new();

            // Deliberately never disposed (see the class doc comment): a round
            // that hits the race can leave `DisposeAsync` unable to ever return,
            // and this test's job is to observe the race, not to tidy up after
            // it. The `MqttPublisher` and its `FakeMqttClient` are simply
            // abandoned to the GC once the round's assertions are done.
            MqttPublisher publisher = new(
                Options.Create(new SimulatorOptions
                {
                    MqttHost = "mosquitto.test:1883",
                    KeycloakUrl = "https://keycloak.test",
                    ClientSecret = "a-secret",
                }),
                tokens,
                NullLogger<MqttPublisher>.Instance,
                client);

            RaceStartAsync(publisher);

            (await WaitUntilAsync(() => client.ConnectAttempts >= 1)).ShouldBeTrue(
                $"round {round}: neither racing call ever connected, so this round proves nothing");

            // Give a second connect (if the race let one through) time to land too —
            // it completes after one more Task.Yield, well inside this window.
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            client.ConnectAttempts.ShouldBe(
                1,
                $"round {round}: {client.ConnectAttempts} independent connects were made from one "
                + $"publisher racing {RacingThreads} concurrent StartAsync calls — the unsynchronised "
                + "`if (loop is not null)` check let more than one connect loop start.");
        }
    }

    /// <summary>Releases <see cref="RacingThreads"/> OS threads at once, each calling <c>StartAsync</c>.</summary>
    private static void RaceStartAsync(MqttPublisher publisher)
    {
        using Barrier barrier = new(RacingThreads);
        Thread[] threads = new Thread[RacingThreads];

        for (int i = 0; i < RacingThreads; i++)
        {
            threads[i] = new Thread(() =>
            {
                barrier.SignalAndWait();
                _ = publisher.StartAsync(CancellationToken.None);
            })
            { IsBackground = true };
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(2), CancellationToken.None);
        }

        return condition();
    }

    /// <summary>
    /// Backend-reviewer finding S2 (should-fix). The sibling fact above never
    /// disposes a racing round's publisher — deliberately, because a hit used
    /// to make <c>DisposeAsync</c> hang forever (see the class doc comment).
    /// That means nothing has ever proven the disposal path itself:
    /// <c>StartAsync</c> could regress to letting the race back in while this
    /// suite still passed, since the only test racing it never calls
    /// <c>DisposeAsync</c>. This fact races <c>StartAsync</c> exactly as the
    /// one above does, then disposes under a hard per-round bound — if the
    /// disposal-hang defect returns, this <c>WaitAsync</c> times out and fails
    /// the test instead of hanging the suite forever.
    /// </summary>
    [Fact]
    public async Task Disposing_a_publisher_after_a_StartAsync_race_completes_rather_than_hanging()
    {
        const int DisposalRounds = 20;
        static TimeSpan DisposeBound() => TimeSpan.FromSeconds(5);

        KeycloakTokenProvider tokens = new(
            new FakeHttpClientFactory(new HttpClient(new StubKeycloak())),
            Options.Create(new SimulatorOptions { KeycloakUrl = "https://keycloak.test" }),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);

        try
        {
            for (int round = 0; round < DisposalRounds; round++)
            {
                FakeMqttClient client = new();
                MqttPublisher publisher = new(
                    Options.Create(new SimulatorOptions
                    {
                        MqttHost = "mosquitto.test:1883",
                        KeycloakUrl = "https://keycloak.test",
                        ClientSecret = "a-secret",
                    }),
                    tokens,
                    NullLogger<MqttPublisher>.Instance,
                    client);

                RaceStartAsync(publisher);

                (await WaitUntilAsync(() => client.ConnectAttempts >= 1)).ShouldBeTrue(
                    $"round {round}: neither racing call ever connected, so this round proves nothing");

                await Should.NotThrowAsync(
                    () => publisher.DisposeAsync().AsTask().WaitAsync(DisposeBound()),
                    $"round {round}: DisposeAsync did not return within {DisposeBound().TotalSeconds}s after "
                    + "a StartAsync race — this is the disposal-hang defect (a RunAsync call paired with a "
                    + "different CancellationTokenSource than the one DisposeAsync awaits), not mere slowness.");
            }
        }
        finally
        {
            tokens.Dispose();
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
