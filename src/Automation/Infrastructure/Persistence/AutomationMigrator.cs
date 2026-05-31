using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence;

public sealed class AutomationMigrator(
    IDbContextFactory<AutomationDbContext> dbContextFactory,
    ILogger<AutomationMigrator> logger) : IMigrator
{
    public string ContextName => "Automation";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using AutomationDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Log.ApplyingMigrations(logger);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        Log.MigrationsApplied(logger);
    }
}
