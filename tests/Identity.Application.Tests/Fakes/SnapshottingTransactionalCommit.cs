using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Records what the event bus held at the moment of each commit, so a test can
/// tell a message captured before the commit (flushed by it) from one captured
/// after (never flushed, because the outbox has already been written).
/// </summary>
public sealed class SnapshottingTransactionalCommit(FakeEventBus bus) : ITransactionalCommit
{
    private readonly List<IReadOnlyCollection<object>> snapshots = [];

    public IReadOnlyList<IReadOnlyCollection<object>> Snapshots => snapshots;

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        snapshots.Add(bus.Published);
        return Task.CompletedTask;
    }
}
