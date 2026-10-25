using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;

public sealed class ListOverlaysQueryHandler(IOverlayQuerySource overlays)
    : IQueryHandler<ListOverlaysQuery, Result<ListOverlaysResult, ListOverlaysError>>
{
    public async Task<Result<ListOverlaysResult, ListOverlaysError>> HandleAsync(ListOverlaysQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.State == OverlayRevisionState.Published)
        {
            var source = await overlays.Overlays.Where(overlay => overlay.Revisions.Any(revision => revision.State == OverlayRevisionState.Published)).ToListAsync(cancellationToken);

            IReadOnlyList<PublishedOverlayDto> published = source
                .Select(OverlayMappings.ToPublishedOverlayDto)
                .OrderBy(dto => dto.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Success(new ListOverlaysResult(Array.Empty<OverlayDto>(), published));
        }

        var all = await overlays.Overlays.ToListAsync(cancellationToken);

        IReadOnlyList<OverlayDto> chains = all.Select(OverlayMappings.ToDto).OrderByDescending(dto => dto.CreatedAt).ToList();

        return Success(new ListOverlaysResult(chains, Array.Empty<PublishedOverlayDto>()));
    }
}
