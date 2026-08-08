using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Creates + publishes the single multi-tile rolling-mill wall over the
/// LayoutComposition REST API (ADR-0111 M2 / ADR-0112): one 2×2 layout whose
/// four tiles are the four stations' camera + overlay. Idempotent: a wall of the
/// same name already present yields a 409 from the create endpoint, and the
/// existing wall is then read back by name. Bearer via the scenario-simulator
/// grant (scopes <c>sse.layouts.write</c> + <c>sse.layouts.read</c>).
///
/// <para>
/// Publishing sends <c>If-Match</c> (ADR-0113). Without it the endpoint
/// answered 428 and the wall stayed a Draft revision forever — created, fully
/// tiled, and never rendered by the kiosk. It surfaced only as
/// "is missing its wall … cannot seed it", because the caller catches and
/// logs rather than failing the run.
/// </para>
///
/// <para>
/// The 409 branch therefore recovers rather than assumes: a wall stranded in
/// Draft by one of those runs is published on the next boot. Treating 409 as
/// "already seeded" was the same silent skip in a different place — the wall
/// existed, so the seeder reported success, and the kiosk still had nothing to
/// render. Mirrors <see cref="OverlayDesignerClient"/>.
/// </para>
/// </summary>
public sealed class LayoutCompositionClient(
    HttpClient http,
    KeycloakTokenProvider tokens,
    ILogger<LayoutCompositionClient> logger)
{
    public async Task EnsureWallAsync(
        string name,
        int rows,
        int cols,
        IReadOnlyList<CorrelatedTile> tiles,
        CancellationToken cancellationToken)
    {
        string token = await tokens.GetAccessTokenAsync(cancellationToken);

        IReadOnlyList<TileBody> tileBodies = tiles
            .Select(tile => new TileBody(tile.Camera, tile.Overlay, tile.Row, tile.Col))
            .ToList();

        using HttpRequestMessage create = new(HttpMethod.Post, "/layouts")
        {
            Content = JsonContent.Create(new CreateLayoutBody(name, new GridBody(rows, cols), tileBodies)),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage created = await http.SendAsync(create, cancellationToken);

        // A wall of this name already exists (a prior run). The create endpoint
        // returns 409 LayoutNameTaken; read it back to find out whether its
        // first revision ever got published.
        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            await RecoverAsync(name, token, cancellationToken);
            return;
        }

        created.EnsureSuccessStatusCode();
        Guid layout = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        await PublishAsync(layout, FreshChainVersion, token, cancellationToken);

        logger.WallSeeded(name, rows, cols, layout);
    }

    private async Task RecoverAsync(string name, string token, CancellationToken cancellationToken)
    {
        LayoutListItem existing = await ReadBackAsync(name, token, cancellationToken);

        // The list projection always carries revisions, so null means the read
        // model changed shape and this recovery has gone blind. Say so rather
        // than inferring the wall is fine.
        if (existing.Revisions is null)
        {
            logger.WallRevisionsMissing(name, existing.LayoutIdentifier);
            return;
        }

        if (!existing.HasDraftFirstRevision())
        {
            logger.WallAlreadyExists(name);
            return;
        }

        await PublishAsync(existing.LayoutIdentifier, existing.Version, token, cancellationToken);
        logger.WallDraftPublished(name, existing.LayoutIdentifier);
    }

    private async Task PublishAsync(
        Guid layout, int expectedVersion, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage publish = new(HttpMethod.Post, $"/layouts/{layout}/revisions/{FirstRevision}/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        publish.Headers.TryAddWithoutValidation(
            "If-Match", ConcurrencyHeaders.ETag(expectedVersion));
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();
    }

    private async Task<LayoutListItem> ReadBackAsync(string name, string token, CancellationToken cancellationToken)
    {
        // GET /layouts with no state filter returns every chain with its full
        // revision history (the Published branch empties Chains), so omit state.
        using HttpRequestMessage list = new(HttpMethod.Get, "/layouts");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
        response.EnsureSuccessStatusCode();

        LayoutListResponse payload = await response.Content.ReadFromJsonAsync<LayoutListResponse>(cancellationToken);
        LayoutListItem match = payload?.Chains?.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return match
            ?? throw new InvalidOperationException($"Wall '{name}' conflicted but could not be read back.");
    }

    /// <summary>
    /// A chain the seeder just created sits at version 0 —
    /// <c>AggregateVersionInterceptor</c> does not bump <c>Added</c> roots.
    /// </summary>
    private const int FreshChainVersion = 0;

    private const int FirstRevision = 1;

    private const string DraftState = "Draft";

    private sealed record CreateLayoutBody(string Name, GridBody Grid, IReadOnlyList<TileBody> Tiles);

    private sealed record GridBody(int Rows, int Cols);

    private sealed record TileBody(Guid CameraIdentifier, Guid? OverlayIdentifier, int Row, int Col);

    private sealed record LayoutListResponse(IReadOnlyList<LayoutListItem> Chains);

    /// <summary>
    /// Just the fields the seeder acts on. Deliberately not the full
    /// <c>LayoutDto</c> — the simulator is dev-only and must not gain a
    /// compile-time dependency on LayoutComposition's read model.
    /// </summary>
    private sealed record LayoutListItem(
        Guid LayoutIdentifier,
        int Version,
        string Name,
        IReadOnlyList<LayoutRevisionItem> Revisions)
    {
        /// <summary>Callers check <c>Revisions is null</c> first.</summary>
        public bool HasDraftFirstRevision() =>
            Revisions.Any(revision =>
                revision.RevisionNumber == FirstRevision
                && string.Equals(revision.State, DraftState, StringComparison.Ordinal));
    }

    private sealed record LayoutRevisionItem(int RevisionNumber, string State);
}
