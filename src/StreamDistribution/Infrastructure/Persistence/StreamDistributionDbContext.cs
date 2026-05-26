using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Stream Distribution bounded context (ADR-0009).
/// Owns the streams table. Wolverine outbox tables live in a sibling
/// schema configured by AddWolverineForContext (ADR-0088).
/// </summary>
public sealed class StreamDistributionDbContext(DbContextOptions<StreamDistributionDbContext> options)
    : DbContext(options)
{
    public DbSet<Domain.Stream.Stream> Streams => Set<Domain.Stream.Stream>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StreamDistributionDbContext).Assembly);
    }
}
