using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.OverlayDesigner.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="OverlayRevisionPublishedDomainEvent"/>
/// into the cross-context <see cref="OverlayRevisionPublishedV1"/>
/// integration event (via the Wolverine outbox, ADR-0088).
///
/// <para>
/// The SignalR push for this lifecycle frame is performed by
/// LayoutComposition, which owns the <c>/hubs/layouts</c> hub and
/// subscribes to <see cref="OverlayRevisionPublishedV1"/> — so the
/// broadcast lives with the hub and this context keeps no dependency on
/// LayoutComposition.
/// </para>
/// </summary>
public sealed class OverlayRevisionPublishedDomainEventHandler(IEventBus events)
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
                PublishedBy: domainEvent.PublishedBy.Value,
                Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.PublishedAt, null, domainEvent.PublishedBy.Value)),
            cancellationToken).ConfigureAwait(false);
    }
}
