using SmartSentinelEye.ScenarioSimulator.Seeding;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

public class AssetCorrelationTableTests
{
    [Fact]
    public void Wall_is_incomplete_until_every_asset_has_both_overlay_and_camera()
    {
        AssetCorrelationTable table = new();
        table.RecordOverlay("a", Guid.NewGuid(), 0, 0);
        table.RecordOverlay("b", Guid.NewGuid(), 0, 1);
        table.RecordCamera("a", Guid.NewGuid());

        table.IsWallComplete(2, out int ready).ShouldBeFalse();
        ready.ShouldBe(1);

        table.RecordCamera("b", Guid.NewGuid());

        table.IsWallComplete(2, out ready).ShouldBeTrue();
        ready.ShouldBe(2);
    }

    [Fact]
    public void Wall_creation_can_be_claimed_exactly_once_until_released()
    {
        AssetCorrelationTable table = new();

        table.TryClaimWallCreation().ShouldBeTrue();
        table.TryClaimWallCreation().ShouldBeFalse();

        table.ReleaseWallClaim();
        table.TryClaimWallCreation().ShouldBeTrue();
    }

    [Fact]
    public void Complete_tiles_are_returned_in_row_major_order()
    {
        AssetCorrelationTable table = new();
        Guid roughing = Guid.NewGuid();
        Guid finishing = Guid.NewGuid();
        Guid cooling = Guid.NewGuid();
        Guid coiler = Guid.NewGuid();

        // Recorded out of grid order.
        table.RecordOverlay("coiler", Guid.NewGuid(), 1, 1);
        table.RecordCamera("coiler", coiler);
        table.RecordOverlay("roughing", Guid.NewGuid(), 0, 0);
        table.RecordCamera("roughing", roughing);
        table.RecordOverlay("cooling", Guid.NewGuid(), 1, 0);
        table.RecordCamera("cooling", cooling);
        table.RecordOverlay("finishing", Guid.NewGuid(), 0, 1);
        table.RecordCamera("finishing", finishing);

        IReadOnlyList<CorrelatedTile> tiles = table.CompleteTiles();

        tiles.Select(tile => (tile.Row, tile.Col)).ShouldBe(new[] { (0, 0), (0, 1), (1, 0), (1, 1) });
        tiles[0].Camera.ShouldBe(roughing);
        tiles[3].Camera.ShouldBe(coiler);
    }

    [Fact]
    public void Assets_missing_a_camera_or_overlay_are_excluded_from_the_tiles()
    {
        AssetCorrelationTable table = new();
        table.RecordOverlay("complete", Guid.NewGuid(), 0, 0);
        table.RecordCamera("complete", Guid.NewGuid());
        table.RecordOverlay("overlay-only", Guid.NewGuid(), 0, 1);

        table.CompleteTiles().Count.ShouldBe(1);
    }
}
