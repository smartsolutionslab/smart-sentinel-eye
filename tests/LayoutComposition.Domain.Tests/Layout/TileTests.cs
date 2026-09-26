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

    /// <summary>
    /// Spec 262 (ADR-0156): the 3-argument constructor stays for the ~15
    /// existing call sites and is the honest meaning of a 1×1 tile — it
    /// delegates to the 4-argument constructor with <see cref="TileSpan.Cell"/>
    /// (plan §2.2).
    /// </summary>
    [Fact]
    public void A_tile_built_with_the_three_argument_constructor_has_a_1x1_span()
    {
        Tile tile = new(
            CameraIdentifier.From(Guid.CreateVersion7()),
            Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0));

        tile.Span.ShouldBe(TileSpan.Cell);
    }

    [Fact]
    public void Tiles_that_only_touch_at_a_corner_do_not_overlap()
    {
        Tile hero = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0), TileSpan.From(2, 2));
        Tile corner = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(2, 2));

        hero.Overlaps(corner).ShouldBeFalse();
    }

    [Fact]
    public void Tiles_that_share_only_an_edge_do_not_overlap()
    {
        Tile left = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0), TileSpan.From(1, 2));
        Tile right = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 2));

        left.Overlaps(right).ShouldBeFalse();
    }

    [Fact]
    public void A_tile_fully_contained_in_another_overlaps_it()
    {
        Tile hero = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0), TileSpan.From(3, 3));
        Tile inner = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(1, 1));

        hero.Overlaps(inner).ShouldBeTrue();
    }

    [Fact]
    public void Tiles_whose_rectangles_partially_intersect_overlap()
    {
        Tile a = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0), TileSpan.From(2, 2));
        Tile b = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(1, 1), TileSpan.From(2, 2));

        a.Overlaps(b).ShouldBeTrue();
    }

    [Fact]
    public void Two_tiles_at_the_same_origin_overlap()
    {
        Tile a = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0));
        Tile b = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0));

        a.Overlaps(b).ShouldBeTrue();
    }

    [Fact]
    public void Overlaps_is_symmetric()
    {
        Tile a = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0), TileSpan.From(2, 2));
        Tile b = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(1, 1), TileSpan.From(2, 2));

        a.Overlaps(b).ShouldBe(b.Overlaps(a));
    }
}
