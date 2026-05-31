using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

public class GetEventQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static EventAggregate Build(EventIdentifier id, string fab = "munich") =>
        new EventBuilder()
            .WithIdentifier(id)
            .WithFab(fab)
            .WithOccurredAt(Now)
            .WithClock(Now)
            .Build();

    [Fact]
    public async Task Returns_EventNotFound_when_no_event_matches_the_identifier()
    {
        TestEventQuerySource source = new([]);
        GetEventQueryHandler handler = new(source);

        Result<EventDto, GetEventError> result = await handler.HandleAsync(
            new GetEventQuery(FabIdentifier.From("munich"), EventIdentifier.New()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetEventError.EventNotFound>();
    }

    [Fact]
    public async Task Returns_a_mapped_DTO_when_the_event_exists()
    {
        EventIdentifier id = EventIdentifier.New();
        TestEventQuerySource source = new([Build(id)]);
        GetEventQueryHandler handler = new(source);

        Result<EventDto, GetEventError> result = await handler.HandleAsync(
            new GetEventQuery(FabIdentifier.From("munich"), id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EventIdentifier.ShouldBe(id.Value);
        result.Value.Fab.ShouldBe("munich");
        result.Value.Source.ShouldBe("plc");
        result.Value.Device.ShouldBe("station-4");
        result.Value.Kind.ShouldBe("PlcCycleStart");
    }

    [Fact]
    public async Task Filters_by_fab_so_an_event_in_a_different_fab_is_not_returned()
    {
        EventIdentifier id = EventIdentifier.New();
        TestEventQuerySource source = new([Build(id, fab: "munich")]);
        GetEventQueryHandler handler = new(source);

        Result<EventDto, GetEventError> result = await handler.HandleAsync(
            new GetEventQuery(FabIdentifier.From("berlin"), id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
    }
}
