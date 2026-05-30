using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;

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
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new OverlayRevisionArchivedV1(
                Overlay: domainEvent.Overlay.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                ArchivedAt: domainEvent.ArchivedAt,
                ArchivedBy: domainEvent.ArchivedBy.Value,
                Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.ArchivedAt, null, domainEvent.ArchivedBy.Value)),
            cancellationToken).ConfigureAwait(false);
    }
}
