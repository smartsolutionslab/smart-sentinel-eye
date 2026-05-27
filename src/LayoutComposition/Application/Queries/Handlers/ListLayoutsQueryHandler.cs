using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

public sealed class ListLayoutsQueryHandler(ILayoutQuerySource layouts)
    : IQueryHandler<ListLayoutsQuery, Result<ListLayoutsResult, ListLayoutsError>>
{
    public async Task<Result<ListLayoutsResult, ListLayoutsError>> HandleAsync(
        ListLayoutsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.State == LayoutRevisionState.Published)
        {
            // Kiosk picker shape: one row per chain that has a Published revision.
            // Filter pushed into SQL via the LayoutRevisionState value-converter.
            List<Layout> source = await layouts.Layouts
                .Where(layout => layout.Revisions.Any(r => r.State == LayoutRevisionState.Published))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<PublishedLayoutDto> published = source
                .Select(layout =>
                {
                    Revision pub = layout.Revisions.Single(r => r.State == LayoutRevisionState.Published);
                    return new PublishedLayoutDto(
                        LayoutIdentifier: layout.Id.Value,
                        Name: layout.Name.Value,
                        RevisionNumber: pub.Number.Value,
                        CameraIdentifier: pub.Camera.Value,
                        OverlayIdentifier: pub.Overlay?.Value,
                        PublishedAt: pub.PublishedAt!.Value);
                })
                .OrderBy(dto => dto.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result<ListLayoutsResult, ListLayoutsError>.Success(
                new ListLayoutsResult(Array.Empty<LayoutDto>(), published));
        }

        // Default / admin shape: every chain with its full revision history.
        List<Layout> all = await layouts.Layouts
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<LayoutDto> chains = all
            .Select(GetLayoutQueryHandler.Map)
            .OrderByDescending(dto => dto.CreatedAt)
            .ToList();

        return Result<ListLayoutsResult, ListLayoutsError>.Success(
            new ListLayoutsResult(chains, Array.Empty<PublishedLayoutDto>()));
    }
}
