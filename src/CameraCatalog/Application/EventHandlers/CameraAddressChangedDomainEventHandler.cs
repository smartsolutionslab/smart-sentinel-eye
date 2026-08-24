using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.CameraCatalog.Application.EventHandlers;

/// <summary>
/// Translates the in-process CameraAddressChangedDomainEvent into the
/// CameraAddressChangedV1 integration event and publishes it via the
/// framework-agnostic IEventBus seam (ADR-0040 + ADR-0057). The bus
/// implementation in ServiceDefaults wraps Wolverine and uses the
/// transactional outbox per ADR-0088.
/// </summary>
/// <remarks>
/// No <c>IJourneyOrigin</c> here, matching
/// <see cref="CameraRetiredDomainEventHandler"/> and for the same reason:
/// spec 027's publisher survey classifies this call site as request-driven,
/// so the publish inherits the causing HTTP request's trace. Beginning a
/// journey here would re-root the announcement and detach it from the operator
/// who caused it.
/// </remarks>
public sealed class CameraAddressChangedDomainEventHandler(IEventBus events)
    : IDomainEventHandler<CameraAddressChangedDomainEvent>
{
    public Task Handle(CameraAddressChangedDomainEvent domainEvent, CancellationToken cancellationToken) =>
        events.PublishAsync(
            new CameraAddressChangedV1(
                Camera: domainEvent.Camera.Value,
                Fab: domainEvent.Fab.Value,
                PreviousUrl: domainEvent.PreviousUrl.Value,
                Url: domainEvent.Url.Value,
                ChangedAt: domainEvent.ChangedAt,
                ChangedBy: domainEvent.ChangedBy.Value,
                Metadata: new EventMetadata(
                    Guid.CreateVersion7(),
                    domainEvent.ChangedAt,
                    domainEvent.Fab.Value,
                    domainEvent.ChangedBy.Value)),
            cancellationToken);
}
