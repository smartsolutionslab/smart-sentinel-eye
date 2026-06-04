using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="LayoutRevisionPublishedDomainEvent"/>
/// into:
/// <list type="number">
/// <item>the cross-context <see cref="LayoutRevisionPublishedV2"/>
/// integration event carrying the full tile set + grid (via the
/// Wolverine outbox, ADR-0088), and</item>
/// <item>a best-effort, <em>lean</em> SignalR broadcast via
/// <see cref="ILayoutLifecycleBroadcaster"/> (no tile set — the picker
/// re-queries on receipt, ADR-0112 §3 / plan T010).</item>
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
        Ensure.That(domainEvent).IsNotNull();

        IReadOnlyList<LayoutTileV2> tiles = domainEvent.Tiles
            .Select(tile => new LayoutTileV2(
                Camera: tile.Camera.Value,
                Overlay: tile.Overlay.Match(overlay => (Guid?)overlay.Value, () => null),
                Row: tile.Position.Row,
                Col: tile.Position.Col))
            .ToList();

        await events.PublishAsync(
            new LayoutRevisionPublishedV2(
                Layout: domainEvent.Layout.Value,
                RevisionNumber: domainEvent.RevisionNumber.Value,
                Name: domainEvent.Name.Value,
                Tiles: tiles,
                GridRows: domainEvent.Grid.Rows,
                GridCols: domainEvent.Grid.Cols,
                PublishedAt: domainEvent.PublishedAt,
                PublishedBy: domainEvent.PublishedBy.Value,
                Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.PublishedAt, null, domainEvent.PublishedBy.Value)),
            cancellationToken);

        await broadcaster.PublishedAsync(
            new LayoutRevisionPublishedNotification(
                domainEvent.Layout,
                domainEvent.RevisionNumber,
                domainEvent.Name,
                domainEvent.PublishedAt),
            cancellationToken);
    }
}
