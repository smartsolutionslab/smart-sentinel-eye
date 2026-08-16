using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries;

/// <summary>
/// Fetches the current health snapshot for one camera's stream (spec 002 FR-005).
/// Returns <c>StreamNotFound</c> if the camera has not yet been provisioned.
///
/// <para>
/// <c>Fabs</c> is the fabs the caller holds (spec 016 FR-005). A stream outside
/// them is reported as not found rather than forbidden: the record carries the
/// MediaMTX path its video is served on, so "it exists but is not yours" is
/// itself worth withholding (FR-006).
/// </para>
/// </summary>
public sealed record GetStreamQuery(IReadOnlyList<FabIdentifier> Fabs, CameraIdentifier Camera)
    : IQuery<Result<StreamHealthDto, GetStreamError>>;
