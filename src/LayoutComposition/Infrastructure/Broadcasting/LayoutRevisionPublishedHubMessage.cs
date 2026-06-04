namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "a revision became Published" SignalR frames. Spec 010
/// keeps the lifecycle frame <em>lean</em> (ADR-0112 §3) — no tile set;
/// the picker re-queries on receipt. Primitive types only — value-object
/// types stay in Domain and never hit the wire.
/// </summary>
public sealed record LayoutRevisionPublishedHubMessage(
    Guid Layout,
    int RevisionNumber,
    string Name,
    DateTimeOffset PublishedAt);
