using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using Wolverine;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Adapter from the framework-agnostic IEventBus (Shared.CQRS) to Wolverine's
/// IMessageBus. Used by Application handlers so the Application project stays
/// Wolverine-free (ADR-0057).
/// </summary>
public sealed class WolverineEventBus(IMessageBus bus, ILogger<WolverineEventBus> logger) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        logger.LogInformation(
            "Publishing integration event {EventType} via Wolverine.",
            typeof(TEvent).FullName);
        return bus.PublishAsync(integrationEvent).AsTask();
    }
}
