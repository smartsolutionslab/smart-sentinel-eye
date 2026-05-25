using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;

public sealed class RegisterCameraCommandHandler(
    ICameraRepository cameras,
    IClock clock,
    ILogger<RegisterCameraCommandHandler> log)
    : ICommandHandler<RegisterCameraCommand, Result<CameraIdentifier, RegisterCameraError>>
{
    public async Task<Result<CameraIdentifier, RegisterCameraError>> HandleAsync(
        RegisterCameraCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await cameras.ExistsByNameAsync(command.Name, cancellationToken).ConfigureAwait(false))
        {
            log.LogInformation(
                "Rejected camera registration: name {CameraName} already in use.",
                command.Name);
            return Result<CameraIdentifier, RegisterCameraError>.Failure(
                new RegisterCameraError.NameAlreadyTaken());
        }

        Domain.Camera.Camera camera = Domain.Camera.Camera.Register(
            command.Name, command.Url, command.RegisteredBy, clock);

        cameras.Add(camera);
        await cameras.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Registered camera {CameraId} with name {CameraName}.",
            camera.Id, camera.Name);

        return Result<CameraIdentifier, RegisterCameraError>.Success(camera.Id);
    }
}
