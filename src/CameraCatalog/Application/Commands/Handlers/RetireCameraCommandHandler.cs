using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;

public sealed class RetireCameraCommandHandler(
    ICameraRepository cameras,
    IClock clock,
    ILogger<RetireCameraCommandHandler> logger)
    : ICommandHandler<RetireCameraCommand, Result<CameraIdentifier, RetireCameraError>>
{
    public async Task<Result<CameraIdentifier, RetireCameraError>> HandleAsync(
        RetireCameraCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (FabIdentifier? fab, CameraIdentifier camera, OperatorIdentifier retiredBy) = command;

        // Scoped to the fab, so another plant's camera is indistinguishable
        // from one that does not exist (FR-004).
        Option<Camera> found = await cameras.GetWithinFabAsync(fab, camera, cancellationToken);

        if (!found.HasValue)
        {
            logger.RejectedCameraRetirementNotFound(camera);
            return Failure(RetireCameraFailures.CameraNotFound(camera.Value));
        }

        Camera retiring = found.Value;

        // Idempotency lives in the aggregate, not here: Retire returns without
        // raising when the camera is already retired, so a second call saves
        // nothing and announces nothing while still succeeding (FR-005).
        retiring.Retire(retiredBy, clock);

        await cameras.SaveAsync(cancellationToken);

        logger.RetiredCamera(retiring.Id, retiring.Name);

        return Success(retiring.Id);
    }
}
