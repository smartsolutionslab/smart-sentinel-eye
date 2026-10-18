using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Re-points a camera's stream at its corrected address (spec 029 FR-013).
/// Keyed on the camera, because that is what the announcement carries and how
/// a stream is identified everywhere else in this context.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent and ordered: the correction rides the outbox and can be
/// redelivered out of order, so a second delivery must re-point to the same
/// place and announce nothing new, and one made earlier than the change the
/// stream already applied is ignored (spec 385), by the camera version it was
/// made against where it carries one (spec 403).
/// </para>
/// <para>
/// The result is an <see cref="Option{T}"/> because "there was no stream" is a
/// success, not a failure — a camera registered without a resolvable fab is
/// never provisioned one (spec 016 FR-004), and reporting that as a failure
/// would have the outbox redeliver the correction forever. The same shape
/// <c>RetireStreamCommand</c> uses, for the same reason.
/// </para>
/// </remarks>
public sealed record RepointStreamCommand(
    CameraIdentifier Camera,
    string RtspSourceUrl,
    SourceChangedAt ChangedAt,
    Option<SourceCameraVersion> CameraVersion)
    : ICommand<Result<Option<StreamIdentifier>, RepointStreamError>>;
