using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

public sealed record GetCameraAddressQuery(FabIdentifier Fab, CameraIdentifier Camera, OperatorIdentifier ReadBy)
    : IQuery<Result<CameraAddressDto, GetCameraAddressError>>;
