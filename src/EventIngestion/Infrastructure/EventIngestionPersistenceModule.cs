using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.EventIngestion.Infrastructure;

/// <summary>
/// Slim persistence-only composition for the EventIngestion context.
/// Used by the MigrationRunner (ADR-0067) which doesn't need
/// Wolverine, hosted services, or the MQTT subscriber — just the
/// DbContext + IMigrator.
/// </summary>
public static class EventIngestionPersistenceModule
{
    public const string DatabaseConnectionName = "event-ingestion-db";

    public static IHostApplicationBuilder AddEventIngestionPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<EventIngestionDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, EventIngestionMigrator>();
        // Partition rollover runs *after* the EF migrations because
        // it depends on the parent `events` table existing. The
        // MigrationRunner runs IMigrator instances in registration
        // order (ADR-0067), so this AddSingleton ordering matters.
        builder.Services.AddSingleton<IMigrator, EventPartitionRolloverMigrator>();

        return builder;
    }
}
