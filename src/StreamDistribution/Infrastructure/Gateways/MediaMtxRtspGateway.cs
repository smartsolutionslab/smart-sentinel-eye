using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

/// <summary>
/// IRtspGateway implementation that talks to MediaMTX's HTTP control API
/// (v3). The typed HttpClient is configured against the Aspire-resolved
/// <c>mediamtx:api</c> endpoint. Polly retry is applied at the HttpClient
/// factory level in <see cref="StreamDistributionInfrastructureModule"/>.
/// </summary>
public sealed class MediaMtxRtspGateway(HttpClient http, ILogger<MediaMtxRtspGateway> log) : IRtspGateway
{
    public async Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(rtspSourceUrl);

        // POST /v3/config/paths/add/{name}
        // Body: { "source": "rtsp://..." } — MediaMTX pulls the source on demand.
        // FFmpeg fallback for non-H.264 inputs is implicit: MediaMTX
        // re-packetizes H.264 and transparently transcodes everything else
        // using the bundled FFmpeg binary (configured via `runOnDemand`-style
        // hooks in the YAML; not needed for v1 because we only ship the
        // latest-ffmpeg image).
        using HttpResponseMessage response = await http
            .PostAsJsonAsync($"/v3/config/paths/add/{path.Value}", new { source = rtspSourceUrl }, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        log.LogInformation("Registered MediaMTX path {Path} -> {Source}.", path, rtspSourceUrl);
    }

    public async Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        using HttpResponseMessage response = await http
            .DeleteAsync($"/v3/config/paths/delete/{path.Value}", cancellationToken)
            .ConfigureAwait(false);

        // 404 is fine — path may have already been removed by a prior call.
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
        log.LogInformation("Removed MediaMTX path {Path}.", path);
    }

    public async Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        using HttpResponseMessage response = await http
            .GetAsync($"/v3/paths/get/{path.Value}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new RtspPathHealth(
                IsReady: false,
                LastError: Option<string>.Some("path not registered"),
                LastFrameAt: Option<DateTimeOffset>.None,
                DetectedMode: TranscodeMode.Unknown);
        }
        response.EnsureSuccessStatusCode();

        // Response shape (subset of MediaMTX /v3/paths/get):
        //   { "ready": bool, "readyTime": "ISO-8601" | null, "tracks": [...] }
        JsonElement payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(cancellationToken)
            .ConfigureAwait(false);

        bool ready = payload.TryGetProperty("ready", out JsonElement readyEl) && readyEl.GetBoolean();
        Option<DateTimeOffset> readyTime = TryReadIsoTimestamp(payload, "readyTime");

        TranscodeMode mode = ready
            ? DetectTranscodeMode(payload)
            : TranscodeMode.Unknown;

        Option<string> error = ready
            ? Option<string>.None
            : Option<string>.Some("not ready");

        return new RtspPathHealth(ready, error, readyTime, mode);
    }

    private static Option<DateTimeOffset> TryReadIsoTimestamp(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out JsonElement element)) return Option<DateTimeOffset>.None;
        if (element.ValueKind != JsonValueKind.String) return Option<DateTimeOffset>.None;
        string? raw = element.GetString();
        if (string.IsNullOrEmpty(raw)) return Option<DateTimeOffset>.None;
        return DateTimeOffset.TryParse(
            raw, System.Globalization.CultureInfo.InvariantCulture, out DateTimeOffset parsed)
            ? Option<DateTimeOffset>.Some(parsed)
            : Option<DateTimeOffset>.None;
    }

    private static TranscodeMode DetectTranscodeMode(JsonElement payload)
    {
        // Best-effort detection: if MediaMTX's runOnDemand hook ran FFmpeg
        // we'd see ffmpegPath / source.type == "rpiCamera" etc. For v1 the
        // YAML config doesn't enable runOnDemand transcoding so everything is
        // Passthrough. Surface the assumption explicitly.
        if (payload.TryGetProperty("source", out JsonElement source) &&
            source.TryGetProperty("type", out JsonElement type) &&
            string.Equals(type.GetString(), "ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return TranscodeMode.Software;
        }
        return TranscodeMode.Passthrough;
    }
}
