using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// MigrationRunner-invoked migrator for the SystemVariables DbContext
/// (ADR-0067). Applies pending EF Core migrations once; idempotent.
/// </summary>
public sealed class SystemVariablesMigrator(
    IDbContextFactory<SystemVariablesDbContext> dbContextFactory,
    ILogger<SystemVariablesMigrator> logger) : IMigrator
{
    public string ContextName => "SystemVariables";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using SystemVariablesDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Applying SystemVariables EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("SystemVariables migrations applied.");
    }
}
