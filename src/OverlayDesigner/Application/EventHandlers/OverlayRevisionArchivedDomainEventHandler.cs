using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.OverlayDesigner.Application.EventHandlers;

/// <summary>
/// Translates <see cref="OverlayRevisionArchivedDomainEvent"/> into the
/// V1 integration event + a SignalR broadcast (same pattern as the
/// Published handler above; force-disconnect semantics for kiosks that
/// were rendering the archived overlay).
/// </summary>
public sealed class OverlayRevisionArchivedDomainEventHandler(
    IEventBus events,
    ILayoutLifecycleBroadcaster broadcaster)
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

        await broadcaster.OverlayArchivedAsync(
            new OverlayLifecycleArchivedNotification(
                Overlay: domainEvent.Overlay.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                ArchivedAt: domainEvent.ArchivedAt),
            cancellationToken).ConfigureAwait(false);
    }
}
