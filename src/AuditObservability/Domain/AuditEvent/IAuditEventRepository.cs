namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Repository contract for audit-event writes (ADR-0041). Reads
/// go through query handlers directly against the DbContext to
/// keep the search/timeline endpoints cheap; this interface only
/// covers the bus-fed write path.
/// </summary>
public interface IAuditEventRepository
{
    /// <summary>
    /// Stage a new audit row for the next <see cref="SaveAsync"/>.
    /// Idempotency on <see cref="AuditEvent.EventIdentifier"/> is
    /// enforced by the persistence layer's unique index —
    /// callers do not need to check for prior rows.
    /// </summary>
    void Add(AuditEvent audit);

    /// <summary>
    /// Persist every staged row. Implementations should use
    /// <c>INSERT ... ON CONFLICT (event_identifier) DO NOTHING</c>
    /// so a Wolverine at-least-once redelivery is silently
    /// absorbed instead of throwing.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
