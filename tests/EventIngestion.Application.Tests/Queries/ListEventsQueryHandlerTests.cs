using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

/// <summary>
/// Validation-path tests for <see cref="ListEventsQueryHandler"/>, plus the
/// cursor round trip across a tied ingested time (spec 360). Real-Postgres
/// ordering and timestamp round-tripping are proven by the integration tests.
/// </summary>
public class ListEventsQueryHandlerTests
{
    private static ListEventsQuery Query(int pageSize = 100, string? cursor = null) =>
        new([FabIdentifier.From("munich")],
            Source: null, Device: null, Kind: null,
            OccurredAfter: null, OccurredBefore: null,
            IngestedAfter: null, IngestedBefore: null,
            pageSize, cursor);

    [Fact]
    public async Task Paging_across_events_sharing_an_ingested_time_serves_each_exactly_once()
    {
        var tied = DateTimeOffset.Parse("2026-05-28T12:00:00Z", CultureInfo.InvariantCulture);
        var first = Ingest(3, tied);
        var second = Ingest(2, tied);
        var third = Ingest(1, tied);
        var older = Ingest(4, tied.AddMinutes(-1));
        ListEventsQueryHandler handler = new(new TestEventQuerySource([older, second, first, third]));

        var pageOne = await handler.HandleAsync(Query(pageSize: 2), CancellationToken.None);
        pageOne.IsSuccess.ShouldBeTrue();
        pageOne.Value.NextCursor.ShouldNotBeNull();

        var pageTwo = await handler.HandleAsync(
            Query(pageSize: 2, cursor: pageOne.Value.NextCursor), CancellationToken.None);
        pageTwo.IsSuccess.ShouldBeTrue();

        var walked = pageOne.Value.Items.Concat(pageTwo.Value.Items)
            .Select(item => item.EventIdentifier)
            .ToList();
        walked.ShouldBe([first.Id.Value, second.Id.Value, third.Id.Value, older.Id.Value]);
        pageTwo.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task PageSize_above_the_maximum_returns_PageSizeOutOfRange()
    {
        ListEventsQueryHandler handler = new(new TestEventQuerySource([]));
        var result = await handler.HandleAsync(
            Query(pageSize: 2_000), CancellationToken.None);
        result.Error.ShouldBeOfType<ListEventsError.PageSizeOutOfRange>();
    }

    private static EventAggregate Ingest(int number, DateTimeOffset ingestedAt) =>
        new EventBuilder()
            .WithIdentifier(EventIdentifier.From(new Guid(0, 0, 0, [0, 0, 0, 0, 0, 0, 0, (byte)number])))
            .WithFab("munich")
            .WithOccurredAt(ingestedAt)
            .WithClock(ingestedAt)
            .Build();

    [Fact]
    public async Task Malformed_cursor_returns_InvalidCursor()
    {
        ListEventsQueryHandler handler = new(new TestEventQuerySource([]));
        var result = await handler.HandleAsync(
            Query(cursor: "not-base64-and-not-a-cursor"), CancellationToken.None);
        result.Error.ShouldBeOfType<ListEventsError.InvalidCursor>();
    }
}
