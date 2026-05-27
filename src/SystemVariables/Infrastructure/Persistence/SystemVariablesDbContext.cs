using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the SystemVariables bounded context.
/// Owns the <c>system_variables</c> table. Wolverine outbox tables
/// live in a sibling schema configured by <c>AddWolverineForContext</c>
/// (ADR-0088).
/// </summary>
public sealed class SystemVariablesDbContext(DbContextOptions<SystemVariablesDbContext> options)
    : DbContext(options)
{
    public DbSet<Variable> Variables => Set<Variable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SystemVariablesDbContext).Assembly);
    }
}
