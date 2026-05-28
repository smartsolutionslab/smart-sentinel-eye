using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the DbContext without
/// going through Aspire / configuration.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EventIngestionDbContext>
{
    public EventIngestionDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<EventIngestionDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=design-time;Username=design;Password=design");
        return new EventIngestionDbContext(builder.Options);
    }
}
