using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

public sealed class GetLayoutQueryHandler(ILayoutQuerySource layouts)
    : IQueryHandler<GetLayoutQuery, Result<LayoutDto, GetLayoutError>>
{
    public async Task<Result<LayoutDto, GetLayoutError>> HandleAsync(
        GetLayoutQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        Layout? layout = await layouts.Layouts
            .SingleOrDefaultAsync(candidate => candidate.Id == query.Layout, cancellationToken);

        if (layout is null)
        {
            return Failure(GetLayoutFailures.LayoutNotFound(query.Layout.Value));
        }

        return Success(Map(layout));
    }

    internal static LayoutDto Map(Layout layout) =>
        new(
            LayoutIdentifier: layout.Id.Value,
            Version: layout.Version,
            Name: layout.Name.Value,
            CreatedAt: layout.CreatedAt,
            CreatedBy: layout.CreatedBy.Value,
            Revisions: layout.Revisions
                .OrderBy(revision => revision.Number.Value)
                .Select(MapRevision)
                .ToList());

    internal static LayoutRevisionDto MapRevision(Revision revision) =>
        new(
            RevisionIdentifier: revision.Id.Value,
            RevisionNumber: revision.Number.Value,
            State: revision.State.Value,
            GridRows: revision.Grid.Rows,
            GridCols: revision.Grid.Cols,
            Tiles: MapTiles(revision),
            CreatedAt: revision.CreatedAt,
            CreatedBy: revision.CreatedBy.Value,
            PublishedAt: revision.PublishedAt,
            ArchivedAt: revision.ArchivedAt);

    internal static IReadOnlyList<TileDto> MapTiles(Revision revision) =>
        revision.Tiles
            .OrderBy(tile => tile.Position.Row)
            .ThenBy(tile => tile.Position.Col)
            .Select(tile => new TileDto(
                CameraIdentifier: tile.Camera.Value,
                OverlayIdentifier: tile.Overlay.Match(overlay => (Guid?)overlay.Value, () => null),
                Row: tile.Position.Row,
                Col: tile.Position.Col))
            .ToList();
}
