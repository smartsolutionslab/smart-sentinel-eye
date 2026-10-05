using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Resolution;

/// <summary>
/// Seeds the in-memory <see cref="IReverseIndex"/> on startup by
/// calling <c>GET /overlays?state=Published</c> on the
/// overlay-designer service (spec 005 plan.md). Best-effort — if
/// overlay-designer is down the index starts empty and self-heals as
/// new <c>OverlayRevisionPublishedV3</c> events arrive via Wolverine.
///
/// <para>
/// The HTTP call uses Aspire's <c>http://overlay-designer</c> service
/// discovery URI and carries the <c>system-variables-seeder</c>
/// service account, attached by
/// <see cref="OverlayDesignerAuthorizationHandler"/> on the named
/// client. Spec 005 T061 specified that account; it did not ship, and
/// once <c>GET /overlays</c> gained <c>sse.overlays.read</c> every cold
/// start was refused (#2158, spec 126).
/// </para>
///
/// <para>
/// <b>A refusal is not a self-healing condition and is no longer
/// reported as one.</b> Self-healing needs an event, and an overlay
/// published before this process started raises none — so a 401 or 403
/// leaves the index empty until someone fixes the credential. That holds
/// whether the refusal arrives from overlay-designer as a status or out of
/// the token mint as an exception, which is the shape a missing service
/// account actually takes.
/// <see cref="Log.SeedRefused"/> says that at <c>Error</c>. It still
/// does not stop the host: refusing to start would trade a degraded
/// index for no SystemVariables at all, which is the trade ADR-0116
/// declined for StreamDistribution's startup attribution.
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
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    logger.SeedRefused(response.StatusCode);
                }
                else
                {
                    logger.SeedNonSuccessStatus(response.StatusCode);
                }

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

                // Spec 150 (#2345): the scalar "text" property is gone — a
                // revision carries "labels", an array of objects each with
                // its own "text". Spec 300 (#2349, ADR-0165) renamed the
                // field again, to "elements", and widened each entry to a
                // kind + colour; a Box/Ellipse has no "text" at all.
                // Reading the old property name ("labels") would compile
                // and throw nothing; it would just seed zero overlays on
                // every cold start — the exact class of bug spec 150
                // shipped once already.
                if (!overlay.TryGetProperty("elements", out JsonElement elementsElement)
                    || elementsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                Guid id = idElement.GetGuid();
                reverseIndex.UpsertOverlayReferences(id, ExtractElementTexts(elementsElement));
                seeded++;
            }

            logger.SeededOverlays(seeded);
        }
        // A refused mint rather than a refused read: the token POST threw before
        // the listing was ever requested, so the 401 arrives as an exception and
        // not as a status. It is the same broken credential reaching the same
        // dead end, so it takes the same branch. HttpClient propagates a
        // handler's exception unwrapped, so the status is readable here without
        // walking InnerException.
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.SeedRefused(ex.StatusCode.GetValueOrDefault());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.SeedFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Reads every element's <c>"text"</c> out of a revision's
    /// <c>"elements"</c> array, in array order (spec 150's ordinal order).
    /// <c>ResolvedTexts</c> is index-aligned with the element array by
    /// contract (<see
    /// cref="SmartSentinelEye.SystemVariables.Application.DTOs.ResolvedOverlaySnapshotDto"/>),
    /// so an element missing <c>"text"</c> (including every
    /// <c>Box</c>/<c>Ellipse</c>, spec 300 #2349) contributes
    /// <see cref="string.Empty"/> at its position rather than being
    /// skipped — skipping would shift every later element onto the wrong
    /// index instead of failing the one element that is actually
    /// malformed.
    /// </summary>
    private static List<string> ExtractElementTexts(JsonElement elementsElement)
    {
        List<string> elementTexts = [];
        foreach (JsonElement element in elementsElement.EnumerateArray())
        {
            elementTexts.Add(
                element.TryGetProperty("text", out JsonElement textElement)
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty);
        }
        return elementTexts;
    }
}
