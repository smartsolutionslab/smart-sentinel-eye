using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Automation.Infrastructure.Persistence;

public sealed class AutomationMigrator(
    IDbContextFactory<AutomationDbContext> dbContextFactory,
    ILogger<AutomationMigrator> log) : IMigrator
{
    public string ContextName => "Automation";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using AutomationDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation("Applying Automation EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        log.LogInformation("Automation migrations applied.");
    }
}
