namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Domain abstraction over the real-time push transport (ADR-0152:
/// SignalR is the real-time transport). The Infrastructure
/// implementation broadcasts to the fab group: every connection
/// holding that fab, i.e. any caller with <c>sse.layouts.read</c> (in
/// production, the kiosk). Failures are best-effort — the kiosk's
/// reconnect-and-reconcile path (FR-012) is the safety net.
/// </summary>
public interface ILayoutLifecycleBroadcaster
{
    Task PublishedAsync(LayoutRevisionPublishedNotification notification, CancellationToken cancellationToken);

    Task ArchivedAsync(LayoutRevisionArchivedNotification notification, CancellationToken cancellationToken);

    Task OverlayPublishedAsync(OverlayLifecyclePublishedNotification notification, CancellationToken cancellationToken);

    Task OverlayArchivedAsync(OverlayLifecycleArchivedNotification notification, CancellationToken cancellationToken);

    Task ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification notification, CancellationToken cancellationToken);

    Task OverlayHighlightedAsync(OverlayHighlightedNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// Spec 258 US1, plan.md §4.3. A wall's <c>Showing</c> scene changed.
    /// Best-effort like every other frame here: FR-008's reconnect re-read
    /// is the safety net.
    /// </summary>
    Task WallSceneChangedAsync(WallSceneChangedNotification notification, CancellationToken cancellationToken);
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
/// here for exactly that reason. Spec 150 (#2345): <c>Elements</c> carries the
/// revision's ordered, non-empty set of 1..8 elements instead of one
/// flattened label. Spec 300 (#2349, ADR-0165) widened each element to carry
/// a kind and colour — renamed from <c>Labels</c>.
/// </summary>
public sealed record OverlayLifecyclePublishedNotification(
    IReadOnlyList<FabIdentifier> Fabs,
    Guid Overlay,
    int RevisionNumber,
    string Name,
    IReadOnlyList<OverlayLifecycleElement> Elements,
    DateTimeOffset PublishedAt);

/// <summary>
/// A single element on the published revision, in ordinal (paint) order
/// (spec 150 FR-005; spec 300 #2349, ADR-0165 — renamed from
/// <c>OverlayLifecycleLabel</c>). Primitives only — see
/// <see cref="OverlayLifecyclePublishedNotification"/>.
/// <c>Text</c>/<c>FontSizePx</c> are present exactly when <c>Kind</c> is
/// <c>"Text"</c>.
/// </summary>
public sealed record OverlayLifecycleElement(
    string Kind,
    string Color,
    decimal NormalizedX,
    decimal NormalizedY,
    decimal NormalizedWidth,
    decimal NormalizedHeight,
    string? Text,
    int? FontSizePx);

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
/// (spec 005 FR-013, widened to a set by spec 150 / #2345). Pushed when a
/// system variable referenced by any label in an overlay's set changes,
/// gets archived, or the overlay itself is republished with new
/// references. <c>Version</c> is a monotonic per-overlay counter so the
/// kiosk can discard out-of-order frames — it bumps once per change, not
/// once per label.
/// </summary>
/// <para>
/// <c>Fab</c> decides who receives it (spec 014 FR-015). A resolved text is
/// one plant's answer for a shared overlay — the same overlay renders
/// different values in different fabs — so delivering it everywhere would put
/// Munich's figure on Dresden's wall.
/// </para>
public sealed record ResolvedOverlayTextChangedNotification(
    Guid Overlay,
    IReadOnlyList<string> ResolvedTexts,
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

/// <summary>
/// Wire shape for "a wall's scene changed" pushes (spec 258 US1). Kept as
/// value-object-typed fields, unlike the primitive-typed notifications
/// above, because this one has no cross-context caller — Wall lives in this
/// same context, so there is no boundary forcing primitives (contrast
/// <see cref="OverlayLifecyclePublishedNotification"/>, built from
/// OverlayDesigner's data).
/// </summary>
public sealed record WallSceneChangedNotification(
    FabIdentifier Fab,
    Wall.WallIdentifier Wall,
    LayoutIdentifier Showing,
    Wall.SceneVersion SceneVersion);
