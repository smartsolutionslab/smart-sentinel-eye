using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class EditDraftRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Label OtherLabel() =>
        Label.From("Updated", 0.2m, 0.3m, 0.4m, 0.5m, 64);

    [Fact]
    public async Task Editing_a_Draft_updates_the_Label()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-1")
            .WithLabel(Label.From("Initial", 0.1m, 0.1m, 0.3m, 0.08m, 32))
            .Build();
        overlays.Add(overlay);
        Label replacement = OtherLabel();

        EditDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(overlay.Id, OverlayRevisionNumber.One, replacement),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        overlay.Revisions.Single().Label.ShouldBe(replacement);
    }

    [Fact]
    public async Task Unknown_overlay_returns_OverlayNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        EditDraftRevisionCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<EditDraftRevisionCommandHandler>.Instance);

        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                OverlayIdentifier.New(), OverlayRevisionNumber.One, OtherLabel()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.OverlayNotFound>();
    }

    [Fact]
    public async Task Missing_revision_returns_OverlayRevisionNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-1")
            .WithLabel(Label.From("Initial", 0.1m, 0.1m, 0.3m, 0.08m, 32))
            .Build();
        overlays.Add(overlay);

        EditDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                overlay.Id, OverlayRevisionNumber.From(42), OtherLabel()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.OverlayRevisionNotFound>();
    }

    [Fact]
    public async Task Editing_a_Published_revision_returns_NotADraft()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-1")
            .WithLabel(Label.From("Initial", 0.1m, 0.1m, 0.3m, 0.08m, 32))
            .Build();
        overlays.Add(overlay);
        overlay.Publish(OverlayRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);

        EditDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(overlay.Id, OverlayRevisionNumber.One, OtherLabel()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<EditDraftRevisionError.NotADraft>();
    }
}
