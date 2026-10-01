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
/// Spec 289 / PR-D, T-D02 (ADR-0144 red). Drives the real, unmodified
/// <c>ScenarioSeeder</c> the same way <see cref="LegacyHighlightRuleBodyTests"/>
/// and <see cref="ScenarioSeederReactionTests"/> do, so every pinned fact
/// below comes from the actual seeding path, not a hand-simplified stand-in.
/// Unlike T-D01's pure <c>ScenarioStoryCheckTests</c>, nothing here references
/// <c>Scenario/ScenarioStoryCheck.cs</c> by name — these facts are observable
/// purely through <see cref="ScenarioSeeder"/>'s existing public surface (the
/// wire body <see cref="AutomationRulesClient"/> sends, and the log), so this
/// class compiles today and fails at <b>run</b> time, for the right reason:
/// the warning the fact expects is not yet logged, or the wire body does not
/// yet carry the source plan.md §5.5 says it must.
///
/// <para>
/// <see cref="LegacyHighlightRuleBodyTests"/> already pins the 14 legacy/
/// reaction bodies (12 highlights + 2 reactions, #2698's renumbering) and
/// must keep passing <b>unmodified</b> once T-D03 lands (FR-009, SC-003) —
/// checked by re-running that file, not duplicated here.
/// </para>
/// </summary>
public sealed class ScenarioSeederStoryCheckTests
{
    private static readonly Guid Overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public async Task An_orphan_reaction_is_still_seeded_and_the_warning_is_logged_once()
    {
        AssetDefinition asset = Asset("coiler", "coiler.mp4");
        asset.Highlight = new HighlightDefinition
        {
            TriggerKind = "CoilWeight",
            Comparison = "gte",
            Threshold = 20,
            DurationMs = 4_000,
        };
        asset.Sensors =
        [
            new SensorDefinition { Kind = "CoilWeight", Unit = "kg", Behaviour = "steady", Source = "plc", Mean = 20 },
        ];
        asset.Reactions =
        [
            // Nothing on this asset ever emits inference/ObjectDetected: no sensor
            // does, and coiler.mp4 has no sidecar at all (not a refused one).
            new ReactionDefinition
            {
                Name = "person-in-exclusion",
                When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
            },
        ];

        RecordingAutomationHandler automation = new();
        RecordingLogger<ScenarioSeeder> log = new();
        await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory: string.Empty, log);

        // US3 (spec.md): "the reaction is still seeded (it is not the
        // simulator's place to refuse a rule a human can also trigger)".
        automation.Creates.Count.ShouldBe(2);
        automation.Creates.ShouldContain(call => call.Body.Name == "rolling-mill-coiler-person-in-exclusion");

        List<string> findingWarnings = RelevantWarnings(log, "coiler", "person-in-exclusion");
        findingWarnings.Count.ShouldBe(1, string.Join(" | ", findingWarnings));
        findingWarnings[0].ShouldContain("inference", Case.Insensitive);
        findingWarnings[0].ShouldContain("ObjectDetected", Case.Sensitive);
    }

    /// <summary>
    /// Decided 2026-10-01 (human, supervised gate): a refused-manifest
    /// reaction logs the existing <c>ReactionSkippedRefusedManifest</c>
    /// <b>and</b> the new <c>ScenarioReactionUnreachable</c> — two warnings
    /// stating two different facts, not deduped. Asserted by distinct-message
    /// count rather than the new warning's exact wording (T-D03's to choose),
    /// so this does not overspecify what the engineer must write.
    /// </summary>
    [Fact]
    public async Task A_refused_manifest_reaction_logs_both_the_existing_and_the_new_warning()
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
            asset.Sensors =
            [
                new SensorDefinition { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 },
            ];
            asset.Reactions =
            [
                new ReactionDefinition
                {
                    Name = "person-in-exclusion",
                    When = new ReactionTrigger { Source = "inference", Kind = "ObjectDetected", Predicate = "$.payload.class == 'person'" },
                    Then = new ReactionAction { Type = "HighlightOverlay", DurationMs = 4_000 },
                },
            ];

            RecordingAutomationHandler automation = new();
            RecordingLogger<ScenarioSeeder> log = new();
            await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory, log);

            // Only the legacy Highlight rule was seeded; the reaction is skipped
            // (pre-filter, PR-B), not created.
            automation.Creates.Count.ShouldBe(1);
            automation.Creates[0].Body.Name.ShouldBe("rolling-mill-station-4-roughing-highlight");

            List<string> findingWarnings = RelevantWarnings(log, "station-4-roughing", "person-in-exclusion");
            findingWarnings.Count.ShouldBe(2, string.Join(" | ", findingWarnings));
            findingWarnings.ShouldContain(message => message.Contains("refused", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    /// <summary>
    /// The miniature defect spec.md's US3 names: today this silently falls
    /// back to <c>plc</c>. FR-013 removes the silence, not the fallback value
    /// — the highlight is still seeded exactly as it would have been.
    /// </summary>
    [Fact]
    public async Task A_highlight_whose_kind_no_sensor_or_valid_cue_emits_is_seeded_with_plc_and_reported()
    {
        AssetDefinition asset = Asset("coiler", "coiler.mp4");
        asset.Highlight = new HighlightDefinition
        {
            TriggerKind = "CoilWeight",
            Comparison = "gte",
            Threshold = 20,
            DurationMs = 4_000,
        };
        asset.Sensors =
        [
            new SensorDefinition { Kind = "Humidity", Unit = "pct", Behaviour = "steady", Source = "plc", Mean = 50 },
        ];

        RecordingAutomationHandler automation = new();
        RecordingLogger<ScenarioSeeder> log = new();
        await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory: string.Empty, log);

        automation.Creates.Count.ShouldBe(1);
        automation.Creates[0].Body.TriggerSource.ShouldBe("plc");

        List<string> findingWarnings = RelevantWarnings(log, "coiler", "CoilWeight");
        findingWarnings.ShouldNotBeEmpty();
    }

    /// <summary>
    /// New fact (2026-10-01 gate amendment): plan.md §5.5's "sensors first,
    /// then cues" derivation. Nothing on this asset's sensors emits
    /// <c>ObjectDetected</c>, but its clip's sidecar carries a default-kind
    /// cue (<c>Kind=ObjectDetected</c>, <c>Source=inference</c>) — the
    /// highlight's seeded <c>TriggerSource</c> must be <c>inference</c>, not
    /// the silent <c>plc</c> default, and the highlight must <b>not</b> be
    /// reported, since it is in fact satisfied.
    /// </summary>
    [Fact]
    public async Task A_highlight_whose_kind_matches_no_sensor_but_matches_a_valid_cue_is_seeded_with_that_cues_source()
    {
        string clipsDirectory = CreateTempClipsDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(clipsDirectory, "coiler.cues.json"),
                """
                {
                  "Clip": "coiler.mp4",
                  "DurationMs": 20000,
                  "Cues": [ { "AtMs": 1000, "Class": "coil", "Confidence": 0.9 } ]
                }
                """);

            AssetDefinition asset = Asset("coiler", "coiler.mp4");
            asset.Highlight = new HighlightDefinition
            {
                TriggerKind = "ObjectDetected",
                Comparison = "gte",
                Threshold = 1,
                DurationMs = 4_000,
            };
            asset.Sensors =
            [
                new SensorDefinition { Kind = "CoilWeight", Unit = "kg", Behaviour = "steady", Source = "plc", Mean = 20 },
            ];

            RecordingAutomationHandler automation = new();
            RecordingLogger<ScenarioSeeder> log = new();
            await RunSeederAsync(OneAssetScenario(asset), automation, clipsDirectory, log);

            automation.Creates.Count.ShouldBe(1);
            automation.Creates[0].Body.TriggerSource.ShouldBe("inference");

            RelevantWarnings(log, "coiler", "ObjectDetected").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(clipsDirectory, recursive: true);
        }
    }

    /// <summary>Every warning naming both <paramref name="asset"/> and <paramref name="reactionOrKind"/>, de-duplicated by text.</summary>
    private static List<string> RelevantWarnings(RecordingLogger<ScenarioSeeder> log, string asset, string reactionOrKind) =>
        log.Entries
            .Where(entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(asset, StringComparison.Ordinal)
                && entry.Message.Contains(reactionOrKind, StringComparison.Ordinal))
            .Select(entry => entry.Message)
            .Distinct()
            .ToList();

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
        ScenarioOptions options, RecordingAutomationHandler automation, string clipsDirectory, ILogger<ScenarioSeeder> log)
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
            new SystemVariablesClient(Failing(), Tokens(), NullLogger<SystemVariablesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            log);

        int expectedCreates = options.Scenarios.Values.Sum(scenario =>
            scenario.Assets.Count(a => a.Highlight is not null)
            + scenario.Assets.Sum(a => a.Reactions?.Count ?? 0));

        await seeder.StartAsync(CancellationToken.None);
        await WaitForCreatesAsync(automation, expectedCreates);
        await seeder.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Waits for every create the scenario can produce — a refused-manifest
    /// reaction produces none, so its expected count already excludes it via
    /// the caller passing a lower <paramref name="atLeast"/>. The settle
    /// delay afterwards gives a warning-only outcome (no create at all, e.g.
    /// the orphan-reaction and refused-manifest facts) time to land before
    /// the caller inspects <see cref="RecordingLogger{T}.Entries"/>.
    /// </summary>
    private static async Task WaitForCreatesAsync(RecordingAutomationHandler automation, int atLeast)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (automation.Creates.Count >= atLeast)
            {
                await Task.Delay(100);
                return;
            }

            await Task.Delay(50);
        }
    }

    private static string CreateTempClipsDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sse-story-check-" + Guid.NewGuid());
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
