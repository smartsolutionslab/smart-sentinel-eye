using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;

public sealed class GetOverlayQueryHandler(IOverlayQuerySource overlays)
    : IQueryHandler<GetOverlayQuery, Result<OverlayDto, GetOverlayError>>
{
    public async Task<Result<OverlayDto, GetOverlayError>> HandleAsync(GetOverlayQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var overlay = await overlays.Overlays.SingleOrDefaultAsync(candidate => candidate.Id == query.Overlay, cancellationToken);

        if (overlay is null)
        {
            return Failure(GetOverlayFailures.OverlayNotFound(query.Overlay.Value));
        }

        return Success(overlay.ToDto());
    }
}
