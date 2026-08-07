using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="OverlayRevisionPublishedV1"/> from
/// OverlayDesigner. Relays it onto the <c>/hubs/layouts</c> SignalR hub
/// via the <see cref="ILayoutLifecycleBroadcaster"/> LayoutComposition
/// owns, so an overlay publish reaches kiosks the same way every other
/// lifecycle frame does. OverlayDesigner only emits the integration
/// event; the broadcast lives here with the hub, so there is no
/// cross-context dependency (mirrors <see cref="OverlayHighlightRequestedV1Handler"/>).
/// </summary>
public sealed class OverlayRevisionPublishedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    ILogger<OverlayRevisionPublishedV1Handler> logger)
{
    public async Task Handle(OverlayRevisionPublishedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (overlay, revisionNumber, name, text, normalizedX, normalizedY, normalizedWidth, normalizedHeight, fontSizePx, publishedAt, _, _) = message;

        await broadcaster.OverlayPublishedAsync(
            new OverlayLifecyclePublishedNotification(
                Overlay: overlay,
                RevisionNumber: revisionNumber,
                Name: name,
                Text: text,
                NormalizedX: normalizedX,
                NormalizedY: normalizedY,
                NormalizedWidth: normalizedWidth,
                NormalizedHeight: normalizedHeight,
                FontSizePx: fontSizePx,
                PublishedAt: publishedAt),
            cancellationToken);

        logger.BroadcastOverlayPublished(overlay, revisionNumber);
    }
}
