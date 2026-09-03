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

        Overlay? overlay = await overlays.Overlays.SingleOrDefaultAsync(candidate => candidate.Id == query.Overlay, cancellationToken);

        if (overlay is null)
        {
            return Failure(GetOverlayFailures.OverlayNotFound(query.Overlay.Value));
        }

        return Success(Map(overlay));
    }

    internal static OverlayDto Map(Overlay overlay) =>
        new(
            OverlayIdentifier: overlay.Id.Value,
            Version: overlay.Version,
            Name: overlay.Name.Value,
            CreatedAt: overlay.Creation.At,
            CreatedBy: overlay.Creation.By.Value,
            Revisions: overlay.Revisions
                .OrderBy(revision => revision.Number.Value)
                .Select(MapRevision)
                .ToList());

    internal static OverlayRevisionDto MapRevision(Revision revision) =>
        new(
            RevisionIdentifier: revision.Id.Value,
            RevisionNumber: revision.Number.Value,
            State: revision.State.Value,
            Text: revision.Label.Text,
            NormalizedX: revision.Label.Position.X,
            NormalizedY: revision.Label.Position.Y,
            NormalizedWidth: revision.Label.Size.Width,
            NormalizedHeight: revision.Label.Size.Height,
            FontSizePx: revision.Label.FontSizePx,
            CreatedAt: revision.Creation.At,
            CreatedBy: revision.Creation.By.Value,
            PublishedAt: revision.PublishedAt?.Value,
            ArchivedAt: revision.ArchivedAt?.Value);
}
