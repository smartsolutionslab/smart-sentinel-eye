using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Creates + publishes the single multi-tile rolling-mill wall over the
/// LayoutComposition REST API (ADR-0111 M2 / ADR-0112): one 2×2 layout whose
/// four tiles are the four stations' camera + overlay. Idempotent: skips if a
/// wall of the same name already exists. Bearer via the scenario-simulator grant
/// (scope sse.layouts.write).
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

        if (await FindByNameAsync(name, token, cancellationToken) is not null)
        {
            logger.WallAlreadyExists(name);
            return;
        }

        IReadOnlyList<TileBody> tileBodies = tiles
            .Select(tile => new TileBody(tile.Camera, tile.Overlay, tile.Row, tile.Col))
            .ToList();

        using HttpRequestMessage create = new(HttpMethod.Post, "/layouts")
        {
            Content = JsonContent.Create(new CreateLayoutBody(name, new GridBody(rows, cols), tileBodies)),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage created = await http.SendAsync(create, cancellationToken);
        created.EnsureSuccessStatusCode();
        Guid layout = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);

        using HttpRequestMessage publish = new(HttpMethod.Post, $"/layouts/{layout}/revisions/1/publish");
        publish.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage published = await http.SendAsync(publish, cancellationToken);
        published.EnsureSuccessStatusCode();

        logger.WallSeeded(name, rows, cols, layout);
    }

    private async Task<Guid?> FindByNameAsync(string name, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage list = new(HttpMethod.Get, "/layouts");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(list, cancellationToken);
        response.EnsureSuccessStatusCode();

        LayoutListResponse payload = await response.Content.ReadFromJsonAsync<LayoutListResponse>(cancellationToken);
        LayoutListItem match = payload?.Chains?.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return match?.LayoutIdentifier;
    }

    private sealed record CreateLayoutBody(string Name, GridBody Grid, IReadOnlyList<TileBody> Tiles);

    private sealed record GridBody(int Rows, int Cols);

    private sealed record TileBody(Guid CameraIdentifier, Guid? OverlayIdentifier, int Row, int Col);

    private sealed record LayoutListResponse(IReadOnlyList<LayoutListItem> Chains);

    private sealed record LayoutListItem(Guid LayoutIdentifier, string Name);
}
