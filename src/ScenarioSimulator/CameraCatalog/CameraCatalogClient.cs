using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.CameraCatalog;

/// <summary>
/// Seeds the camera catalog (the source of truth, ADR-0111) over its REST API.
/// Registration is idempotent: the catalog rejects a duplicate name with 409,
/// which we treat as "already seeded" so a worker restart is a no-op. The
/// bearer token comes from the <c>scenario-simulator</c> client_credentials
/// grant (scope <c>sse.cameras.write</c>).
/// </summary>
public sealed class CameraCatalogClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    ILogger<CameraCatalogClient> logger)
{
    public async Task<bool> RegisterCameraAsync(string name, string rtspUrl, CancellationToken cancellationToken)
    {
        string token = await tokens.GetAccessTokenAsync(cancellationToken);

        using HttpRequestMessage request = new(HttpMethod.Post, "/cameras")
        {
            Content = JsonContent.Create(new RegisterCameraBody(name, rtspUrl)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            logger.CameraAlreadyRegistered(name);
            return false;
        }

        response.EnsureSuccessStatusCode();
        logger.CameraRegistered(name, rtspUrl);
        return true;
    }

    // Matches CameraCatalog.Api RegisterCameraRequest { Name, RtspUrl }.
    private sealed record RegisterCameraBody(string Name, string RtspUrl);
}
