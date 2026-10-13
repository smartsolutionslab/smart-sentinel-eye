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
        Ensure.That(query).IsNotNull();

        (var fabs, var state) = query;

        // FR-005: only layouts in fabs the caller holds. Applied to both
        // shapes below — the kiosk picker leaks as readily as the admin list.
        var visible = layouts.Layouts.Where(layout => fabs.Contains(layout.Fab));

        if (state == LayoutRevisionState.Published)
        {
            return Success(await ListPublishedAsync(visible, cancellationToken));
        }

        return Success(await ListChainsAsync(visible, cancellationToken));
    }

    // Kiosk picker shape: one row per chain that has a Published revision.
    // Filter pushed into SQL via the LayoutRevisionState value-converter.
    private static async Task<ListLayoutsResult> ListPublishedAsync(
        IQueryable<Layout> visible,
        CancellationToken cancellationToken)
    {
        var source = await visible
            .Where(layout => layout.Revisions.Any(revision => revision.State == LayoutRevisionState.Published))
            .ToListAsync(cancellationToken);

        IReadOnlyList<PublishedLayoutDto> published = source
            .Select(layout =>
            {
                var pub = layout.Revisions.Single(revision => revision.State == LayoutRevisionState.Published);
                return new PublishedLayoutDto(
                    LayoutIdentifier: layout.Id.Value,
                    Name: layout.Name.Value,
                    RevisionNumber: pub.Number.Value,
                    GridRows: pub.Grid.Rows,
                    GridCols: pub.Grid.Cols,
                    Tiles: GetLayoutQueryHandler.MapTiles(pub),
                    PublishedAt: pub.PublishedAt!.Value);
            })
            .OrderBy(dto => dto.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ListLayoutsResult(Array.Empty<LayoutDto>(), published);
    }

    // Default / admin shape: every chain with its full revision history.
    private static async Task<ListLayoutsResult> ListChainsAsync(
        IQueryable<Layout> visible,
        CancellationToken cancellationToken)
    {
        var all = await visible
            .ToListAsync(cancellationToken);

        IReadOnlyList<LayoutDto> chains = all
            .Select(GetLayoutQueryHandler.Map)
            .OrderByDescending(dto => dto.CreatedAt)
            .ToList();

        return new ListLayoutsResult(chains, Array.Empty<PublishedLayoutDto>());
    }
}
