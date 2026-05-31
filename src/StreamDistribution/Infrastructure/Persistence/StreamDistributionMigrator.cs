using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

/// <summary>
/// MigrationRunner-invoked migrator for the Stream Distribution DbContext
/// (ADR-0067). Applies pending EF Core migrations once; idempotent.
/// </summary>
public sealed class StreamDistributionMigrator(
    IDbContextFactory<StreamDistributionDbContext> dbContextFactory,
    ILogger<StreamDistributionMigrator> logger) : IMigrator
{
    public string ContextName => "StreamDistribution";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using StreamDistributionDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Applying Stream Distribution EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Stream Distribution migrations applied.");
    }
}
