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
/// same name already present yields a 409 from the create endpoint, which we
/// treat as "already seeded". Bearer via the scenario-simulator grant — the
/// client holds only the write scopes (sse.layouts.write), so it never reads
/// (GET /layouts requires sse.layouts.read) and relies on the 409 instead.
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
/// Known gap: unlike <see cref="OverlayDesignerClient"/>, this client cannot
/// recover a Draft left behind by an earlier failure. That needs a read-back
/// on the 409 branch, and the scenario-simulator grant has no
/// <c>sse.layouts.read</c> (the overlay client only gained its read scope in
/// #1121). Adding it means a realm change; until then a stranded Draft wall
/// has to be deleted so this path re-creates it.
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

        // A wall of this name already exists (a prior run): idempotent, nothing
        // to do. The create endpoint returns 409 LayoutNameTaken for a duplicate.
        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            logger.WallAlreadyExists(name);
            return;
        }

        created.EnsureSuccessStatusCode();
        Guid layout = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/layouts/{layout}/revisions/{FirstRevision}/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        publish.Headers.TryAddWithoutValidation(
            "If-Match", ConcurrencyHeaders.ETag(FreshChainVersion));
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();

        logger.WallSeeded(name, rows, cols, layout);
    }

    /// <summary>
    /// A chain the seeder just created sits at version 0 —
    /// <c>AggregateVersionInterceptor</c> does not bump <c>Added</c> roots.
    /// </summary>
    private const int FreshChainVersion = 0;

    private const int FirstRevision = 1;

    private sealed record CreateLayoutBody(string Name, GridBody Grid, IReadOnlyList<TileBody> Tiles);

    private sealed record GridBody(int Rows, int Cols);

    private sealed record TileBody(Guid CameraIdentifier, Guid? OverlayIdentifier, int Row, int Col);
}
