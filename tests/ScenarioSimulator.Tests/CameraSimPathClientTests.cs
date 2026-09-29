using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-B, T-B03 (ADR-0144 red). <c>CameraSimPathClient</c> does not
/// exist yet; this class is the target shape (plan.md §4, T-B12) — a plain
/// <c>GET /v3/paths/get/{path}</c> against camera-sim's MediaMTX control API
/// (no Keycloak bearer: the control plane is unauthenticated on the docker
/// network, the same as <c>CameraSimProvisioner</c>). FR-004's anchor is
/// <c>readyTime</c> off this response.
///
/// <para>
/// <b>Judgment call:</b> the canned body below is a best-effort reconstruction
/// of MediaMTX 1.21's <c>/v3/paths/get/{name}</c> shape (<c>name</c>,
/// <c>confName</c>, <c>source</c>, <c>ready</c>, <c>readyTime</c>,
/// <c>tracks</c>, <c>bytesReceived</c>, <c>bytesSent</c>, <c>readers</c>) —
/// plan.md §2 records that the architect verified the live shape against the
/// running dev stack on 2026-09-29 but does not quote the literal JSON. If the
/// live field names differ from this, this test (and the client's DTO) need
/// adjusting against a real query before Phase 5; flagged explicitly in the
/// handback.
/// </para>
/// </summary>
public sealed class CameraSimPathClientTests
{
    private const string ReadyBody =
        """
        {
          "name": "station-4-roughing",
          "confName": "station-4-roughing",
          "source": { "type": "rtspSession", "id": "8c2e6f3a-1b2c-4d5e-9f01-abcdef123456" },
          "ready": true,
          "readyTime": "2026-09-29T10:15:23.123456789Z",
          "tracks": ["H264"],
          "bytesReceived": 481920,
          "bytesSent": 0,
          "readers": []
        }
        """;

    private const string NotReadyBody =
        """
        {
          "name": "station-4-roughing",
          "confName": "station-4-roughing",
          "source": null,
          "ready": false,
          "readyTime": null,
          "tracks": [],
          "bytesReceived": 0,
          "bytesSent": 0,
          "readers": []
        }
        """;

    [Fact]
    public async Task A_ready_path_parses_to_Ready_and_its_readyTime()
    {
        CameraSimPathClient client = ClientReturning(HttpStatusCode.OK, ReadyBody);

        Option<PathState> state = await client.GetAsync("station-4-roughing", CancellationToken.None);

        state.HasValue.ShouldBeTrue();
        state.Value.Ready.ShouldBeTrue();
        state.Value.ReadyTime.ShouldBe(
            DateTimeOffset.Parse("2026-09-29T10:15:23.123456789Z", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_path_with_no_reader_maps_to_not_ready()
    {
        CameraSimPathClient client = ClientReturning(HttpStatusCode.OK, NotReadyBody);

        Option<PathState> state = await client.GetAsync("station-4-roughing", CancellationToken.None);

        state.HasValue.ShouldBeTrue();
        state.Value.Ready.ShouldBeFalse();
    }

    [Fact]
    public async Task A_404_for_an_unprovisioned_path_maps_to_None()
    {
        CameraSimPathClient client = ClientReturning(HttpStatusCode.NotFound, string.Empty);

        Option<PathState> state = await client.GetAsync("no-such-path", CancellationToken.None);

        state.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task A_server_error_is_logged_and_returns_None_rather_than_throwing()
    {
        CameraSimPathClient client = ClientReturning(HttpStatusCode.InternalServerError, "boom");

        Option<PathState> state = await client.GetAsync("station-4-roughing", CancellationToken.None);

        state.HasValue.ShouldBeFalse();
    }

    /// <summary>
    /// Backend-reviewer finding S1 (should-fix). <c>ParseReadyTime</c> uses
    /// <c>DateTimeOffset.Parse</c>, not <c>TryParse</c> — introduced by the fix
    /// for the sub-tick rounding disagreement with System.Text.Json's built-in
    /// converter, which reads <c>ReadyTime</c> as a raw <c>string?</c> and
    /// parses it by hand. A malformed <c>readyTime</c> string now throws
    /// <see cref="FormatException"/> out of <c>GetAsync</c>, uncaught —
    /// against the client's own "never throws" design (every other failure
    /// path here returns <c>None</c> and logs).
    /// </summary>
    [Fact]
    public async Task A_malformed_readyTime_string_returns_None_rather_than_throwing()
    {
        const string malformedBody =
            """{ "name": "station-4-roughing", "ready": true, "readyTime": "not-a-timestamp" }""";
        CameraSimPathClient client = ClientReturning(HttpStatusCode.OK, malformedBody);

        Option<PathState> state = await client.GetAsync("station-4-roughing", CancellationToken.None);

        state.HasValue.ShouldBeFalse();
    }

    private static CameraSimPathClient ClientReturning(HttpStatusCode status, string body)
    {
        HttpClient http = new(new StubHandler(status, body)) { BaseAddress = new Uri("https://camera-sim.test") };
        return new CameraSimPathClient(http, NullLogger<CameraSimPathClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.ShouldStartWith("/v3/paths/get/");

            HttpResponseMessage response = new(status);
            if (!string.IsNullOrEmpty(body))
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
