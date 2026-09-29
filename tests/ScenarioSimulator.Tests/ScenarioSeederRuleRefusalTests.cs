using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Issue B1 (backend-reviewer, spec 289 PR-C). spec.md US2's "unparseable value
/// expression" acceptance scenario: <c>Then Automation answers 400 and the
/// simulator logs the rule name and the error. And the other reactions still
/// seed.</c>
///
/// <para>
/// <b>Currently red.</b> <c>AutomationRulesClient.CreateOrResolveVersionAsync</c>
/// calls <c>EnsureSuccessStatusCode()</c> on a 400, which throws
/// <see cref="HttpRequestException"/> straight into <c>ScenarioSeeder</c>'s
/// per-asset <c>try</c>/<c>catch</c> (<c>ScenarioSeeder.cs</c> around line 84) —
/// that catch drops <b>every later reaction on the asset, and its camera
/// registration too</b>, not just the one refused rule. This class drives the
/// real, unmodified <c>ScenarioSeeder</c> the same way
/// <see cref="ScenarioSeederReactionTests"/> does, so what it observes is the
/// actual seeding path, not a hand-simplified stand-in.
/// </para>
///
/// <para>
/// Unlike <see cref="AutomationRulesClientTests.A_400_on_create_is_refused_without_throwing"/>
/// this test needs no new type to compile — it only exercises the existing,
/// public seeding path — so its red is a genuine assertion failure at runtime,
/// not a build break.
/// </para>
/// </summary>
public sealed class ScenarioSeederRuleRefusalTests
{
    private static readonly Guid Overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public async Task A_400_on_one_reactions_rule_create_does_not_drop_the_assets_other_reaction_or_its_camera_registration()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Reactions =
        [
            new ReactionDefinition
            {
                Name = "reaction-one", // its rule-create is refused (400)
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
            },
            new ReactionDefinition
            {
                Name = "reaction-two", // unrelated — must still seed
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'forklift'" },
                Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
            },
        ];

        SequencedAutomationHandler automation = new();
        RecordingCameraCatalogHandler cameras = new();
        RecordingLogger<ScenarioSeeder> log = new();

        await RunSeederAsync(OneAssetScenario(asset), automation, cameras, log);

        automation.Creates.Select(call => call.Body.Name).ShouldBe([
            "rolling-mill-station-4-roughing-reaction-one",
            "rolling-mill-station-4-roughing-reaction-two",
        ], "the first reaction's 400 must not stop the second from ever being attempted");

        automation.Publishes.ShouldContain(
            call => call.PathAndQuery == "/rules/rolling-mill-station-4-roughing-reaction-two/publish?fabId=munich",
            "the second reaction's rule must still be created and published");
        automation.Publishes.ShouldNotContain(
            call => call.PathAndQuery.StartsWith("/rules/rolling-mill-station-4-roughing-reaction-one/", StringComparison.Ordinal),
            "the refused rule must never be published");

        cameras.Registrations.ShouldContain(
            "station-4-roughing", "the asset's camera registration must still happen after a refused reaction");

        log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("reaction-one", StringComparison.Ordinal),
            "the refused rule's name must be logged (spec.md US2: \"the simulator logs the rule name and the error\")");
    }

    private static ScenarioOptions OneAssetScenario(AssetDefinition asset) => new()
    {
        Active = ["rolling-mill"],
        Scenarios = new Dictionary<string, ScenarioDefinition>
        {
            ["rolling-mill"] = new() { Name = "Rolling Mill", Assets = [asset] },
        },
    };

    private static AssetDefinition Asset(string key, string clip) => new()
    {
        Key = key,
        Name = key,
        Camera = new CameraDefinition { Path = key, Clip = clip },
        Overlay = new OverlayDefinition { Label = key, X = 0.1, Y = 0.05, Width = 0.8, Height = 0.18, FontSize = 24 },
        Tile = new TileDefinition { Row = 0, Col = 0 },
    };

    private static async Task RunSeederAsync(
        ScenarioOptions options,
        SequencedAutomationHandler automation,
        RecordingCameraCatalogHandler cameras,
        ILogger<ScenarioSeeder> log)
    {
        options.ClipsDirectory = string.Empty;
        IOptions<ScenarioOptions> wrapped = Options.Create(options);
        AssetCorrelationTable correlation = new();

        ScenarioSeeder seeder = new(
            new CameraCatalogClient(
                new HttpClient(cameras) { BaseAddress = new Uri("https://cameras.test") },
                Tokens(), Simulator(), NullLogger<CameraCatalogClient>.Instance),
            new OverlayDesignerClient(SucceedingOverlayHandler(), Tokens(), NullLogger<OverlayDesignerClient>.Instance),
            new AutomationRulesClient(
                new HttpClient(automation) { BaseAddress = new Uri("https://automation.test") },
                Tokens(), NullLogger<AutomationRulesClient>.Instance),
            new SystemVariablesClient(Failing(), Tokens(), NullLogger<SystemVariablesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            log);

        // StartAsync returns at the first await inside ExecuteAsync, so the run
        // continues on a background task (the same pattern every other
        // ScenarioSeeder test in this suite uses). Bounded wait: under today's
        // bug neither the second reaction nor the camera registration is ever
        // attempted, so there is no positive signal to wait on — only a cap
        // that gives the fixed behaviour room to complete.
        await seeder.StartAsync(CancellationToken.None);
        await WaitForSettledAsync(automation, cameras);
        await seeder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForSettledAsync(SequencedAutomationHandler automation, RecordingCameraCatalogHandler cameras)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (automation.Publishes.Any(publish => publish.PathAndQuery.Contains("reaction-two", StringComparison.Ordinal))
                && cameras.Registrations.Count > 0)
            {
                // One more beat so nothing still in flight is missed.
                await Task.Delay(100);
                return;
            }

            await Task.Delay(50);
        }
    }

    private static HttpClient Failing() =>
        new(new ThrowingHandler()) { BaseAddress = new Uri("https://unreachable.test") };

    private static HttpClient SucceedingOverlayHandler() =>
        new(new OverlayHandler(Overlay)) { BaseAddress = new Uri("https://overlays.test") };

    private static IOptions<SimulatorOptions> Simulator() =>
        Options.Create(new SimulatorOptions
        {
            KeycloakUrl = "https://keycloak.test",
            Realm = "smart-sentinel-eye",
            ClientId = "scenario-simulator",
            ClientSecret = "stub-secret",
        });

    private static KeycloakTokenProvider Tokens() =>
        new(new FakeHttpClientFactory(new HttpClient(new StubTokenHandler())), Simulator(), TimeProvider.System, NullLogger<KeycloakTokenProvider>.Instance);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record RuleBody(
        string Name, string TriggerSource, string TriggerKind, string Predicate, string ActionType,
        string? VariableName, string? ValueExpression, Guid? OverlayIdentifier, int? DurationMs);

    private sealed record CreateCall(string PathAndQuery, RuleBody Body);

    private sealed record PublishCall(string PathAndQuery, string IfMatch);

    /// <summary>
    /// Refuses the first <c>POST /rules</c> with a 400 (the unparseable value
    /// expression from spec.md US2's scenario), and creates + publishes every
    /// one after it — the shape "one bad reaction, the rest fine" needs.
    /// </summary>
    private sealed class SequencedAutomationHandler : HttpMessageHandler
    {
        private int createAttempts;

        public List<CreateCall> Creates { get; } = [];

        public List<PublishCall> Publishes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string pathAndQuery = request.RequestUri!.PathAndQuery;

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/rules")
            {
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                RuleBody body = JsonSerializer.Deserialize<RuleBody>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Rule create body did not deserialize.");

                int attempt = Interlocked.Increment(ref createAttempts);
                lock (Creates)
                {
                    Creates.Add(new CreateCall(pathAndQuery, body));
                }

                if (attempt == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            """{"title":"RULE_INVALID_INPUT","detail":"predicate does not parse as AEL: unexpected token '&' at position 12.","status":400}""",
                            Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/publish", StringComparison.Ordinal))
            {
                string ifMatch = request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values) : string.Empty;
                lock (Publishes)
                {
                    Publishes.Add(new PublishCall(pathAndQuery, ifMatch));
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Always succeeds, recording every camera name it was asked to register.</summary>
    private sealed class RecordingCameraCatalogHandler : HttpMessageHandler
    {
        public List<string> Registrations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/cameras")
            {
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                RegisterCameraBody body = JsonSerializer.Deserialize<RegisterCameraBody>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Camera register body did not deserialize.");
                lock (Registrations)
                {
                    Registrations.Add(body.Name);
                }

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent($"\"{Guid.CreateVersion7()}\"", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private sealed record RegisterCameraBody(string Name, string RtspUrl);
    }

    private sealed class OverlayHandler(Guid overlay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/overlays")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"\"{overlay}\"", Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("service did not answer in time"));
    }

    private sealed class StubTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""",
                    Encoding.UTF8, "application/json"),
            });
    }
}
