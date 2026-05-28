using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Automation.Infrastructure;

/// <summary>
/// Slim persistence-only composition for the Automation context.
/// Used by the MigrationRunner (ADR-0067) which doesn't need
/// Wolverine, hosted services, or the rule cache — just the
/// DbContext + IMigrator.
/// </summary>
public static class AutomationPersistenceModule
{
    public const string DatabaseConnectionName = "automation-db";

    public static IHostApplicationBuilder AddAutomationPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<AutomationDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, AutomationMigrator>();

        return builder;
    }
}
