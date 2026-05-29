using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.AuditObservability.Infrastructure;

public static class AuditObservabilityPersistenceModule
{
    public const string DatabaseConnectionName = "audit-db";

    public static IHostApplicationBuilder AddAuditObservabilityPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<AuditObservabilityDbContext>(options =>
            options.UseNpgsql(connectionString));
        builder.Services.AddDbContext<AuditObservabilityDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, AuditObservabilityMigrator>();

        return builder;
    }
}
