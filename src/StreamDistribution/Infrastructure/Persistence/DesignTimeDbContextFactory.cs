using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the DbContext without going
/// through Aspire / configuration. Uses a placeholder connection string —
/// only the EF model metadata is needed at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StreamDistributionDbContext>
{
    public StreamDistributionDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<StreamDistributionDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=design-time;Username=design;Password=design");
        return new StreamDistributionDbContext(builder.Options);
    }
}
