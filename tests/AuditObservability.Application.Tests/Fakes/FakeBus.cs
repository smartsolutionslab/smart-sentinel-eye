using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Fakes;

public sealed class FakeBus : IEventBus
{
    private readonly List<object> _published = new();

    public IReadOnlyList<object> Published => _published;

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        _published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
