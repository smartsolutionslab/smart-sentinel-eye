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

    private static OverlayElement SampleLabel() =>
        OverlayElement.TextElement("Line-1", 32, NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.3m, 0.08m), OverlayColor.Default);

    [Fact]
    public async Task First_creation_with_a_unique_name_returns_a_new_OverlayIdentifier()
    {
        InMemoryOverlayRepository overlays = new();
        CreateOverlayDraftCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<CreateOverlayDraftCommandHandler>.Instance);

        Result<OverlayIdentifier, CreateOverlayDraftError> result = await handler.HandleAsync(
            new CreateOverlayDraftCommand(
                OverlayName.From("Line-1 Title"),
                [SampleLabel()],
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
                [SampleLabel()],
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateOverlayDraftError.OverlayNameTaken>();
        overlays.Overlays.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_label_set_returns_a_400_and_creates_nothing()
    {
        InMemoryOverlayRepository overlays = new();
        CreateOverlayDraftCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<CreateOverlayDraftCommandHandler>.Instance);

        Result<OverlayIdentifier, CreateOverlayDraftError> result = await handler.HandleAsync(
            new CreateOverlayDraftCommand(
                OverlayName.From("Line-1 Title"),
                [],
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateOverlayDraftError.EmptyElementSet>();
        overlays.Overlays.ShouldBeEmpty();
    }

    [Fact]
    public async Task More_labels_than_the_ceiling_returns_a_400_and_creates_nothing()
    {
        InMemoryOverlayRepository overlays = new();
        CreateOverlayDraftCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<CreateOverlayDraftCommandHandler>.Instance);
        List<OverlayElement> tooMany = Enumerable.Range(0, OverlayElement.MaxElements + 1).Select(_ => SampleLabel()).ToList();

        Result<OverlayIdentifier, CreateOverlayDraftError> result = await handler.HandleAsync(
            new CreateOverlayDraftCommand(
                OverlayName.From("Line-1 Title"),
                tooMany,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<CreateOverlayDraftError.TooManyElements>();
        overlays.Overlays.ShouldBeEmpty();
    }
}
