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
///
/// <para>
/// <c>Fabs</c> is the fabs the caller holds (spec 016 FR-005). A stream in
/// another fab is omitted exactly like one that was never provisioned — the
/// caller cannot tell the two apart, which is FR-006 on the batch route.
/// </para>
/// </summary>
public sealed record ListStreamsQuery(
    IReadOnlyList<FabIdentifier> Fabs, IReadOnlyList<CameraIdentifier> Cameras)
    : IQuery<Result<IReadOnlyList<StreamHealthDto>, ListStreamsError>>;

public static class ListStreamsDefaults
{
    public const int MaximumBatchSize = 200;
}
