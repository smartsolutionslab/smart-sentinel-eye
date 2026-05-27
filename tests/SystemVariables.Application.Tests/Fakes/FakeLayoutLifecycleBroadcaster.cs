using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Fakes;

public sealed class FakeLayoutLifecycleBroadcaster : ILayoutLifecycleBroadcaster
{
    public List<ResolvedOverlayTextChangedNotification> ResolvedTextChanged { get; } = new();

    public Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken)
    {
        ResolvedTextChanged.Add(notification);
        return Task.CompletedTask;
    }
}
