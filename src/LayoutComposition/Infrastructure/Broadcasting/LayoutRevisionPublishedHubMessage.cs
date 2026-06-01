namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

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
