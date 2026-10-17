#nullable enable
namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// Abstracts the SFU's HTTP control plane behind a domain-friendly
/// interface (ADR-0061 forward-compatible interfaces). The concrete
/// implementation in Infrastructure talks to MediaMTX; a future swap
/// (LiveKit, raw Pion) is a one-class change.
/// </summary>
public interface IRtspGateway
{
    /// <summary>
    /// Registers a path with MediaMTX, pointed at <paramref name="rtspSourceUrl"/>.
    ///
    /// <para>
    /// Idempotent on the path name (spec 309 FR-002): a 400 for a path that
    /// already exists succeeds without verifying its configured source
    /// matches <paramref name="rtspSourceUrl"/>. This makes a redelivery of a
    /// provisioning attempt — or a resilience-handler retry of a lost
    /// response — safe to call again, but it does not confirm a retry's path
    /// is pointed at the same source as the first attempt's — the periodic
    /// <c>MediaMtxSourceRepairSweep</c> is what repairs that. Any other
    /// rejection (e.g. a source MediaMTX cannot parse) still throws
    /// <see cref="HttpRequestException"/>.
    /// </para>
    /// </summary>
    Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken);

    Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken);

    /// <summary>
    /// Points an existing path at a different source, keeping the path itself
    /// (spec 029 FR-013).
    ///
    /// <para>
    /// Distinct from remove-then-add on purpose. Removing and re-adding leaves
    /// a window in which the path does not exist, and the health watcher sweeps
    /// every two seconds — so an address correction would reliably produce a
    /// spurious Degraded announcement, which since #1801 reaches every
    /// subscriber and the audit trail. Changing the source in place has no such
    /// window, and keeps the path name a viewer is watching (FR-014).
    /// </para>
    /// </summary>
    Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken);

    Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches health for every path MediaMTX currently reports
    /// (<c>GET /v3/paths/list</c>) rather than one <see cref="GetPathHealthAsync"/>
    /// round trip per path.
    ///
    /// <para>
    /// <c>StreamHealthWatcher</c>'s sweep is the reason this exists. ADR-0171
    /// routes every control-API call — including this one — back through the
    /// same <c>/streams/authorize</c> hook a real WHEP viewer's open uses, and
    /// that hook is rate-limited per source address. Probing per path scaled
    /// the hook-call rate with the camera count: at the 250-camera production
    /// target and a 2 s poll, ~125 hook calls/s against a 2000/min (~33/s)
    /// ceiling — enough to start refusing real viewers with no attacker
    /// involved. One call per *page* per sweep tick — MediaMTX paginates this
    /// endpoint at 100 items and takes no "give me everything" option — stays
    /// clear of that ceiling at any realistic scale: ~3 calls/tick at the
    /// 250-camera target.
    /// </para>
    ///
    /// <para>
    /// A path MediaMTX does not report is simply absent from the result — the
    /// caller reads that the same way <see cref="GetPathHealthAsync"/>'s own
    /// 404 branch does, as "not registered", not as a probe failure.
    /// </para>
    ///
    /// <para>
    /// A list that cannot be read to its end, because a <c>pageCount</c> cannot be
    /// read or the page cap is reached, throws <see cref="HttpRequestException"/>
    /// and never returns part of the list. A caller reads an absent path as "not
    /// registered", so a partial result would report live streams as gone.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists every configured MediaMtx path name. Used by the startup
    /// reconciler to find orphans (paths in MediaMTX that no longer back
    /// a DB stream — e.g. left over after a manual MediaMTX restart, or a
    /// stream that was deleted while MediaMTX was down).
    /// </summary>
    Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists the configured <c>source</c> of every canonical path
    /// (<c>GET /v3/config/paths/list</c>), verbatim. The value is MediaMTX's own
    /// vocabulary (<c>publisher</c>, <c>rtsp://…</c>), not necessarily a
    /// <see cref="StreamSourceUrl"/>, so it stays a string. A path whose
    /// <c>source</c> is missing or not a string is omitted. Used by the source
    /// repair to find a path pulling an address the database does not record.
    /// </summary>
    Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of an RTSP path's runtime state as reported by the SFU.
/// </summary>
/// <param name="IsReady"><c>true</c> when the SFU has decoded at least one frame in the recent past.</param>
/// <param name="LastError">Most recent error string from the SFU, if any (null when the path has never failed).</param>
/// <param name="LastFrameAt">When the SFU last received a frame (null until the first frame).</param>
/// <param name="DetectedMode">Whether the SFU is in passthrough or software-transcode mode for this path.</param>
public sealed record RtspPathHealth(
    bool IsReady,
    string? LastError,
    DateTimeOffset? LastFrameAt,
    TranscodeMode DetectedMode);
