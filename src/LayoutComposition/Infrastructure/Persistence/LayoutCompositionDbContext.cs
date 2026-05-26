using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the LayoutComposition bounded context (ADR-0009).
/// Owns the <c>layouts</c> + <c>layout_revisions</c> tables. Wolverine
/// outbox tables live in a sibling schema configured by
/// <c>AddWolverineForContext</c> (ADR-0088).
/// </summary>
public sealed class LayoutCompositionDbContext(DbContextOptions<LayoutCompositionDbContext> options)
    : DbContext(options)
{
    public DbSet<Layout> Layouts => Set<Layout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LayoutCompositionDbContext).Assembly);
    }
}
