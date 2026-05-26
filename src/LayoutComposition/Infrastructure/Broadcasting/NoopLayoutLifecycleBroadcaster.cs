using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// No-op broadcaster used in PR C while the SignalR hub doesn't exist
/// yet — the real <c>SignalRLayoutLifecycleBroadcaster</c> lands in PR E.
/// Logs each broadcast attempt so we can see in dev that the domain
/// event handler is calling through, without committing to the
/// transport implementation in this PR.
/// </summary>
public sealed class NoopLayoutLifecycleBroadcaster(ILogger<NoopLayoutLifecycleBroadcaster> log)
    : ILayoutLifecycleBroadcaster
{
    public Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        log.LogInformation(
            "Layout {Layout} revision {Revision} published (no-op broadcaster; SignalR lands in PR E).",
            notification.Layout, notification.RevisionNumber);
        return Task.CompletedTask;
    }

    public Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        log.LogInformation(
            "Layout {Layout} revision {Revision} archived (no-op broadcaster; SignalR lands in PR E).",
            notification.Layout, notification.RevisionNumber);
        return Task.CompletedTask;
    }
}
