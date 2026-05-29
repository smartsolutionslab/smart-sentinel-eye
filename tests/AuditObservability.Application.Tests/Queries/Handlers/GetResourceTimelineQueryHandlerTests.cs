using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

public class GetResourceTimelineQueryHandlerTests
{
    private static readonly DateTimeOffset Base =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private const string TargetOverlay = "overlay-pilot";

    private static GetResourceTimelineQuery Q(
        string resourceKind = "overlay",
        string resourceIdentifier = TargetOverlay,
        string fab = "munich",
        DateTimeOffset? since = null,
        int pageSize = 50) =>
        new(resourceKind, resourceIdentifier, fab, since, null, pageSize, null);

    private static AuditEventEntity Row(
        int offsetMinutes, string overlay = TargetOverlay, string kind = "overlay") =>
        new AuditEventBuilder()
            .WithOccurredAt(Base.AddMinutes(offsetMinutes))
            .WithResource(kind, overlay)
            .WithFab("munich")
            .Build();

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Returns_rows_ascending_by_OccurredAt()
    {
        TestAuditEventQuerySource source = new([Row(10), Row(0), Row(5)]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(3);
        result.Value.Rows[0].OccurredAt.ShouldBe(Base);
        result.Value.Rows[2].OccurredAt.ShouldBe(Base.AddMinutes(10));
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Excludes_rows_for_a_different_resource()
    {
        TestAuditEventQuerySource source = new([
            Row(0),
            Row(5, overlay: "overlay-other"),
            Row(10),
        ]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(2);
        result.Value.Rows.All(r => r.ResourceIdentifier == TargetOverlay).ShouldBeTrue();
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Since_filter_narrows_the_window()
    {
        TestAuditEventQuerySource source = new([Row(0), Row(5), Row(10)]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(since: Base.AddMinutes(7)), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
        result.Value.Rows[0].OccurredAt.ShouldBe(Base.AddMinutes(10));
    }

    [Fact(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    public async Task Rejects_an_unknown_resource_kind()
    {
        TestAuditEventQuerySource source = new([]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(resourceKind: "audit"), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetResourceTimelineError.UnknownResourceKind>();
    }

    [Theory(Skip = "IQueryable + Option<T> in-memory rewriter limit — covered by integration tests against the real DbContext in PR D.")]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Default_pageSize_kicks_in_for_zero_and_rejects_over_maximum(int pageSize)
    {
        TestAuditEventQuerySource source = new([Row(0), Row(5)]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(pageSize: pageSize), default);

        if (pageSize > GetResourceTimelineQueryHandler.MaximumPageSize)
        {
            result.IsFailure.ShouldBeTrue();
            result.Error.ShouldBeOfType<GetResourceTimelineError.PageSizeOutOfRange>();
        }
        else
        {
            result.IsSuccess.ShouldBeTrue();
            result.Value.Rows.Count.ShouldBe(2);
        }
    }
}
