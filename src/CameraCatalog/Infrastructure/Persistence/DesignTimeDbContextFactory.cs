using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef migrations add` build the DbContext without going through
/// Aspire / configuration. Uses a placeholder connection string — only the
/// EF model metadata is needed at design time, not a real connection.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CameraCatalogDbContext>
{
    public CameraCatalogDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CameraCatalogDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=design-time;Username=design;Password=design");
        return new CameraCatalogDbContext(builder.Options);
    }
}
