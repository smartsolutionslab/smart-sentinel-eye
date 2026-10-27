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
/// overlay-designer service (spec 005 plan.md), and keeps trying until it
/// succeeds or the host stops (spec 465, #3182).
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
/// <b>Nothing but this seed fills the index for an overlay published
/// before the process started.</b> Self-healing needs an event, and such an
/// overlay raises none — so a timeout, a non-success status or a refusal
/// (a status, or the token mint's exception) leaves the index empty for the
/// life of the host unless the attempt is repeated. Every failure is therefore
/// retried with a capped, doubling delay, on top of the up-to-30 s attempt
/// the resilience handler already allows (ADR-0143). The refusal is still
/// logged at <c>Error</c>.
/// </para>
///
/// <para>
/// The loop runs in the background: a degraded index beats no
/// SystemVariables at all, which is the trade ADR-0116 declined for
/// StreamDistribution's startup attribution. A listing is applied through
/// <see cref="IReverseIndex.TrySeedOverlayReferences"/>, so it never undoes
/// what a publish or archive event set while the seed was retrying.
/// </para>
/// </summary>
public sealed class ReverseIndexSeederHostedService(
    IHttpClientFactory httpClientFactory,
    IReverseIndex reverseIndex,
    ILogger<ReverseIndexSeederHostedService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hands control back to the host before the first request, so starting
        // never waits on the seed, however fast the transport answers.
        await Task.Yield();

        var attempt = 1;
        var delay = InitialDelay;
        try
        {
            while (!await AttemptAsync(attempt, delay, stoppingToken))
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
                attempt++;
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaximumDelay.Ticks));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown ends the loop; it is not a failure.
        }
    }

    private async Task<bool> AttemptAsync(int attempt, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        try
        {
            // The factory, not a typed client: this is a hosted service and so a
            // singleton, and a typed client held by one never gets its handler
            // rotated. The base address comes from the registration.
            using var client = httpClientFactory.CreateClient("overlay-designer");

            using var response = await client
                .GetAsync("/overlays?state=Published", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogUnsuccessfulStatus(response.StatusCode, attempt, retryDelay);
                return false;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!payload.TryGetProperty("published", out var published))
            {
                logger.SeedMissingPublishedKey(attempt, retryDelay);
                return false;
            }

            var seeded = SeedOverlays(published);

            logger.SeededOverlays(seeded);
            return true;
        }
        // A refused mint rather than a refused read: the token POST threw before
        // the listing was ever requested, so the 401 arrives as an exception and
        // not as a status. It is the same broken credential, so it takes the same
        // branch. HttpClient propagates a handler's exception unwrapped, so the
        // status is readable here without walking InnerException.
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.SeedRefused(ex.StatusCode.GetValueOrDefault(), attempt, retryDelay);
            return false;
        }
        // A timeout surfaced by HttpClient is itself an OperationCanceledException;
        // only a cancelled host token is a shutdown rather than a failed attempt.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.SeedFailed(ex, attempt, retryDelay);
            return false;
        }
    }

    private void LogUnsuccessfulStatus(HttpStatusCode statusCode, int attempt, TimeSpan retryDelay)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.SeedRefused(statusCode, attempt, retryDelay);
        }
        else
        {
            logger.SeedNonSuccessStatus(statusCode, attempt, retryDelay);
        }
    }

    private int SeedOverlays(JsonElement published)
    {
        var seeded = 0;
        foreach (var overlay in published.EnumerateArray())
        {
            if (!overlay.TryGetProperty("overlayIdentifier", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !idElement.TryGetGuid(out var id))
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
            if (!overlay.TryGetProperty("elements", out var elementsElement)
                || elementsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (reverseIndex.TrySeedOverlayReferences(id, ExtractElementTexts(elementsElement)))
            {
                seeded++;
            }
        }

        return seeded;
    }

    /// <summary>
    /// Reads every element's <c>"text"</c> out of a revision's
    /// <c>"elements"</c> array, in array order (spec 150's ordinal order).
    /// This cached list stays index-aligned with the element array by
    /// <see cref="IReverseIndex"/>'s own contract — unchanged by spec 301
    /// (#2720): so an element missing <c>"text"</c> (including every
    /// <c>Box</c>/<c>Ellipse</c>, spec 300 #2349) contributes
    /// <see cref="string.Empty"/> at its position rather than being
    /// skipped — skipping would shift every later element onto the wrong
    /// index instead of failing the one element that is actually
    /// malformed. <see cref="SmartSentinelEye.SystemVariables.Application.Resolution.ResolvedTextPairs"/>
    /// is the layer above this that drops that empty entry and keys the
    /// rest by template; <see
    /// cref="SmartSentinelEye.SystemVariables.Application.DTOs.ResolvedOverlaySnapshotDto"/>
    /// no longer carries a positional list at all.
    /// </summary>
    private static List<string> ExtractElementTexts(JsonElement elementsElement)
    {
        List<string> elementTexts = [];
        foreach (var element in elementsElement.EnumerateArray())
        {
            elementTexts.Add(
                element.TryGetProperty("text", out var textElement)
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty);
        }
        return elementTexts;
    }
}
