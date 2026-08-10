using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="ResolvedOverlayTextChangedV1"/> from
/// SystemVariables. Relays the already-resolved overlay text onto the
/// <c>/hubs/layouts</c> SignalR hub via the broadcaster LayoutComposition
/// owns (spec 005 FR-013). SystemVariables does the resolution; the
/// broadcast lives here with the hub. See
/// <see cref="OverlayRevisionPublishedV1Handler"/> for the rationale.
/// </summary>
public sealed class ResolvedOverlayTextChangedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    ILogger<ResolvedOverlayTextChangedV1Handler> logger)
{
    public async Task Handle(ResolvedOverlayTextChangedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (overlay, resolvedText, version, metadata) = message;

        // FR-015: the push goes to the fab the change happened in, and nowhere
        // else. A frame with no fab is dropped rather than broadcast widely —
        // the same overlay resolves to different values per fab (ADR-0115), so
        // "send it to everyone" would put one plant's figure on another's wall.
        // Said out loud, because a silent drop here looks exactly like a
        // kiosk that simply never updated.
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.ResolvedOverlayTextChangedWithoutFab(overlay, version);

            return;
        }

        await broadcaster.ResolvedOverlayTextChangedAsync(
            new ResolvedOverlayTextChangedNotification(
                overlay,
                resolvedText,
                version,
                metadata.Fab),
            cancellationToken);

        logger.BroadcastResolvedOverlayTextChanged(overlay, version);
    }
}
