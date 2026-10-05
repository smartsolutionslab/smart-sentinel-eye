using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="OverlayRevisionPublishedV3"/> from
/// OverlayDesigner (spec 300, #2349, ADR-0165 — renamed from
/// <c>OverlayRevisionPublishedV2Handler</c>). Relays it onto the
/// <c>/hubs/layouts</c> SignalR hub via the
/// <see cref="ILayoutLifecycleBroadcaster"/> LayoutComposition owns, so an
/// overlay publish reaches kiosks the same way every other lifecycle frame
/// does. OverlayDesigner only emits the integration event; the broadcast
/// lives here with the hub, so there is no cross-context dependency
/// (mirrors <see cref="OverlayHighlightRequestedV1Handler"/>).
/// </summary>
public sealed class OverlayRevisionPublishedV3Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    FabsReferencingOverlayQueryHandler referencingFabs,
    ILogger<OverlayRevisionPublishedV3Handler> logger)
{
    public async Task Handle(OverlayRevisionPublishedV3 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (overlay, revisionNumber, name, elements, publishedAt, _, _) = message;

        // Resolved here rather than in the broadcaster: the broadcaster maps a
        // notification to a hub message and sends it, and a database query
        // there would make it the only piece of Infrastructure/Broadcasting
        // that reads state. An overlay nobody references resolves to an empty
        // set and therefore reaches nobody (FR-011).
        IReadOnlyList<FabIdentifier> fabs = await referencingFabs.HandleAsync(overlay, cancellationToken);

        await broadcaster.OverlayPublishedAsync(
            new OverlayLifecyclePublishedNotification(
                Fabs: fabs,
                Overlay: overlay,
                RevisionNumber: revisionNumber,
                Name: name,
                Elements: [.. elements.Select(element => new OverlayLifecycleElement(
                    element.Kind, element.Color, element.NormalizedX, element.NormalizedY, element.NormalizedWidth, element.NormalizedHeight, element.Text, element.FontSizePx))],
                PublishedAt: publishedAt),
            cancellationToken);

        logger.BroadcastOverlayPublished(overlay, revisionNumber);
    }
}
