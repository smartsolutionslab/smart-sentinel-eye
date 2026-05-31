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
        ArgumentNullException.ThrowIfNull(command);
        var (name, url, registeredBy) = command;

        if (await cameras.ExistsByNameAsync(name, cancellationToken).ConfigureAwait(false))
        {
            Log.RejectedCameraRegistrationNameInUse(logger, name);
            return Result<CameraIdentifier, RegisterCameraError>.Failure(
                new RegisterCameraError.NameAlreadyTaken());
        }

        Domain.Camera.Camera camera = Domain.Camera.Camera.Register(
            name, url, registeredBy, clock);

        cameras.Add(camera);
        await cameras.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.RegisteredCamera(logger, camera.Id, camera.Name);

        return Result<CameraIdentifier, RegisterCameraError>.Success(camera.Id);
    }
}
