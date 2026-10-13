using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>Parses the wire-shape grid and tiles of a layout request into domain values.</summary>
internal static class LayoutRequestParsing
{
    internal static GridDimensions ParseGrid(GridRequest grid)
    {
        Ensure.That(grid).IsNotNull();
        return GridDimensions.From(grid.Rows, grid.Cols);
    }

    internal static List<Tile> ParseTiles(IReadOnlyList<TileRequest> requests)
    {
        Ensure.That(requests).IsNotNull();
        return requests
            .Select(tile =>
            {
                Ensure.That(tile).IsNotNull();
                return new Tile(
                    CameraIdentifier.From(tile.CameraIdentifier),
                    tile.OverlayIdentifier is { } overlayId
                        ? Option<OverlayIdentifier>.Some(OverlayIdentifier.From(overlayId))
                        : Option<OverlayIdentifier>.None,
                    GridPosition.From(tile.Row, tile.Col),
                    TileSpan.From(tile.RowSpan, tile.ColSpan));
            })
            .ToList();
    }
}
