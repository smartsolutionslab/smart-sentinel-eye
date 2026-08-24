using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;

public sealed class RegisterCameraCommandHandler(
    ICameraRepository cameras,
    IClock clock,
    ILogger<RegisterCameraCommandHandler> logger)
    : ICommandHandler<RegisterCameraCommand, Result<CameraIdentifier, RegisterCameraError>>
{
    public async Task<Result<CameraIdentifier, RegisterCameraError>> HandleAsync(
        RegisterCameraCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (FabIdentifier? fab, CameraName? name, RtspUrl? url, OperatorIdentifier registeredBy) = command;

        // Scoped to the fab: another plant holding the name is not a collision
        // at all (FR-002).
        if (await cameras.ExistsByNameAsync(fab, name, Option<CameraIdentifier>.None, cancellationToken))
        {
            logger.RejectedCameraRegistrationNameInUse(name);
            return Failure(RegisterCameraFailures.NameAlreadyTaken(fab.Value, name.Value));
        }

        Domain.Camera.Camera camera = Domain.Camera.Camera.Register(
            fab, name, url, registeredBy, clock);

        cameras.Add(camera);
        await cameras.SaveAsync(cancellationToken);

        logger.RegisteredCamera(camera.Id, camera.Name);

        return Success(camera.Id);
    }
}
