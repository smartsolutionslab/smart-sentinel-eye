using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

/// <summary>
/// ADR-0113 Layer 1: every mutating command refuses an expected version that
/// no longer matches the chain. Each test also asserts the aggregate was left
/// alone — the check is only worth having if it runs *before* the mutation,
/// and a handler that rejected after mutating would still return the right
/// error while corrupting state.
/// </summary>
public class StaleVersionRejectionTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private const int Stale = 41;

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publish_rejects_a_stale_version_and_leaves_the_revision_in_Draft()
    {
        (InMemoryLayoutRepository layouts, FakeClock clock, Layout layout) = Seeded();

        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand([Munich], layout.Id, LayoutRevisionNumber.One, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
    }

    [Fact]
    public async Task Archive_rejects_a_stale_version_and_leaves_the_revision_in_Draft()
    {
        (InMemoryLayoutRepository layouts, FakeClock clock, Layout layout) = Seeded();

        ArchiveRevisionCommandHandler handler = new(layouts, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand([Munich], layout.Id, LayoutRevisionNumber.One, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Draft);
    }

    [Fact]
    public async Task BranchDraft_rejects_a_stale_version_and_adds_no_revision()
    {
        (InMemoryLayoutRepository layouts, FakeClock clock, Layout layout) = Seeded();
        layout.Publish(LayoutRevisionNumber.One, Editor(), clock);

        BranchDraftRevisionCommandHandler handler = new(layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], layout.Id, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        layout.Revisions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Revert_rejects_a_stale_version_and_leaves_the_revision_Published()
    {
        (InMemoryLayoutRepository layouts, FakeClock clock, Layout layout) = Seeded();
        layout.Publish(LayoutRevisionNumber.One, Editor(), clock);

        RevertRevisionCommandHandler handler = new(layouts, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand([Munich], layout.Id, LayoutRevisionNumber.One, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        layout.Revisions.Single().State.ShouldBe(LayoutRevisionState.Published);
    }

    [Fact]
    public async Task EditDraft_rejects_a_stale_version_and_leaves_the_tiles_untouched()
    {
        (InMemoryLayoutRepository layouts, FakeClock clock, Layout layout) = Seeded();
        Guid originalCamera = layout.Revisions.Single().Tiles.Single().Camera.Value;

        EditDraftRevisionCommandHandler handler = new(layouts, FakeCameraFabGuard.Permissive(), clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand([Munich], 
                layout.Id,
                LayoutRevisionNumber.One,
                GridDimensions.Cell,
                [new Tile(CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None, GridPosition.From(0, 0))],
                Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        layout.Revisions.Single().Tiles.Single().Camera.Value.ShouldBe(originalCamera);
    }

    [Fact]
    public async Task The_matching_version_is_accepted()
    {
        (InMemoryLayoutRepository layouts, FakeClock clock, Layout layout) = Seeded();

        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand([Munich], layout.Id, LayoutRevisionNumber.One, Editor(), layout.Version),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    private static void ShouldBeStale(bool isFailure, ApiError error)
    {
        isFailure.ShouldBeTrue();
        error.Code.ShouldBe("LAYOUT_REVISION_STALE");
    }

    private static OperatorIdentifier Editor() => OperatorIdentifier.From(Guid.CreateVersion7());

    private static (InMemoryLayoutRepository, FakeClock, Layout) Seeded()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(layout);

        return (layouts, clock, layout);
    }
}
