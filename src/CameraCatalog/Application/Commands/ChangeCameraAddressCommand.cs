using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Corrects a camera's RTSP address (spec 029 FR-003).
/// </summary>
/// <remarks>
/// <para>
/// Keyed on <paramref name="Camera"/> for the reason spec 028 keyed retirement
/// that way: a name identifies at most one <em>active</em> camera per fab but
/// several over time, so a name-keyed correction could address the wrong one.
/// </para>
/// <para>
/// <paramref name="ExpectedVersion"/> is the version the caller read
/// (ADR-0113). It is required rather than optional — a change that did not
/// have to say what it was based on would reopen the lost-update hole the
/// two-layer scheme closes — and there is no retry on conflict.
/// </para>
/// </remarks>
public sealed record ChangeCameraAddressCommand(
    FabIdentifier Fab,
    CameraIdentifier Camera,
    RtspUrl Url,
    int ExpectedVersion,
    OperatorIdentifier ChangedBy)
    : ICommand<Result<CameraIdentifier, ChangeCameraAddressError>>;
