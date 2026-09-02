using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ServiceDefaults;

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

        // On 409 the chain is already there but its first revision may still be
        // Draft: every run between ADR-0113 making If-Match mandatory and this
        // client learning to send it created the overlay and then failed to
        // publish it, so an existing database holds overlays that never render.
        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            OverlayListItem existing = await ReadBackAsync(name, token, cancellationToken);

            // The list projection always carries revisions, so null means the
            // read model changed shape and this recovery has gone blind. Say
            // so — treating it as "nothing to publish" would be the same
            // silent skip that stranded these overlays in Draft.
            if (existing.Revisions is null)
            {
                logger.OverlayRevisionsMissing(name, existing.OverlayIdentifier);
                return existing.OverlayIdentifier;
            }

            if (!existing.HasDraftFirstRevision())
            {
                logger.OverlayAlreadyExists(name, existing.OverlayIdentifier);
                return existing.OverlayIdentifier;
            }

            await PublishAsync(existing.OverlayIdentifier, existing.Version, token, cancellationToken);
            logger.OverlayDraftPublished(name, existing.OverlayIdentifier);
            return existing.OverlayIdentifier;
        }

        created.EnsureSuccessStatusCode();
        Guid overlay = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        await PublishAsync(overlay, FreshChainVersion, token, cancellationToken);
        logger.OverlaySeeded(name, overlay);
        return overlay;
    }

    private async Task PublishAsync(
        Guid overlay, int expectedVersion, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage publish = new(HttpMethod.Post, $"/overlays/{overlay}/revisions/{FirstRevision}/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        publish.Headers.TryAddWithoutValidation(
            "If-Match", ConcurrencyHeaders.ETag(expectedVersion));
        using HttpResponseMessage response = await http.SendAsync(publish, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<OverlayListItem> ReadBackAsync(string name, string token, CancellationToken cancellationToken)
    {
        // GET /overlays with no state filter returns all chains (the Published
        // branch empties Chains), so omit state to recover the existing id.
        using HttpRequestMessage list = new(HttpMethod.Get, "/overlays");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
        response.EnsureSuccessStatusCode();

        OverlayListResponse? payload = await response.Content.ReadFromJsonAsync<OverlayListResponse>(cancellationToken);
        OverlayListItem? match = payload?.Chains?.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return match
            ?? throw new InvalidOperationException($"Overlay '{name}' conflicted but could not be read back.");
    }

    /// <summary>
    /// A chain the seeder just created sits at version 0 —
    /// <c>AggregateVersionInterceptor</c> does not bump <c>Added</c> roots.
    /// </summary>
    private const int FreshChainVersion = 0;

    private const int FirstRevision = 1;

    private const string DraftState = "Draft";

    private sealed record CreateOverlayBody(string Name, OverlayLabel Label);

    private sealed record OverlayListResponse(IReadOnlyList<OverlayListItem> Chains);

    /// <summary>
    /// Just the fields the seeder acts on. Deliberately not the full
    /// <c>OverlayDto</c> — the simulator is dev-only and must not gain a
    /// compile-time dependency on OverlayDesigner's read model.
    /// </summary>
    private sealed record OverlayListItem(
        Guid OverlayIdentifier,
        int Version,
        string Name,
        IReadOnlyList<OverlayRevisionItem> Revisions)
    {
        /// <summary>Callers check <c>Revisions is null</c> first.</summary>
        public bool HasDraftFirstRevision() =>
            Revisions.Any(revision =>
                revision.RevisionNumber == FirstRevision
                && string.Equals(revision.State, DraftState, StringComparison.Ordinal));
    }

    private sealed record OverlayRevisionItem(int RevisionNumber, string State);
}
