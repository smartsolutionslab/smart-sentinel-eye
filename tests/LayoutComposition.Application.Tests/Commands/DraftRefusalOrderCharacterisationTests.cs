using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 345, ADR-0144): pins the order in which the edit and
/// branch handlers refuse, and what the cross-fab refusal carries. The existing
/// tests exercise each refusal alone; none of them has two refusable
/// conditions true at once, which is exactly what reordering an extracted
/// refusal would change.
/// </summary>
public class DraftRefusalOrderCharacterisationTests
{
    private const int Stale = 41;

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static Tile TileFor(CameraIdentifier camera) =>
        new(camera, Option<OverlayIdentifier>.None, GridPosition.From(0, 0));

    private static OperatorIdentifier NewOperator() => OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task Edit_with_a_camera_outside_the_fab_returns_TileCameraOutsideFab_listing_those_cameras()
    {
        InMemoryLayoutRepository layouts = new();
        var layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);
        var outsider = CameraIdentifier.From(Guid.CreateVersion7());
        CapturingLogger<EditDraftRevisionCommandHandler> logger = new();
        EditDraftRevisionCommandHandler handler = new(
            layouts,
            FakeCameraFabGuard.Strict(),
            new FakeClock(FixedMoment),
            logger);

        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                [Munich],
                layout.Id,
                LayoutRevisionNumber.One,
                GridDimensions.Cell,
                [TileFor(outsider)],
                layout.Version),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        var refusal = result.Error.ShouldBeOfType<EditDraftRevisionError.TileCameraOutsideFab>();
        refusal.Code.ShouldBe("LAYOUT_TILE_CAMERA_OUTSIDE_FAB");
        refusal.Fab.ShouldBe("munich");
        refusal.Cameras.ShouldBe([outsider.Value]);
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Message.ShouldBe("Refused 1 tile(s) naming a camera outside fab munich (spec 017 FR-014).");
    }

    [Fact]
    public async Task Edit_with_a_camera_outside_the_fab_and_a_stale_version_returns_TileCameraOutsideFab()
    {
        InMemoryLayoutRepository layouts = new();
        var layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);
        EditDraftRevisionCommandHandler handler = new(
            layouts,
            FakeCameraFabGuard.Strict(),
            new FakeClock(FixedMoment),
            NullLogger<EditDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                [Munich],
                layout.Id,
                LayoutRevisionNumber.One,
                GridDimensions.Cell,
                [TileFor(CameraIdentifier.From(Guid.CreateVersion7()))],
                Stale),
            CancellationToken.None);

        result.Error.ShouldBeOfType<EditDraftRevisionError.TileCameraOutsideFab>();
    }

    [Fact]
    public async Task Edit_with_a_stale_version_and_a_missing_revision_returns_LayoutRevisionStale()
    {
        InMemoryLayoutRepository layouts = new();
        var layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);
        EditDraftRevisionCommandHandler handler = new(
            layouts,
            FakeCameraFabGuard.Permissive(),
            new FakeClock(FixedMoment),
            NullLogger<EditDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                [Munich],
                layout.Id,
                LayoutRevisionNumber.From(42),
                GridDimensions.Cell,
                [TileFor(CameraIdentifier.From(Guid.CreateVersion7()))],
                Stale),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutRevisionStale>();
        refusal.ExpectedVersion.ShouldBe(Stale);
        refusal.ActualVersion.ShouldBe(layout.Version);
    }

    [Fact]
    public async Task Edit_with_a_stale_version_and_a_published_revision_returns_LayoutRevisionStale()
    {
        InMemoryLayoutRepository layouts = new();
        var layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, NewOperator(), new FakeClock(FixedMoment));
        layouts.Add(layout);
        EditDraftRevisionCommandHandler handler = new(
            layouts,
            FakeCameraFabGuard.Permissive(),
            new FakeClock(FixedMoment),
            NullLogger<EditDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                [Munich],
                layout.Id,
                LayoutRevisionNumber.One,
                GridDimensions.Cell,
                [TileFor(CameraIdentifier.From(Guid.CreateVersion7()))],
                Stale),
            CancellationToken.None);

        result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutRevisionStale>();
    }

    [Fact]
    public async Task Edit_with_a_current_version_and_a_missing_revision_returns_LayoutRevisionNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        var layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, NewOperator(), new FakeClock(FixedMoment));
        layouts.Add(layout);
        EditDraftRevisionCommandHandler handler = new(
            layouts,
            FakeCameraFabGuard.Permissive(),
            new FakeClock(FixedMoment),
            NullLogger<EditDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                [Munich],
                layout.Id,
                LayoutRevisionNumber.From(42),
                GridDimensions.Cell,
                [TileFor(CameraIdentifier.From(Guid.CreateVersion7()))],
                layout.Version),
            CancellationToken.None);

        result.Error.ShouldBeOfType<EditDraftRevisionError.LayoutRevisionNotFound>();
    }

    [Fact]
    public async Task Edit_with_an_empty_tile_set_and_an_unknown_layout_returns_GridEmpty()
    {
        InMemoryLayoutRepository layouts = new();
        EditDraftRevisionCommandHandler handler = new(
            layouts,
            FakeCameraFabGuard.Permissive(),
            new FakeClock(FixedMoment),
            NullLogger<EditDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                [Munich],
                LayoutIdentifier.New(),
                LayoutRevisionNumber.One,
                GridDimensions.Cell,
                [],
                0),
            CancellationToken.None);

        result.Error.ShouldBeOfType<EditDraftRevisionError.GridEmpty>();
    }

    [Fact]
    public async Task Branch_of_a_chain_with_an_open_draft_and_a_stale_version_returns_LayoutRevisionStale()
    {
        InMemoryLayoutRepository layouts = new();
        var draftOnly = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(draftOnly);
        BranchDraftRevisionCommandHandler handler = new(
            layouts,
            new FakeClock(FixedMoment),
            NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], draftOnly.Id, NewOperator(), Stale),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<BranchDraftRevisionError.LayoutRevisionStale>();
        refusal.ExpectedVersion.ShouldBe(Stale);
        refusal.ActualVersion.ShouldBe(draftOnly.Version);
    }

    [Fact]
    public async Task Branch_of_a_fully_archived_chain_with_a_taken_name_and_a_stale_version_returns_LayoutRevisionStale()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        var by = NewOperator();
        var stranded = new LayoutBuilder().Named("rolling-mill").At(FixedMoment).Build();
        stranded.Publish(LayoutRevisionNumber.One, by, clock);
        stranded.ArchiveRevision(LayoutRevisionNumber.One, by, clock);
        layouts.Add(stranded);
        layouts.Add(new LayoutBuilder().Named("rolling-mill").At(FixedMoment).Build());
        BranchDraftRevisionCommandHandler handler = new(
            layouts,
            clock,
            NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], stranded.Id, by, Stale),
            CancellationToken.None);

        result.Error.ShouldBeOfType<BranchDraftRevisionError.LayoutRevisionStale>();
    }

    [Fact]
    public async Task Branch_of_a_chain_with_an_open_draft_and_a_current_version_names_the_open_draft()
    {
        InMemoryLayoutRepository layouts = new();
        var draftOnly = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(draftOnly);
        BranchDraftRevisionCommandHandler handler = new(
            layouts,
            new FakeClock(FixedMoment),
            NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], draftOnly.Id, NewOperator(), draftOnly.Version),
            CancellationToken.None);

        var refusal = result.Error.ShouldBeOfType<BranchDraftRevisionError.NoPublishedRevisionToBranchFrom>();
        refusal.Layout.ShouldBe(draftOnly.Id.Value);
        refusal.DraftRevision.ShouldBe(1);
    }
}
