using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Retires the stream belonging to a retired camera (spec 028 FR-008). Keyed on
/// the camera because that is what the announcement carries, and because a
/// stream is identified by its camera everywhere else in this context.
///
/// <para>
/// Idempotent: the retirement rides the outbox and can be redelivered, so a
/// second delivery must be a no-op rather than a second announcement.
/// </para>
///
/// <para>
/// The result is an <see cref="Option{T}"/> because "there was no stream" is a
/// success, not a failure. A camera registered without a resolvable fab is
/// never provisioned one (spec 016 FR-004), and reporting that as a failure
/// would have the outbox redeliver the retirement forever for a camera that
/// never had a stream to retire. <c>None</c> says nothing was there; every
/// actual failure is transient and worth retrying.
/// </para>
/// </summary>
public sealed record RetireStreamCommand(CameraIdentifier Camera)
    : ICommand<Result<Option<StreamIdentifier>, RetireStreamError>>;
