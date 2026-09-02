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

    /// <summary>The largest page the catalog serves; it refuses more rather than clamping.</summary>
    private const int PageSize = 200;

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
            // **Every page, not the first.** This asked for one page of 200 —
            // the largest the endpoint serves — and searched it by name. Past
            // 200 cameras a camera that exists was reported "not present in the
            // catalog listing": not silence but a false statement about the
            // catalogue, and the correlation to a wall tile lost with it. The
            // constitution targets 250 per fab, so the target itself reaches it.
            //
            // Raising the number would not work: the endpoint refuses anything
            // above 200 rather than clamping, so the page size is a ceiling and
            // the offset is the only way through.
            int offset = 0;
            int reported = 0;

            do
            {
                using HttpRequestMessage list = new(
                    HttpMethod.Get, $"/cameras?limit={PageSize}&offset={offset}");
                list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
                response.EnsureSuccessStatusCode();

                CameraPage? payload = await response.Content.ReadFromJsonAsync<CameraPage>(cancellationToken);
                IReadOnlyList<CameraSummary> items = payload?.Items ?? [];

                CameraSummary? match = items
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));

                if (match is not null)
                {
                    return match.CameraIdentifier;
                }

                // An empty page ends the walk whatever the count says. Without
                // it a count that outruns the rows — a camera retired between
                // two requests — spins here forever.
                if (items.Count == 0)
                {
                    break;
                }

                reported = payload?.Count ?? 0;
                offset += items.Count;
            }
            while (offset < reported);

            logger.CameraReadBackFailed(
                name, $"not present in any of the {reported} cameras the catalog listed");

            return null;
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
    //
    // **`Count` is one of those fields, and it was the omission.** Leaving it
    // out did not merely let the "there is more" signal be ignored — it made the
    // signal inexpressible, so nothing could have caught the truncation by
    // reading this code. A hand-rolled projection is where such a field goes
    // missing before anybody gets the chance to ignore it.
    private sealed record CameraPage(IReadOnlyList<CameraSummary> Items, int Count);

    private sealed record CameraSummary(Guid CameraIdentifier, string Name);
}
