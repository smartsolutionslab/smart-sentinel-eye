namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event published when a Layout revision transitions to the
/// Published state. Versioned per ADR-0073; subscribers consume via
/// Wolverine RabbitMQ with per-module queue isolation (ADR-0088).
///
/// Primitive types (Guid, string, int, DateTimeOffset) are used at the
/// wire boundary — value-object types stay inside their owning context
/// per ADR-0040. <c>Layout</c> is the chain identifier;
/// <c>RevisionNumber</c> identifies which revision within the chain was
/// published.
/// </summary>
public sealed record LayoutRevisionPublishedV1(
    Guid Layout,
    int RevisionNumber,
    string Name,
    Guid Camera,
    DateTimeOffset PublishedAt,
    Guid PublishedBy) : IIntegrationEvent;
