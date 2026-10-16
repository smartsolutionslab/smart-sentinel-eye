using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.EventHandlers;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>
/// The real <see cref="CameraRegisteredSimHandler"/> over a recording camera-sim, with the
/// handler's and the provisioner's loggers writing to one recorder. No production seam: every
/// collaborator is built through the public constructor, as the seeder rig does.
/// </summary>
internal sealed class SimHandlerRig
{
    private const string AlreadyExists = "path already exists";

    private readonly CameraRegisteredSimHandler handler;

    public SimHandlerRig(ScenarioOptions? scenarios = null, bool cameraSimAlreadyHasThePath = false)
    {
        var options = Options.Create(scenarios ?? new ScenarioOptions());

        CameraSim = new RigHandler(
            cameraSimAlreadyHasThePath ? AddIsRefused : null,
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        var provisioner = new CameraSimProvisioner(
            new HttpClient(CameraSim) { BaseAddress = new Uri("http://camera-sim:9997") },
            Log.CreateLogger<CameraSimProvisioner>());

        var layouts = new LayoutCompositionClient(
            new HttpClient(new RigHandler(null, _ => throw new HttpRequestException("layouts unreachable")))
            {
                BaseAddress = new Uri("https://layouts.test"),
            },
            Tokens(),
            Log.CreateLogger<LayoutCompositionClient>());

        var correlation = new AssetCorrelationTable();

        handler = new CameraRegisteredSimHandler(
            provisioner,
            correlation,
            new WallSeeder(layouts, correlation, options, Log.CreateLogger<WallSeeder>()),
            options,
            Log.CreateLogger<CameraRegisteredSimHandler>());
    }

    public RecordingLoggerProvider Log { get; } = new();

    public RigHandler CameraSim { get; }

    public Task HandleAsync(Guid camera, string name, string url)
    {
        CameraRegisteredV1 message = new(
            camera,
            name,
            url,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid(), DateTimeOffset.UtcNow, null, null));

        return handler.Handle(message, CancellationToken.None);
    }

    private static HttpResponseMessage? AddIsRefused(RigRequest request) =>
        request.Path.StartsWith("/v3/config/paths/add/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(AlreadyExists, Encoding.UTF8) }
            : null;

    private static KeycloakTokenProvider Tokens() =>
        new(
            new FakeHttpClientFactory(new HttpClient(new RigHandler(
                null,
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""",
                        Encoding.UTF8,
                        "application/json"),
                }))),
            Options.Create(new SimulatorOptions
            {
                KeycloakUrl = "https://keycloak.test",
                Realm = "smart-sentinel-eye",
                ClientId = "scenario-simulator",
                ClientSecret = "stub-secret",
            }),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);
}
