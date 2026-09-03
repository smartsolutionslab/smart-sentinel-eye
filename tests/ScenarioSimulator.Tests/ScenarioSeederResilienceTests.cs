using System.Net;
using System.Text;
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
/// Issue 1900. `ScenarioSeeder` is a `BackgroundService`, so an exception
/// escaping its loop trips `BackgroundServiceExceptionBehavior.StopHost` and
/// kills the whole simulator — the billet timeline, the CameraRegisteredV1
/// consumer and the rest of the pass — while every other service stays healthy
/// and the stack looks entirely fine.
///
/// <para>
/// It happened on a transient 30 s HTTP timeout during overlay seeding, and the
/// slow calls recur on every boot: only whether the retries fit the budget
/// varies. Two consecutive boots produced a dead worker and a healthy one from
/// the same condition.
/// </para>
///
/// <para>
/// The regression guard is the <b>summary line</b>. If the exception escapes
/// again, the run never reaches the end and that line is never written — so
/// asserting it is present is equivalent to asserting the loop survived.
/// </para>
/// </summary>
public sealed class ScenarioSeederResilienceTests
{
    [Fact]
    public async Task An_asset_that_throws_does_not_take_the_worker_down()
    {
        CapturingLogger<ScenarioSeeder> log = new();

        await RunAsync(log);

        // Reached the end of the pass rather than dying inside it.
        log.Warnings.ShouldContain(entry => entry.Contains("INCOMPLETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_failing_asset_is_named_rather_than_silently_skipped()
    {
        CapturingLogger<ScenarioSeeder> log = new();

        await RunAsync(log);

        // Two assets configured, both failing: each says which scenario and which
        // asset. A count-only summary would leave the reader guessing which tile
        // is missing from the wall.
        log.Warnings.Count(entry => entry.Contains("could not be seeded", StringComparison.Ordinal))
            .ShouldBe(2);
        log.Warnings.ShouldContain(entry => entry.Contains("station-4", StringComparison.Ordinal));
        log.Warnings.ShouldContain(entry => entry.Contains("station-7", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs the seeder against collaborators whose HTTP calls all fail, which is
    /// what a service too slow to answer inside its budget looks like from here.
    /// </summary>
    private static async Task RunAsync(CapturingLogger<ScenarioSeeder> log)
    {
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
                        Asset("station-4"),
                        Asset("station-7"),
                    ],
                },
            },
        };

        AssetCorrelationTable correlation = new();
        IOptions<ScenarioOptions> wrapped = Options.Create(options);

        ScenarioSeeder seeder = new(
            new CameraCatalogClient(Failing(), Tokens(), Simulator(), NullLogger<CameraCatalogClient>.Instance),
            new OverlayDesignerClient(Failing(), Tokens(), NullLogger<OverlayDesignerClient>.Instance),
            new AutomationRulesClient(Failing(), Tokens(), NullLogger<AutomationRulesClient>.Instance),
            correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(Failing(), Tokens(), NullLogger<LayoutCompositionClient>.Instance),
                correlation,
                wrapped,
                NullLogger<WallSeeder>.Instance),
            log);

        // StartAsync returns at the first await inside ExecuteAsync, so the run
        // continues on a background task — exactly as it does in the host, which
        // is the point. Stop after giving it room to finish.
        await seeder.StartAsync(CancellationToken.None);
        await WaitForSummaryAsync(log);
        await seeder.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForSummaryAsync(CapturingLogger<ScenarioSeeder> log)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (log.Warnings.Any(entry => entry.Contains("INCOMPLETE", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private static AssetDefinition Asset(string key) => new()
    {
        Key = key,
        Name = key,
        Camera = new CameraDefinition { Path = key, Clip = $"{key}.mp4" },
        Overlay = new OverlayDefinition { Label = key, X = 0.1, Y = 0.1, Width = 0.5, Height = 0.2, FontSize = 24 },
        Tile = new TileDefinition { Row = 0, Col = 0 },
    };

    /// <summary>Every request fails — the shape of a service that cannot answer in time.</summary>
    private static HttpClient Failing() =>
        new(new ThrowingHandler()) { BaseAddress = new Uri("https://unreachable.test") };

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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (warnings)
                {
                    return warnings.ToArray();
                }
            }
        }

        /// <summary>The seeder opens no scopes; this exists only to satisfy ILogger.</summary>
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            lock (warnings)
            {
                warnings.Add(formatter(state, exception));
            }
        }
    }
}

/// <summary>
/// Shared no-op scope. Outside the generic logger on purpose: a static field in
/// a generic type is one per closed type, which SonarAnalyzer flags and which
/// would be a real bug if the field held anything.
/// </summary>
internal sealed class Scope : IDisposable
{
    public static readonly Scope Instance = new();

    public void Dispose()
    {
    }
}
