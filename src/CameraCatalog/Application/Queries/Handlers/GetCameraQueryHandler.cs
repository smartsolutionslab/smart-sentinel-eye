using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;

public sealed class GetCameraQueryHandler(ICameraQuerySource cameras)
    : IQueryHandler<GetCameraQuery, Result<CameraDto, GetCameraError>>
{
    public async Task<Result<CameraDto, GetCameraError>> HandleAsync(
        GetCameraQuery query,
        CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (fabs, camera) = query;

        // The fab is part of the predicate, not a check afterwards: another
        // plant's camera is never materialised, so it cannot be leaked by a
        // caller that forgets to compare (FR-006). Spec 028 made the same
        // choice in CameraRepository.GetWithinFabAsync, and for the same
        // reason — a camera record carries its RTSP address.
        //
        // Value objects, not their inner values: Fab and Id are mapped with
        // value conversions, which EF translates for the whole property but
        // not for a member access on it.
        FabIdentifier[] scopedFabs = [.. fabs];

        // Materialised rather than FirstOrDefault: NRT is disabled (ADR-0048),
        // so a possibly-null result needs a shape the compiler is happy with.
        // GetRuleQueryHandler resolves it the same way. At most one row can
        // match — the identifier is the primary key.
        List<Camera> matches = await cameras.Cameras
            .Where(candidate => scopedFabs.Contains(candidate.Fab) && candidate.Id == camera)
            .Take(1)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Failure(GetCameraFailures.CameraNotFound(camera.Value));
        }

        // No filter on status. A retired camera is returned with its status
        // (FR-002) — retirement removes a camera from the default listing, not
        // from existence.
        Camera found = matches[0];

        return Success(new CameraDto(
            found.Id.Value,
            found.Version,
            found.Fab.Value,
            found.Name.Value,
            found.Url.Value,
            found.Registration.At,
            found.Status.Value));
    }
}
