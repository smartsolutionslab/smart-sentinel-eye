using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 390 / #2976, adversarial pass (T011). The registration's failure paths: a refused POST
/// throws into the seeder, and a failed read-back logs its reason. Neither may carry the URL the
/// body was built from, nor another camera's URL from the listing it read.
/// </summary>
public sealed class CameraCatalogClientLogRedactionAdversarialTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_registration_carries_no_url_into_the_exception_or_a_log(HttpStatusCode status)
    {
        RigHandler catalog = new(null, request => Json(status, $"{{\"detail\":\"bad rtspUrl {request.Body}\"}}"));
        RecordingLoggerProvider log = new();

        var exception = await Should.ThrowAsync<HttpRequestException>(() => Client(catalog, log)
            .RegisterCameraAsync("Station 4", "PATHMARK", CancellationToken.None));

        exception.ToString().ShouldNotContain("PATHMARK", customMessage: exception.ToString());
        log.Dump.ShouldNotContain("PATHMARK", customMessage: log.Dump);
        log.Entries.ShouldNotContain(entry => entry.Message.StartsWith("Registered camera", StringComparison.Ordinal), log.Dump);
    }

    [Fact]
    public async Task A_read_back_that_cannot_parse_the_listing_logs_no_camera_url_from_it()
    {
        RigHandler catalog = new(
            request => request.Method == "POST" ? Json(HttpStatusCode.Conflict, "{}") : null,
            _ => Json(
                HttpStatusCode.OK,
                """
                {"items":[
                  {"cameraIdentifier":"0199b0c8-7a11-7c2e-9d2e-1f2a3b4c5d6e","name":"Other","rtspUrl":"rtsp://USERMARK:PWMARK@cam.example/PATHMARK"},
                  {"cameraIdentifier":"rtsp://USERMARK:PWMARK@cam.example/PATHMARK","name":"Broken"}
                ],"count":2}
                """));
        RecordingLoggerProvider log = new();

        var registered = await Client(catalog, log).RegisterCameraAsync("Station 4", "station-4", CancellationToken.None);

        registered.ShouldBeNull();
        log.Entries.ShouldContain(entry => entry.Message.StartsWith("Could not read back camera 'Station 4'", StringComparison.Ordinal), log.Dump);

        foreach (var marker in new[] { "USERMARK", "PWMARK", "PATHMARK" })
        {
            log.Dump.ShouldNotContain(marker, customMessage: $"'{marker}' leaked:{Environment.NewLine}{log.Dump}");
        }
    }

    private static CameraCatalogClient Client(RigHandler catalog, RecordingLoggerProvider log) =>
        new(
            new HttpClient(catalog) { BaseAddress = new Uri("https://cameras.test") },
            Tokens(),
            Options.Create(new SimulatorOptions { RtspHost = "rtsp.test" }),
            log.CreateLogger<CameraCatalogClient>());

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
