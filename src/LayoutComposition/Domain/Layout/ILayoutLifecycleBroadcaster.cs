namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Domain abstraction over the real-time push transport (ADR-0076 v1
/// SignalR; future v2 candidates kept swappable per constitution IX).
/// The Infrastructure implementation broadcasts to all connected admin
/// + kiosk clients; failures are best-effort — the kiosk's reconnect-
/// and-reconcile path (FR-012) is the safety net.
/// </summary>
public interface ILayoutLifecycleBroadcaster
{
    Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken);

    Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// Wire shape for "a revision became Published" pushes. Mirrors the
/// integration event but stays inside the domain so the broadcaster
/// contract doesn't need a Shared.Contracts dependency.
/// </summary>
public sealed record LayoutRevisionPublishedNotification(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    LayoutName Name,
    CameraIdentifier Camera,
    DateTimeOffset PublishedAt);

/// <summary>
/// Wire shape for "a revision became Archived" pushes. Carries the bare
/// minimum the kiosk needs to decide whether to force-disconnect.
/// </summary>
public sealed record LayoutRevisionArchivedNotification(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    DateTimeOffset ArchivedAt);
