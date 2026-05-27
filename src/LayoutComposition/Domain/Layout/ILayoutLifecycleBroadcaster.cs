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

    Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken);

    Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken);

    Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken);
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

/// <summary>
/// Wire shape for "an overlay revision became Published" pushes. The
/// cross-context bridge from OverlayDesigner.Application
/// (spec 004 plan.md — single documented allow-rule); primitive types
/// only so the broadcaster contract does not need to reference
/// OverlayDesigner.Domain.
/// </summary>
public sealed record OverlayLifecyclePublishedNotification(
    Guid Overlay,
    int RevisionNumber,
    string Name,
    string Text,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    int FontSizePx,
    DateTimeOffset PublishedAt);

/// <summary>
/// Wire shape for "an overlay revision became Archived" pushes.
/// Primitive types only — see <see cref="OverlayLifecyclePublishedNotification"/>.
/// </summary>
public sealed record OverlayLifecycleArchivedNotification(
    Guid Overlay,
    int RevisionNumber,
    DateTimeOffset ArchivedAt);

/// <summary>
/// Wire shape for "an overlay's resolved text changed" pushes
/// (spec 005 FR-013). Pushed when a system variable referenced by an
/// overlay's label changes, gets archived, or the overlay itself is
/// republished with new references. <c>Version</c> is a monotonic
/// per-overlay counter so the kiosk can discard out-of-order frames.
/// </summary>
public sealed record ResolvedOverlayTextChangedNotification(
    Guid Overlay,
    string ResolvedText,
    long Version);
