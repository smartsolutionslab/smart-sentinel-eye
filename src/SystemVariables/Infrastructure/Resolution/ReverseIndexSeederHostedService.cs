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
    ILogger<ReverseIndexSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The factory, not a typed client: this is a hosted service and so a
            // singleton, and a typed client held by one never gets its handler
            // rotated. The base address comes from the registration.
            using HttpClient client = httpClientFactory.CreateClient("overlay-designer");

            using HttpResponseMessage response = await client
                .GetAsync("/overlays?state=Published", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.SeedNonSuccessStatus(response.StatusCode);
                return;
            }

            JsonElement payload = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!payload.TryGetProperty("published", out JsonElement published))
            {
                logger.SeedMissingPublishedKey();
                return;
            }

            int seeded = 0;
            foreach (JsonElement overlay in published.EnumerateArray())
            {
                if (!overlay.TryGetProperty("overlayIdentifier", out JsonElement idElement))
                {
                    continue;
                }

                if (!overlay.TryGetProperty("text", out JsonElement textElement))
                {
                    continue;
                }

                Guid id = idElement.GetGuid();
                string text = textElement.GetString() ?? string.Empty;
                reverseIndex.UpsertOverlayReferences(id, text);
                seeded++;
            }

            logger.SeededOverlays(seeded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.SeedFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
