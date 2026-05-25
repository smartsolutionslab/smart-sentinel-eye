namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Application-facing seam for publishing integration events (ADR-0040 +
/// ADR-0057 + ADR-0088). The implementation in ServiceDefaults wraps
/// Wolverine's IMessageBus — application code stays Wolverine-free.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
