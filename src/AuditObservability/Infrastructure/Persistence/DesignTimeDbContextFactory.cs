using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add` so the
/// CLI can spin up the DbContext outside the Aspire host. The
/// connection string is a dev placeholder; only the model + the
/// hypertable migration SQL are generated against it.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditObservabilityDbContext>
{
    public AuditObservabilityDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AuditObservabilityDbContext> options = new();
        options.UseNpgsql("Host=localhost;Port=5432;Database=audit-db;Username=postgres;Password=postgres");
        return new AuditObservabilityDbContext(options.Options);
    }
}
