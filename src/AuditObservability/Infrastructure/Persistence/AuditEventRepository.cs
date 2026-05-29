using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

/// <summary>
/// Production repository. <see cref="SaveAsync"/> emits an
/// <c>INSERT ... ON CONFLICT (event_identifier) DO NOTHING</c>
/// per pending row so Wolverine at-least-once redeliveries are
/// absorbed silently — the unique index on
/// <see cref="AuditEvent.EventIdentifier"/> is the gate.
/// </summary>
public sealed class AuditEventRepository(AuditObservabilityDbContext dbContext) : IAuditEventRepository
{
    private readonly List<AuditEventEntity> _pending = new();

    public void Add(AuditEventEntity audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        _pending.Add(audit);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0) return;

        foreach (AuditEventEntity row in _pending)
        {
            string? fab = row.Fab?.Value;
            string? resourceKind = row.ResourceKind?.Value;
            string? resourceIdentifier = row.ResourceIdentifier?.Value;
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO audit_events (
                    audit_id, occurred_at, received_at, fab_id,
                    event_kind, resource_kind, resource_identifier,
                    actor_identifier, actor_username, event_identifier,
                    payload, payload_size_bytes, schema_version)
                VALUES (
                    {row.Id.Value}, {row.OccurredAt}, {row.ReceivedAt},
                    {fab},
                    {row.EventKind.Value},
                    {resourceKind},
                    {resourceIdentifier},
                    {row.Actor.Value}, {row.ActorUsername},
                    {row.EventIdentifier.Value},
                    {row.Payload}::jsonb, {row.PayloadSizeBytes},
                    {row.SchemaVersion})
                ON CONFLICT (event_identifier) DO NOTHING
                """,
                cancellationToken).ConfigureAwait(false);
        }

        _pending.Clear();
    }
}
