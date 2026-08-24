namespace SmartSentinelEye.Shared.Contracts.CameraCatalog;

/// <summary>
/// Integration event published when a camera's RTSP address is corrected
/// (spec 029 FR-013). Versioned per ADR-0073; subscribers consume via
/// Wolverine RabbitMQ with per-module queue isolation (ADR-0088).
///
/// Primitive types (Guid, string, DateTimeOffset) are used at the wire
/// boundary — value-object types stay inside their owning context per
/// ADR-0040.
/// </summary>
/// <remarks>
/// <para>
/// Two consumers, one event. AuditObservability records it (FR-011), and
/// StreamDistribution re-points the SFU at the new source (FR-013) — which is
/// why adopting the cross-context half cost the consumer rather than the
/// announcement: the audit trail needed this event regardless.
/// </para>
/// <para>
/// <paramref name="PreviousUrl"/> travels alongside the new one so a subscriber
/// can tell a real move from a redelivery without re-reading the aggregate, and
/// so the audit trail records what changed rather than merely that something
/// did.
/// </para>
/// </remarks>
public sealed record CameraAddressChangedV1(
    Guid Camera,
    string Fab,
    string PreviousUrl,
    string Url,
    DateTimeOffset ChangedAt,
    Guid ChangedBy,
    EventMetadata Metadata) : IIntegrationEvent;
