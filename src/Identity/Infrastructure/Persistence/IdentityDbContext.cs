using Microsoft.EntityFrameworkCore;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Identity bounded context (spec 008).
/// Owns the <c>registered_clients</c> table. Wolverine outbox
/// tables live in a sibling schema configured by
/// <c>AddWolverineForContext</c> (ADR-0088).
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<RegisteredClientAggregate> RegisteredClients => Set<RegisteredClientAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
