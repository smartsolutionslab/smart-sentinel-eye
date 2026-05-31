using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="OverlayRevisionArchivedV1"/> from
/// OverlayDesigner. Relays it onto the <c>/hubs/layouts</c> SignalR hub
/// via the broadcaster LayoutComposition owns (force-disconnect
/// semantics for kiosks rendering the archived overlay). See
/// <see cref="OverlayRevisionPublishedV1Handler"/> for the rationale.
/// </summary>
public sealed class OverlayRevisionArchivedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    ILogger<OverlayRevisionArchivedV1Handler> logger)
{
    public async Task Handle(OverlayRevisionArchivedV1 message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await broadcaster.OverlayArchivedAsync(
            new OverlayLifecycleArchivedNotification(
                Overlay: message.Overlay,
                RevisionNumber: message.RevisionNumber,
                ArchivedAt: message.ArchivedAt),
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Broadcast OverlayArchived for overlay {Overlay} revision {Revision}.",
            message.Overlay, message.RevisionNumber);
    }
}
