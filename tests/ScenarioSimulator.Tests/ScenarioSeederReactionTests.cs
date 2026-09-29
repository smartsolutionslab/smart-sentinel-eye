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
/// Spec 289 / PR-B, T-B08 (ADR-0144 red). <c>AssetDefinition.Reactions</c> and
/// <c>ScenarioSeeder</c>'s reaction-seeding pass do not exist yet; this class
/// is the target shape (plan.md §5.4, T-B14). Drives the real, unmodified
/// <c>ScenarioSeeder</c> the same way <c>LegacyHighlightRuleBodyTests</c> and
/// <c>ScenarioSeederResilienceTests</c> do, so the pins here come from the
/// actual seeding path.
///
/// <para>
/// <see cref="LegacyHighlightRuleBodyTests"/> already pins the 12 legacy
/// bodies and must keep passing <b>unmodified</b> once this PR lands
/// (FR-009, SC-003) — that is checked by re-running that file, not by
/// duplicating it here.
/// </para>
/// </summary>
public sealed class ScenarioSeederReactionTests
{
    private static readonly Guid Overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public async Task A_declared_HighlightOverlay_reaction_produces_one_extra_create_then_publish_with_the_wrapped_predicate()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Highlight = new HighlightDefinition
        {
            TriggerKind = "Temperature",
            Comparison = "gte",
            Threshold = 1_100,
            DurationMs = 4_000,
        };
        asset.Sensors = [new SensorDefinition { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 }];
        asset.Reactions =
        [
            new ReactionDefinition
            {
                Name = "person-in-exclusion",
                When = new ReactionTrigger
                {
                    Source = "inference", Kind = "ObjectDetected",
                    Predicate = "$.payload.class == 'person' && $.payload.confidence >= 0.8",
                },
                Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
            },
        ];

        RecordingAutomationHandler automation = new();
        await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory: string.Empty);

        // One create for the legacy Highlight rule, one for the declared reaction.
        automation.Creates.Count.ShouldBe(2);

        CreateCall reactionCreate = automation.Creates
            .Single(call => call.Body.Name == "rolling-mill-station-4-roughing-person-in-exclusion");
        reactionCreate.Body.TriggerSource.ShouldBe("inference");
        reactionCreate.Body.TriggerKind.ShouldBe("ObjectDetected");
        reactionCreate.Body.Predicate.ShouldBe(
            "$.device == 'station-4-roughing' && "
            + "($.payload.class == 'person' && $.payload.confidence >= 0.8)");
        reactionCreate.Body.ActionType.ShouldBe("HighlightOverlay");
        reactionCreate.Body.OverlayIdentifier.ShouldBe(Overlay);

        automation.Publishes.ShouldContain(
            call => call.PathAndQuery == "/rules/rolling-mill-station-4-roughing-person-in-exclusion/publish?fabId=munich");
    }

    [Fact]
    public async Task A_refused_reaction_is_logged_by_name_and_the_assets_other_rules_still_seed()
    {
        AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
        asset.Highlight = new HighlightDefinition
        {
            TriggerKind = "Temperature",
            Comparison = "gte",
            Threshold = 1_100,
            DurationMs = 4_000,
        };
        asset.Sensors = [new SensorDefinition { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 }];
        asset.Reactions =
        [
            new ReactionDefinition
            {
                Name = "unrecognised-action",
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                Then = new ReactionAction { Type = "SwitchWallScene" }, // not a supported action in PR-B
            },
        ];

        RecordingAutomationHandler automation = new();
        RecordingLogger<ScenarioSeeder> log = new();
        await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory: string.Empty, log);

        // Only the legacy Highlight rule was seeded; the refused reaction created nothing.
        automation.Creates.Count.ShouldBe(1);
        automation.Creates[0].Body.Name.ShouldBe("rolling-mill-station-4-roughing-highlight");

        log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("unrecognised-action", StringComparison.Ordinal));
    }

    /// <summary>
    /// US1's "malformed manifest" acceptance scenario (spec.md §2, US1): a
    /// refused sidecar must not silently seed a rule that can never fire
    /// because nothing — no sensor, no cue — will ever emit its trigger.
    /// </summary>
    [Fact]
    public async Task A_reaction_whose_trigger_only_a_cue_could_satisfy_is_skipped_when_the_clips_sidecar_is_refused()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            // Refused: AtMs (25000) is past the declared 20000ms duration.
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "mill-roughing.cues.json"),
                """
                {
                  "Clip": "mill-roughing.mp4",
                  "DurationMs": 20000,
                  "Cues": [ { "AtMs": 25000, "Class": "person", "Confidence": 0.9 } ]
                }
                """);

            AssetDefinition asset = Asset("station-4-roughing", "mill-roughing.mp4");
            asset.Highlight = new HighlightDefinition
            {
                TriggerKind = "Temperature",
                Comparison = "gte",
                Threshold = 1_100,
                DurationMs = 4_000,
            };
            asset.Sensors = [new SensorDefinition { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 }];
            asset.Reactions =
            [
                // Nothing on this asset emits inference/ObjectDetected: no sensor does,
                // and the only source of it — the clip's sidecar — was refused.
                new ReactionDefinition
                {
                    Name = "person-in-exclusion",
                    When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                    Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
                },
            ];

            RecordingAutomationHandler automation = new();
            await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory);

            // Only the legacy Highlight rule (satisfied by the Temperature sensor)
            // was seeded; the cue-only reaction was skipped, not created.
            automation.Creates.Count.ShouldBe(1);
            automation.Creates[0].Body.Name.ShouldBe("rolling-mill-station-4-roughing-highlight");
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
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
        ScenarioOptions options, RecordingAutomationHandler automation, string clipsDirectory, ILogger<ScenarioSeeder>? log = null)
    {
        options.ClipsDirectory = clipsDirectory;
        IOptions<ScenarioOptions> wrapped = Options.Create(options);
        AssetCorrelationTable correlation = new();

        ScenarioSeeder seeder = new(
            new CameraCatalogClient(Failing(), Tokens(), Simulator(), NullLogger<CameraCatalogClient>.Instance),
            new OverlayDesignerClient(SucceedingOverlayHandler(), Tokens(), NullLogger<OverlayDesignerClient>.Instance),
            new AutomationRulesClient(
                new HttpClient(automation) { BaseAddress = new Uri("https://automation.test") },
                Tokens(), NullLogger<AutomationRulesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            log ?? NullLogger<ScenarioSeeder>.Instance);

        int expectedCreates = options.Scenarios.Values.Sum(scenario =>
            scenario.Assets.Count(a => a.Highlight is not null)
            + scenario.Assets.Sum(a => a.Reactions?.Count ?? 0));

        await seeder.StartAsync(CancellationToken.None);
        await WaitForCreatesAsync(automation, expectedCreates);
        await seeder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForCreatesAsync(RecordingAutomationHandler automation, int atLeast)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (automation.Creates.Count >= atLeast)
            {
                // One more beat so a refused reaction's warning (which produces no
                // create) has had time to be logged too.
                await Task.Delay(50);
                return;
            }

            await Task.Delay(50);
        }
    }

    private static string CreateTempClipsDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sse-seeder-cues-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        return directory;
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

    private sealed class RecordingAutomationHandler : HttpMessageHandler
    {
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
                lock (Creates)
                {
                    Creates.Add(new CreateCall(pathAndQuery, body));
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
