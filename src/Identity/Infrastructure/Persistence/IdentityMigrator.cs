using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

public sealed class IdentityMigrator(
    IDbContextFactory<IdentityDbContext> dbContextFactory,
    ILogger<IdentityMigrator> log) : IMigrator
{
    public string ContextName => "Identity";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using IdentityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation("Applying Identity EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        log.LogInformation("Identity migrations applied.");
    }
}
