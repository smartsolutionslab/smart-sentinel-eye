using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static Tile TileAt(CameraIdentifier camera, int row, int col) =>
        new(camera, Option<OverlayIdentifier>.None, GridPosition.From(row, col));

    [Fact]
    public void CreateDraft_yields_revision_one_in_Draft_state_with_no_pending_events()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());

        Domain.Layout.Layout layout = new LayoutBuilder()
            .Named("Line-1 Entrance")
            .ForCamera(camera)
            .At(FixedMoment)
            .Build();

        layout.Name.Value.ShouldBe("Line-1 Entrance");
        layout.Revisions.Count.ShouldBe(1);
        Revision only = layout.Revisions[0];
        only.Number.ShouldBe(LayoutRevisionNumber.One);
        only.State.ShouldBe(LayoutRevisionState.Draft);
        only.Grid.ShouldBe(GridDimensions.Cell);
        only.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(camera);
        only.PublishedAt.ShouldBeNull();
        only.ArchivedAt.ShouldBeNull();
        layout.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void CreateDraft_carries_the_full_multi_tile_grid()
    {
        CameraIdentifier cameraA = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier cameraB = CameraIdentifier.From(Guid.CreateVersion7());
        IReadOnlyList<Tile> tiles = [TileAt(cameraA, 0, 0), TileAt(cameraB, 0, 1)];

        Domain.Layout.Layout layout = new LayoutBuilder()
            .WithGrid(GridDimensions.From(1, 2))
            .WithTiles(tiles)
            .Build();

        Revision only = layout.Revisions.Single();
        only.Grid.ShouldBe(GridDimensions.From(1, 2));
        only.Tiles.Count.ShouldBe(2);
    }

    [Fact]
    public void Publish_a_Draft_transitions_to_Published_and_raises_LayoutRevisionPublished()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Named("Line-1").Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.Publish(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Published);
        layout.Revisions.Single().PublishedAt.ShouldBe(FixedMoment);

        LayoutRevisionPublishedDomainEvent evt =
            layout.PendingEvents.OfType<LayoutRevisionPublishedDomainEvent>().ShouldHaveSingleItem();
        evt.Layout.ShouldBe(layout.Id);
        evt.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        evt.PublishedBy.ShouldBe(by);
        evt.Tiles.ShouldHaveSingleItem();
        evt.Grid.ShouldBe(GridDimensions.Cell);
    }

    [Fact]
    public void BranchDraft_off_Published_yields_revision_two_in_Draft_with_the_same_tiles()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(camera).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        Revision draft = layout.BranchDraft(by, clock);

        draft.Number.Value.ShouldBe(2);
        draft.State.ShouldBe(LayoutRevisionState.Draft);
        draft.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(camera);
        layout.Revisions.Count.ShouldBe(2);
    }

    [Fact]
    public void BranchDraft_without_a_Published_revision_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Action act = () => layout.BranchDraft(by, new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Publish_a_new_revision_atomically_archives_the_previous_Published()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        Revision draftTwo = layout.BranchDraft(by, clock);
        layout.ClearPendingEvents();

        layout.Publish(draftTwo.Number, by, clock);

        layout.Revisions.Count(r => r.State == LayoutRevisionState.Published).ShouldBe(1);
        layout.Revisions.Single(r => r.Number == LayoutRevisionNumber.One)
            .State.ShouldBe(LayoutRevisionState.Archived);
        layout.Revisions.Single(r => r.Number == draftTwo.Number)
            .State.ShouldBe(LayoutRevisionState.Published);

        layout.PendingEvents.OfType<LayoutRevisionArchivedDomainEvent>()
            .ShouldHaveSingleItem()
            .RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        layout.PendingEvents.OfType<LayoutRevisionPublishedDomainEvent>()
            .ShouldHaveSingleItem()
            .RevisionNumber.ShouldBe(draftTwo.Number);
    }

    [Fact]
    public void At_most_one_revision_is_Published_after_any_sequence_of_operations()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.Publish(LayoutRevisionNumber.One, by, clock);
        Revision two = layout.BranchDraft(by, clock);
        layout.Publish(two.Number, by, clock);
        Revision three = layout.BranchDraft(by, clock);
        layout.Publish(three.Number, by, clock);

        layout.Revisions.Count(r => r.State == LayoutRevisionState.Published).ShouldBe(1);
        layout.Revisions.Single(r => r.State == LayoutRevisionState.Published)
            .Number.ShouldBe(three.Number);
    }

    [Fact]
    public void EditDraft_in_place_replaces_the_tile_set_without_spawning_a_new_revision()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        CameraIdentifier cameraA = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier cameraB = CameraIdentifier.From(Guid.CreateVersion7());
        IReadOnlyList<Tile> tiles = [TileAt(cameraA, 0, 0), TileAt(cameraB, 1, 1)];

        layout.EditDraft(LayoutRevisionNumber.One, GridDimensions.Default, tiles, clock);

        layout.Revisions.Count.ShouldBe(1);
        Revision only = layout.Revisions.Single();
        only.State.ShouldBe(LayoutRevisionState.Draft);
        only.Grid.ShouldBe(GridDimensions.Default);
        only.Tiles.Count.ShouldBe(2);
    }

    [Fact]
    public void Revert_on_a_Published_revision_brings_it_back_to_Draft()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        layout.Revert(LayoutRevisionNumber.One, by, clock);

        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
        layout.Revisions.Single().PublishedAt.ShouldBeNull();
        layout.PendingEvents.OfType<LayoutRevisionArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Publishing_a_missing_revision_number_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());

        Action act = () => layout.Publish(
            LayoutRevisionNumber.From(99), by, new LayoutBuilder.TestClock(FixedMoment));
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void CreateDraft_carries_the_optional_overlay_on_its_tile()
    {
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().WithOverlay(overlay).Build();

        Tile tile = layout.Revisions.Single().Tiles.ShouldHaveSingleItem();
        tile.Overlay.HasValue.ShouldBeTrue();
        tile.Overlay.Value.ShouldBe(overlay);
    }

    [Fact]
    public void BranchDraft_carries_the_overlay_from_the_Published_revisions_tile()
    {
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().WithOverlay(overlay).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        Revision draft = layout.BranchDraft(by, clock);

        draft.Tiles.ShouldHaveSingleItem().Overlay.Value.ShouldBe(overlay);
    }

    [Fact]
    public void EditDraft_on_a_Published_revision_throws()
    {
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        IReadOnlyList<Tile> tiles = [TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0)];

        Action act = () => layout.EditDraft(LayoutRevisionNumber.One, GridDimensions.Cell, tiles, clock);
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void ValidateGrid_accepts_a_valid_full_2x2_wall()
    {
        IReadOnlyList<Tile> tiles =
        [
            TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0),
            TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 1),
            TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 1, 0),
            TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 1, 1),
        ];

        Domain.Layout.Layout.ValidateGrid(GridDimensions.Default, tiles).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ValidateGrid_accepts_a_sparse_grid()
    {
        IReadOnlyList<Tile> tiles = [TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0)];

        Domain.Layout.Layout.ValidateGrid(GridDimensions.Default, tiles).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ValidateGrid_rejects_an_empty_tile_set_as_Empty()
    {
        Option<GridViolation> violation =
            Domain.Layout.Layout.ValidateGrid(GridDimensions.Cell, Array.Empty<Tile>());

        violation.HasValue.ShouldBeTrue();
        violation.Value.ShouldBe(GridViolation.Empty);
    }

    [Fact]
    public void ValidateGrid_rejects_two_tiles_at_the_same_position_as_DuplicatePosition()
    {
        IReadOnlyList<Tile> tiles =
        [
            TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0),
            TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0),
        ];

        Domain.Layout.Layout.ValidateGrid(GridDimensions.Default, tiles)
            .Value.ShouldBe(GridViolation.DuplicatePosition);
    }

    [Fact]
    public void ValidateGrid_rejects_an_out_of_bounds_tile_as_OutOfBounds()
    {
        IReadOnlyList<Tile> tiles = [TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 1)];

        Domain.Layout.Layout.ValidateGrid(GridDimensions.Cell, tiles)
            .Value.ShouldBe(GridViolation.OutOfBounds);
    }

    [Fact]
    public void ValidateGrid_rejects_an_oversized_grid_as_TooLarge()
    {
        IReadOnlyList<Tile> tiles = [TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0)];

        // 3x3 = 9 cells > MaxCells; GridDimensions.From would reject too, but
        // ValidateGrid guards even a manually-constructed oversize grid.
        Domain.Layout.Layout.ValidateGrid(new GridDimensions(3, 3), tiles)
            .Value.ShouldBe(GridViolation.TooLarge);
    }

    [Fact]
    public void CreateDraft_records_the_fab_the_layout_belongs_to()
    {
        Domain.Layout.Layout layout = new LayoutBuilder()
            .WithFab(FabIdentifier.From("dresden"))
            .Build();

        // dresden, not munich: everything else defaults to munich, so an
        // ignored argument would pass a munich assertion.
        layout.Fab.ShouldBe(FabIdentifier.From("dresden"));
    }

    [Fact]
    public void CreateDraft_requires_a_fab()
    {
        Should.Throw<ArgumentException>(() => Domain.Layout.Layout.CreateDraft(
            null!,
            LayoutName.From("Line-1 Entrance"),
            GridDimensions.Cell,
            [TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0)],
            OperatorIdentifier.From(Guid.CreateVersion7()),
            new LayoutBuilder.TestClock(FixedMoment)));
    }

    /// <summary>
    /// FR-002: a layout's fab is fixed at creation. Every transition the
    /// aggregate has must therefore leave it alone. The five below are all of
    /// them — <c>Publish</c>, <c>BranchDraft</c>, <c>EditDraft</c>,
    /// <c>Revert</c> and <c>ArchiveRevision</c>; checked against the class
    /// rather than assumed, because spec 015's equivalent asserted against a
    /// decommission that was never implemented.
    /// </summary>
    [Fact]
    public void The_fab_survives_every_revision_transition()
    {
        FabIdentifier dresden = FabIdentifier.From("dresden");
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        LayoutBuilder.TestClock clock = new(FixedMoment);
        Domain.Layout.Layout layout = new LayoutBuilder().WithFab(dresden).Build();

        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.Fab.ShouldBe(dresden);

        Revision draft = layout.BranchDraft(by, clock);
        layout.Fab.ShouldBe(dresden);

        layout.EditDraft(
            draft.Number,
            GridDimensions.Cell,
            [TileAt(CameraIdentifier.From(Guid.CreateVersion7()), 0, 0)],
            clock);
        layout.Fab.ShouldBe(dresden);

        // Revert targets the *Published* revision (Published -> Draft), not
        // the draft branched off it.
        layout.Revert(LayoutRevisionNumber.One, by, clock);
        layout.Fab.ShouldBe(dresden);

        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);
        layout.Fab.ShouldBe(dresden);
    }

    /// <summary>
    /// Spec 037 FR-001/FR-002 (ADR-0121). Asserts the PAYLOAD, not that a draft
    /// appeared. A fallback that branched an empty draft would satisfy any
    /// assertion about a draft existing while recovering nothing — and the
    /// payload is the entire reason recovery beats recreating the layout by
    /// hand, which already works because a stranded chain releases its name.
    ///
    /// <para>
    /// The overlay binding is asserted deliberately: it is the part of a tile
    /// most easily dropped by a copy, and the part an operator is least likely
    /// to notice missing until a kiosk shows a wall with no labels.
    /// </para>
    /// </summary>
    [Fact]
    public void BranchDraft_on_a_fully_archived_chain_recovers_the_grid_and_every_tile()
    {
        CameraIdentifier left = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier right = CameraIdentifier.From(Guid.CreateVersion7());
        OverlayIdentifier overlay = OverlayIdentifier.From(Guid.CreateVersion7());
        Tile bound = new(left, Option<OverlayIdentifier>.Some(overlay), GridPosition.From(0, 0));
        Domain.Layout.Layout layout = new LayoutBuilder()
            .WithGrid(GridDimensions.From(1, 2))
            .WithTiles([bound, TileAt(right, 0, 1)])
            .Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);

        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);
        layout.ClearPendingEvents();

        Revision recovered = layout.BranchDraft(by, clock);

        recovered.Number.Value.ShouldBe(2);
        recovered.State.ShouldBe(LayoutRevisionState.Draft);
        recovered.Grid.Rows.ShouldBe(1);
        recovered.Grid.Cols.ShouldBe(2);
        recovered.Tiles.Count.ShouldBe(2);
        recovered.Tiles.ShouldContain(tile =>
            tile.Camera == left && tile.Overlay.HasValue && tile.Overlay.Value == overlay);
        recovered.Tiles.ShouldContain(tile => tile.Camera == right);
    }

    /// <summary>
    /// Spec 037 FR-004. Branching alone is not recovery — a draft nobody can
    /// publish leaves the layout exactly as unusable as before while passing
    /// every assertion in the test above.
    /// </summary>
    [Fact]
    public void A_recovered_draft_can_be_edited_and_published()
    {
        CameraIdentifier replacement = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);
        layout.ArchiveRevision(LayoutRevisionNumber.One, by, clock);

        Revision recovered = layout.BranchDraft(by, clock);
        layout.EditDraft(recovered.Number, GridDimensions.Cell, [TileAt(replacement, 0, 0)], clock);
        layout.Publish(recovered.Number, by, clock);

        recovered.State.ShouldBe(LayoutRevisionState.Published);
        recovered.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(replacement);
    }

    /// <summary>
    /// Spec 037 FR-008. Asserts WHICH revision was copied, not that the branch
    /// succeeded — the two are indistinguishable on success alone, and this is
    /// the case a widened "newest revision, whatever its state" fallback breaks
    /// in the opposite direction: it would copy the abandoned draft instead of
    /// the live published wall.
    /// </summary>
    [Fact]
    public void BranchDraft_prefers_the_Published_revision_over_an_archived_newer_one()
    {
        CameraIdentifier live = CameraIdentifier.From(Guid.CreateVersion7());
        CameraIdentifier abandoned = CameraIdentifier.From(Guid.CreateVersion7());
        Domain.Layout.Layout layout = new LayoutBuilder().ForCamera(live).Build();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        IClock clock = new LayoutBuilder.TestClock(FixedMoment);
        layout.Publish(LayoutRevisionNumber.One, by, clock);

        Revision draftTwo = layout.BranchDraft(by, clock);
        layout.EditDraft(draftTwo.Number, GridDimensions.Cell, [TileAt(abandoned, 0, 0)], clock);
        layout.ArchiveRevision(draftTwo.Number, by, clock);

        Revision draftThree = layout.BranchDraft(by, clock);

        draftThree.Number.Value.ShouldBe(3);
        draftThree.Tiles.ShouldHaveSingleItem().Camera.ShouldBe(live);
    }

    /// <summary>
    /// The FR-002 guarantee is structural, not behavioural: nothing outside
    /// the aggregate can assign the fab, so it cannot be made to change. A
    /// behavioural test would not catch someone adding a setter, and unlike
    /// spec 016 there is no legitimate one-way fill here — the backfill runs
    /// in SQL, so the aggregate needs no write path at all.
    /// </summary>
    [Fact]
    public void The_fab_cannot_be_set_or_moved_from_outside_the_aggregate()
    {
        typeof(Domain.Layout.Layout)
            .GetProperty(nameof(Domain.Layout.Layout.Fab))!
            .GetSetMethod(nonPublic: false)
            .ShouldBeNull();

        // Property accessors excluded — get_Fab is the reader asserted above.
        typeof(Domain.Layout.Layout)
            .GetMethods()
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ShouldNotContain(name => name.Contains("Fab", StringComparison.Ordinal));
    }
}
