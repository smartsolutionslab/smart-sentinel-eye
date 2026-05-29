using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

public sealed class AuditObservabilityMigrator(
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    ILogger<AuditObservabilityMigrator> log) : IMigrator
{
    public string ContextName => "AuditObservability";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using AuditObservabilityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation("Applying AuditObservability EF Core migrations.");
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        log.LogInformation("AuditObservability migrations applied.");
    }
}
