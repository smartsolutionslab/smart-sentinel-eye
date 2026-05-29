using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Fakes;

public sealed class InMemoryAuditEventRepository : IAuditEventRepository
{
    private readonly List<AuditEventEntity> _committed = new();
    private readonly List<AuditEventEntity> _pending = new();

    public IReadOnlyList<AuditEventEntity> Committed => _committed;

    public int SaveAsyncCallCount { get; private set; }

    public void Add(AuditEventEntity audit) => _pending.Add(audit);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        SaveAsyncCallCount++;
        foreach (AuditEventEntity row in _pending)
        {
            // Idempotent on EventIdentifier — mirrors the production
            // INSERT ... ON CONFLICT (event_identifier) DO NOTHING.
            if (!_committed.Any(c => c.EventIdentifier == row.EventIdentifier))
            {
                _committed.Add(row);
            }
        }
        _pending.Clear();
        return Task.CompletedTask;
    }
}
