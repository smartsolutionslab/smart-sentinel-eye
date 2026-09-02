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

        (IReadOnlyList<FabIdentifier> fabs, LayoutIdentifier identifier) = query;

        // FR-006: the fab is part of the lookup rather than a check afterwards,
        // so a layout outside the caller's fabs and one that never existed take
        // the same path out of here and produce the same response.
        Layout? layout = await layouts.Layouts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == identifier && fabs.Contains(candidate.Fab),
                cancellationToken);

        if (layout is null)
        {
            return Failure(GetLayoutFailures.LayoutNotFound(identifier.Value));
        }

        return Success(Map(layout));
    }

    internal static LayoutDto Map(Layout layout) =>
        new(
            LayoutIdentifier: layout.Id.Value,
            Version: layout.Version,
            Fab: layout.Fab.Value,
            Name: layout.Name.Value,
            CreatedAt: layout.Creation.At,
            CreatedBy: layout.Creation.By.Value,
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
            CreatedAt: revision.Creation.At,
            CreatedBy: revision.Creation.By.Value,
            PublishedAt: revision.PublishedAt?.Value,
            ArchivedAt: revision.ArchivedAt?.Value);

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
