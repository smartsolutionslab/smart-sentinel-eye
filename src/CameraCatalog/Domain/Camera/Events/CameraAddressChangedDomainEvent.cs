using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera.Events;

/// <summary>
/// In-process domain event raised when a camera's RTSP address is corrected
/// (spec 029 FR-003). Never crosses the bounded-context boundary; the
/// Application layer translates it to CameraAddressChangedV1
/// (Shared.Contracts) before publishing (ADR-0040).
/// </summary>
/// <remarks>
/// Carries <paramref name="PreviousUrl"/> as well as the new one. The audit
/// trail's value here is the delta — "the address changed" without saying from
/// what records that something happened rather than what — and stream
/// distribution can tell a real move from a redelivery without re-reading the
/// aggregate.
/// </remarks>
public sealed record CameraAddressChangedDomainEvent(
    CameraIdentifier Camera,
    FabIdentifier Fab,
    RtspUrl PreviousUrl,
    RtspUrl Url,
    DateTimeOffset ChangedAt,
    OperatorIdentifier ChangedBy) : IDomainEvent;
