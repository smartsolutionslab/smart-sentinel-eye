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
}
