using System.Net;
using System.Net.Http;
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
/// S1 (backend-reviewer, spec 289 PR-C). Variables now seed before the
/// overlay, the legacy highlight rule, every reaction and the camera
/// registration (plan.md §5.4) — so an unhandled exception from
/// <see cref="SystemVariablesClient.EnsureVariableAsync"/> (a transient I/O
/// fault, distinct from the 400/409 the client already turns into a
/// <see cref="VariableSeedResult"/> without throwing) must not propagate up
/// to <c>ScenarioSeeder</c>'s per-asset <c>catch</c>: that would cost the
/// whole asset — its overlay, its legacy highlight rule and its camera
/// registration — over one variable, which is a bigger blast radius than
/// plan.md §5.4 intended (it only ever asked that the <em>dependent
/// reaction</em> be skipped). Drives the real, unmodified
/// <c>ScenarioSeeder</c>, the same way <see cref="ScenarioSeederVariableTests"/>
/// and <see cref="ScenarioSeederRuleRefusalTests"/> do.
/// </summary>
public sealed class ScenarioSeederVariableFailureTests
{
    [Fact]
    public async Task A_variables_IO_failure_does_not_lose_the_assets_overlay_highlight_rule_or_camera_registration()
    {
        AssetDefinition asset = new()
        {
            Key = "station-9-forging",
            Name = "station-9-forging",
            Camera = new CameraDefinition { Path = "station-9-forging", Clip = "forging.mp4" },
            Overlay = new OverlayDefinition { Label = "station-9-forging", X = 0.1, Y = 0.05, Width = 0.8, Height = 0.18, FontSize = 24 },
            Tile = new TileDefinition { Row = 0, Col = 0 },
            Highlight = new HighlightDefinition
            {
                TriggerKind = "Temperature",
                Comparison = "gte",
                Threshold = 1_100,
                DurationMs = 4_000,
            },
            Sensors = [new SensorDefinition { Kind = "Temperature", Unit = "degC", Behaviour = "steady", Source = "plc", Mean = 1_100 }],
            Variables = [new VariableDefinition { Name = "forging_zone_state", Type = "String" }],
        };

        ThrowingVariablesHandler variablesHandler = new();
        RecordingAutomationHandler automation = new();
        RecordingCameraCatalogHandler cameras = new();
        RecordingLogger<ScenarioSeeder> log = new();

        await RunSeederAsync(OneAssetScenario(asset), automation, variablesHandler, cameras, log);

        automation.Creates.ShouldContain(
            call => call.Body.Name == "rolling-mill-station-9-forging-highlight",
            "the variable's I/O failure must not cost the asset its legacy highlight rule");
        automation.Publishes.ShouldContain(
            call => call.PathAndQuery == "/rules/rolling-mill-station-9-forging-highlight/publish?fabId=munich");

        cameras.Registrations.ShouldContain(
            "station-9-forging", "the asset's camera registration must still happen after a failed variable seed");

        log.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("forging_zone_state", StringComparison.Ordinal),
            "the failed variable must be named in a warning, not swallowed silently");
    }

    private static ScenarioOptions OneAssetScenario(AssetDefinition asset) => new()
    {
        Active = ["rolling-mill"],
        Scenarios = new Dictionary<string, ScenarioDefinition>
        {
            ["rolling-mill"] = new() { Name = "Rolling Mill", Assets = [asset] },
        },
    };

    private static async Task RunSeederAsync(
        ScenarioOptions options,
        RecordingAutomationHandler automation,
        ThrowingVariablesHandler variablesHandler,
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
            new SystemVariablesClient(
                new HttpClient(variablesHandler) { BaseAddress = new Uri("https://system-variables.test") },
                Tokens(), NullLogger<SystemVariablesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            log);

        // StartAsync returns at the first await inside ExecuteAsync; the run
        // continues on a background task (the same pattern every other
        // ScenarioSeeder test in this suite uses). Under today's bug the
        // variable's exception trips the per-asset catch before the highlight
        // rule or the camera registration are ever attempted, so there is no
        // positive signal to wait on beyond a cap that gives the fixed
        // behaviour room to complete.
        await seeder.StartAsync(CancellationToken.None);
        await WaitForSettledAsync(automation, cameras);
        await seeder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForSettledAsync(RecordingAutomationHandler automation, RecordingCameraCatalogHandler cameras)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (automation.Publishes.Count > 0 && cameras.Registrations.Count > 0)
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
        new(new OverlayHandler(Guid.Parse("00000000-0000-0000-0000-0000000000bb"))) { BaseAddress = new Uri("https://overlays.test") };

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

    /// <summary>Throws on every call — the transient-I/O-fault shape S1 targets, distinct from a 400/409.</summary>
    private sealed class ThrowingVariablesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("system-variables did not answer in time"));
    }

    /// <summary>Always succeeds, recording every rule create + publish.</summary>
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
