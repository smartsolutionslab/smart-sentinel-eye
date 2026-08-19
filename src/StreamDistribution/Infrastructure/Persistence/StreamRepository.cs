using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

public sealed class StreamRepository(
    StreamDistributionDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IStreamRepository
{
    public async Task<Option<Domain.Stream.Stream>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = await dbContext.Streams.FirstOrDefaultAsync(candidate => candidate.Id == stream, cancellationToken);
        return found is null ? Option<Domain.Stream.Stream>.None : Option<Domain.Stream.Stream>.Some(found);
    }

    public async Task<Option<Domain.Stream.Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = await dbContext.Streams.FirstOrDefaultAsync(candidate => candidate.Camera == camera, cancellationToken);
        return found is null ? Option<Domain.Stream.Stream>.None : Option<Domain.Stream.Stream>.Some(found);
    }

    public async Task<Option<Domain.Stream.Stream>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();
        Domain.Stream.Stream? found = await dbContext.Streams.FirstOrDefaultAsync(candidate => candidate.Path == path, cancellationToken);
        return found is null ? Option<Domain.Stream.Stream>.None : Option<Domain.Stream.Stream>.Some(found);
    }

    public void Add(Domain.Stream.Stream stream)
    {
        Ensure.That(stream).IsNotNull();
        dbContext.Streams.Add(stream);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Domain.Stream.Stream[] tracked = dbContext.ChangeTracker.Entries<Domain.Stream.Stream>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Dispatch first: the announcement is captured into the outbox, and the
        // commit below writes the rows and the messages in one transaction
        // (spec 021 FR-001). It used to be the other way round, and the gap
        // between the two was where an integration event went missing.
        //
        // Which means a handler now runs before the write is durable, and one
        // that throws fails the write rather than leaving the row behind. Every
        // handler on this path publishes and does nothing else - checked across
        // all twelve (research.md R2), not assumed.
        foreach (Domain.Stream.Stream? stream in tracked)
        {
            IDomainEvent[] events = stream.PendingEvents.ToArray();
            stream.ClearPendingEvents();
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
