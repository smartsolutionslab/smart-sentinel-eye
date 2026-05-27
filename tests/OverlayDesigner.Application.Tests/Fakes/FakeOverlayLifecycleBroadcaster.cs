using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;

/// <summary>
/// Recording fake of the shared <see cref="ILayoutLifecycleBroadcaster"/>
/// for OverlayDesigner Application tests. The OverlayDesigner side only
/// exercises the two Overlay* methods; the Layout* methods are no-ops
/// here (they are reachable via the abstraction but never invoked from
/// OverlayDesigner handlers — spec 004 plan: the cross-context bridge
/// flows OverlayDesigner → broadcaster only).
/// </summary>
public sealed class FakeOverlayLifecycleBroadcaster : ILayoutLifecycleBroadcaster
{
    public List<OverlayLifecyclePublishedNotification> Published { get; } = new();

    public List<OverlayLifecycleArchivedNotification> Archived { get; } = new();

    public Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    public Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken)
    {
        Archived.Add(notification);
        return Task.CompletedTask;
    }

    public Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
