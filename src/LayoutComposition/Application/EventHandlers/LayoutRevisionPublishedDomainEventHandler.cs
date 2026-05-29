using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="LayoutRevisionPublishedDomainEvent"/>
/// into:
/// <list type="number">
/// <item>the cross-context <see cref="LayoutRevisionPublishedV1"/>
/// integration event (via the Wolverine outbox, ADR-0088), and</item>
/// <item>a best-effort SignalR broadcast via
/// <see cref="ILayoutLifecycleBroadcaster"/>.</item>
/// </list>
/// Broadcast failures are swallowed by the broadcaster impl; the
/// kiosk's reconnect-and-reconcile path is the safety net (spec 003
/// FR-012).
/// </summary>
public sealed class LayoutRevisionPublishedDomainEventHandler(
    IEventBus events,
    ILayoutLifecycleBroadcaster broadcaster)
    : IDomainEventHandler<LayoutRevisionPublishedDomainEvent>
{
    public async Task Handle(LayoutRevisionPublishedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new LayoutRevisionPublishedV1(
                Layout: domainEvent.Layout.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                Name: domainEvent.Name.Value,
                Camera: domainEvent.Camera.Value,
                PublishedAt: domainEvent.PublishedAt,
                PublishedBy: domainEvent.PublishedBy.Value,
                Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.PublishedAt, null, domainEvent.PublishedBy.Value)),
            cancellationToken).ConfigureAwait(false);

        await broadcaster.PublishedAsync(
            new LayoutRevisionPublishedNotification(
                domainEvent.Layout,
                domainEvent.RevisionNumber,
                domainEvent.Name,
                domainEvent.Camera,
                domainEvent.PublishedAt),
            cancellationToken).ConfigureAwait(false);
    }
}
