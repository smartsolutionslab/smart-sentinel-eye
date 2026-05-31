using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// MigrationRunner-invoked migrator for the EventIngestion DbContext
/// (ADR-0067). Applies pending EF Core migrations once; idempotent.
/// </summary>
public sealed class EventIngestionMigrator(
    IDbContextFactory<EventIngestionDbContext> dbContextFactory,
    ILogger<EventIngestionMigrator> logger) : IMigrator
{
    public string ContextName => "EventIngestion";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using EventIngestionDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Log.ApplyingMigrations(logger);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        Log.MigrationsApplied(logger);
    }
}
