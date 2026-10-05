using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Queries;

public class GetOverlayQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Returns_the_chain_when_it_exists()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-1")
            .WithLabel(OverlayElement.TextElement("Hello", 32, NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.3m, 0.08m), OverlayColor.Default))
            .Build();
        overlays.Add(overlay);
        IOverlayQuerySource source = new InMemoryOverlayQuerySource(overlays);

        GetOverlayQueryHandler handler = new(source);
        Result<OverlayDto, GetOverlayError> result = await handler.HandleAsync(
            new GetOverlayQuery(overlay.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OverlayIdentifier.ShouldBe(overlay.Id.Value);
        result.Value.Revisions.Single().State.ShouldBe("Draft");
        result.Value.Revisions.Single().Elements.Single().Text.ShouldBe("Hello");
    }

    // Without the version on the read side a caller has nothing to put in
    // If-Match, and the cross-request check degrades to no check (ADR-0113).
    [Fact]
    public async Task The_dto_carries_the_aggregate_version()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-2")
            .WithLabel(OverlayElement.TextElement("Hello", 32, NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.3m, 0.08m), OverlayColor.Default))
            .Build();
        overlays.Add(overlay);

        GetOverlayQueryHandler handler = new(new InMemoryOverlayQuerySource(overlays));
        Result<OverlayDto, GetOverlayError> result = await handler.HandleAsync(
            new GetOverlayQuery(overlay.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(overlay.Version);
    }

    // The projection copies four same-typed decimals across and nothing
    // asserted any of them, so transposing the width and height projections
    // left the whole suite green. Four distinct values, each read back off its
    // own field, so a swap of any pair fails.
    [Fact]
    public async Task The_dto_carries_the_labels_position_and_size()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-3")
            .WithLabel(OverlayElement.TextElement("Hello", 32, NormalizedPosition.From(0.11m, 0.22m), NormalizedSize.From(0.33m, 0.44m), OverlayColor.Default))
            .Build();
        overlays.Add(overlay);

        GetOverlayQueryHandler handler = new(new InMemoryOverlayQuerySource(overlays));
        Result<OverlayDto, GetOverlayError> result = await handler.HandleAsync(
            new GetOverlayQuery(overlay.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        OverlayElementDto element = result.Value.Revisions.Single().Elements.Single();
        element.NormalizedX.ShouldBe(0.11m);
        element.NormalizedY.ShouldBe(0.22m);
        element.NormalizedWidth.ShouldBe(0.33m);
        element.NormalizedHeight.ShouldBe(0.44m);
        element.FontSizePx.ShouldBe(32);
    }

    [Fact]
    public async Task The_dto_carries_every_label_in_submission_order()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        OverlayElement first = OverlayElement.TextElement("First", 16, NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.Default);
        OverlayElement second = OverlayElement.TextElement("Second", 20, NormalizedPosition.From(0.2m, 0.2m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.Default);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-4")
            .WithLabels([first, second])
            .Build();
        overlays.Add(overlay);

        GetOverlayQueryHandler handler = new(new InMemoryOverlayQuerySource(overlays));
        Result<OverlayDto, GetOverlayError> result = await handler.HandleAsync(
            new GetOverlayQuery(overlay.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<OverlayElementDto> elements = result.Value.Revisions.Single().Elements;
        elements.Count.ShouldBe(2);
        elements[0].Text.ShouldBe("First");
        elements[1].Text.ShouldBe("Second");
    }

    [Fact]
    public async Task Returns_OverlayNotFound_when_the_chain_does_not_exist()
    {
        InMemoryOverlayRepository overlays = new();
        IOverlayQuerySource source = new InMemoryOverlayQuerySource(overlays);
        GetOverlayQueryHandler handler = new(source);

        Result<OverlayDto, GetOverlayError> result = await handler.HandleAsync(
            new GetOverlayQuery(OverlayIdentifier.New()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetOverlayError.OverlayNotFound>();
    }
}
