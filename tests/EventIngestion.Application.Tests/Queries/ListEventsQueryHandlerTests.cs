using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

/// <summary>
/// Validation-path tests for <see cref="ListEventsQueryHandler"/>.
/// The happy-path filter / cursor pagination behaviour is exercised
/// by the integration test (T103, polish gate) against a real
/// Postgres — the in-memory LINQ rewriter can't translate the
/// handler's descending-order + Take + cursor-compare chain
/// (it works fine on EF Core's Npgsql provider).
/// </summary>
public class ListEventsQueryHandlerTests
{
    private static ListEventsQuery Query(int pageSize = 100, string? cursor = null) =>
        new(FabIdentifier.From("munich"),
            Source: null, Device: null, Kind: null,
            OccurredAfter: null, OccurredBefore: null,
            IngestedAfter: null, IngestedBefore: null,
            pageSize, cursor);

    [Fact]
    public async Task PageSize_above_the_maximum_returns_PageSizeOutOfRange()
    {
        ListEventsQueryHandler handler = new(new TestEventQuerySource([]));
        Result<EventPageDto, ListEventsError> result = await handler.HandleAsync(
            Query(pageSize: 2_000), CancellationToken.None);
        result.Error.ShouldBeOfType<ListEventsError.PageSizeOutOfRange>();
    }

    [Fact]
    public async Task Malformed_cursor_returns_InvalidCursor()
    {
        ListEventsQueryHandler handler = new(new TestEventQuerySource([]));
        Result<EventPageDto, ListEventsError> result = await handler.HandleAsync(
            Query(cursor: "not-base64-and-not-a-cursor"), CancellationToken.None);
        result.Error.ShouldBeOfType<ListEventsError.InvalidCursor>();
    }
}
