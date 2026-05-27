namespace SmartSentinelEye.Shared.Contracts.OverlayDesigner;

/// <summary>
/// Integration event published when an Overlay revision transitions to
/// the Archived state — either explicitly via Archive, or implicitly
/// when a newer revision of the same chain is published (the atomic-
/// swap path per spec 004 FR-003). Versioned per ADR-0073.
/// </summary>
public sealed record OverlayRevisionArchivedV1(
    Guid Overlay,
    int RevisionNumber,
    DateTimeOffset ArchivedAt,
    Guid ArchivedBy) : IIntegrationEvent;
