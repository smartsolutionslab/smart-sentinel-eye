using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.CameraCatalog.Application.EventHandlers;

/// <summary>
/// Translates the in-process CameraRegisteredDomainEvent into the
/// CameraRegisteredV1 integration event and publishes it via the
/// framework-agnostic IEventBus seam (ADR-0040 + ADR-0057). The bus
/// implementation in ServiceDefaults wraps Wolverine and uses the
/// transactional outbox per ADR-0088.
/// </summary>
public sealed class CameraRegisteredDomainEventHandler(IEventBus events)
    : IDomainEventHandler<CameraRegisteredDomainEvent>
{
    public Task Handle(CameraRegisteredDomainEvent domainEvent, CancellationToken cancellationToken) =>
        events.PublishAsync(
            new CameraRegisteredV1(
                Camera: domainEvent.Camera.Value,
                Name: domainEvent.Name.Value,
                Url: domainEvent.Url.Value,
                RegisteredAt: domainEvent.RegisteredAt,
                RegisteredBy: domainEvent.RegisteredBy.Value,
                // FR-012: the fab travels with the event, so a subscriber can
                // tell which plant it concerns without calling back into this
                // context per camera. StreamDistribution's own fab scoping is
                // the next feature and would otherwise start by adding this.
                //
                // Additive: EventMetadata.Fab already exists and was stamped
                // null, so no version bump under ADR-0073.
                Metadata: new EventMetadata(
                    Guid.CreateVersion7(),
                    domainEvent.RegisteredAt,
                    domainEvent.Fab.Value,
                    domainEvent.RegisteredBy.Value)),
            cancellationToken);
}
