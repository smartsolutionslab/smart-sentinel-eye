using System.Globalization;
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

            if (!FoldPage(addPage, payload, listPath, page))
            {
                var end = HasItemsArray(payload) ? PageReadEnd.Complete : PageReadEnd.ItemsUnreadable;
                return new PageRead(end, page);
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

    private static bool HasItemsArray(JsonElement payload) =>
        payload.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array;

    // EnsureSuccessStatusCode formats the peer's reason phrase into the message;
    // a hostile or misconfigured peer could put a credential there (spec 409 FR-104).
    private static void EnsureSuccess(HttpResponseMessage response, string route)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            HttpRequestError.Unknown,
            $"MediaMTX {route} answered {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)}.",
            null,
            response.StatusCode);
    }

    private async Task<JsonElement> ReadPageAsync(string listPath, int page, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"{listPath}?page={page}&itemsPerPage=100", cancellationToken);
        EnsureSuccess(response, listPath);

        JsonElement payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw InvalidPage(
                listPath,
                page,
                $"is not valid JSON (line {ex.LineNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}, byte {ex.BytePositionInLine?.ToString(CultureInfo.InvariantCulture) ?? "-"})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            throw InvalidPage(listPath, page, "could not be decoded");
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw InvalidPage(listPath, page, "is not a JSON object");
        }

        return payload;
    }

    private static bool FoldPage(
        Func<JsonElement, bool> addPage,
        JsonElement payload,
        string listPath,
        int page)
    {
        try
        {
            return addPage(payload);
        }
        catch (InvalidOperationException)
        {
            throw InvalidPage(listPath, page, WrongKindReason);
        }
        catch (JsonException)
        {
            throw InvalidPage(listPath, page, WrongKindReason);
        }
    }

    private const string WrongKindReason = "has an item field of the wrong kind or an undecodable string";

    private static HttpRequestException InvalidPage(string listPath, int page, string reason) =>
        new(HttpRequestError.InvalidResponse, $"MediaMTX list {listPath} page {page} {reason}.");

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
            case PageReadEnd.ItemsUnreadable when warn:
                logger.MediaMtxConfiguredSourcesItemsUnreadable(read.Page);
                break;
            case PageReadEnd.ItemsUnreadable:
                logger.MediaMtxConfiguredSourcesItemsStillUnreadable(read.Page);
                break;
            default:
                logger.MediaMtxConfiguredSourcesPageCapStillReached(MaxListPages);
                break;
        }
    }
}
