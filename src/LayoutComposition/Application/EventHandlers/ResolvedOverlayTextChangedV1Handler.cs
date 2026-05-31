using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

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
        ArgumentNullException.ThrowIfNull(message);

        await broadcaster.ResolvedOverlayTextChangedAsync(
            new ResolvedOverlayTextChangedNotification(
                message.Overlay,
                message.ResolvedText,
                message.Version),
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Broadcast ResolvedOverlayTextChanged for overlay {Overlay} (version {Version}).",
            message.Overlay, message.Version);
    }
}
