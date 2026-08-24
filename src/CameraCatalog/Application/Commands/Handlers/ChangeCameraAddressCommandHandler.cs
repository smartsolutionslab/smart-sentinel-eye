using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;

public sealed class ChangeCameraAddressCommandHandler(
    ICameraRepository cameras,
    IClock clock,
    ILogger<ChangeCameraAddressCommandHandler> logger)
    : ICommandHandler<ChangeCameraAddressCommand, Result<CameraIdentifier, ChangeCameraAddressError>>
{
    public async Task<Result<CameraIdentifier, ChangeCameraAddressError>> HandleAsync(
        ChangeCameraAddressCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, camera, url, expectedVersion, changedBy) = command;

        // Scoped to the fab, so another plant's camera is indistinguishable
        // from one that does not exist (FR-006). The fab is in the predicate,
        // so the row is never materialised rather than materialised and
        // refused.
        Option<Camera> found = await cameras.GetWithinFabAsync(fab, camera, cancellationToken);

        if (!found.HasValue)
        {
            logger.RejectedCameraAddressChangeNotFound(camera);
            return Failure(ChangeCameraAddressFailures.CameraNotFound(camera.Value));
        }

        Camera changing = found.Value;

        // Version before terminal state: a caller working from a stale read
        // should be told their view is stale, not told about a state their
        // view may not have shown them (ADR-0113, no retry on conflict).
        if (changing.Version != expectedVersion)
        {
            logger.RejectedCameraAddressChangeStaleVersion(camera, expectedVersion, changing.Version);
            return Failure(ChangeCameraAddressFailures.VersionMismatch(
                camera.Value, expectedVersion, changing.Version));
        }

        // The terminal rule lives in the aggregate, which throws; this
        // translates it. A guard here instead would be bypassable by the next
        // caller (FR-005).
        try
        {
            changing.ChangeAddress(url, changedBy, clock);
        }
        catch (InvalidOperationException)
        {
            return Failure(ChangeCameraAddressFailures.CameraRetired(camera.Value));
        }

        // Saved unconditionally, including when ChangeAddress raised nothing
        // because the address was already correct: SaveAsync dispatches the
        // pending events, and with none pending it commits nothing and
        // announces nothing while the request still succeeds.
        await cameras.SaveAsync(cancellationToken);

        logger.ChangedCameraAddress(changing.Id, changing.Url);

        return Success(changing.Id);
    }
}
