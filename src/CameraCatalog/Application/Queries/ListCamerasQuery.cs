using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Lists registered cameras with client-controlled sort + pagination
/// per spec 001-register-camera FR-007a + FR-007b.
/// </summary>
/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 015 FR-005). A list spans all
/// of them when none is named — the deliberate asymmetry with the write path,
/// which must choose. A listing that refused a multi-fab operator would be
/// unusable for exactly the people it exists for.
/// </summary>
public sealed record ListCamerasQuery(
    IReadOnlyList<FabIdentifier> Fabs, string Sort, string Order, int Offset, int Limit)
    : IQuery<Result<CameraListPageDto, ListCamerasError>>;

public static class ListCamerasDefaults
{
    public const string DefaultSort = "registeredAt";
    public const string DefaultOrder = "desc";
    public const int DefaultOffset = 0;
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 200;
}
