using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

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
        ArgumentNullException.ThrowIfNull(message);

        await broadcaster.OverlayPublishedAsync(
            new OverlayLifecyclePublishedNotification(
                Overlay: message.Overlay,
                RevisionNumber: message.RevisionNumber,
                Name: message.Name,
                Text: message.Text,
                NormalizedX: message.NormalizedX,
                NormalizedY: message.NormalizedY,
                NormalizedWidth: message.NormalizedWidth,
                NormalizedHeight: message.NormalizedHeight,
                FontSizePx: message.FontSizePx,
                PublishedAt: message.PublishedAt),
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Broadcast OverlayPublished for overlay {Overlay} revision {Revision}.",
            message.Overlay, message.RevisionNumber);
    }
}
