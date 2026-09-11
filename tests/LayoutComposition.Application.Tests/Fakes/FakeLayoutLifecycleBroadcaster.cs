using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Recording fake <see cref="ILayoutLifecycleBroadcaster"/>. Tests
/// assert against the captured notification lists.
/// </summary>
public sealed class FakeLayoutLifecycleBroadcaster : ILayoutLifecycleBroadcaster
{
    /// <summary>
    /// Runs inside the two pushes that end §IV's <c>event → overlay state</c>
    /// leg, before either returns. Lets a test spend time while the push is
    /// still in flight, so a measurement taken after the push differs
    /// observably from one taken before it (spec 133 SC-001).
    /// </summary>
    public Action? DuringPush { get; set; }

    public List<LayoutRevisionPublishedNotification> Published { get; } = [];

    public List<LayoutRevisionArchivedNotification> Archived { get; } = [];

    public List<OverlayLifecyclePublishedNotification> OverlaysPublished { get; } = [];

    public List<OverlayLifecycleArchivedNotification> OverlaysArchived { get; } = [];

    public Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    public Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken)
    {
        Archived.Add(notification);
        return Task.CompletedTask;
    }

    public Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken)
    {
        OverlaysPublished.Add(notification);
        return Task.CompletedTask;
    }

    public Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken)
    {
        OverlaysArchived.Add(notification);
        return Task.CompletedTask;
    }

    public List<ResolvedOverlayTextChangedNotification> ResolvedTextChanged { get; } = [];

    public Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken)
    {
        DuringPush?.Invoke();
        ResolvedTextChanged.Add(notification);
        return Task.CompletedTask;
    }

    public List<OverlayHighlightedNotification> Highlighted { get; } = [];

    public Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken)
    {
        DuringPush?.Invoke();
        Highlighted.Add(notification);
        return Task.CompletedTask;
    }
}
