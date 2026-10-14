using System.Reflection;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.TestData;

/// <summary>
/// Gives a built audit row a literal identifier. <c>AuditEvent.From</c> always draws a fresh
/// Guid v7, so a test that must pin tie-break order or a cursor literal cannot otherwise
/// choose it. The identifier is set through the property's own (init-only) accessor, so
/// everything else about the row is exactly what <see cref="AuditEventBuilder"/> built.
/// </summary>
internal static class PinnedAuditEvent
{
    private static readonly PropertyInfo IdentifierProperty =
        typeof(AuditEventEntity).GetProperty(nameof(AuditEventEntity.Id))!;

    public static AuditEventEntity WithIdentifier(this AuditEventEntity auditEvent, string identifier)
    {
        IdentifierProperty.SetValue(auditEvent, AuditEventIdentifier.From(Guid.Parse(identifier)));
        return auditEvent;
    }
}
