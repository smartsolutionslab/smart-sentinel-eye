using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Redaction;

/// <summary>Phase 4a scaffold (spec 449); the behaviour lands in phase 4b.</summary>
public sealed class AuditUrlRedactionStep(
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    IOptions<AuditUrlRedactionOptions> options,
    ILogger<AuditUrlRedactionStep> logger)
{
    public Task RunAsync(CancellationToken cancellationToken)
    {
        _ = (dbContextFactory, options, logger);
        return Task.CompletedTask;
    }
}
