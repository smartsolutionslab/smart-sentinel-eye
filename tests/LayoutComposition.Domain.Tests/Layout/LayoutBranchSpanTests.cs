using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// Spec 258 §1 finding 1 (ADR-0156): <c>Revision.NewDraft</c> re-constructs
/// every tile via the 3-argument constructor, which silently drops any span
/// wider than 1×1. <c>BranchDraft</c> calls <c>NewDraft</c> to clone the
/// Published revision's tiles, so a published hero wall would branch into a
/// draft whose hero tile is flattened back to 1×1 unless the clone carries
/// <c>tile.Span</c> forward (plan §2.5). If this test arrives green on
/// unchanged code, it is not exercising <c>BranchDraft</c>.
/// </summary>
public class LayoutBranchSpanTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void BranchDraft_of_a_published_hero_wall_keeps_the_2x2_span()
    {
        CameraIdentifier hero = CameraIdentifier.From(Guid.CreateVersion7());
        Tile heroTile = new(
            hero, Option<OverlayIdentifier>.None, GridPosition.From(0, 0), TileSpan.From(2, 2));
        Tile filler = new(
            CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None,
            GridPosition.From(2, 2));
        Domain.Layout.Layout layout = new LayoutBuilder()
            .WithGrid(GridDimensions.From(3, 3))
            .WithTiles([heroTile, filler])
            .Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        Revision draft = layout.BranchDraft(by, clock);

        Tile branchedHero = draft.Tiles.Single(tile => tile.Camera == hero);
        branchedHero.Span.ShouldBe(TileSpan.From(2, 2));
    }
}
