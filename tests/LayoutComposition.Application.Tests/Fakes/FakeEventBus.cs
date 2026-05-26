using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Recording fake <see cref="IEventBus"/> for handler tests.
/// </summary>
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
