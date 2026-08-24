using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Fetches one camera by its identifier (spec 029 FR-001).
///
/// <para>
/// <c>Fabs</c> is every fab the caller holds, not one they chose. A read does
/// not have to choose (spec 015 FR-005), and an identifier already determines
/// a single camera — so the question is whether that camera is in any of the
/// caller's fabs, not which fab they meant. That is also why spec 015's
/// withdrawn FR-010 has nothing to describe here: an identifier is never
/// ambiguous the way a name is.
/// </para>
/// </summary>
public sealed record GetCameraQuery(IReadOnlyList<FabIdentifier> Fabs, CameraIdentifier Camera)
    : IQuery<Result<CameraDto, GetCameraError>>;
