using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;

/// <summary>
/// Hands one camera's full stored address to its writer, and records that it did (ADR-0179). The audit event
/// is published and committed before the address is returned, so a failure to record discloses nothing.
/// </summary>
public sealed class GetCameraAddressQueryHandler(
    ICameraQuerySource cameras,
    IEventBus events,
    ITransactionalCommit commit,
    IClock clock)
    : IQueryHandler<GetCameraAddressQuery, Result<CameraAddressDto, GetCameraAddressError>>
{
    public async Task<Result<CameraAddressDto, GetCameraAddressError>> HandleAsync(
        GetCameraAddressQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (fab, camera, readBy) = query;

        // The fab is part of the predicate, as in GetCameraQueryHandler: another fab's camera is never
        // materialised, so it is indistinguishable from an unknown one. A retired camera is excluded for the
        // same reason (FR-A04a): there is no address left to correct, and PATCH refuses it.
        var matches = await cameras.Cameras
            .Where(candidate => candidate.Fab == fab
                && candidate.Id == camera
                && candidate.Status != CameraStatus.Decommissioned)
            .Take(1)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Failure(GetCameraAddressFailures.CameraNotFound(camera.Value));
        }

        var found = matches[0];
        var readAt = clock.UtcNow;

        // From the publish on, nothing takes the request token: a disconnect must not skip the audit
        // the disclosure below depends on (RotateWebhookClientCommandHandler). A throw here propagates,
        // so no address is returned (FR-A06). The audit survives a crash only because this type is on
        // CameraCatalog's durableOutboxMessageTypes (ADR-0185); an undeclared type is sent Inline.
        await events.PublishAsync(
            new CameraAddressReadV1(
                camera.Value,
                fab.Value,
                readBy.Value,
                readAt,
                new EventMetadata(Guid.CreateVersion7(), readAt, fab.Value, readBy.Value)),
            CancellationToken.None);

        await commit.CommitAsync(CancellationToken.None);

        return Success(new CameraAddressDto(found.Url.Value, found.Version));
    }
}
