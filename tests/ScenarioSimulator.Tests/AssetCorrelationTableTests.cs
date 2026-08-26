using SmartSentinelEye.ScenarioSimulator.Seeding;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

public class AssetCorrelationTableTests
{
    [Fact]
    public void Wall_is_incomplete_until_every_asset_has_both_overlay_and_camera()
    {
        AssetCorrelationTable table = new();
        table.RecordOverlay("mill", "a", Guid.NewGuid(), 0, 0);
        table.RecordOverlay("mill", "b", Guid.NewGuid(), 0, 1);
        table.RecordCamera("a", Guid.NewGuid());

        table.IsWallComplete("mill", 2, out int ready).ShouldBeFalse();
        ready.ShouldBe(1);

        table.RecordCamera("b", Guid.NewGuid());

        table.IsWallComplete("mill", 2, out ready).ShouldBeTrue();
        ready.ShouldBe(2);
    }

    [Fact]
    public void Wall_creation_can_be_claimed_exactly_once_until_released()
    {
        AssetCorrelationTable table = new();

        table.TryClaimWallCreation("mill").ShouldBeTrue();
        table.TryClaimWallCreation("mill").ShouldBeFalse();

        table.ReleaseWallClaim("mill");
        table.TryClaimWallCreation("mill").ShouldBeTrue();
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
        table.RecordOverlay("mill", "coiler", Guid.NewGuid(), 1, 1);
        table.RecordCamera("coiler", coiler);
        table.RecordOverlay("mill", "roughing", Guid.NewGuid(), 0, 0);
        table.RecordCamera("roughing", roughing);
        table.RecordOverlay("mill", "cooling", Guid.NewGuid(), 1, 0);
        table.RecordCamera("cooling", cooling);
        table.RecordOverlay("mill", "finishing", Guid.NewGuid(), 0, 1);
        table.RecordCamera("finishing", finishing);

        IReadOnlyList<CorrelatedTile> tiles = table.CompleteTiles("mill");

        tiles.Select(tile => (tile.Row, tile.Col)).ShouldBe(new[] { (0, 0), (0, 1), (1, 0), (1, 1) });
        tiles[0].Camera.ShouldBe(roughing);
        tiles[3].Camera.ShouldBe(coiler);
    }

    [Fact]
    public void Assets_missing_a_camera_or_overlay_are_excluded_from_the_tiles()
    {
        AssetCorrelationTable table = new();
        table.RecordOverlay("mill", "complete", Guid.NewGuid(), 0, 0);
        table.RecordCamera("complete", Guid.NewGuid());
        table.RecordOverlay("mill", "overlay-only", Guid.NewGuid(), 0, 1);

        table.CompleteTiles("mill").Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 044. Before scenarios were scoped, one flat row set meant a second
    /// plant's cameras composed into the first plant's wall, and whichever
    /// scenario finished first claimed wall creation for all of them.
    /// </summary>
    [Fact]
    public void One_plants_assets_never_compose_into_another_plants_wall()
    {
        AssetCorrelationTable table = new();

        table.RecordOverlay("mill", "roughing", Guid.NewGuid(), 0, 0);
        table.RecordCamera("roughing", Guid.NewGuid());
        table.RecordOverlay("paper", "refiners", Guid.NewGuid(), 0, 0);
        table.RecordCamera("refiners", Guid.NewGuid());

        table.CompleteTiles("mill").Count.ShouldBe(1);
        table.CompleteTiles("paper").Count.ShouldBe(1);

        // A one-asset mill is complete even though two assets are recorded.
        table.IsWallComplete("mill", 1, out _).ShouldBeTrue();

        // And claiming one plant's wall leaves the other's available.
        table.TryClaimWallCreation("mill").ShouldBeTrue();
        table.TryClaimWallCreation("paper").ShouldBeTrue();
    }
}
