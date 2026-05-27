using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class BranchDraftRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Branching_off_Published_yields_revision_two_in_Draft()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From("Line-1"),
            Label.From("Hello", 0.1m, 0.1m, 0.3m, 0.08m, 32),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);
        overlay.Publish(OverlayRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(
                overlay.Id, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        overlay.Revisions.Single(r => r.Number == OverlayRevisionNumber.From(2)).State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public async Task Unknown_overlay_returns_OverlayNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        BranchDraftRevisionCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(
                OverlayIdentifier.New(), OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.OverlayNotFound>();
    }

    [Fact]
    public async Task Branching_without_a_Published_revision_returns_NoPublishedRevisionToBranchFrom()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From("Line-1"),
            Label.From("Hello", 0.1m, 0.1m, 0.3m, 0.08m, 32),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(
                overlay.Id, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.NoPublishedRevisionToBranchFrom>();
    }
}
