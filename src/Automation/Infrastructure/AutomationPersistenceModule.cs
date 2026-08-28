using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Automation.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(builder).IsNotNull();

        string connectionString = builder.GetBoundedPostgresConnectionString(DatabaseConnectionName);

        builder.Services.AddDbContextFactory<AutomationDbContext>(options =>
            options.UseNpgsql(connectionString)
                .AddInterceptors(new AggregateVersionInterceptor()));

        builder.Services.AddSingleton<IMigrator, AutomationMigrator>();

        return builder;
    }
}
