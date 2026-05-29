using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

/// <summary>
/// Read-side IQueryable seam for audit rows (ADR-0041).
/// Infrastructure backs it with the DbContext; Application stays
/// EF-Core-free at the call site so handler tests can substitute
/// an in-memory IQueryable.
/// </summary>
public interface IAuditEventQuerySource
{
    IQueryable<AuditEventEntity> AuditEvents { get; }
}
