using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Returns the current resolved label text for an overlay
/// (spec 005 US3). Used by the kiosk's cold-load path before SignalR
/// updates start flowing.
/// </summary>
public sealed record GetOverlaySnapshotQuery(Guid OverlayIdentifier)
    : IQuery<Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>>;
