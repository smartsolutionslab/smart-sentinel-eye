using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// SignalR-backed implementation of
/// <see cref="ILayoutLifecycleBroadcaster"/>. Broadcasts to every
/// connected client (admin or kiosk). Failures are best-effort —
/// the kiosk's reconnect-and-reconcile path (FR-012) is the safety
/// net so a dropped frame never leaves a kiosk staring at an archived
/// layout.
/// </summary>
public sealed class SignalRLayoutLifecycleBroadcaster(
    IHubContext<LayoutLifecycleHub, ILayoutLifecycleClient> hub,
    ILogger<SignalRLayoutLifecycleBroadcaster> logger)
    : ILayoutLifecycleBroadcaster
{
    public async Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        LayoutRevisionPublishedHubMessage message = new(
            Layout: notification.Layout.Value,
            RevisionNumber: notification.RevisionNumber.Value,
            Name: notification.Name.Value,
            Camera: notification.Camera.Value,
            PublishedAt: notification.PublishedAt);

        try
        {
            await hub.Clients.All.LayoutRevisionPublished(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "SignalR broadcast for LayoutRevisionPublished({Layout},{Revision}) failed; reconcile-on-reconnect will recover.",
                notification.Layout, notification.RevisionNumber);
        }
    }

    public async Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        LayoutRevisionArchivedHubMessage message = new(
            Layout: notification.Layout.Value,
            RevisionNumber: notification.RevisionNumber.Value,
            ArchivedAt: notification.ArchivedAt);

        try
        {
            await hub.Clients.All.LayoutRevisionArchived(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "SignalR broadcast for LayoutRevisionArchived({Layout},{Revision}) failed; reconcile-on-reconnect will recover.",
                notification.Layout, notification.RevisionNumber);
        }
    }

    public async Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        OverlayRevisionPublishedHubMessage message = new(
            Overlay: notification.Overlay,
            RevisionNumber: notification.RevisionNumber,
            Name: notification.Name,
            Text: notification.Text,
            NormalizedX: notification.NormalizedX,
            NormalizedY: notification.NormalizedY,
            NormalizedWidth: notification.NormalizedWidth,
            NormalizedHeight: notification.NormalizedHeight,
            FontSizePx: notification.FontSizePx,
            PublishedAt: notification.PublishedAt);

        try
        {
            await hub.Clients.All.OverlayRevisionPublished(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "SignalR broadcast for OverlayRevisionPublished({Overlay},{Revision}) failed; reconcile-on-reconnect will recover.",
                notification.Overlay, notification.RevisionNumber);
        }
    }

    public async Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        OverlayRevisionArchivedHubMessage message = new(
            Overlay: notification.Overlay,
            RevisionNumber: notification.RevisionNumber,
            ArchivedAt: notification.ArchivedAt);

        try
        {
            await hub.Clients.All.OverlayRevisionArchived(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "SignalR broadcast for OverlayRevisionArchived({Overlay},{Revision}) failed; reconcile-on-reconnect will recover.",
                notification.Overlay, notification.RevisionNumber);
        }
    }

    public async Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ResolvedOverlayTextChangedHubMessage message = new(
            Overlay: notification.Overlay,
            ResolvedText: notification.ResolvedText,
            Version: notification.Version);

        try
        {
            await hub.Clients.All.ResolvedOverlayTextChanged(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "SignalR broadcast for ResolvedOverlayTextChanged({Overlay}, v{Version}) failed; reconcile-on-reconnect will recover.",
                notification.Overlay, notification.Version);
        }
    }

    public async Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        OverlayHighlightChangedHubMessage message = new(
            Overlay: notification.Overlay,
            DurationMs: notification.DurationMs);

        try
        {
            await hub.Clients.All.OverlayHighlightChanged(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "SignalR broadcast for OverlayHighlightChanged({Overlay}, {DurationMs} ms) failed; reconcile-on-reconnect will recover.",
                notification.Overlay, notification.DurationMs);
        }
    }
}
