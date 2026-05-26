using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries;

/// <summary>
/// Fetches the current health snapshot for one camera's stream (spec 002 FR-005).
/// Returns <c>StreamNotFound</c> if the camera has not yet been provisioned.
/// </summary>
public sealed record GetStreamQuery(CameraIdentifier Camera)
    : IQuery<Result<StreamHealthDto, GetStreamError>>;
