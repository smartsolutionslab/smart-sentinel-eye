using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;

public sealed class RenameCameraCommandHandler(
    ICameraRepository cameras,
    IClock clock,
    ILogger<RenameCameraCommandHandler> logger)
    : ICommandHandler<RenameCameraCommand, Result<CameraIdentifier, RenameCameraError>>
{
    public async Task<Result<CameraIdentifier, RenameCameraError>> HandleAsync(
        RenameCameraCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var (fab, camera, name, expectedVersion, renamedBy) = command;

        // Scoped to the fab, so another plant's camera is indistinguishable
        // from one that does not exist (spec 029 FR-006). The fab is in the
        // predicate, so the row is never materialised rather than materialised
        // and refused — and every later step here would otherwise leak its
        // existence by answering something more specific than 404.
        Option<Camera> found = await cameras.GetWithinFabAsync(fab, camera, cancellationToken);

        if (!found.HasValue)
        {
            logger.RejectedCameraRenameNotFound(camera);
            return Failure(RenameCameraFailures.CameraNotFound(camera.Value));
        }

        Camera renaming = found.Value;

        // Version before terminal state, matching the address correction: a
        // caller working from a stale read should be told their view is stale,
        // not told about a state their view may not have shown them.
        if (renaming.Version != expectedVersion)
        {
            logger.RejectedCameraRenameStaleVersion(camera, expectedVersion, renaming.Version);
            return Failure(RenameCameraFailures.VersionStale(
                camera.Value, expectedVersion, renaming.Version));
        }

        // Uniqueness last, because it is the only step that reads other rows —
        // and excluding this camera, or it finds itself: it is active, in this
        // fab, and holds this normalised name whenever the rename is a no-op or
        // changes only letter case (spec 033 FR-010, ADR-0120).
        //
        // This is a guard, not a guarantee. The check and the write are not
        // atomic, so ux_cameras_fab_name_normalized_active is what actually
        // holds the invariant under a race. Both are required and they do
        // different jobs; concluding the index makes this redundant is the
        // defect spec 028 found on this same predicate.
        bool taken = await cameras.ExistsByNameAsync(
            fab, name, Option<CameraIdentifier>.Some(camera), cancellationToken);

        if (taken)
        {
            logger.RejectedCameraRenameNameTaken(camera, name);
            return Failure(RenameCameraFailures.NameTaken(name.Value, fab.Value));
        }

        // The terminal rule lives in the aggregate, which throws; this
        // translates it. A guard here instead would be a second copy of the
        // rule, which is how spec 028's defect happened (FR-009).
        try
        {
            renaming.Rename(name, renamedBy, clock);
        }
        catch (InvalidOperationException)
        {
            return Failure(RenameCameraFailures.CameraRetired(camera.Value));
        }

        // Saved unconditionally, including when Rename raised nothing because
        // the name was already exactly right: SaveAsync dispatches the pending
        // events, and with none pending it commits nothing and announces
        // nothing while the request still succeeds (FR-010).
        await cameras.SaveAsync(cancellationToken);

        logger.RenamedCamera(renaming.Id, renaming.Name);

        return Success(renaming.Id);
    }
}
