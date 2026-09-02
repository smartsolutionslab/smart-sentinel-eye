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
        ActorUsername: audit.ActorUsername?.Value,
        EventIdentifier: audit.EventIdentifier.Value,
        Payload: audit.Payload.Content.Value,
        PayloadSizeBytes: audit.Payload.Size.Value,
        SchemaVersion: audit.SchemaVersion.Value);
}
