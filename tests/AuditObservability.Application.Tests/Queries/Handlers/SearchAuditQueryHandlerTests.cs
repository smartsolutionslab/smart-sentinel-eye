using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

public class SearchAuditQueryHandlerTests
{
    private static SearchAuditQuery DefaultQuery(
        string? fab = "munich",
        string[]? callerFabs = null,
        string? actorUsername = null,
        string? eventKind = null,
        string? cursor = null,
        int pageSize = 50) =>
        new(fab, callerFabs ?? ["munich"], null, actorUsername, eventKind, null, null, null, null, pageSize, cursor);

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Returns_rows_filtered_by_fab_in_descending_OccurredAt_order()
    {
        DateTimeOffset baseTime = DateTimeOffset.Parse("2026-05-29T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        AuditEventEntity older = new AuditEventBuilder()
            .WithOccurredAt(baseTime).WithFab("munich").Build();
        AuditEventEntity newer = new AuditEventBuilder()
            .WithOccurredAt(baseTime.AddMinutes(5)).WithFab("munich").Build();
        AuditEventEntity wrongFab = new AuditEventBuilder()
            .WithOccurredAt(baseTime.AddMinutes(10)).WithFab("berlin").Build();

        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource([older, newer, wrongFab]));

        var result = await handler.HandleAsync(DefaultQuery(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(2);
        result.Value.Rows[0].OccurredAt.ShouldBe(newer.OccurredAt);
        result.Value.Rows[1].OccurredAt.ShouldBe(older.OccurredAt);
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Without_fab_uses_the_caller_fab_set()
    {
        AuditEventEntity munich = new AuditEventBuilder().WithFab("munich").Build();
        AuditEventEntity berlin = new AuditEventBuilder().WithFab("berlin").Build();
        AuditEventEntity unscoped = new AuditEventBuilder().WithFab(null).Build();

        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource([munich, berlin, unscoped]));

        var result = await handler.HandleAsync(
            DefaultQuery(fab: null, callerFabs: ["munich"]), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
        result.Value.Rows[0].Fab.ShouldBe("munich");
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Cursor_pagination_round_trips_without_overlap()
    {
        DateTimeOffset baseTime = DateTimeOffset.Parse("2026-05-29T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        List<AuditEventEntity> rows = [..
            Enumerable.Range(0, 5)
                .Select(i => new AuditEventBuilder()
                    .WithOccurredAt(baseTime.AddMinutes(i))
                    .WithEventIdentifier(Guid.CreateVersion7())
                    .Build())];

        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource(rows));

        var page1 = await handler.HandleAsync(DefaultQuery(pageSize: 2), default);
        page1.Value.Rows.Count.ShouldBe(2);
        page1.Value.NextCursor.ShouldNotBeNull();

        var page2 = await handler.HandleAsync(
            DefaultQuery(pageSize: 2, cursor: page1.Value.NextCursor), default);
        page2.Value.Rows.Count.ShouldBe(2);
        page2.Value.NextCursor.ShouldNotBeNull();

        IEnumerable<Guid> seen = page1.Value.Rows.Concat(page2.Value.Rows)
            .Select(r => r.AuditIdentifier);
        seen.Distinct().Count().ShouldBe(4);
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Empty_result_returns_an_empty_page_not_an_error()
    {
        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource([]));

        var result = await handler.HandleAsync(DefaultQuery(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBeEmpty();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Rejects_an_unparseable_cursor()
    {
        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource([]));

        var result = await handler.HandleAsync(
            DefaultQuery(cursor: "this-is-not-a-cursor"), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<SearchAuditError.InvalidCursor>();
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Rejects_pageSize_above_the_maximum()
    {
        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource([]));

        var result = await handler.HandleAsync(DefaultQuery(pageSize: 201), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<SearchAuditError.PageSizeOutOfRange>();
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Filter_by_actor_username_narrows_the_result()
    {
        AuditEventEntity admin = new AuditEventBuilder()
            .WithActor(Guid.CreateVersion7(), username: "admin@munich.test").Build();
        AuditEventEntity operatorRow = new AuditEventBuilder()
            .WithActor(Guid.CreateVersion7(), username: "op-3@munich.test").Build();

        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource([admin, operatorRow]));

        var result = await handler.HandleAsync(
            DefaultQuery(actorUsername: "admin@munich.test"), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
        result.Value.Rows[0].ActorUsername.ShouldBe("admin@munich.test");
    }
}
