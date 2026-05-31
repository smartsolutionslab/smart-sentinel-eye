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

        await BroadcastAsync(
            () => hub.Clients.All.LayoutRevisionPublished(message),
            ex => Log.LayoutRevisionPublishedBroadcastFailed(logger, ex, notification.Layout, notification.RevisionNumber))
            .ConfigureAwait(false);
    }

    public async Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        LayoutRevisionArchivedHubMessage message = new(
            Layout: notification.Layout.Value,
            RevisionNumber: notification.RevisionNumber.Value,
            ArchivedAt: notification.ArchivedAt);

        await BroadcastAsync(
            () => hub.Clients.All.LayoutRevisionArchived(message),
            ex => Log.LayoutRevisionArchivedBroadcastFailed(logger, ex, notification.Layout, notification.RevisionNumber))
            .ConfigureAwait(false);
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

        await BroadcastAsync(
            () => hub.Clients.All.OverlayRevisionPublished(message),
            ex => Log.OverlayRevisionPublishedBroadcastFailed(logger, ex, notification.Overlay, notification.RevisionNumber))
            .ConfigureAwait(false);
    }

    public async Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        OverlayRevisionArchivedHubMessage message = new(
            Overlay: notification.Overlay,
            RevisionNumber: notification.RevisionNumber,
            ArchivedAt: notification.ArchivedAt);

        await BroadcastAsync(
            () => hub.Clients.All.OverlayRevisionArchived(message),
            ex => Log.OverlayRevisionArchivedBroadcastFailed(logger, ex, notification.Overlay, notification.RevisionNumber))
            .ConfigureAwait(false);
    }

    public async Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ResolvedOverlayTextChangedHubMessage message = new(
            Overlay: notification.Overlay,
            ResolvedText: notification.ResolvedText,
            Version: notification.Version);

        await BroadcastAsync(
            () => hub.Clients.All.ResolvedOverlayTextChanged(message),
            ex => Log.ResolvedOverlayTextChangedBroadcastFailed(logger, ex, notification.Overlay, notification.Version))
            .ConfigureAwait(false);
    }

    public async Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        OverlayHighlightChangedHubMessage message = new(
            Overlay: notification.Overlay,
            DurationMs: notification.DurationMs);

        await BroadcastAsync(
            () => hub.Clients.All.OverlayHighlightChanged(message),
            ex => Log.OverlayHighlightChangedBroadcastFailed(logger, ex, notification.Overlay, notification.DurationMs))
            .ConfigureAwait(false);
    }

    // Best-effort broadcast: a transient hub failure is logged and swallowed
    // so a dropped frame never breaks the write that triggered it (FR-012
    // reconnect-and-reconcile is the safety net). Cancellation is never
    // swallowed — it propagates so the caller's token still wins.
    private static async Task BroadcastAsync(Func<Task> send, Action<Exception> onFailure)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onFailure(ex);
        }
    }
}
