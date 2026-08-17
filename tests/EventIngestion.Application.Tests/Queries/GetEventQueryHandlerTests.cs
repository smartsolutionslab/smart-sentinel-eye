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
            new GetEventQuery([FabIdentifier.From("munich")], EventIdentifier.New()),
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
            new GetEventQuery([FabIdentifier.From("munich")], id),
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
            new GetEventQuery([FabIdentifier.From("berlin")], id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
    }

    /// <summary>
    /// FR-004. The same failure an unknown identifier produces, so the caller
    /// cannot learn that the event exists — and the fab is part of the lookup
    /// rather than a check afterwards, so both leave by one path.
    /// </summary>
    [Fact]
    public async Task An_event_outside_the_callers_fabs_is_reported_as_not_found()
    {
        EventIdentifier id = EventIdentifier.New();
        TestEventQuerySource source = new([Build(id, fab: "munich")]);
        GetEventQueryHandler handler = new(source);

        Result<EventDto, GetEventError> hidden = await handler.HandleAsync(
            new GetEventQuery([FabIdentifier.From("dresden")], id), CancellationToken.None);
        Result<EventDto, GetEventError> absent = await handler.HandleAsync(
            new GetEventQuery([FabIdentifier.From("dresden")], EventIdentifier.New()),
            CancellationToken.None);

        hidden.IsFailure.ShouldBeTrue();
        absent.IsFailure.ShouldBeTrue();
        hidden.Error.ShouldBeOfType<GetEventError.EventNotFound>();
        absent.Error.ShouldBeOfType<GetEventError.EventNotFound>();
    }

    /// <summary>
    /// FR-003: a read spans every fab the caller holds, rather than making
    /// them choose one as the write path does.
    /// </summary>
    [Fact]
    public async Task A_multi_fab_caller_reaches_an_event_in_either_of_their_fabs()
    {
        EventIdentifier id = EventIdentifier.New();
        TestEventQuerySource source = new([Build(id, fab: "dresden")]);
        GetEventQueryHandler handler = new(source);

        Result<EventDto, GetEventError> result = await handler.HandleAsync(
            new GetEventQuery(
                [FabIdentifier.From("munich"), FabIdentifier.From("dresden")], id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
