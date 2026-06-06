using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Creates + publishes the single multi-tile rolling-mill wall over the
/// LayoutComposition REST API (ADR-0111 M2 / ADR-0112): one 2×2 layout whose
/// four tiles are the four stations' camera + overlay. Idempotent: a wall of the
/// same name already present yields a 409 from the create endpoint, which we
/// treat as "already seeded". Bearer via the scenario-simulator grant — the
/// client holds only the write scopes (sse.layouts.write), so it never reads
/// (GET /layouts requires sse.layouts.read) and relies on the 409 instead.
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

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/layouts/{layout}/revisions/1/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();

        logger.WallSeeded(name, rows, cols, layout);
    }

    private sealed record CreateLayoutBody(string Name, GridBody Grid, IReadOnlyList<TileBody> Tiles);

    private sealed record GridBody(int Rows, int Cols);

    private sealed record TileBody(Guid CameraIdentifier, Guid? OverlayIdentifier, int Row, int Col);
}
