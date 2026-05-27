using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class ArchiveRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Overlay Seed(InMemoryOverlayRepository overlays, FakeClock clock)
    {
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From("Line-1 Title"),
            Label.From("Hello", 0.5m, 0.05m, 0.3m, 0.08m, 48),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);
        return overlay;
    }

    [Fact]
    public async Task Archiving_a_Draft_transitions_it_to_Archived()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);

        ArchiveRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(
                overlay.Id, OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Archived);
    }

    [Fact]
    public async Task Unknown_overlay_returns_OverlayNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        ArchiveRevisionCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<ArchiveRevisionCommandHandler>.Instance);

        Result<OverlayRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(
                OverlayIdentifier.New(), OverlayRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ArchiveRevisionError.OverlayNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_OverlayRevisionNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = Seed(overlays, clock);

        ArchiveRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(
                overlay.Id, OverlayRevisionNumber.From(99),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ArchiveRevisionError.OverlayRevisionNotFound>();
    }
}
