using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.CameraCatalog;

/// <summary>
/// Seeds the camera catalog (the source of truth, ADR-0111) over its REST API
/// and returns the camera's identifier. Registration is idempotent: the catalog
/// rejects a duplicate name with 409, and the existing camera is then read back
/// by name. The bearer token comes from the <c>scenario-simulator</c>
/// client_credentials grant (scopes <c>sse.cameras.write</c> +
/// <c>sse.cameras.read</c>).
///
/// <para>
/// The read-back matters beyond convenience: the identifier is what correlates
/// a camera to its wall tile. Returning only "did I create it" meant a restart
/// with cameras already present could never rebuild the wall, because the ids
/// arrived solely on <c>CameraRegisteredV1</c> — an event that does not fire
/// for cameras that already exist.
/// </para>
/// </summary>
public sealed class CameraCatalogClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    IOptions<SimulatorOptions> options,
    ILogger<CameraCatalogClient> logger)
{
    /// <summary>
    /// The camera's identifier, or <c>null</c> when an already-registered
    /// camera could not be read back. Null is not fatal: the caller simply
    /// cannot correlate that camera to a wall tile, which is how this behaved
    /// before the read-back existed.
    /// </summary>
    public async Task<Guid?> RegisterCameraAsync(string name, string cameraPath, CancellationToken cancellationToken)
    {
        string token = await tokens.GetAccessTokenAsync(cancellationToken);
        string rtspUrl = $"rtsp://{options.Value.RtspHost.Trim('/')}/{cameraPath}";

        using HttpRequestMessage request = new(HttpMethod.Post, "/cameras")
        {
            Content = JsonContent.Create(new RegisterCameraBody(name, rtspUrl)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            logger.CameraAlreadyRegistered(name);
            return await ReadBackAsync(name, token, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        Guid camera = await response.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        logger.CameraRegistered(name, rtspUrl);
        return camera;
    }

    /// <summary>
    /// Best-effort read-back. A failure here must not take the worker down:
    /// the read needs <c>sse.cameras.read</c>, and a grant still missing that
    /// scope answers 403 — which, thrown, would trip
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c> and kill the whole
    /// simulator over a wall tile. Degrade to "id unknown" instead.
    /// </summary>
    private async Task<Guid?> ReadBackAsync(string name, string token, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage list = new(HttpMethod.Get, "/cameras?limit=200");
            list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
            response.EnsureSuccessStatusCode();

            CameraPage payload = await response.Content.ReadFromJsonAsync<CameraPage>(cancellationToken);
            CameraSummary match = payload?.Items?
                .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));

            if (match is null)
            {
                logger.CameraReadBackFailed(name, "not present in the catalog listing");
            }

            return match?.CameraIdentifier;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.CameraReadBackFailed(name, ex.Message);
            return null;
        }
    }

    // Matches CameraCatalog.Api RegisterCameraRequest { Name, RtspUrl }.
    private sealed record RegisterCameraBody(string Name, string RtspUrl);

    // Just the fields the seeder correlates on — deliberately not the full
    // CameraSummaryDto, so the simulator gains no compile-time dependency on
    // CameraCatalog's read model.
    private sealed record CameraPage(IReadOnlyList<CameraSummary> Items);

    private sealed record CameraSummary(Guid CameraIdentifier, string Name);
}
