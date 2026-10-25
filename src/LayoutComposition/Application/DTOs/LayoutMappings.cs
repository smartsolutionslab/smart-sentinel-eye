using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.DTOs;

internal static class LayoutMappings
{
    public static LayoutDto ToLayoutDto(this Layout layout) =>
        new(
            LayoutIdentifier: layout.Id.Value,
            Version: layout.Version,
            Fab: layout.Fab.Value,
            Name: layout.Name.Value,
            CreatedAt: layout.Creation.At,
            CreatedBy: layout.Creation.By.Value,
            Revisions: layout.Revisions
                .OrderBy(revision => revision.Number.Value)
                .Select(ToRevisionDto)
                .ToList());

    public static LayoutRevisionDto ToRevisionDto(this Revision revision) =>
        new(
            RevisionIdentifier: revision.Id.Value,
            RevisionNumber: revision.Number.Value,
            State: revision.State.Value,
            GridRows: revision.Grid.Rows,
            GridCols: revision.Grid.Cols,
            Tiles: revision.ToTileDtos(),
            CreatedAt: revision.Creation.At,
            CreatedBy: revision.Creation.By.Value,
            PublishedAt: revision.PublishedAt?.Value,
            ArchivedAt: revision.ArchivedAt?.Value);

    public static IReadOnlyList<TileDto> ToTileDtos(this Revision revision) =>
        revision.Tiles
            .OrderBy(tile => tile.Position.Row)
            .ThenBy(tile => tile.Position.Col)
            .Select(tile => new TileDto(
                CameraIdentifier: tile.Camera.Value,
                OverlayIdentifier: tile.Overlay.Match(overlay => (Guid?)overlay.Value, () => null),
                Row: tile.Position.Row,
                Col: tile.Position.Col,
                RowSpan: tile.Span.Rows,
                ColSpan: tile.Span.Cols))
            .ToList();

    public static PublishedLayoutDto ToPublishedLayoutDto(this Layout layout)
    {
        var pub = layout.Revisions.Single(revision => revision.State == LayoutRevisionState.Published);
        return new PublishedLayoutDto(
            LayoutIdentifier: layout.Id.Value,
            Name: layout.Name.Value,
            RevisionNumber: pub.Number.Value,
            GridRows: pub.Grid.Rows,
            GridCols: pub.Grid.Cols,
            Tiles: pub.ToTileDtos(),
            PublishedAt: pub.PublishedAt!.Value);
    }
}
