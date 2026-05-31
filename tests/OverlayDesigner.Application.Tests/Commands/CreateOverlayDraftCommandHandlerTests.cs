using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class CreateOverlayDraftCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Label SampleLabel() =>
        Label.From("Line-1", 0.1m, 0.1m, 0.3m, 0.08m, 32);

    [Fact]
    public async Task First_creation_with_a_unique_name_returns_a_new_OverlayIdentifier()
    {
        InMemoryOverlayRepository overlays = new();
        CreateOverlayDraftCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<CreateOverlayDraftCommandHandler>.Instance);

        Result<OverlayIdentifier, CreateOverlayDraftError> result = await handler.HandleAsync(
            new CreateOverlayDraftCommand(
                OverlayName.From("Line-1 Title"),
                SampleLabel(),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        overlays.Overlays.Count.ShouldBe(1);
        Overlay created = overlays.Overlays[0];
        created.Id.ShouldBe(result.Value);
        created.Name.Value.ShouldBe("Line-1 Title");
        created.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public async Task A_name_collision_with_a_non_archived_chain_returns_OverlayNameTaken()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay existing = new OverlayBuilder()
            .At(clock.UtcNow)
            .WithLabel(SampleLabel())
            .Build();
        overlays.Add(existing);

        CreateOverlayDraftCommandHandler handler = new(
            overlays, clock, NullLogger<CreateOverlayDraftCommandHandler>.Instance);
        Result<OverlayIdentifier, CreateOverlayDraftError> result = await handler.HandleAsync(
            new CreateOverlayDraftCommand(
                OverlayName.From("Line-1 Title"),
                SampleLabel(),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateOverlayDraftError.OverlayNameTaken>();
        overlays.Overlays.Count.ShouldBe(1);
    }
}
