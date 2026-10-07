using System.Collections.Concurrent;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

public sealed class FakeEventBus : IEventBus
{
    private readonly ConcurrentQueue<object> published = new();

    public IReadOnlyCollection<object> Published => published.ToArray();

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        published.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}
