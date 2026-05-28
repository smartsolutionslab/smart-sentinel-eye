using Microsoft.EntityFrameworkCore;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the EventIngestion bounded context. Owns the
/// partitioned <c>events</c> table (spec 006 FR-012). Wolverine outbox
/// tables live in a sibling schema configured by
/// <c>AddWolverineForContext</c> (ADR-0088).
/// </summary>
public sealed class EventIngestionDbContext(DbContextOptions<EventIngestionDbContext> options)
    : DbContext(options)
{
    public DbSet<EventAggregate> Events => Set<EventAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventIngestionDbContext).Assembly);
    }
}
