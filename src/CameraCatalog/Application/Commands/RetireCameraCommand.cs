using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Retires a camera (spec 028, #1433). Terminal, and idempotent: retiring a
/// camera that is already retired succeeds and announces nothing further.
/// </summary>
/// <remarks>
/// Keyed on <paramref name="Camera"/> rather than the name, because this
/// feature is what makes a name reusable — a name identifies at most one
/// <em>active</em> camera per fab but several over time, and a retire keyed
/// that way could not address a camera that is already retired.
/// </remarks>
public sealed record RetireCameraCommand(
    FabIdentifier Fab,
    CameraIdentifier Camera,
    OperatorIdentifier RetiredBy)
    : ICommand<Result<CameraIdentifier, RetireCameraError>>;
