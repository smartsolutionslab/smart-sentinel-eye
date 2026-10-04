using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="OverlayRevisionPublishedDomainEvent"/>
/// into the cross-context <see cref="OverlayRevisionPublishedV2"/>
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
        Ensure.That(domainEvent).IsNotNull();

        var (overlay, revisionNumber, name, labels, publishedAt, publishedBy) = domainEvent;

        await events.PublishAsync(
            new OverlayRevisionPublishedV2(
                Overlay: overlay.Value,
                RevisionNumber: revisionNumber.Value,
                Name: name.Value,
                Labels: [.. labels.Select(label => new OverlayLabelV2(
                    label.Text, label.Position.X, label.Position.Y, label.Size.Width, label.Size.Height, label.FontSizePx))],
                PublishedAt: publishedAt,
                PublishedBy: publishedBy.Value,
                Metadata: new EventMetadata(Guid.CreateVersion7(), publishedAt, null, publishedBy.Value)),
            cancellationToken);
    }
}
