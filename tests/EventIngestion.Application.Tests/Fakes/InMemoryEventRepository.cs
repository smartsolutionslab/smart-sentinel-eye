using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

public sealed class InMemoryEventRepository : IEventRepository
{
    private readonly List<EventAggregate> events = [];

    private int committed;

    public IReadOnlyList<EventAggregate> Events => events;

    /// <summary>
    /// When set, the next <see cref="SaveAsync"/> throws it and discards
    /// everything added since the last successful save — the all-or-nothing
    /// insert the batch handler is built around (spec 020 FR-010). Left null,
    /// this fake behaves exactly as it always has.
    /// </summary>
    public Exception? SaveFailure { get; set; }

    public Task<Option<EventAggregate>> GetByIdentifierAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken)
    {
        EventAggregate? found = events.SingleOrDefault(e =>
            e.Fab == fab && e.Id == identifier);
        return Task.FromResult(found is null
            ? Option<EventAggregate>.None
            : Option<EventAggregate>.Some(found));
    }

    public Task<bool> ExistsAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(events.Any(e => e.Fab == fab && e.Id == identifier));

    public Task<IReadOnlySet<EventIdentifier>> ExistingAsync(
        IReadOnlyCollection<(FabIdentifier Fab, EventIdentifier Identifier)> candidates,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<EventIdentifier>>(
            candidates
                .Where(candidate => events.Any(e => e.Fab == candidate.Fab && e.Id == candidate.Identifier))
                .Select(candidate => candidate.Identifier)
                .ToHashSet());

    public void Add(EventAggregate @event) => events.Add(@event);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        if (SaveFailure is not null)
        {
            events.RemoveRange(committed, events.Count - committed);
            return Task.FromException(SaveFailure);
        }

        committed = events.Count;
        foreach (EventAggregate e in events)
        {
            e.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
