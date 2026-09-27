namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// The one thing both enforcement points ask (spec 270, ADR-0160 §2): is this
/// token refused because its client was disabled after it was minted? Reads
/// an in-process snapshot only — no I/O, no lock on the request path
/// (FR-003).
/// </summary>
public interface IRevokedClientRegistry
{
    bool Refuses(string? azp, DateTimeOffset? issuedAt);
}
