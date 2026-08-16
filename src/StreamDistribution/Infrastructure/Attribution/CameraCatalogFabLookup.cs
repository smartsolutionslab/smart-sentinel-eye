using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

/// <summary>
/// Reads the camera catalogue over HTTP to learn each camera's fab
/// (ADR-0116). The one cross-context call StreamDistribution makes, and only
/// at startup for streams that have no fab yet.
///
/// <para>
/// The listing is fab-scoped like every other read, so the service account
/// this presents belongs to every fab group — a stream's fab is precisely
/// what is unknown, so the query cannot be narrowed in advance. That is why
/// the client holds <c>sse.cameras.read</c> and nothing else.
/// </para>
/// </summary>
public sealed class CameraCatalogFabLookup(
    HttpClient httpClient,
    CameraCatalogTokenProvider tokens,
    IOptions<StreamFabAttributionOptions> options) : ICameraFabLookup
{
    public async Task<IReadOnlyDictionary<Guid, string>> FabsByCameraAsync(CancellationToken cancellationToken)
    {
        string token = await tokens.GetAccessTokenAsync(cancellationToken);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int pageSize = options.Value.PageSize;
        Dictionary<Guid, string> fabs = [];

        int offset = 0;
        int fetched;
        do
        {
            fetched = await ReadPageAsync(fabs, offset, pageSize, cancellationToken);
            offset += pageSize;
        }
        while (fetched == pageSize);

        return fabs;
    }

    /// <summary>Adds one page to <paramref name="fabs"/>; returns its row count.</summary>
    private async Task<int> ReadPageAsync(
        Dictionary<Guid, string> fabs, int offset, int pageSize, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"/cameras?offset={offset}&limit={pageSize}", cancellationToken);
        response.EnsureSuccessStatusCode();

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        JsonElement items = page.GetProperty("items");

        foreach (JsonElement row in items.EnumerateArray())
        {
            string? fab = row.GetProperty("fab").GetString();
            if (!string.IsNullOrWhiteSpace(fab))
            {
                fabs[row.GetProperty("cameraIdentifier").GetGuid()] = fab;
            }
        }

        return items.GetArrayLength();
    }
}
