using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Seeds + publishes a per-asset overlay over the OverlayDesigner REST API
/// (ADR-0111 M2) and returns its identifier. Idempotent: a duplicate name (409)
/// reads the existing overlay back by name. Bearer via the scenario-simulator
/// client_credentials grant.
/// </summary>
/// <remarks>
/// The read-back needs <c>sse.overlays.read</c> as well as the write scopes.
/// The grant was write-only until #1121: the 409 branch then 403'd, the
/// exception escaped <see cref="ScenarioSeeder"/>, and
/// <c>BackgroundServiceExceptionBehavior.StopHost</c> killed the worker — so a
/// re-run of an already-seeded stack came up with no cameras and no video at
/// all. The identifier cannot simply be skipped on 409: it is what binds each
/// overlay to its layout tile.
/// </remarks>
public sealed class OverlayDesignerClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    ILogger<OverlayDesignerClient> logger)
{
    public async Task<Guid> EnsureOverlayAsync(string name, OverlayLabel label, CancellationToken cancellationToken)
    {
        string token = await tokens.GetAccessTokenAsync(cancellationToken);

        using HttpRequestMessage create = new(HttpMethod.Post, "/overlays")
        {
            Content = JsonContent.Create(new CreateOverlayBody(name, label)),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage created = await http.SendAsync(create, cancellationToken);

        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            Guid existing = await ReadBackAsync(name, token, cancellationToken);
            logger.OverlayAlreadyExists(name, existing);
            return existing;
        }

        created.EnsureSuccessStatusCode();
        Guid overlay = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        await PublishAsync(overlay, token, cancellationToken);
        logger.OverlaySeeded(name, overlay);
        return overlay;
    }

    private async Task PublishAsync(Guid overlay, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage publish = new(HttpMethod.Post, $"/overlays/{overlay}/revisions/1/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(publish, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> ReadBackAsync(string name, string token, CancellationToken cancellationToken)
    {
        // GET /overlays with no state filter returns all chains (the Published
        // branch empties Chains), so omit state to recover the existing id.
        using HttpRequestMessage list = new(HttpMethod.Get, "/overlays");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
        response.EnsureSuccessStatusCode();

        OverlayListResponse payload = await response.Content.ReadFromJsonAsync<OverlayListResponse>(cancellationToken);
        OverlayListItem match = payload?.Chains?.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return match?.OverlayIdentifier
            ?? throw new InvalidOperationException($"Overlay '{name}' conflicted but could not be read back.");
    }

    private sealed record CreateOverlayBody(string Name, OverlayLabel Label);

    private sealed record OverlayListResponse(IReadOnlyList<OverlayListItem> Chains);

    private sealed record OverlayListItem(Guid OverlayIdentifier, string Name);
}
