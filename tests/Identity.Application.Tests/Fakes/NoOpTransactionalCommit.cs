using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Stands in for the outbox flush. These tests are about the rotation's
/// decisions, not about durability — the real commit is exercised against a
/// real outbox in the integration suite, where it can actually prove something.
/// </summary>
public sealed class NoOpTransactionalCommit : ITransactionalCommit
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
}
