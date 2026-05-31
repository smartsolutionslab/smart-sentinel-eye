using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

/// <summary>
/// MigrationRunner-invoked migrator for the LayoutComposition DbContext
/// (ADR-0067). Applies pending EF Core migrations once; idempotent.
/// </summary>
public sealed class LayoutCompositionMigrator(
    IDbContextFactory<LayoutCompositionDbContext> dbContextFactory,
    ILogger<LayoutCompositionMigrator> logger) : IMigrator
{
    public string ContextName => "LayoutComposition";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using LayoutCompositionDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Applying LayoutComposition EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("LayoutComposition migrations applied.");
    }
}
