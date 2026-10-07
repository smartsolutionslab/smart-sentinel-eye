using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Fakes;

public sealed class InMemoryAuditEventRepository : IAuditEventRepository
{
    private readonly List<AuditEventEntity> committed = [];
    private readonly List<AuditEventEntity> pending = [];

    public IReadOnlyList<AuditEventEntity> Committed => committed;

    public int SaveAsyncCallCount { get; private set; }

    public void Add(AuditEventEntity audit) => pending.Add(audit);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        SaveAsyncCallCount++;
        foreach (AuditEventEntity row in pending)
        {
            // Idempotent on EventIdentifier — mirrors the production
            // INSERT ... ON CONFLICT (event_identifier) DO NOTHING.
            if (!committed.Any(c => c.EventIdentifier == row.EventIdentifier))
            {
                committed.Add(row);
            }
        }
        pending.Clear();
        return Task.CompletedTask;
    }
}
