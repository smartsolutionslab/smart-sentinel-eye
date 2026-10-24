using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.AuditObservability.Infrastructure.Redaction;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

public sealed class AuditObservabilityMigrator(
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    AuditUrlRedactionStep urlRedactionStep,
    ILogger<AuditObservabilityMigrator> logger) : IMigrator
{
    public string ContextName => "AuditObservability";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        logger.ApplyingMigrations();
        await context.Database.MigrateAsync(cancellationToken);
        logger.MigrationsApplied();

        await urlRedactionStep.RunAsync(cancellationToken);
    }
}
