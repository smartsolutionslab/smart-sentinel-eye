using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.EventHandlers;

/// <summary>
/// Translates <see cref="OverlayRevisionArchivedDomainEvent"/> into the
/// <see cref="OverlayRevisionArchivedV1"/> integration event (Wolverine
/// outbox). LayoutComposition subscribes to it and pushes the
/// force-disconnect SignalR frame on the hub it owns — same split as
/// <see cref="OverlayRevisionPublishedDomainEventHandler"/>.
/// </summary>
public sealed class OverlayRevisionArchivedDomainEventHandler(IEventBus events)
    : IDomainEventHandler<OverlayRevisionArchivedDomainEvent>
{
    public async Task Handle(OverlayRevisionArchivedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (overlay, revisionNumber, archivedAt, archivedBy) = domainEvent;

        await events.PublishAsync(
            new OverlayRevisionArchivedV1(
                Overlay: overlay.Value,
                RevisionNumber: revisionNumber.Value,
                ArchivedAt: archivedAt,
                ArchivedBy: archivedBy.Value,
                Metadata: new EventMetadata(
                    Guid.CreateVersion7(),
                    archivedAt,
                    null,
                    archivedBy.Value)),
            cancellationToken);
    }
}
