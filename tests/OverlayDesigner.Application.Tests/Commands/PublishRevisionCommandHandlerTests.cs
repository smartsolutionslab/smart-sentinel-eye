using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class PublishRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Overlay Seed(InMemoryOverlayRepository overlays, FakeClock clock)
    {
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From("Line-1 Title"),
            Label.From("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);
        return overlay;
    }

    [Fact]
    public async Task Publishing_a_Draft_transitions_it_to_Published()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);

        PublishRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(
                overlay.Id, OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Published);
    }

    [Fact]
    public async Task Unknown_overlay_returns_OverlayNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        PublishRevisionCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<PublishRevisionCommandHandler>.Instance);

        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(
                OverlayIdentifier.New(),
                OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.OverlayNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_OverlayRevisionNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);

        PublishRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(
                overlay.Id, OverlayRevisionNumber.From(42),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.OverlayRevisionNotFound>();
    }

    [Fact]
    public async Task Publishing_a_non_Draft_revision_returns_InvalidStateTransition()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);
        overlay.Publish(OverlayRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);

        PublishRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(
                overlay.Id, OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.InvalidStateTransition>();
    }
}
