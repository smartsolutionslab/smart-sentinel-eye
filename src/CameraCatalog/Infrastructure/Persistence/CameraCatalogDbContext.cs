using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Camera Catalog bounded context (ADR-0009). Owns
/// the camera aggregate's table; Wolverine outbox tables are auto-managed in
/// a sibling schema configured by AddWolverineForContext (ADR-0088).
/// </summary>
public sealed class CameraCatalogDbContext(DbContextOptions<CameraCatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Camera> Cameras => Set<Camera>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CameraCatalogDbContext).Assembly);
    }
}
