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
    public async Task<Guid> RegisterCameraAsync(string name, string cameraPath, CancellationToken cancellationToken)
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
            Guid existing = await ReadBackAsync(name, token, cancellationToken);
            logger.CameraAlreadyRegistered(name);
            return existing;
        }

        response.EnsureSuccessStatusCode();
        Guid camera = await response.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        logger.CameraRegistered(name, rtspUrl);
        return camera;
    }

    private async Task<Guid> ReadBackAsync(string name, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage list = new(HttpMethod.Get, "/cameras?limit=200");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
        response.EnsureSuccessStatusCode();

        CameraPage payload = await response.Content.ReadFromJsonAsync<CameraPage>(cancellationToken);
        CameraSummary match = payload?.Items?
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));

        return match?.CameraIdentifier
            ?? throw new InvalidOperationException($"Camera '{name}' conflicted but could not be read back.");
    }

    // Matches CameraCatalog.Api RegisterCameraRequest { Name, RtspUrl }.
    private sealed record RegisterCameraBody(string Name, string RtspUrl);

    // Just the fields the seeder correlates on — deliberately not the full
    // CameraSummaryDto, so the simulator gains no compile-time dependency on
    // CameraCatalog's read model.
    private sealed record CameraPage(IReadOnlyList<CameraSummary> Items);

    private sealed record CameraSummary(Guid CameraIdentifier, string Name);
}
