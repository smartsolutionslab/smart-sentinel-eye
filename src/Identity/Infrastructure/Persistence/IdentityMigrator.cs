using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

public sealed class IdentityMigrator(
    IDbContextFactory<IdentityDbContext> dbContextFactory,
    ILogger<IdentityMigrator> logger) : IMigrator
{
    public string ContextName => "Identity";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using IdentityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Applying Identity EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Identity migrations applied.");
    }
}
