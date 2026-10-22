using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;

// Red-phase skeleton (spec 438 T003): lets the tests compile, implements nothing.
public sealed class GetCameraAddressQueryHandler
    : IQueryHandler<GetCameraAddressQuery, Result<CameraAddressDto, GetCameraAddressError>>
{
    public GetCameraAddressQueryHandler(
        ICameraQuerySource cameras,
        IEventBus events,
        ITransactionalCommit commit,
        IClock clock)
    {
    }

    public Task<Result<CameraAddressDto, GetCameraAddressError>> HandleAsync(
        GetCameraAddressQuery query,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
