namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Typed SignalR client interface (server-to-client methods). Concrete
/// names map directly to method names on the JS-side ``HubConnection``.
/// </summary>
public interface ILayoutLifecycleClient
{
    Task LayoutRevisionPublished(LayoutRevisionPublishedHubMessage message);

    Task LayoutRevisionArchived(LayoutRevisionArchivedHubMessage message);

    Task OverlayRevisionPublished(OverlayRevisionPublishedHubMessage message);

    Task OverlayRevisionArchived(OverlayRevisionArchivedHubMessage message);
}

/// <summary>
/// Wire shape for "a revision became Published" SignalR frames.
/// Primitive types only — value-object types stay in Domain and never
/// hit the wire (mirrors the V1 integration-event pattern).
/// </summary>
public sealed record LayoutRevisionPublishedHubMessage(
    Guid Layout,
    int RevisionNumber,
    string Name,
    Guid Camera,
    DateTimeOffset PublishedAt);

/// <summary>
/// Wire shape for "a revision became Archived" SignalR frames.
/// </summary>
public sealed record LayoutRevisionArchivedHubMessage(
    Guid Layout,
    int RevisionNumber,
    DateTimeOffset ArchivedAt);

/// <summary>
/// Wire shape for "an overlay revision became Published" SignalR frames.
/// Primitive types only — mirrors the V1 integration-event shape so
/// kiosks can render without an extra fetch.
/// </summary>
public sealed record OverlayRevisionPublishedHubMessage(
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
/// Wire shape for "an overlay revision became Archived" SignalR frames.
/// </summary>
public sealed record OverlayRevisionArchivedHubMessage(
    Guid Overlay,
    int RevisionNumber,
    DateTimeOffset ArchivedAt);
