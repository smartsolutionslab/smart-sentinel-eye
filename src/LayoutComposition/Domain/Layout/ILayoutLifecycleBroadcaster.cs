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

    Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// Wire shape for "a revision became Published" pushes. Spec 010 keeps
/// the lifecycle frame <em>lean</em> (ADR-0112 §3, plan T010): it carries
/// only the chain identity + name so the picker invalidates its list and
/// re-queries. The tile set rides the <c>LayoutRevisionPublishedV2</c>
/// integration event, not this SignalR frame. Stays inside the domain so
/// the broadcaster contract doesn't need a Shared.Contracts dependency.
/// </summary>
public sealed record LayoutRevisionPublishedNotification(
    FabIdentifier Fab,
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    LayoutName Name,
    DateTimeOffset PublishedAt);

/// <summary>
/// Wire shape for "a revision became Archived" pushes. Carries the bare
/// minimum the kiosk needs to decide whether to force-disconnect.
/// </summary>
public sealed record LayoutRevisionArchivedNotification(
    FabIdentifier Fab,
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    DateTimeOffset ArchivedAt);

/// <summary>
/// Wire shape for "an overlay revision became Published" pushes. The
/// cross-context bridge from OverlayDesigner.Application
/// (spec 004 plan.md — single documented allow-rule); primitive types
/// only so the broadcaster contract does not need to reference
/// OverlayDesigner.Domain — including its NormalizedPosition and
/// NormalizedSize, which group these same four coordinates and were declined
/// here for exactly that reason.
/// </summary>
public sealed record OverlayLifecyclePublishedNotification(
    IReadOnlyList<FabIdentifier> Fabs,
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
    IReadOnlyList<FabIdentifier> Fabs,
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
/// <para>
/// <c>Fab</c> decides who receives it (spec 014 FR-015). A resolved text is
/// one plant's answer for a shared overlay — the same overlay renders
/// different values in different fabs — so delivering it everywhere would put
/// Munich's figure on Dresden's wall.
/// </para>
public sealed record ResolvedOverlayTextChangedNotification(
    Guid Overlay,
    string ResolvedText,
    long Version,
    string Fab);

/// <summary>
/// Wire shape for "an overlay should be highlighted" pushes
/// (spec 007 FR-019). Pushed when an Automation rule's
/// <c>HighlightOverlay</c> action fires. The kiosk applies the
/// <c>ssE-overlay-highlight</c> CSS class for
/// <see cref="DurationMs"/> milliseconds and auto-reverts.
/// </summary>
/// <para>
/// <c>Fab</c> decides who receives it (spec 014 FR-015). A highlight is a
/// visible change on a wall, requested by one plant's rule — the fab travels
/// on the Automation event that triggers it, so no new concept is needed to
/// address it correctly (#1397).
/// </para>
public sealed record OverlayHighlightedNotification(
    Guid Overlay,
    int DurationMs,
    string Fab);
