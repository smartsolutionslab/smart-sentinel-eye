using SmartSentinelEye.AuditObservability.Application.DTOs;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

internal static class AuditRowMapper
{
    public static AuditRowDto Map(AuditEventEntity audit) => new(
        AuditIdentifier: audit.Id.Value,
        OccurredAt: audit.OccurredAt,
        ReceivedAt: audit.ReceivedAt,
        Fab: audit.Fab.HasValue ? audit.Fab.Value.Value : null,
        EventKind: audit.EventKind.Value,
        ResourceKind: audit.ResourceKind.HasValue ? audit.ResourceKind.Value.Value : null,
        ResourceIdentifier: audit.ResourceIdentifier.HasValue ? audit.ResourceIdentifier.Value.Value : null,
        ActorIdentifier: audit.Actor.Value,
        ActorIsSystem: audit.Actor.IsSystem,
        ActorUsername: audit.ActorUsername.HasValue ? audit.ActorUsername.Value : null,
        EventIdentifier: audit.EventIdentifier.Value,
        Payload: audit.Payload,
        PayloadSizeBytes: audit.PayloadSizeBytes,
        SchemaVersion: audit.SchemaVersion);
}
