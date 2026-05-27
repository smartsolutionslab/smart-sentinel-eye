using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the OverlayDesigner bounded context.
/// Owns the <c>overlays</c> + <c>overlay_revisions</c> tables. Wolverine
/// outbox tables live in a sibling schema configured by
/// <c>AddWolverineForContext</c> (ADR-0088).
/// </summary>
public sealed class OverlayDesignerDbContext(DbContextOptions<OverlayDesignerDbContext> options)
    : DbContext(options)
{
    public DbSet<Overlay> Overlays => Set<Overlay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OverlayDesignerDbContext).Assembly);
    }
}
