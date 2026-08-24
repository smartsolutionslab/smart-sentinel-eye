using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.CameraCatalog.Application.EventHandlers;

/// <summary>
/// Translates the in-process CameraRetiredDomainEvent into the CameraRetiredV1
/// integration event and publishes it via the framework-agnostic IEventBus
/// seam (ADR-0040 + ADR-0057). The bus implementation in ServiceDefaults wraps
/// Wolverine and uses the transactional outbox per ADR-0088.
/// </summary>
/// <remarks>
/// No <c>IJourneyOrigin</c> here, and that is deliberate. Spec 027's publisher
/// survey classifies this call site as request-driven: the retirement is
/// caused by an HTTP request, and an HTTP publish inherits that request's cause
/// — observed directly in trace <c>c4f226c1…</c>, where both <c>send</c> spans
/// sat under the <c>POST</c> Server span. Beginning a journey here would
/// re-root the announcement and detach it from the operator who caused it.
/// </remarks>
public sealed class CameraRetiredDomainEventHandler(IEventBus events)
    : IDomainEventHandler<CameraRetiredDomainEvent>
{
    public Task Handle(CameraRetiredDomainEvent domainEvent, CancellationToken cancellationToken) =>
        events.PublishAsync(
            new CameraRetiredV1(
                Camera: domainEvent.Camera.Value,
                Fab: domainEvent.Fab.Value,
                Name: domainEvent.Name.Value,
                RetiredAt: domainEvent.RetiredAt,
                RetiredBy: domainEvent.RetiredBy.Value,
                Metadata: new EventMetadata(
                    Guid.CreateVersion7(),
                    domainEvent.RetiredAt,
                    domainEvent.Fab.Value,
                    domainEvent.RetiredBy.Value)),
            cancellationToken);
}
