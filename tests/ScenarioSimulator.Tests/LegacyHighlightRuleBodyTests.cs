using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-A (ADR-0144 characterisation). Pins the exact wire body of the
/// 12 legacy <c>Highlight</c> rules the three shipped scenarios seed today,
/// <b>before</b> <c>AutomationRulesClient.EnsureRuleAsync</c> is refactored from
/// its current 9 parameters to a <c>RuleSeed</c> (plan.md §3.4, §7). Every
/// expected value below is a literal copied by hand from
/// <c>Scenarios/{rolling-mill,paper-mill,electronics}.json</c> and the current
/// <c>SeedOverlayAndRuleAsync</c> / <c>AutomationRulesClient.Operator</c>
/// mapping, never read back from the code under test (memory: <i>an assertion
/// must not check its own input</i>). This class must still pass, unmodified,
/// once PR-A's refactor lands (FR-009, SC-003).
/// </summary>
public sealed class LegacyHighlightRuleBodyTests
{
    [Fact]
    public async Task Every_asset_in_the_three_shipped_scenarios_seeds_its_legacy_highlight_rule_with_the_exact_wire_body()
    {
        RecordingAutomationHandler automation = new();
        Guid overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

        await SeedAsync(automation, overlay);

        automation.Creates.Count.ShouldBe(12);
        automation.Publishes.Count.ShouldBe(12);

        AssertRule(automation, "rolling-mill-station-4-roughing-highlight", "plc", "Temperature",
            "$.device == 'station-4-roughing' && $.payload.value >= 1100", overlay, 4000);
        AssertRule(automation, "rolling-mill-station-7-finishing-highlight", "plc", "StripSpeed",
            "$.device == 'station-7-finishing' && $.payload.value >= 8", overlay, 4000);
        AssertRule(automation, "rolling-mill-cooling-bed-highlight", "plc", "Temperature",
            "$.device == 'cooling-bed' && $.payload.value <= 700", overlay, 4000);
        AssertRule(automation, "rolling-mill-coiler-highlight", "plc", "CoilWeight",
            "$.device == 'coiler' && $.payload.value >= 20", overlay, 4000);

        AssertRule(automation, "paper-mill-pressure-screener-highlight", "plc", "DifferentialPressure",
            "$.device == 'paper-pressure-screener' && $.payload.value >= 1.4", overlay, 4000);
        AssertRule(automation, "paper-mill-press-group-highlight", "plc", "NipLoad",
            "$.device == 'paper-press-group' && $.payload.value >= 90", overlay, 4000);
        AssertRule(automation, "paper-mill-after-drying-highlight", "plc", "Moisture",
            "$.device == 'paper-after-drying' && $.payload.value >= 7.5", overlay, 4000);
        AssertRule(automation, "paper-mill-packaging-highlight", "plc", "PalletCount",
            "$.device == 'paper-packaging' && $.payload.value >= 12", overlay, 4000);

        AssertRule(automation, "electronics-moulding-highlight", "plc", "CycleTime",
            "$.device == 'electronics-moulding' && $.payload.value >= 42", overlay, 4000);
        AssertRule(automation, "electronics-smd-line-highlight", "plc", "PlacementRate",
            "$.device == 'electronics-smd-line' && $.payload.value <= 14000", overlay, 4000);
        AssertRule(automation, "electronics-conveyor-highlight", "plc", "JamCount",
            "$.device == 'electronics-conveyor' && $.payload.value >= 2", overlay, 5000);
        AssertRule(automation, "electronics-inspection-highlight", "plc", "RejectCount",
            "$.device == 'electronics-inspection' && $.payload.value >= 5", overlay, 4000);
    }

    /// <summary>
    /// F-2 (plan.md §9): an unmapped comparison silently falls back to <c>&gt;=</c>.
    /// This is a known defect, characterised deliberately so a later behaviour
    /// fix (out of PR-A's scope) changes this fact on purpose rather than by
    /// accident.
    /// </summary>
    [Fact]
    public async Task An_unknown_comparison_operator_silently_defaults_to_greater_than_or_equal()
    {
        RecordingAutomationHandler automation = new();
        Guid overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

        ScenarioOptions options = new()
        {
            Active = ["odd-plant"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["odd-plant"] = new()
                {
                    Name = "Odd Plant",
                    Assets =
                    [
                        new AssetDefinition
                        {
                            Key = "odd-station",
                            Name = "Odd Station",
                            Camera = new CameraDefinition { Path = "odd-station", Clip = "odd.mp4" },
                            Overlay = new OverlayDefinition
                            {
                                Label = "ODD", X = 0.1, Y = 0.1, Width = 0.5, Height = 0.2, FontSize = 24,
                            },
                            Tile = new TileDefinition { Row = 0, Col = 0 },
                            Highlight = new HighlightDefinition
                            {
                                TriggerKind = "Oddity",
                                Comparison = "between",
                                Threshold = 5,
                                DurationMs = 1000,
                            },
                            Sensors =
                            [
                                new SensorDefinition { Kind = "Oddity", Unit = "unit", Behaviour = "steady", Source = "plc", Mean = 5 },
                            ],
                        },
                    ],
                },
            },
        };

        await RunSeederAsync(options, automation, overlay);

        automation.Creates.Count.ShouldBe(1);
        automation.Creates[0].Body.Predicate.ShouldBe(
            "$.device == 'odd-station' && $.payload.value >= 5");
    }

    /// <summary>
    /// The 12 pinned assets above are all sourced <c>"plc"</c>, so that fact alone
    /// cannot tell a real sensor-source lookup from a hardcoded <c>"plc"</c>
    /// literal. This proves the lookup itself: one asset's sensor source
    /// (<c>"camera"</c>) is carried through when it matches the highlight's
    /// trigger kind, and a second asset with no matching sensor falls back to
    /// <c>"plc"</c>.
    /// </summary>
    [Fact]
    public async Task A_sensor_source_matching_the_trigger_kind_is_used_and_an_unmatched_trigger_kind_falls_back_to_plc()
    {
        RecordingAutomationHandler automation = new();
        Guid overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

        ScenarioOptions options = new()
        {
            Active = ["mixed-source-plant"],
            Scenarios = new Dictionary<string, ScenarioDefinition>
            {
                ["mixed-source-plant"] = new()
                {
                    Name = "Mixed Source Plant",
                    Assets =
                    [
                        new AssetDefinition
                        {
                            Key = "vision-station",
                            Name = "Vision Station",
                            Camera = new CameraDefinition { Path = "vision-station", Clip = "vision.mp4" },
                            Overlay = new OverlayDefinition
                            {
                                Label = "VISION", X = 0.1, Y = 0.1, Width = 0.5, Height = 0.2, FontSize = 24,
                            },
                            Tile = new TileDefinition { Row = 0, Col = 0 },
                            Highlight = new HighlightDefinition
                            {
                                TriggerKind = "DefectCount",
                                Comparison = "gte",
                                Threshold = 3,
                                DurationMs = 1000,
                            },
                            Sensors =
                            [
                                new SensorDefinition { Kind = "DefectCount", Unit = "unit", Behaviour = "steady", Source = "camera", Mean = 3 },
                            ],
                        },
                        new AssetDefinition
                        {
                            Key = "unsensed-station",
                            Name = "Unsensed Station",
                            Camera = new CameraDefinition { Path = "unsensed-station", Clip = "unsensed.mp4" },
                            Overlay = new OverlayDefinition
                            {
                                Label = "UNSENSED", X = 0.1, Y = 0.1, Width = 0.5, Height = 0.2, FontSize = 24,
                            },
                            Tile = new TileDefinition { Row = 0, Col = 1 },
                            Highlight = new HighlightDefinition
                            {
                                TriggerKind = "Temperature",
                                Comparison = "gte",
                                Threshold = 100,
                                DurationMs = 1000,
                            },
                            Sensors =
                            [
                                new SensorDefinition { Kind = "Humidity", Unit = "unit", Behaviour = "steady", Source = "plc", Mean = 50 },
                            ],
                        },
                    ],
                },
            },
        };

        await RunSeederAsync(options, automation, overlay);

        automation.Creates.Count.ShouldBe(2);
        automation.Creates.Single(call => call.Body.Name == "mixed-source-plant-vision-station-highlight")
            .Body.TriggerSource.ShouldBe("camera");
        automation.Creates.Single(call => call.Body.Name == "mixed-source-plant-unsensed-station-highlight")
            .Body.TriggerSource.ShouldBe("plc");
    }

    private static void AssertRule(
        RecordingAutomationHandler automation,
        string name,
        string triggerSource,
        string triggerKind,
        string predicate,
        Guid overlay,
        int durationMs)
    {
        CreateCall create = automation.Creates.Where(call => call.Body.Name == name).ShouldHaveSingleItem();

        create.PathAndQuery.ShouldBe("/rules?fabId=munich");
        create.Body.ShouldBe(new RuleBody(
            name, triggerSource, triggerKind, predicate, "HighlightOverlay", null, null, overlay, durationMs));

        PublishCall publish = automation.Publishes
            .Where(call => call.PathAndQuery == $"/rules/{name}/publish?fabId=munich")
            .ShouldHaveSingleItem();
        publish.IfMatch.ShouldBe("\"0\"");
    }

    /// <summary>
    /// Drives the real, unmodified <c>ScenarioSeeder</c> across all three shipped
    /// scenario files, exactly as production wires it, so the pinned bodies come
    /// from the actual seeding path rather than a hand-simplified stand-in.
    /// Camera-catalog and layout-composition calls are made to fail: both
    /// failures are caught internally (the per-asset try/catch in
    /// <c>ScenarioSeeder.ExecuteAsync</c>, and <c>WallSeeder</c>'s own guard) and
    /// never reach the rule-seeding path this test pins.
    /// </summary>
    private static Task SeedAsync(RecordingAutomationHandler automation, Guid overlay) =>
        RunSeederAsync(LoadShippedScenarios(), automation, overlay);

    private static async Task RunSeederAsync(ScenarioOptions options, RecordingAutomationHandler automation, Guid overlay)
    {
        IOptions<ScenarioOptions> wrapped = Options.Create(options);
        IOptions<SimulatorOptions> simulator = Simulator();
        AssetCorrelationTable correlation = new();

        ScenarioSeeder seeder = new(
            new CameraCatalogClient(Failing(), Tokens(), simulator, NullLogger<CameraCatalogClient>.Instance),
            new OverlayDesignerClient(SucceedingOverlayHandler(overlay), Tokens(), NullLogger<OverlayDesignerClient>.Instance),
            new AutomationRulesClient(new HttpClient(automation) { BaseAddress = new Uri("https://automation.test") },
                Tokens(), NullLogger<AutomationRulesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            NullLogger<ScenarioSeeder>.Instance);

        // StartAsync returns at the first await inside ExecuteAsync and the run
        // continues on a background task (the same pattern
        // ScenarioSeederResilienceTests uses), so wait for every expected create
        // to have landed before asserting.
        await seeder.StartAsync(CancellationToken.None);
        await WaitForCreatesAsync(automation, options.Scenarios.Values.Sum(scenario =>
            scenario.Assets.Count(asset => asset.Highlight is not null)));
        await seeder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForCreatesAsync(RecordingAutomationHandler automation, int expected)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (automation.Publishes.Count >= expected)
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Loads the real <c>Scenarios/*.json</c> files the same way
    /// <c>ScenarioFileTests.Load</c> and production's own configuration binding
    /// do, so the 12 pinned assets are exactly what ships.
    /// </summary>
    private static ScenarioOptions LoadShippedScenarios()
    {
        string scenarios = Path.Combine(AppContext.BaseDirectory, "Scenarios");

        IConfigurationBuilder builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);

        foreach (string file in Directory.EnumerateFiles(scenarios, "*.json"))
        {
            builder.AddJsonFile(file, optional: false);
        }

        ScenarioOptions options = new();
        builder.Build().GetSection(ScenarioOptions.SectionName).Bind(options);
        return options;
    }

    private static IOptions<SimulatorOptions> Simulator() =>
        Options.Create(new SimulatorOptions
        {
            KeycloakUrl = "https://keycloak.test",
            Realm = "smart-sentinel-eye",
            ClientId = "scenario-simulator",
            ClientSecret = "stub-secret",
        });

    private static KeycloakTokenProvider Tokens() =>
        new(
            new FakeHttpClientFactory(new HttpClient(new StubTokenHandler())),
            Simulator(),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);

    /// <summary>Every request fails — camera-catalog and layout-composition are not what this test pins.</summary>
    private static HttpClient Failing() =>
        new(new ThrowingHandler()) { BaseAddress = new Uri("https://unreachable.test") };

    /// <summary>
    /// Always creates + publishes with the given fixed identifier, so every
    /// asset's rule body carries a known, literal overlay id.
    /// </summary>
    private static HttpClient SucceedingOverlayHandler(Guid overlay) =>
        new(new OverlayHandler(overlay)) { BaseAddress = new Uri("https://overlays.test") };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Just the fields the seeder puts on the wire, deserialized independently of the private <c>CreateRuleBody</c> the client actually sends.</summary>
    private sealed record RuleBody(
        string Name,
        string TriggerSource,
        string TriggerKind,
        string Predicate,
        string ActionType,
        string? VariableName,
        string? ValueExpression,
        Guid? OverlayIdentifier,
        int? DurationMs);

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
                    ? string.Join(",", values)
                    : string.Empty;

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
                    Encoding.UTF8,
                    "application/json"),
            });
    }
}
