using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Twin of <see cref="LayoutRevisionPublishedDomainEventHandler"/>:
/// publishes <see cref="LayoutRevisionArchivedV1"/> + broadcasts the
/// Archived notification to connected kiosks so they can force-
/// disconnect (FR-011).
/// </summary>
public sealed class LayoutRevisionArchivedDomainEventHandler(
    IEventBus events,
    ILayoutLifecycleBroadcaster broadcaster)
    : IDomainEventHandler<LayoutRevisionArchivedDomainEvent>
{
    public async Task Handle(LayoutRevisionArchivedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new LayoutRevisionArchivedV1(
                Layout: domainEvent.Layout.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                ArchivedAt: domainEvent.ArchivedAt,
                ArchivedBy: domainEvent.ArchivedBy.Value),
            cancellationToken).ConfigureAwait(false);

        await broadcaster.ArchivedAsync(
            new LayoutRevisionArchivedNotification(
                domainEvent.Layout,
                domainEvent.RevisionNumber,
                domainEvent.ArchivedAt),
            cancellationToken).ConfigureAwait(false);
    }
}
