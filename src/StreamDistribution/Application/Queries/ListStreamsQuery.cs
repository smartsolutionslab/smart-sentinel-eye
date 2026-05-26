using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.DTOs;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Queries;

/// <summary>
/// Batch read of stream health for the camera identifiers the UI is rendering
/// (spec 002 FR-006). Returns one DTO per requested identifier; missing
/// streams are silently omitted from the result so the UI can render the
/// catalog-side "not yet provisioned" state for them.
/// </summary>
public sealed record ListStreamsQuery(IReadOnlyList<CameraIdentifier> Cameras)
    : IQuery<Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>>;

public static class ListStreamsDefaults
{
    public const int MaximumBatchSize = 200;
}
