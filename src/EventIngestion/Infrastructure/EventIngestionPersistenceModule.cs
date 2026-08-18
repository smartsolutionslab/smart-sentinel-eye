using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(builder).IsNotNull();

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<EventIngestionDbContext>(options =>
            options.UseNpgsql(connectionString)
                .AddInterceptors(new AggregateVersionInterceptor()));

        builder.Services.AddSingleton<IMigrator, EventIngestionMigrator>();
        builder.Services.AddSingleton<FabPartitionProvisioner>();

        // EventPartitionRolloverMigrator is NOT registered here, and that is a
        // change from spec 006. Since spec 019 it needs an IProvisionedFabSource
        // — the realm's list of fabs — which only MigrationRunner can supply,
        // because the realm belongs to Identity and no context may reference
        // another. Registering it here would leave every Api host holding a
        // singleton it cannot construct.
        //
        // It still runs after the EF migrations: MigrationRunner registers it
        // last, and runs IMigrator instances in registration order (ADR-0067).

        return builder;
    }
}
