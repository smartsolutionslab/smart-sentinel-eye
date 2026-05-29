using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Recording fake <see cref="ILayoutLifecycleBroadcaster"/>. Tests
/// assert against the captured notification lists.
/// </summary>
public sealed class FakeLayoutLifecycleBroadcaster : ILayoutLifecycleBroadcaster
{
    public List<LayoutRevisionPublishedNotification> Published { get; } = new();

    public List<LayoutRevisionArchivedNotification> Archived { get; } = new();

    public List<OverlayLifecyclePublishedNotification> OverlaysPublished { get; } = new();

    public List<OverlayLifecycleArchivedNotification> OverlaysArchived { get; } = new();

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

    public List<ResolvedOverlayTextChangedNotification> ResolvedTextChanged { get; } = new();

    public Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken)
    {
        ResolvedTextChanged.Add(notification);
        return Task.CompletedTask;
    }

    public List<OverlayHighlightedNotification> Highlighted { get; } = new();

    public Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken)
    {
        Highlighted.Add(notification);
        return Task.CompletedTask;
    }
}
