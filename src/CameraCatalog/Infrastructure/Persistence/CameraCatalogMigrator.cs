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
    ILogger<CameraCatalogMigrator> log) : IMigrator
{
    public string ContextName => "CameraCatalog";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using CameraCatalogDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation("Applying Camera Catalog EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        log.LogInformation("Camera Catalog migrations applied.");
    }
}
