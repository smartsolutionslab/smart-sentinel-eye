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
    /// Idempotency on the pair <see cref="AuditEvent.EventIdentifier"/> +
    /// <c>OccurredAt</c> is enforced by the persistence layer's unique
    /// index — callers do not need to check for prior rows, provided a
    /// redelivery of the same event carries the same <c>OccurredAt</c>
    /// rather than re-deriving it.
    /// </summary>
    void Add(AuditEvent audit);

    /// <summary>
    /// Persist every staged row. Implementations should use
    /// <c>INSERT ... ON CONFLICT (event_identifier, occurred_at) DO
    /// NOTHING</c> so a Wolverine at-least-once redelivery is silently
    /// absorbed instead of throwing. The index carries
    /// <c>occurred_at</c> because TimescaleDB forbids a unique index
    /// that omits the hypertable partitioning column (TS103); a given
    /// event's <c>occurred_at</c> is stable, so the pair still dedups
    /// redeliveries.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
