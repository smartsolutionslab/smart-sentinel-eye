using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(message).IsNotNull();

        var (overlayIdentifier, durationMs, _, causingEventIdentifier, metadata) = message;

        // The fab was already on the wire and discarded: Automation is
        // fab-scoped (spec 013) and stamps it. A highlight lights up a wall,
        // so sending one plant's to every plant is a visible cross-fab effect,
        // not just leaked metadata (#1397).
        //
        // Dropped rather than broadcast widely when absent, and logged: a
        // highlight that never appears looks the same as one nobody sent.
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.OverlayHighlightWithoutFab(overlayIdentifier, causingEventIdentifier);

            return;
        }

        await broadcaster.OverlayHighlightedAsync(
            new OverlayHighlightedNotification(overlayIdentifier, durationMs, metadata.Fab),
            cancellationToken);

        logger.BroadcastOverlayHighlightChanged(overlayIdentifier, durationMs, causingEventIdentifier);
    }
}
