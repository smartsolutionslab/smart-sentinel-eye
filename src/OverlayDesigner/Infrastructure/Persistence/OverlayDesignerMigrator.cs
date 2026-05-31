using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

/// <summary>
/// MigrationRunner-invoked migrator for the OverlayDesigner DbContext
/// (ADR-0067). Applies pending EF Core migrations once; idempotent.
/// </summary>
public sealed class OverlayDesignerMigrator(
    IDbContextFactory<OverlayDesignerDbContext> dbContextFactory,
    ILogger<OverlayDesignerMigrator> logger) : IMigrator
{
    public string ContextName => "OverlayDesigner";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using OverlayDesignerDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Log.ApplyingMigrations(logger);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        Log.MigrationsApplied(logger);
    }
}
