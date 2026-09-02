using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Cameras;

/// <summary>
/// Asks CameraCatalog which cameras are in a fab, over its published HTTP API
/// (spec 017 FR-014). The first synchronous cross-context call this context
/// makes — plan.md §III records it as a bounded exception.
///
/// <para>
/// It carries <b>the caller's own bearer token</b>, forwarded from the
/// incoming request, and narrows the listing to the layout's fab. That is what
/// keeps this a smaller exception than ADR-0116's: no service account exists
/// for it, and it can see exactly what the operator can already see and
/// nothing more. It also reuses CameraCatalog's own fab scoping rather than
/// re-implementing a rule another context owns.
/// </para>
///
/// <para>
/// The token is read from the incoming request rather than threaded through the
/// command, because a credential has no business in the Application layer.
/// Framework coupling belongs in the adapter — and specifically in
/// <see cref="CallerTokenForwardingHandler"/>, which is the adapter's adapter:
/// this class knows there is an authorised caller, not how the authorisation
/// travels.
/// </para>
/// </summary>
public sealed class CameraCatalogFabGuard(HttpClient httpClient) : ICameraFabGuard
{
    private const int PageSize = 200;

    public async Task<IReadOnlyList<CameraIdentifier>> CamerasOutsideFabAsync(
        FabIdentifier fab,
        IReadOnlyList<CameraIdentifier> cameras,
        CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(cameras).IsNotNull();

        if (cameras.Count == 0)
        {
            return [];
        }

        HashSet<Guid> inFab = await CamerasInFabAsync(fab, cancellationToken);

        // Unknown and other-fab land here together, by construction (FR-015).
        return [.. cameras.Where(camera => !inFab.Contains(camera.Value)).Distinct()];
    }

    private async Task<HashSet<Guid>> CamerasInFabAsync(
        FabIdentifier fab, CancellationToken cancellationToken)
    {
        HashSet<Guid> identifiers = [];
        int offset = 0;
        int fetched;
        do
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"/cameras?fabId={fab.Value}&offset={offset}&limit={PageSize}", cancellationToken);
            response.EnsureSuccessStatusCode();

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            JsonElement items = page.GetProperty("items");

            foreach (JsonElement row in items.EnumerateArray())
            {
                identifiers.Add(row.GetProperty("cameraIdentifier").GetGuid());
            }

            fetched = items.GetArrayLength();
            offset += PageSize;
        }
        while (fetched == PageSize);

        return identifiers;
    }
}
