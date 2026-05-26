using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Abstracts the SFU's HTTP control plane behind a domain-friendly
/// interface (ADR-0061 forward-compatible interfaces). The concrete
/// implementation in Infrastructure talks to MediaMTX; a future swap
/// (LiveKit, raw Pion) is a one-class change.
/// </summary>
public interface IRtspGateway
{
    Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken);

    Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken);

    Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of an RTSP path's runtime state as reported by the SFU.
/// </summary>
/// <param name="IsReady"><c>true</c> when the SFU has decoded at least one frame in the recent past.</param>
/// <param name="LastError">Most recent error string from the SFU, if any (None when the path has never failed).</param>
/// <param name="LastFrameAt">When the SFU last received a frame (None until the first frame).</param>
/// <param name="DetectedMode">Whether the SFU is in passthrough or software-transcode mode for this path.</param>
public sealed record RtspPathHealth(
    bool IsReady,
    Option<string> LastError,
    Option<DateTimeOffset> LastFrameAt,
    TranscodeMode DetectedMode);
