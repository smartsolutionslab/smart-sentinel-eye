using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;

public sealed class ListOverlaysQueryHandler(IOverlayQuerySource overlays)
    : IQueryHandler<ListOverlaysQuery, Result<ListOverlaysResult, ListOverlaysError>>
{
    public async Task<Result<ListOverlaysResult, ListOverlaysError>> HandleAsync(
        ListOverlaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.State == OverlayRevisionState.Published)
        {
            List<Overlay> source = await overlays.Overlays
                .Where(overlay => overlay.Revisions.Any(revision => revision.State == OverlayRevisionState.Published))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<PublishedOverlayDto> published = source
                .Select(overlay =>
                {
                    Revision pub = overlay.Revisions.Single(revision => revision.State == OverlayRevisionState.Published);
                    return new PublishedOverlayDto(
                        OverlayIdentifier: overlay.Id.Value,
                        Name: overlay.Name.Value,
                        RevisionNumber: pub.Number.Value,
                        Text: pub.Label.Text,
                        PublishedAt: pub.PublishedAt!.Value);
                })
                .OrderBy(dto => dto.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result<ListOverlaysResult, ListOverlaysError>.Success(
                new ListOverlaysResult(Array.Empty<OverlayDto>(), published));
        }

        List<Overlay> all = await overlays.Overlays
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OverlayDto> chains = all
            .Select(GetOverlayQueryHandler.Map)
            .OrderByDescending(dto => dto.CreatedAt)
            .ToList();

        return Result<ListOverlaysResult, ListOverlaysError>.Success(
            new ListOverlaysResult(chains, Array.Empty<PublishedOverlayDto>()));
    }
}
