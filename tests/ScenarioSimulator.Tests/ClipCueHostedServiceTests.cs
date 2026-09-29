using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Cues;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-B, T-B07 (ADR-0144 red). <c>ClipCueHostedService</c> does not
/// exist yet; this class is the target shape (plan.md §5.2, T-B15) — a
/// <c>BackgroundService</c> running one loop per camera-sim path: poll
/// readiness, schedule via <see cref="CueSchedule"/>, publish via
/// <see cref="MqttPublisher"/> (calling its <c>StartAsync</c> too — the second
/// caller <see cref="MqttPublisherStartTests"/> proves must not race the
/// billet timeline's).
///
/// <para>
/// Uses a fake path client (a stub <see cref="HttpMessageHandler"/> whose
/// response can be changed mid-test), a fake publisher sink
/// (<see cref="FakeMqttClient"/>, ungated so it connects immediately) and the
/// hand-written <see cref="ManualTimeProvider"/> — no wall-clock sleeps drive
/// the schedule; <see cref="ManualTimeProvider.Advance"/> does.
/// </para>
/// </summary>
public sealed class ClipCueHostedServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_ready_paths_cue_is_emitted_at_readyTime_plus_AtMs()
    {
        string clipsDirectory = CreateClipsDirectory(("mill-roughing", 6_000, (5_000, "person", 0.9)));
        MutableCameraSimHandler pathResponses = new();
        pathResponses.SetReady("station-4-roughing", Start);
        FakeMqttClient client = new();
        ManualTimeProvider clock = new(Start);

        await using Harness harness = Harness.Create(
            clipsDirectory, pathResponses, client, clock,
            OneAssetScenario("rolling-mill", "station-4-roughing", "mill-roughing.mp4"));

        await harness.Service.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => client.IsConnected)).ShouldBeTrue("the cue service never connected its publisher");

        // Repeated 1s advances (the loop's own re-poll cadence), reaching the
        // 5_000ms cue.
        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        (await WaitUntilAsync(() => client.Published.Count >= 1)).ShouldBeTrue(
            "the cue never fired for a path that has been ready the whole time");

        using JsonDocument document = JsonDocument.Parse(client.Published[0]);
        document.RootElement.GetProperty("payload").GetProperty("class").GetString().ShouldBe("person");
        document.RootElement.GetProperty("payload").GetProperty("clipOffsetMs").GetInt32().ShouldBe(5_000);

        await harness.Service.StopAsync(CancellationToken.None);
        Directory.Delete(clipsDirectory, recursive: true);
    }

    [Fact]
    public async Task A_path_with_no_reader_emits_no_cue()
    {
        string clipsDirectory = CreateClipsDirectory(("mill-roughing", 6_000, (500, "person", 0.9)));
        MutableCameraSimHandler pathResponses = new();
        pathResponses.SetNotReady("station-4-roughing");
        FakeMqttClient client = new();
        ManualTimeProvider clock = new(Start);

        await using Harness harness = Harness.Create(
            clipsDirectory, pathResponses, client, clock,
            OneAssetScenario("rolling-mill", "station-4-roughing", "mill-roughing.mp4"));

        await harness.Service.StartAsync(CancellationToken.None);

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        client.Published.ShouldBeEmpty("no reader means no video means no cue (FR-005)");

        await harness.Service.StopAsync(CancellationToken.None);
        Directory.Delete(clipsDirectory, recursive: true);
    }

    /// <summary>
    /// Backend-reviewer finding B1 (blocker), at the hosted-service level.
    /// <see cref="ClipCueHostedService"/> never records a cursor after a
    /// <c>Wait</c> decision, so every tick re-enters <c>CueSchedule</c>'s
    /// cold-start search — which only accepts a candidate whose <c>AtMs</c> is
    /// still at-or-after the current offset. A wake-up landing a few
    /// milliseconds <em>past</em> the cue's due time (not exactly on it, as
    /// every other test in this file arranges by advancing in whole seconds
    /// onto a whole-second <c>AtMs</c>) makes the cue silently unreachable
    /// until the next loop — on a real clock, in practice never, since the
    /// next tick is late again by construction.
    /// </summary>
    [Fact]
    public async Task A_wake_up_a_few_milliseconds_past_the_cues_due_time_still_emits_it_rather_than_skipping_a_whole_loop()
    {
        string clipsDirectory = CreateClipsDirectory(("mill-roughing", 6_000, (2_000, "person", 0.9)));
        MutableCameraSimHandler pathResponses = new();
        pathResponses.SetReady("station-4-roughing", Start);
        FakeMqttClient client = new();
        ManualTimeProvider clock = new(Start);

        await using Harness harness = Harness.Create(
            clipsDirectory, pathResponses, client, clock,
            OneAssetScenario("rolling-mill", "station-4-roughing", "mill-roughing.mp4"));

        await harness.Service.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => client.IsConnected)).ShouldBeTrue();

        // First tick (at t=0) sees the 2_000ms cue is not due yet and waits,
        // capped at 1s -> fires at t=1_000ms exactly.
        clock.Advance(TimeSpan.FromMilliseconds(1_000));
        await Task.Delay(20);

        // Second tick recomputes cold-start at t=1_000ms, still not due, waits
        // ~1_000ms more -> due at t=2_000ms. Land 5ms past it, not on it.
        clock.Advance(TimeSpan.FromMilliseconds(1_005));
        await Task.Delay(20);

        (await WaitUntilAsync(() => client.Published.Count >= 1)).ShouldBeTrue(
            "the cue was 5ms late at wake-up and was never emitted — the cold-start search jumped "
            + "past it to the next loop instead of catching it within the 250ms late tolerance");

        await harness.Service.StopAsync(CancellationToken.None);
        Directory.Delete(clipsDirectory, recursive: true);
    }

    [Fact]
    public async Task A_readyTime_that_changes_mid_run_re_anchors_rather_than_firing_off_the_stale_phase()
    {
        // A cue far enough into the clip that the loop's 1s-capped wait polls
        // readiness (and so notices a changed readyTime) several times before
        // the cue would otherwise be due.
        string clipsDirectory = CreateClipsDirectory(("mill-roughing", 10_000, (4_000, "person", 0.9)));
        MutableCameraSimHandler pathResponses = new();
        pathResponses.SetReady("station-4-roughing", Start);
        FakeMqttClient client = new();
        ManualTimeProvider clock = new(Start);

        await using Harness harness = Harness.Create(
            clipsDirectory, pathResponses, client, clock,
            OneAssetScenario("rolling-mill", "station-4-roughing", "mill-roughing.mp4"));

        await harness.Service.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => client.IsConnected)).ShouldBeTrue();

        // Two seconds in (short of the original 4_000ms cue), the path restarts:
        // camera-sim reports a brand new readyTime, as it does when a closed
        // wall is reopened and camera-sim replays the clip from offset 0.
        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        DateTimeOffset restartedAt = clock.GetUtcNow();
        pathResponses.SetReady("station-4-roughing", restartedAt);

        // Advance to where the cue would have fired under the OLD anchor
        // (Start + 4_000ms) — it must not have, because the phase reset.
        while (clock.GetUtcNow() < Start + TimeSpan.FromMilliseconds(4_000))
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        client.Published.ShouldBeEmpty(
            "the old anchor's due time passed, but the path restarted before it — a cue firing here "
            + "would be reacting to a picture the clip is no longer showing");

        // Now advance to where the cue is due under the NEW anchor.
        while (clock.GetUtcNow() < restartedAt + TimeSpan.FromMilliseconds(4_000))
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        (await WaitUntilAsync(() => client.Published.Count >= 1)).ShouldBeTrue(
            "the cue never fired against the restarted anchor either");

        await harness.Service.StopAsync(CancellationToken.None);
        Directory.Delete(clipsDirectory, recursive: true);
    }

    [Fact]
    public async Task A_camera_sim_failure_on_one_path_does_not_stop_cues_on_another()
    {
        string clipsDirectory = CreateClipsDirectory(
            ("mill-roughing", 6_000, (2_000, "person", 0.9)),
            ("paper-packaging", 6_000, (2_000, "carton", 0.9)));

        MutableCameraSimHandler pathResponses = new();
        pathResponses.SetFailing("station-4-roughing");
        pathResponses.SetReady("paper-packaging", Start);

        FakeMqttClient client = new();
        ManualTimeProvider clock = new(Start);

        ScenarioOptions options = new()
        {
            Active = ["rolling-mill"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = new()
                {
                    Name = "Rolling Mill",
                    Assets =
                    [
                        AssetWithClip("station-4-roughing", "mill-roughing.mp4"),
                        AssetWithClip("paper-packaging", "paper-packaging.mp4"),
                    ],
                },
            },
        };

        await using Harness harness = Harness.Create(clipsDirectory, pathResponses, client, clock, options);

        await harness.Service.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => client.IsConnected)).ShouldBeTrue();

        // Two simultaneously-live per-path loops (the failing path's not-ready
        // retry timer and the healthy path's schedule-wait timer). Wait for
        // both to actually be pending before advancing — a fixed real-time
        // sleep here is reliable only when the thread pool happens to already
        // be warm (e.g. as part of a larger suite run) and flaky in isolation.
        for (int i = 0; i < 3; i++)
        {
            (await clock.WaitForPendingTimersAsync(2, TimeSpan.FromSeconds(5))).ShouldBeTrue(
                "both path loops never simultaneously reached a wait — one of them is stuck or crashed "
                + "before registering its next timer");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        (await WaitUntilAsync(() => client.Published.Any(
            payload => payload.Contains("carton", StringComparison.Ordinal)))).ShouldBeTrue(
            "the healthy path's cue never fired — a failure on the other path took the whole service down");

        await harness.Service.StopAsync(CancellationToken.None);
        Directory.Delete(clipsDirectory, recursive: true);
    }

    [Fact]
    public async Task Cues_run_for_every_active_scenario_not_only_the_animated_one()
    {
        string clipsDirectory = CreateClipsDirectory(
            ("mill-roughing", 6_000, (2_000, "person", 0.9)),
            ("electronics-inspection", 6_000, (2_000, "defect", 0.9)));

        MutableCameraSimHandler pathResponses = new();
        pathResponses.SetReady("station-4-roughing", Start);
        pathResponses.SetReady("electronics-inspection", Start);

        FakeMqttClient client = new();
        ManualTimeProvider clock = new(Start);

        ScenarioOptions options = new()
        {
            // "rolling-mill" is Active[0] (Animated); "electronics" is not, and
            // must still get its cues (FR-006, unlike the billet timeline).
            Active = ["rolling-mill", "electronics"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["rolling-mill"] = new()
                {
                    Name = "Rolling Mill",
                    Assets = [AssetWithClip("station-4-roughing", "mill-roughing.mp4")],
                },
                ["electronics"] = new()
                {
                    Name = "Electronics",
                    Assets = [AssetWithClip("electronics-inspection", "electronics-inspection.mp4")],
                },
            },
        };

        await using Harness harness = Harness.Create(clipsDirectory, pathResponses, client, clock, options);

        await harness.Service.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => client.IsConnected)).ShouldBeTrue();

        // Two simultaneously-live per-path loops (one per scenario). Wait for
        // both to actually be pending before advancing — see the identical
        // comment on A_camera_sim_failure_on_one_path_does_not_stop_cues_on_another.
        for (int i = 0; i < 3; i++)
        {
            (await clock.WaitForPendingTimersAsync(2, TimeSpan.FromSeconds(5))).ShouldBeTrue(
                "both path loops never simultaneously reached a wait — one of them is stuck or crashed "
                + "before registering its next timer");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        (await WaitUntilAsync(() => client.Published.Count >= 2)).ShouldBeTrue(
            "only one scenario's cue fired — the non-animated scenario's path was never scheduled");

        client.Published.ShouldContain(p => p.Contains("\"person\"", StringComparison.Ordinal));
        client.Published.ShouldContain(p => p.Contains("\"defect\"", StringComparison.Ordinal));

        await harness.Service.StopAsync(CancellationToken.None);
        Directory.Delete(clipsDirectory, recursive: true);
    }

    private static ScenarioOptions OneAssetScenario(string scenarioKey, string path, string clip) => new()
    {
        Active = [scenarioKey],
        Scenarios = new Dictionary<string, ScenarioDefinition>
        {
            [scenarioKey] = new() { Name = scenarioKey, Assets = [AssetWithClip(path, clip)] },
        },
    };

    private static AssetDefinition AssetWithClip(string path, string clip) => new()
    {
        Key = path,
        Name = path,
        Camera = new CameraDefinition { Path = path, Clip = clip },
    };

    /// <summary>Writes one <c>&lt;clip&gt;.cues.json</c> sidecar per tuple, each with a single cue.</summary>
    private static string CreateClipsDirectory(params (string ClipBaseName, int DurationMs, (int AtMs, string Class, double Confidence) Cue)[] clips)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sse-hosted-cues-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);

        foreach ((string clipBaseName, int durationMs, (int atMs, string @class, double confidence)) in clips)
        {
            File.WriteAllText(
                Path.Combine(directory, $"{clipBaseName}.cues.json"),
                $$"""
                {
                  "Clip": "{{clipBaseName}}.mp4",
                  "DurationMs": {{durationMs}},
                  "Cues": [ { "AtMs": {{atMs}}, "Class": "{{@class}}", "Confidence": {{confidence.ToString(CultureInfo.InvariantCulture)}} } ]
                }
                """);
        }

        return directory;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        }

        return condition();
    }

    /// <summary>Wires one <see cref="ClipCueHostedService"/> against fakes, owning their disposal.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly KeycloakTokenProvider tokens;
        private readonly MqttPublisher publisher;

        private Harness(ClipCueHostedService service, KeycloakTokenProvider tokens, MqttPublisher publisher)
        {
            Service = service;
            this.tokens = tokens;
            this.publisher = publisher;
        }

        public ClipCueHostedService Service { get; }

        public static Harness Create(
            string clipsDirectory,
            MutableCameraSimHandler pathResponses,
            FakeMqttClient mqttClient,
            ManualTimeProvider clock,
            ScenarioOptions options)
        {
            options.ClipsDirectory = clipsDirectory;

            SimulatorOptions simulatorOptions = new()
            {
                MqttHost = "mosquitto.test:1883",
                KeycloakUrl = "https://keycloak.test",
                ClientSecret = "a-secret",
                CameraSimApiUrl = "https://camera-sim.test",
            };

            KeycloakTokenProvider tokens = new(
                new FakeHttpClientFactory(new HttpClient(new StubKeycloak())),
                Options.Create(simulatorOptions),
                TimeProvider.System,
                NullLogger<KeycloakTokenProvider>.Instance);

            MqttPublisher publisher = new(
                Options.Create(simulatorOptions), tokens, NullLogger<MqttPublisher>.Instance, mqttClient);

            CameraSimPathClient pathClient = new(
                new HttpClient(pathResponses) { BaseAddress = new Uri("https://camera-sim.test") },
                NullLogger<CameraSimPathClient>.Instance);

            ClipCueHostedService service = new(
                Options.Create(options),
                pathClient,
                publisher,
                new MqttSampleMapper(clock),
                clock,
                NullLogger<ClipCueHostedService>.Instance);

            return new Harness(service, tokens, publisher);
        }

        public async ValueTask DisposeAsync()
        {
            await publisher.DisposeAsync();
            tokens.Dispose();
        }
    }

    /// <summary>Answers <c>GET /v3/paths/get/{path}</c> per path, changeable mid-test.</summary>
    private sealed class MutableCameraSimHandler : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly Dictionary<string, Func<HttpResponseMessage>> responders = new(StringComparer.Ordinal);

        public void SetReady(string path, DateTimeOffset readyTime) =>
            Set(path, () => Json(HttpStatusCode.OK, $$"""
                { "name": "{{path}}", "ready": true, "readyTime": "{{readyTime:O}}" }
                """));

        public void SetNotReady(string path) =>
            Set(path, () => Json(HttpStatusCode.OK, $$"""{ "name": "{{path}}", "ready": false, "readyTime": null }"""));

        public void SetFailing(string path) =>
            Set(path, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        private void Set(string path, Func<HttpResponseMessage> responder)
        {
            lock (gate)
            {
                responders[path] = responder;
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath["/v3/paths/get/".Length..];

            Func<HttpResponseMessage>? responder;
            lock (gate)
            {
                responders.TryGetValue(path, out responder);
            }

            return Task.FromResult(responder?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
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
                    Encoding.UTF8, "application/json"),
            });
        }
    }
}
