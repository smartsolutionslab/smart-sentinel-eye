using Microsoft.EntityFrameworkCore;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Automation bounded context (spec 007).
/// Owns the <c>rules</c> table. Wolverine outbox tables live in a
/// sibling schema configured by <c>AddWolverineForContext</c>
/// (ADR-0088).
/// </summary>
public sealed class AutomationDbContext(DbContextOptions<AutomationDbContext> options)
    : DbContext(options)
{
    public DbSet<RuleAggregate> Rules => Set<RuleAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutomationDbContext).Assembly);
    }
}
