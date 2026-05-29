using SmartSentinelEye.AuditObservability.Application.Queries;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

/// <summary>
/// Production read-side seam — wraps the DbContext's
/// <c>AuditEvents</c> <see cref="IQueryable{T}"/> so Application
/// handlers can compose LINQ on it without taking a DbContext
/// dependency directly.
/// </summary>
public sealed class AuditEventQuerySource(AuditObservabilityDbContext dbContext) : IAuditEventQuerySource
{
    public IQueryable<AuditEventEntity> AuditEvents => dbContext.AuditEvents.AsQueryable();
}
