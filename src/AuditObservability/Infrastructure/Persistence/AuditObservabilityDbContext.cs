using Microsoft.EntityFrameworkCore;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the AuditObservability bounded context
/// (spec 009). Owns the <c>audit_events</c> hypertable; Wolverine
/// outbox tables live in a sibling schema configured by
/// <c>AddWolverineForContext</c> (ADR-0088).
/// </summary>
public sealed class AuditObservabilityDbContext(DbContextOptions<AuditObservabilityDbContext> options)
    : DbContext(options)
{
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditObservabilityDbContext).Assembly);
    }
}
