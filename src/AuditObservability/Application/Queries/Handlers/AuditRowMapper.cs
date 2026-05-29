using SmartSentinelEye.AuditObservability.Application.DTOs;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

internal static class AuditRowMapper
{
    public static AuditRowDto Map(AuditEventEntity audit) => new(
        AuditIdentifier: audit.Id.Value,
        OccurredAt: audit.OccurredAt,
        ReceivedAt: audit.ReceivedAt,
        Fab: audit.Fab?.Value,
        EventKind: audit.EventKind.Value,
        ResourceKind: audit.ResourceKind?.Value,
        ResourceIdentifier: audit.ResourceIdentifier?.Value,
        ActorIdentifier: audit.Actor.Value,
        ActorIsSystem: audit.Actor.IsSystem,
        ActorUsername: audit.ActorUsername,
        EventIdentifier: audit.EventIdentifier.Value,
        Payload: audit.Payload,
        PayloadSizeBytes: audit.PayloadSizeBytes,
        SchemaVersion: audit.SchemaVersion);
}
