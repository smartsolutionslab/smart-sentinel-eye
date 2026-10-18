using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

public sealed partial class MediaMtxRtspGateway
{
    private async Task<PageRead> ReadPagesAsync(
        string listPath,
        Func<JsonElement, bool> addPage,
        CancellationToken cancellationToken)
    {
        for (var page = 0; page < MaxListPages; page++)
        {
            var payload = await ReadPageAsync(listPath, page, cancellationToken);

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

    private async Task<JsonElement> ReadPageAsync(string listPath, int page, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"{listPath}?page={page}&itemsPerPage=100", cancellationToken);
        response.EnsureSuccessStatusCode();

        JsonElement payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException(
                HttpRequestError.InvalidResponse,
                $"MediaMTX list {listPath} page {page} is not valid JSON.",
                ex);
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new HttpRequestException(
                HttpRequestError.InvalidResponse,
                $"MediaMTX list {listPath} page {page} is not a JSON object.");
        }

        return payload;
    }

    private static void EnsureObjectItem(JsonElement item, string listName)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new HttpRequestException(
                HttpRequestError.InvalidResponse,
                $"MediaMTX {listName} list has an items element that is not a JSON object.");
        }
    }

    private static bool AddPageToSources(JsonElement payload, Dictionary<MediaMtxPath, string> sources)
    {
        if (!payload.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            EnsureObjectItem(item, "config path");

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
            EnsureObjectItem(item, "config path");

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

    // Spec 409: this read runs every sweep tick, so a persistently cut-short
    // list would otherwise Warn on every one. Only the source repair's read is
    // throttled; the startup reconcile's reads run once.
    private void WarnOnUnfinishedRead(PageRead read)
    {
        if (read.End == PageReadEnd.Complete)
        {
            readWarnings.Clear();
            return;
        }

        var warn = readWarnings.ShouldWarn(read.End);
        switch (read.End)
        {
            case PageReadEnd.PageCountUnreadable when warn:
                logger.MediaMtxConfiguredSourcesPageCountUnreadable(read.Page);
                break;
            case PageReadEnd.PageCountUnreadable:
                logger.MediaMtxConfiguredSourcesPageCountStillUnreadable(read.Page);
                break;
            case PageReadEnd.PageCapReached when warn:
                logger.MediaMtxConfiguredSourcesPageCapReached(MaxListPages);
                break;
            default:
                logger.MediaMtxConfiguredSourcesPageCapStillReached(MaxListPages);
                break;
        }
    }
}
