using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.CameraCatalog.Application.EventHandlers;

/// <summary>
/// Translates the in-process CameraRenamedDomainEvent into the CameraRenamedV1
/// integration event and publishes it via the framework-agnostic IEventBus seam
/// (ADR-0040 + ADR-0057). The bus implementation in ServiceDefaults wraps
/// Wolverine and uses the transactional outbox per ADR-0088.
/// </summary>
/// <remarks>
/// No <c>IJourneyOrigin</c>, matching
/// <see cref="CameraAddressChangedDomainEventHandler"/> and for the same
/// reason: spec 027's publisher survey classifies this call site as
/// request-driven, so the publish inherits the causing HTTP request's trace.
/// Beginning a journey here would re-root the announcement and detach it from
/// the operator who caused it.
/// </remarks>
public sealed class CameraRenamedDomainEventHandler(IEventBus events)
    : IDomainEventHandler<CameraRenamedDomainEvent>
{
    public Task Handle(CameraRenamedDomainEvent domainEvent, CancellationToken cancellationToken) =>
        events.PublishAsync(
            new CameraRenamedV1(
                Camera: domainEvent.Camera.Value,
                Fab: domainEvent.Fab.Value,
                PreviousName: domainEvent.PreviousName.Value,
                Name: domainEvent.Name.Value,
                RenamedAt: domainEvent.RenamedAt,
                RenamedBy: domainEvent.RenamedBy.Value,
                Metadata: new EventMetadata(
                    Guid.CreateVersion7(),
                    domainEvent.RenamedAt,
                    domainEvent.Fab.Value,
                    domainEvent.RenamedBy.Value)),
            cancellationToken);
}
