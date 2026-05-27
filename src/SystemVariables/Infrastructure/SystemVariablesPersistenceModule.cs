using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.SystemVariables.Infrastructure;

/// <summary>
/// Slim persistence-only composition for the SystemVariables context.
/// Used by the MigrationRunner (ADR-0067), which doesn't need
/// Wolverine, repositories, hosted services, or SignalR — just the
/// DbContext + IMigrator.
/// </summary>
public static class SystemVariablesPersistenceModule
{
    public const string DatabaseConnectionName = "system-variables-db";

    public static IHostApplicationBuilder AddSystemVariablesPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseConnectionName}' is required.");

        builder.Services.AddDbContextFactory<SystemVariablesDbContext>(options =>
            options.UseNpgsql(connectionString));

        builder.Services.AddSingleton<IMigrator, SystemVariablesMigrator>();

        return builder;
    }
}
