using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

public sealed class AuditObservabilityMigrator(
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    ILogger<AuditObservabilityMigrator> logger) : IMigrator
{
    public string ContextName => "AuditObservability";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using AuditObservabilityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Log.ApplyingMigrations(logger);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        Log.MigrationsApplied(logger);
    }
}
