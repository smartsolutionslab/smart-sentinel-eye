using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;

public sealed class FakeEventBus : IEventBus
{
    public List<object> Published { get; } = new();

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
