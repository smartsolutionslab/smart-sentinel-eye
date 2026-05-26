using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure;

/// <summary>
/// Slim persistence-only composition for the Stream Distribution context.
/// Used by the MigrationRunner (ADR-0067), which doesn't need Wolverine,
/// MediaMTX, repositories, or background services — just the DbContext +
/// IMigrator. Mirrors AddCameraCatalogPersistence from spec 001.
/// </summary>
public static class StreamDistributionPersistenceModule
{
    public const string DatabaseConnectionName = "stream-distribution-db";

    public static IHostApplicationBuilder AddStreamDistributionPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<StreamDistributionDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, StreamDistributionMigrator>();

        return builder;
    }
}
