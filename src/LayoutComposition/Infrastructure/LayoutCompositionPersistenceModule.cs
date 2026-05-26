using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.LayoutComposition.Infrastructure;

/// <summary>
/// Slim persistence-only composition for the LayoutComposition context.
/// Used by the MigrationRunner (ADR-0067), which doesn't need Wolverine,
/// SignalR, repositories, or background services — just the DbContext +
/// IMigrator. Mirrors AddStreamDistributionPersistence from spec 002.
/// </summary>
public static class LayoutCompositionPersistenceModule
{
    public const string DatabaseConnectionName = "layout-composition-db";

    public static IHostApplicationBuilder AddLayoutCompositionPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<LayoutCompositionDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, LayoutCompositionMigrator>();

        return builder;
    }
}
