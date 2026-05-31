using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="OverlayHighlightRequestedV1"/>
/// (spec 007 → 003/005 bridge). Calls the existing
/// <see cref="ILayoutLifecycleBroadcaster"/> so the highlight ride
/// the same <c>/hubs/layouts</c> SignalR hub that already carries
/// every other overlay/layout lifecycle frame.
/// </summary>
public sealed class OverlayHighlightRequestedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    ILogger<OverlayHighlightRequestedV1Handler> logger)
{
    public async Task Handle(OverlayHighlightRequestedV1 message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await broadcaster.OverlayHighlightedAsync(
            new OverlayHighlightedNotification(message.OverlayIdentifier, message.DurationMs),
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Broadcast OverlayHighlightChanged for overlay {Overlay} ({Duration} ms; caused by {CausingEvent}).",
            message.OverlayIdentifier, message.DurationMs, message.CausingEventIdentifier);
    }
}
