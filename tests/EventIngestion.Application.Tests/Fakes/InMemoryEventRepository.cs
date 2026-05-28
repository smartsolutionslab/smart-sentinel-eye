using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

public sealed class InMemoryEventRepository : IEventRepository
{
    private readonly List<EventAggregate> _events = new();

    public IReadOnlyList<EventAggregate> Events => _events;

    public Task<Option<EventAggregate>> GetByIdentifierAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken)
    {
        EventAggregate? found = _events.SingleOrDefault(e =>
            e.Fab == fab && e.Id == identifier);
        return Task.FromResult(found is null
            ? Option<EventAggregate>.None
            : Option<EventAggregate>.Some(found));
    }

    public Task<bool> ExistsAsync(
        FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
        Task.FromResult(_events.Any(e => e.Fab == fab && e.Id == identifier));

    public void Add(EventAggregate @event) => _events.Add(@event);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (EventAggregate e in _events)
        {
            e.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
