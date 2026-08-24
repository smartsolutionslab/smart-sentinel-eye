using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera.Events;

/// <summary>
/// In-process domain event raised when a camera is renamed (spec 033 FR-005).
/// Never crosses the bounded-context boundary; the Application layer translates
/// it to CameraRenamedV1 (Shared.Contracts) before publishing (ADR-0040).
/// </summary>
/// <remarks>
/// <para>
/// Carries <paramref name="PreviousName"/> as well as the new one, for the same
/// reason <see cref="CameraAddressChangedDomainEvent"/> carries the previous
/// URL: an audit entry reading "renamed to line-4-inlet" records that something
/// happened without saying what was corrected, which is most of the value.
/// </para>
/// <para>
/// A rename appends to history; it does not revisit it (FR-013). Earlier events
/// carry the name as it was at that moment and stay that way.
/// </para>
/// </remarks>
public sealed record CameraRenamedDomainEvent(
    CameraIdentifier Camera,
    FabIdentifier Fab,
    CameraName PreviousName,
    CameraName Name,
    DateTimeOffset RenamedAt,
    OperatorIdentifier RenamedBy) : IDomainEvent;
