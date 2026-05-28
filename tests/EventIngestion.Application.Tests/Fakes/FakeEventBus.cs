using System.Collections.Concurrent;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

public sealed class FakeEventBus : IEventBus
{
    private readonly ConcurrentQueue<object> _published = new();

    public IReadOnlyCollection<object> Published => _published.ToArray();

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        _published.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}
