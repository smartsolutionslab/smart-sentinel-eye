using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;

/// <summary>
/// MigrationRunner-invoked migrator for the Camera Catalog DbContext
/// (ADR-0067). Applies pending EF Core migrations once; idempotent.
/// </summary>
public sealed class CameraCatalogMigrator(
    IDbContextFactory<CameraCatalogDbContext> dbContextFactory,
    ILogger<CameraCatalogMigrator> logger) : IMigrator
{
    public string ContextName => "CameraCatalog";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using CameraCatalogDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Applying Camera Catalog EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Camera Catalog migrations applied.");
    }
}
