namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event published when a Layout revision transitions to the
/// Archived state — either explicitly via Archive, or implicitly when a
/// newer revision of the same chain is published (FR-003). Versioned per
/// ADR-0073; subscribers consume via Wolverine RabbitMQ with per-module
/// queue isolation (ADR-0088).
/// </summary>
public sealed record LayoutRevisionArchivedV1(
    Guid Layout,
    int RevisionNumber,
    DateTimeOffset ArchivedAt,
    Guid ArchivedBy) : IIntegrationEvent;
