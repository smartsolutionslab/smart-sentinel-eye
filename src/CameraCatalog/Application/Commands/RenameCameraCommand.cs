using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Corrects a camera's name (spec 033 FR-005).
/// </summary>
/// <remarks>
/// <para>
/// Permitted at all because a camera is addressed by its identifier, so the
/// name is an attribute and nothing refers to the old value (ADR-0120). The
/// same operation on a rule or a variable would be an identity change, which is
/// why neither offers one.
/// </para>
/// <para>
/// Keyed on <paramref name="Camera"/>, not on the old name — a name identifies
/// at most one <em>active</em> camera per fab but several over time, so a
/// name-keyed rename could address the wrong one. That is the reason spec 028
/// keyed retirement this way, and it is what makes this feature possible.
/// </para>
/// <para>
/// <paramref name="ExpectedVersion"/> is required (ADR-0113). A rename is
/// version-checked, unlike retirement: retiring is idempotent and converges on
/// one outcome, while a rename changes an attribute other writers may be
/// looking at, and two blind renames would silently pick a winner.
/// </para>
/// </remarks>
public sealed record RenameCameraCommand(
    FabIdentifier Fab,
    CameraIdentifier Camera,
    CameraName Name,
    int ExpectedVersion,
    OperatorIdentifier RenamedBy)
    : ICommand<Result<CameraIdentifier, RenameCameraError>>;
