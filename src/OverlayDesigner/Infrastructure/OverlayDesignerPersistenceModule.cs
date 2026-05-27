using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure;

/// <summary>
/// Slim persistence-only composition for the OverlayDesigner context.
/// Used by the MigrationRunner (ADR-0067), which doesn't need Wolverine,
/// SignalR, repositories, or background services — just the DbContext +
/// IMigrator.
/// </summary>
public static class OverlayDesignerPersistenceModule
{
    public const string DatabaseConnectionName = "overlay-designer-db";

    public static IHostApplicationBuilder AddOverlayDesignerPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<OverlayDesignerDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, OverlayDesignerMigrator>();

        return builder;
    }
}
