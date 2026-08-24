namespace SmartSentinelEye.Shared.Contracts.CameraCatalog;

/// <summary>
/// Integration event published when a camera is renamed (spec 033 FR-012).
/// Versioned per ADR-0073; subscribers consume via Wolverine RabbitMQ with
/// per-module queue isolation (ADR-0088).
///
/// Primitive types (Guid, string, DateTimeOffset) are used at the wire
/// boundary — value-object types stay inside their owning context per
/// ADR-0040.
/// </summary>
/// <remarks>
/// <para>
/// One consumer: AuditObservability records it. Nothing else needs it, because
/// no other context stores a camera's name — layouts reference cameras by
/// identifier, which is exactly what makes a rename safe (ADR-0120).
/// </para>
/// <para>
/// <paramref name="PreviousName"/> travels alongside the new one because the
/// audit trail's whole value here is the delta: "renamed to line-4-inlet"
/// records that something happened without saying what was corrected.
/// </para>
/// <para>
/// Earlier events are untouched by a rename (FR-013).
/// <c>CameraRegisteredV1</c> and <c>CameraRetiredV1</c> carry the name as it
/// was at that moment, and the audit trail records what was true when — not
/// what is true now.
/// </para>
/// </remarks>
public sealed record CameraRenamedV1(
    Guid Camera,
    string Fab,
    string PreviousName,
    string Name,
    DateTimeOffset RenamedAt,
    Guid RenamedBy,
    EventMetadata Metadata) : IIntegrationEvent;
