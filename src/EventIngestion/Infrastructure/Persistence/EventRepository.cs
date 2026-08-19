using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class EventRepository(
    EventIngestionDbContext dbContext,
    ITransactionalCommit commit,
    IDomainEventDispatcher domainEventDispatcher) : IEventRepository
{
    public async Task<Option<EventAggregate>> GetByIdentifierAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken)
    {
        EventAggregate? found = await dbContext.Events
            .Where(eventEntity => eventEntity.Fab == fab && eventEntity.Id == identifier)
            .FirstOrDefaultAsync(cancellationToken);
        return found is null ? Option<EventAggregate>.None : Option<EventAggregate>.Some(found);
    }

    public Task<bool> ExistsAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
        dbContext.Events.AnyAsync(eventEntity => eventEntity.Fab == fab && eventEntity.Id == identifier, cancellationToken);

    public async Task<IReadOnlySet<EventIdentifier>> ExistingAsync(
        IReadOnlyCollection<(FabIdentifier Fab, EventIdentifier Identifier)> candidates,
        CancellationToken cancellationToken)
    {
        Ensure.That(candidates).IsNotNull();

        if (candidates.Count == 0)
        {
            return new HashSet<EventIdentifier>();
        }

        // Both lists are passed so the fab predicate can prune partitions. It
        // is a superset test — a fab from one pair could in principle match an
        // identifier from another — but the identifiers are Guid v7, so a
        // false positive would need a collision, and the unique constraint is
        // still the durable backstop underneath (spec 006 FR-002).
        FabIdentifier[] fabs = [.. candidates.Select(candidate => candidate.Fab).Distinct()];
        EventIdentifier[] identifiers = [.. candidates.Select(candidate => candidate.Identifier)];

        EventIdentifier[] found = await dbContext.Events
            .Where(eventEntity =>
                fabs.Contains(eventEntity.Fab) && identifiers.Contains(eventEntity.Id))
            .Select(eventEntity => eventEntity.Id)
            .ToArrayAsync(cancellationToken);

        return found.ToHashSet();
    }

    public void Add(EventAggregate @event)
    {
        Ensure.That(@event).IsNotNull();
        dbContext.Events.Add(@event);
    }

    /// <summary>
    /// Dispatches first, then commits the rows and the messages together
    /// (spec 021 FR-001).
    ///
    /// <para>
    /// <b>The order is the guarantee.</b> It used to be the other way round —
    /// commit, then announce — and the gap between the two was where an
    /// integration event went missing: the row was already durable, the
    /// announcement was not, and nothing held a copy of it. Dispatching first
    /// puts the message in the outbox, and
    /// the commit writes both in one transaction.
    /// </para>
    ///
    /// <para>
    /// Which means <b>a domain-event handler now runs before the write is
    /// durable</b>, and a handler that throws fails the write rather than
    /// leaving the row behind. That is only acceptable because every handler on
    /// this path publishes and does nothing else — checked, not assumed
    /// (research.md R2). A handler added here that writes, calls out, or has
    /// any other side effect breaks that and must not be added without
    /// revisiting this.
    /// </para>
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        EventAggregate[] tracked = dbContext.ChangeTracker
            .Entries<EventAggregate>()
            .Where(entry => entry.Entity.PendingEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        // Collected first and dispatched once. Dispatching per aggregate was
        // invisible while the caller saved one event at a time and becomes a
        // round trip per event the moment a batch is saved (spec 020 FR-010).
        List<IDomainEvent> pending = [];
        foreach (EventAggregate @event in tracked)
        {
            pending.AddRange(@event.PendingEvents);
            @event.ClearPendingEvents();
        }

        // Every event is offered to the dispatcher even after one of them
        // throws, and the failures are raised together at the end. Spec 020
        // added this so one bad handler could not strand the other 199 in a
        // batch; it still holds, and it now aborts the write with them rather
        // than leaving 200 rows announced to nobody.
        List<Exception> failures = [];
        foreach (IDomainEvent domainEvent in pending)
        {
            try
            {
                await domainEventDispatcher.DispatchAsync([domainEvent], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            // "Not committed", not "rolled back". Nothing has been committed here
            // because the commit below is skipped — but the successful handlers'
            // messages and the added rows are still sitting in this scope, so a
            // later commit on the same scope would send them. Every caller today
            // uses a fresh AsyncServiceScope per attempt, which is what makes
            // that unreachable; the wording no longer promises a rollback this
            // method does not perform.
            throw new AggregateException(
                $"{failures.Count} of {pending.Count} domain event(s) could not be captured; "
                + "this unit of work is not committed and must be discarded with its scope.",
                failures);
        }

        await commit.CommitAsync(cancellationToken);
    }
}
