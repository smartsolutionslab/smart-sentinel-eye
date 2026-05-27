using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Resolution;

/// <summary>
/// Seeds the in-memory <see cref="IReverseIndex"/> on startup by
/// calling <c>GET /overlays?state=Published</c> on the
/// overlay-designer service (spec 005 plan.md). Best-effort — if the
/// seeder fails (overlay-designer down, auth missing, etc.), the
/// index starts empty and self-heals as new
/// <c>OverlayRevisionPublishedV1</c> events arrive via Wolverine.
///
/// <para>
/// The HTTP call uses Aspire's <c>http://overlay-designer</c> service
/// discovery URI. Auth is deferred — v1 hits the endpoint
/// unauthenticated and accepts a 401 as "skip seeding for now"; a
/// production deployment would use a service-account Keycloak token
/// (deferred to spec 007's Identity hardening).
/// </para>
/// </summary>
public sealed class ReverseIndexSeederHostedService(
    IHttpClientFactory httpClientFactory,
    IReverseIndex reverseIndex,
    ILogger<ReverseIndexSeederHostedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = httpClientFactory.CreateClient("overlay-designer");
            // S1075 flags the literal URI. Suppressed: Aspire service
            // discovery resolves "http://overlay-designer" to the
            // actual endpoint at runtime — there's no configurable
            // alternative for v1.
#pragma warning disable S1075
            client.BaseAddress ??= new Uri("http://overlay-designer");
#pragma warning restore S1075

            using HttpResponseMessage response = await client
                .GetAsync("/overlays?state=Published", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "ReverseIndex seed: overlay-designer returned {Status}; starting with empty index. " +
                    "The index will populate as new OverlayRevisionPublishedV1 events arrive.",
                    response.StatusCode);
                return;
            }

            JsonElement payload = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            if (!payload.TryGetProperty("published", out JsonElement published))
            {
                log.LogWarning("ReverseIndex seed: response missing 'published' key; index left empty.");
                return;
            }

            int seeded = 0;
            foreach (JsonElement overlay in published.EnumerateArray())
            {
                if (!overlay.TryGetProperty("overlayIdentifier", out JsonElement idElement)) continue;
                if (!overlay.TryGetProperty("text", out JsonElement textElement)) continue;
                Guid id = idElement.GetGuid();
                string text = textElement.GetString() ?? string.Empty;
                reverseIndex.UpsertOverlayReferences(id, text);
                seeded++;
            }

            log.LogInformation("ReverseIndex seeded with {Count} published overlays.", seeded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex,
                "ReverseIndex seed failed; starting with empty index. " +
                "Self-heal will kick in as overlay V1 events arrive.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
