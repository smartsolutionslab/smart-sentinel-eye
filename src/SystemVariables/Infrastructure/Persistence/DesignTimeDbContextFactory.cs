using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the DbContext without
/// going through Aspire / configuration.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SystemVariablesDbContext>
{
    public SystemVariablesDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SystemVariablesDbContext> builder = new();
        builder.UseNpgsql("Host=localhost;Database=design-time;Username=design;Password=design");
        return new SystemVariablesDbContext(builder.Options);
    }
}
