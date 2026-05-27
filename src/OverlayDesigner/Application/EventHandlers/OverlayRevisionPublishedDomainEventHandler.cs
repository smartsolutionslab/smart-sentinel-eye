using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.OverlayDesigner.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="OverlayRevisionPublishedDomainEvent"/>
/// into:
/// <list type="number">
/// <item>the cross-context <see cref="OverlayRevisionPublishedV1"/>
/// integration event (via the Wolverine outbox, ADR-0088), and</item>
/// <item>a best-effort SignalR broadcast via
/// <see cref="ILayoutLifecycleBroadcaster"/> — the single hub for both
/// layout + overlay lifecycle events (spec 004 plan.md), reached through
/// the documented cross-context exception.</item>
/// </list>
/// Broadcast failures are swallowed by the broadcaster impl; the
/// kiosk's reconnect-and-reconcile path is the safety net (spec 003
/// FR-012, inherited).
/// </summary>
public sealed class OverlayRevisionPublishedDomainEventHandler(
    IEventBus events,
    ILayoutLifecycleBroadcaster broadcaster)
    : IDomainEventHandler<OverlayRevisionPublishedDomainEvent>
{
    public async Task Handle(OverlayRevisionPublishedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new OverlayRevisionPublishedV1(
                Overlay: domainEvent.Overlay.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                Name: domainEvent.Name.Value,
                Text: domainEvent.Label.Text,
                NormalizedX: domainEvent.Label.NormalizedX,
                NormalizedY: domainEvent.Label.NormalizedY,
                NormalizedWidth: domainEvent.Label.NormalizedWidth,
                NormalizedHeight: domainEvent.Label.NormalizedHeight,
                FontSizePx: domainEvent.Label.FontSizePx,
                PublishedAt: domainEvent.PublishedAt,
                PublishedBy: domainEvent.PublishedBy.Value),
            cancellationToken).ConfigureAwait(false);

        await broadcaster.OverlayPublishedAsync(
            new OverlayLifecyclePublishedNotification(
                Overlay: domainEvent.Overlay.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                Name: domainEvent.Name.Value,
                Text: domainEvent.Label.Text,
                NormalizedX: domainEvent.Label.NormalizedX,
                NormalizedY: domainEvent.Label.NormalizedY,
                NormalizedWidth: domainEvent.Label.NormalizedWidth,
                NormalizedHeight: domainEvent.Label.NormalizedHeight,
                FontSizePx: domainEvent.Label.FontSizePx,
                PublishedAt: domainEvent.PublishedAt),
            cancellationToken).ConfigureAwait(false);
    }
}
