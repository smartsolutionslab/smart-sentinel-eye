using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(notification).IsNotNull();

        LayoutRevisionPublishedHubMessage message = new(
            Layout: notification.Layout.Value,
            RevisionNumber: notification.RevisionNumber.Value,
            Name: notification.Name.Value,
            Camera: notification.Camera.Value,
            PublishedAt: notification.PublishedAt);

        await BroadcastAsync(
            () => hub.Clients.All.LayoutRevisionPublished(message),
            ex => logger.LayoutRevisionPublishedBroadcastFailed(ex, notification.Layout, notification.RevisionNumber));
    }

    public async Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken)
    {
        Ensure.That(notification).IsNotNull();

        LayoutRevisionArchivedHubMessage message = new(
            Layout: notification.Layout.Value,
            RevisionNumber: notification.RevisionNumber.Value,
            ArchivedAt: notification.ArchivedAt);

        await BroadcastAsync(
            () => hub.Clients.All.LayoutRevisionArchived(message),
            ex => logger.LayoutRevisionArchivedBroadcastFailed(ex, notification.Layout, notification.RevisionNumber));
    }

    public async Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken)
    {
        Ensure.That(notification).IsNotNull();
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

        await BroadcastAsync(
            () => hub.Clients.All.OverlayRevisionPublished(message),
            ex => logger.OverlayRevisionPublishedBroadcastFailed(ex, notification.Overlay, notification.RevisionNumber));
    }

    public async Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken)
    {
        Ensure.That(notification).IsNotNull();

        OverlayRevisionArchivedHubMessage message = new(
            Overlay: notification.Overlay,
            RevisionNumber: notification.RevisionNumber,
            ArchivedAt: notification.ArchivedAt);

        await BroadcastAsync(
            () => hub.Clients.All.OverlayRevisionArchived(message),
            ex => logger.OverlayRevisionArchivedBroadcastFailed(ex, notification.Overlay, notification.RevisionNumber));
    }

    public async Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken)
    {
        Ensure.That(notification).IsNotNull();

        ResolvedOverlayTextChangedHubMessage message = new(
            Overlay: notification.Overlay,
            ResolvedText: notification.ResolvedText,
            Version: notification.Version);

        await BroadcastAsync(
            () => hub.Clients.All.ResolvedOverlayTextChanged(message),
            ex => logger.ResolvedOverlayTextChangedBroadcastFailed(ex, notification.Overlay, notification.Version));
    }

    public async Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken)
    {
        Ensure.That(notification).IsNotNull();

        OverlayHighlightChangedHubMessage message = new(
            Overlay: notification.Overlay,
            DurationMs: notification.DurationMs);

        await BroadcastAsync(
            () => hub.Clients.All.OverlayHighlightChanged(message),
            ex => logger.OverlayHighlightChangedBroadcastFailed(ex, notification.Overlay, notification.DurationMs));
    }

    // Best-effort broadcast: a transient hub failure is logged and swallowed
    // so a dropped frame never breaks the write that triggered it (FR-012
    // reconnect-and-reconcile is the safety net). Cancellation is never
    // swallowed — it propagates so the caller's token still wins.
    private static async Task BroadcastAsync(Func<Task> send, Action<Exception> onFailure)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onFailure(ex);
        }
    }
}
