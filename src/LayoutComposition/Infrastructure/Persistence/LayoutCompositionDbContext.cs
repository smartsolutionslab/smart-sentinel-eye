using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the LayoutComposition bounded context (ADR-0009).
/// Owns the <c>layouts</c> + <c>layout_revisions</c> tables and, since
/// spec 258, <c>walls</c> + <c>wall_scenes</c>. Wolverine outbox tables live
/// in a sibling schema configured by <c>AddWolverineForContext</c>
/// (ADR-0088).
/// </summary>
public sealed class LayoutCompositionDbContext(DbContextOptions<LayoutCompositionDbContext> options)
    : DbContext(options)
{
    public DbSet<Layout> Layouts => Set<Layout>();

    public DbSet<Wall> Walls => Set<Wall>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Ensure.That(modelBuilder).IsNotNull();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LayoutCompositionDbContext).Assembly);
    }
}
