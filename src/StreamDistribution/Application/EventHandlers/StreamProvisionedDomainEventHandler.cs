using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

namespace SmartSentinelEye.StreamDistribution.Application.EventHandlers;

/// <summary>
/// In-process handler for the <see cref="StreamProvisionedDomainEvent"/>.
/// Records a structured log entry for audit; no integration event is
/// published here (the first <c>StreamHealthChangedV1</c> fires when the
/// stream transitions out of Provisioning).
/// </summary>
public sealed class StreamProvisionedDomainEventHandler(ILogger<StreamProvisionedDomainEventHandler> logger)
    : IDomainEventHandler<StreamProvisionedDomainEvent>
{
    public Task Handle(StreamProvisionedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        Log.StreamProvisioned(logger, domainEvent.Stream, domainEvent.Camera, domainEvent.Path, domainEvent.ProvisionedBy);
        return Task.CompletedTask;
    }
}
