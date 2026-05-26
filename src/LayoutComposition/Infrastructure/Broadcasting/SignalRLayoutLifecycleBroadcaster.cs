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
    ILogger<SignalRLayoutLifecycleBroadcaster> log)
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
            log.LogWarning(ex,
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
            log.LogWarning(ex,
                "SignalR broadcast for LayoutRevisionArchived({Layout},{Revision}) failed; reconcile-on-reconnect will recover.",
                notification.Layout, notification.RevisionNumber);
        }
    }
}
