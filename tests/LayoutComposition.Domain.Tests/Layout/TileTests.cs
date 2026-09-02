using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class TileTests
{
    [Fact]
    public void A_tile_with_an_overlay_exposes_it_as_Some()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());

        Tile tile = new(camera, Option<OverlayIdentifier>.Some(overlay), GridPosition.From(1, 0));

        tile.Camera.ShouldBe(camera);
        tile.Overlay.HasValue.ShouldBeTrue();
        tile.Overlay.Value.ShouldBe(overlay);
        tile.OverlayValue.ShouldBe(overlay);
        tile.Position.ShouldBe(GridPosition.From(1, 0));
    }

    [Fact]
    public void A_tile_without_an_overlay_exposes_None()
    {
        Tile tile = new(
            CameraIdentifier.From(Guid.CreateVersion7()),
            Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0));

        tile.Overlay.HasValue.ShouldBeFalse();
        tile.OverlayValue.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Tiles_with_the_same_camera_overlay_and_position_are_equal()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());

        Tile a = new(camera, Option<OverlayIdentifier>.None, GridPosition.From(0, 1));
        Tile b = new(camera, Option<OverlayIdentifier>.None, GridPosition.From(0, 1));

        a.ShouldBe(b);
    }
}
