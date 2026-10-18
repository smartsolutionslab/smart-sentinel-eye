using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

/// <summary>
/// IRtspGateway implementation that talks to MediaMTX's HTTP control API
/// (v3). The typed HttpClient is configured against the Aspire-resolved
/// <c>mediamtx:api</c> endpoint. Retry, timeout and circuit breaking come
/// from the standard resilience handler ServiceDefaults applies to every
/// client, not from this context's registration.
/// </summary>
public sealed class MediaMtxRtspGateway(HttpClient http, ILogger<MediaMtxRtspGateway> logger) : IRtspGateway
{
    private const int MaxListPages = 1000;

    private enum PageReadEnd { Complete, PageCountUnreadable, PageCapReached }

    private readonly record struct PageRead(PageReadEnd End, int Page);

    public async Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();
        Ensure.That(rtspSourceUrl).IsNotNull().IsNotNullOrWhiteSpace();

        // POST /v3/config/paths/add/{name}
        // Body: { "source": "rtsp://..." } — MediaMTX pulls the source on demand.
        // FFmpeg fallback for non-H.264 inputs is implicit: MediaMTX
        // re-packetizes H.264 and transparently transcodes everything else
        // using the bundled FFmpeg binary (configured via `runOnDemand`-style
        // hooks in the YAML; not needed for v1 because we only ship the
        // ffmpeg-bundled image variant).
        using var response = await http
            .PostAsJsonAsync($"/v3/config/paths/add/{path.Value}", new { source = rtspSourceUrl }, cancellationToken);

        // Idempotent on the path name (spec 309 FR-002): MediaMTX answers 400
        // for several add/ faults (bad source, bad config) and only the error
        // string distinguishes them, so a string match would be coupled to
        // wording a MediaMTX bump may change silently. Asking the GET
        // endpoint directly fails safe — anything but 200 rethrows the
        // original add/ failure below.
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            using var check = await http.GetAsync($"/v3/config/paths/get/{path.Value}", cancellationToken);
            if (check.StatusCode == System.Net.HttpStatusCode.OK)
            {
                logger.MediaMtxPathAlreadyRegistered(path);
                return;
            }
        }

        response.EnsureSuccessStatusCode();
        logger.RegisteredMediaMtxPath(path, UrlRedaction.ToOrigin(rtspSourceUrl));
    }

    public async Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();
        Ensure.That(rtspSourceUrl).IsNotNull().IsNotNullOrWhiteSpace();

        // PATCH /v3/config/paths/patch/{name} changes the source in place.
        // add/ would reject an existing path and delete+add would leave a
        // window with no path at all, which the two-second health sweep would
        // see and announce.
        using var response = await http
            .PatchAsJsonAsync($"/v3/config/paths/patch/{path.Value}", new { source = rtspSourceUrl }, cancellationToken);

        response.EnsureSuccessStatusCode();
        logger.RepointedMediaMtxPath(path, UrlRedaction.ToOrigin(rtspSourceUrl));
    }

    public async Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();

        using var response = await http.DeleteAsync($"/v3/config/paths/delete/{path.Value}", cancellationToken);

        // 404 is fine — path may have already been removed by a prior call.
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
        logger.RemovedMediaMtxPath(path);
    }

    public async Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken)
    {
        // GET /v3/config/paths/list is paginated the same way the runtime
        // /v3/paths/list endpoint is (MediaMTX's internal/api/paginate.go
        // defaults itemsPerPage to 100, sorted by Name ascending) — it shares
        // ReadPagesAsync with GetAllPathsHealthAsync, because looping until
        // pageCount is exhausted matters at the 250-camera target. Filters to
        // canonical cam-{guid} names so manually-created MediaMTX paths are
        // left alone.
        List<MediaMtxPath> paths = [];

        var read = await ReadPagesAsync(
            "/v3/config/paths/list",
            payload => AddPageToPaths(payload, paths),
            cancellationToken);

        switch (read.End)
        {
            case PageReadEnd.PageCountUnreadable:
                logger.MediaMtxConfiguredPathsPageCountUnreadable(read.Page);
                break;
            case PageReadEnd.PageCapReached:
                logger.MediaMtxConfiguredPathsPageCapReached(MaxListPages);
                break;
        }

        return paths;
    }

    private async Task<PageRead> ReadPagesAsync(
        string listPath,
        Func<JsonElement, bool> addPage,
        CancellationToken cancellationToken)
    {
        for (var page = 0; page < MaxListPages; page++)
        {
            using var response = await http.GetAsync(
                $"{listPath}?page={page}&itemsPerPage=100", cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (!addPage(payload))
            {
                return new PageRead(PageReadEnd.Complete, page);
            }

            // Absent pageCount (an older MediaMTX, or a test double that never
            // set it) reads as "one page".
            if (!payload.TryGetProperty("pageCount", out var pageCountElement)
                || pageCountElement.ValueKind != JsonValueKind.Number)
            {
                return new PageRead(PageReadEnd.Complete, page);
            }

            if (!pageCountElement.TryGetInt32(out var pageCount))
            {
                return new PageRead(PageReadEnd.PageCountUnreadable, page);
            }

            if (page + 1 >= pageCount)
            {
                return new PageRead(PageReadEnd.Complete, page);
            }
        }

        return new PageRead(PageReadEnd.PageCapReached, MaxListPages - 1);
    }

    public async Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
        CancellationToken cancellationToken)
    {
        // Shares ReadPagesAsync with ListConfiguredPathsAsync, keeping each
        // item's `source` verbatim: the repair compares it ordinally with the
        // database.
        Dictionary<MediaMtxPath, string> sources = [];

        var read = await ReadPagesAsync(
            "/v3/config/paths/list",
            payload => AddPageToSources(payload, sources),
            cancellationToken);

        switch (read.End)
        {
            case PageReadEnd.PageCountUnreadable:
                logger.MediaMtxConfiguredSourcesPageCountUnreadable(read.Page);
                break;
            case PageReadEnd.PageCapReached:
                logger.MediaMtxConfiguredSourcesPageCapReached(MaxListPages);
                break;
        }

        return sources;
    }

    private static bool AddPageToSources(JsonElement payload, Dictionary<MediaMtxPath, string> sources)
    {
        if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = name.GetString() ?? string.Empty;
            if (MediaMtxPath.IsCanonical(raw) && TryReadSource(item, out var source))
            {
                sources[MediaMtxPath.From(raw)] = source;
            }
        }

        return items.GetArrayLength() > 0;
    }

    private static bool TryReadSource(JsonElement item, out string source)
    {
        source = string.Empty;
        if (!item.TryGetProperty("source", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        source = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool AddPageToPaths(JsonElement payload, List<MediaMtxPath> paths)
    {
        if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var raw = name.GetString() ?? string.Empty;
            if (!MediaMtxPath.IsCanonical(raw))
            {
                continue;
            }
            paths.Add(MediaMtxPath.From(raw));
        }

        return items.GetArrayLength() > 0;
    }

    public async Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();

        using var response = await http.GetAsync($"/v3/paths/get/{path.Value}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotRegistered;
        }
        response.EnsureSuccessStatusCode();

        // Response shape (subset of MediaMTX /v3/paths/get):
        //   { "ready": bool, "readyTime": "ISO-8601" | null, "tracks": [...] }
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        return ParsePathHealth(payload);
    }

    public async Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(CancellationToken cancellationToken)
    {
        // GET /v3/paths/list (the runtime list, not /v3/config/paths/list):
        // each page's `items` array holds every path's current health shaped
        // exactly like a single /v3/paths/get response plus a `name`. One hook
        // call per *page* per sweep tick instead of one per stream — see
        // IRtspGateway.GetAllPathsHealthAsync's doc comment for why that
        // matters.
        //
        // MediaMTX paginates this endpoint (internal/api/paginate.go defaults
        // itemsPerPage to 100, sorted by Name ascending) and does not take a
        // "give me everything" option, so a single unpaged read silently
        // dropped every path past the 100th — path names are cam-<Guid v7>,
        // which sort by creation time, so this was every camera past the
        // 100th at any deployment past that size (250-camera target). Looping
        // until pageCount is exhausted is ReadPagesAsync's job, shared with
        // the sibling /v3/config/paths/list read.
        Dictionary<MediaMtxPath, RtspPathHealth> health = [];

        var pagesRead = 0;
        var itemsUnreadablePage = -1;

        var read = await ReadPagesAsync(
            "/v3/paths/list",
            payload =>
            {
                pagesRead++;
                if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    itemsUnreadablePage = pagesRead - 1;
                    return false;
                }

                return AddPageToHealth(payload, health);
            },
            cancellationToken);

        if (itemsUnreadablePage >= 0)
        {
            throw new HttpRequestException(
                HttpRequestError.InvalidResponse,
                $"MediaMTX path list page {itemsUnreadablePage} has no readable items array; the health sweep cannot tell an unread path from a missing one.");
        }

        return CompleteHealthOrThrow(read, health);
    }

    private static Dictionary<MediaMtxPath, RtspPathHealth> CompleteHealthOrThrow(PageRead read, Dictionary<MediaMtxPath, RtspPathHealth> health)
        // A partial result would be read by the caller as "not registered" for
        // every path past the cut, so a read that cannot finish fails instead.
        => read.End switch
        {
            PageReadEnd.Complete => health,
            PageReadEnd.PageCapReached => throw new HttpRequestException(
                HttpRequestError.InvalidResponse,
                $"MediaMTX path list still reported more pages after {MaxListPages} pages; the health sweep cannot tell an unread path from a missing one."),
            PageReadEnd.PageCountUnreadable => throw new HttpRequestException(
                HttpRequestError.InvalidResponse,
                $"MediaMTX path list reported a pageCount that is not a 32-bit integer on page {read.Page}; the health sweep cannot tell an unread path from a missing one."),
            _ => throw new UnreachableException("ReadPagesAsync returned an undefined PageReadEnd."),
        };

    private static bool AddPageToHealth(JsonElement payload, Dictionary<MediaMtxPath, RtspPathHealth> health)
    {
        if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = name.GetString() ?? string.Empty;
            if (!MediaMtxPath.IsCanonical(raw))
            {
                continue;
            }

            health[MediaMtxPath.From(raw)] = ParsePathHealth(item);
        }

        return items.GetArrayLength() > 0;
    }

    private static readonly RtspPathHealth NotRegistered = new(
        IsReady: false,
        LastError: "path not registered",
        LastFrameAt: null,
        DetectedMode: TranscodeMode.Unknown);

    private static RtspPathHealth ParsePathHealth(JsonElement payload)
    {
        var ready = payload.TryGetProperty("ready", out var readyEl) && readyEl.GetBoolean();
        var readyTime = TryReadIsoTimestamp(payload, "readyTime");

        var mode = ready ? DetectTranscodeMode(payload) : TranscodeMode.Unknown;

        var error = ready ? null : "not ready";

        return new RtspPathHealth(ready, error, readyTime, mode);
    }

    private static DateTimeOffset? TryReadIsoTimestamp(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = element.GetString() ?? string.Empty;

        if (raw.Length == 0)
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static TranscodeMode DetectTranscodeMode(JsonElement payload)
    {
        // Best-effort detection: if MediaMTX's runOnDemand hook ran FFmpeg
        // we'd see ffmpegPath / source.type == "rpiCamera" etc. For v1 the
        // YAML config doesn't enable runOnDemand transcoding so everything is
        // Passthrough. Surface the assumption explicitly.
        if (payload.TryGetProperty("source", out var source) &&
            source.TryGetProperty("type", out var type) &&
            string.Equals(type.GetString(), "ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return TranscodeMode.Software;
        }
        return TranscodeMode.Passthrough;
    }
}
