using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>Spec 390 / #2976, US3: the simulator's own registration line names the origin only.</summary>
public sealed class CameraCatalogClientLogRedactionTests
{
    [Fact]
    public async Task Registering_a_scenario_camera_logs_the_origin_not_the_path()
    {
        var camera = Guid.CreateVersion7();
        RigHandler catalog = new(null, _ => Json(HttpStatusCode.Created, $"\"{camera}\""));
        RecordingLoggerProvider log = new();
        var options = Options.Create(new SimulatorOptions { RtspHost = "rtsp.test" });

        var registered = await new CameraCatalogClient(
                new HttpClient(catalog) { BaseAddress = new Uri("https://cameras.test") },
                Tokens(),
                options,
                log.CreateLogger<CameraCatalogClient>())
            .RegisterCameraAsync("Station 4", "secret-path", CancellationToken.None);

        // Behaviour that must not move: the body still carries the full URL.
        registered.ShouldBe(camera);
        catalog.Requests.Single(request => request.Path == "/cameras").Body.ShouldContain("rtsp://rtsp.test/secret-path");

        log.Entries.Select(entry => entry.Message).ShouldContain("Registered camera 'Station 4' -> rtsp://rtsp.test.", log.Dump);
        log.Dump.ShouldNotContain("secret-path", customMessage: log.Dump);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static KeycloakTokenProvider Tokens() =>
        new(
            new FakeHttpClientFactory(new HttpClient(new RigHandler(
                null,
                _ => Json(HttpStatusCode.OK, """{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""")))),
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
