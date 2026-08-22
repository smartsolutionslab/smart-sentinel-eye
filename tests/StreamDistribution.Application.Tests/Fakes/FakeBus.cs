using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

public sealed class FakeBus : IEventBus
{
    private readonly List<object> published = [];

    public IReadOnlyList<object> Published => published;

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
