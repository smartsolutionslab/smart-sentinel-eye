using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Application.Queries;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
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
        Overlay overlay = Overlay.CreateDraft(
            OverlayName.From("Line-1"),
            Label.From("Hello", 0.1m, 0.1m, 0.3m, 0.08m, 32),
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock);
        overlays.Add(overlay);
        IOverlayQuerySource source = new InMemoryOverlayQuerySource(overlays);

        GetOverlayQueryHandler handler = new(source);
        Result<OverlayDto, GetOverlayError> result = await handler.HandleAsync(
            new GetOverlayQuery(overlay.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OverlayIdentifier.ShouldBe(overlay.Id.Value);
        result.Value.Revisions.Single().State.ShouldBe("Draft");
        result.Value.Revisions.Single().Text.ShouldBe("Hello");
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
