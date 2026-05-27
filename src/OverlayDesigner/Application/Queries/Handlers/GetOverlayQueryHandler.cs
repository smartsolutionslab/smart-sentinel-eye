using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;

public sealed class GetOverlayQueryHandler(IOverlayQuerySource overlays)
    : IQueryHandler<GetOverlayQuery, Result<OverlayDto, GetOverlayError>>
{
    public async Task<Result<OverlayDto, GetOverlayError>> HandleAsync(
        GetOverlayQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Overlay? overlay = await overlays.Overlays
            .SingleOrDefaultAsync(candidate => candidate.Id == query.Overlay, cancellationToken)
            .ConfigureAwait(false);

        if (overlay is null)
        {
            return Result<OverlayDto, GetOverlayError>.Failure(
                new GetOverlayError.OverlayNotFound(query.Overlay.Value));
        }

        return Result<OverlayDto, GetOverlayError>.Success(Map(overlay));
    }

    internal static OverlayDto Map(Overlay overlay) =>
        new(
            OverlayIdentifier: overlay.Id.Value,
            Name: overlay.Name.Value,
            CreatedAt: overlay.CreatedAt,
            CreatedBy: overlay.CreatedBy.Value,
            Revisions: overlay.Revisions
                .OrderBy(r => r.Number.Value)
                .Select(MapRevision)
                .ToList());

    internal static OverlayRevisionDto MapRevision(Revision revision) =>
        new(
            RevisionIdentifier: revision.Id.Value,
            RevisionNumber: revision.Number.Value,
            State: revision.State.Value,
            Text: revision.Label.Text,
            NormalizedX: revision.Label.NormalizedX,
            NormalizedY: revision.Label.NormalizedY,
            NormalizedWidth: revision.Label.NormalizedWidth,
            NormalizedHeight: revision.Label.NormalizedHeight,
            FontSizePx: revision.Label.FontSizePx,
            CreatedAt: revision.CreatedAt,
            CreatedBy: revision.CreatedBy.Value,
            PublishedAt: revision.PublishedAt,
            ArchivedAt: revision.ArchivedAt);
}
