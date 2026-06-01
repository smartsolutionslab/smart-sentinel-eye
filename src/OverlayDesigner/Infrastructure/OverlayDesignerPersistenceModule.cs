using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(builder).IsNotNull();

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName) ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<OverlayDesignerDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, OverlayDesignerMigrator>();

        return builder;
    }
}
