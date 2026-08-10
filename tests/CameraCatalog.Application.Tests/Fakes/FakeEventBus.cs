using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;

/// <summary>
/// Records what was published. The integration event is the only thing another
/// context sees, so asserting on it is asserting on the contract rather than on
/// an internal call.
/// </summary>
public sealed class FakeEventBus : IEventBus
{
    private readonly List<object> _published = [];

    public IReadOnlyList<object> Published => _published;

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        _published.Add(integrationEvent);

        return Task.CompletedTask;
    }
}
