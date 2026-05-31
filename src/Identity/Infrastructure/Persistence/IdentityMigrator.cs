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

        Log.ApplyingMigrations(logger);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        Log.MigrationsApplied(logger);
    }
}
