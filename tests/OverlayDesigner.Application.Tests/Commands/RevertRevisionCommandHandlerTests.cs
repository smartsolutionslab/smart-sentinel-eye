using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class RevertRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Overlay Seed(InMemoryOverlayRepository overlays, FakeClock clock)
    {
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From("Line-1"),
            Label.From("Hello", 0.1m, 0.1m, 0.3m, 0.08m, 32),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);
        return overlay;
    }

    [Fact]
    public async Task Reverting_a_Published_revision_flips_it_back_to_Draft()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);
        overlay.Publish(OverlayRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);

        RevertRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                overlay.Id, OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public async Task Unknown_overlay_returns_OverlayNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        RevertRevisionCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<RevertRevisionCommandHandler>.Instance);

        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                OverlayIdentifier.New(), OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RevertRevisionError.OverlayNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_OverlayRevisionNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);

        RevertRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                overlay.Id, OverlayRevisionNumber.From(42),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RevertRevisionError.OverlayRevisionNotFound>();
    }

    [Fact]
    public async Task Reverting_a_Draft_revision_returns_NotPublished()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);

        RevertRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(
                overlay.Id, OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<RevertRevisionError.NotPublished>();
    }
}
