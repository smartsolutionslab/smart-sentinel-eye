using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
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
        new(resourceKind, ResourceIdentifier.From(resourceIdentifier), FabIdentifier.From(fab), since, null, pageSize, null);

    private static AuditEventEntity Row(
        int offsetMinutes, string overlay = TargetOverlay, string kind = "overlay", string? fab = "munich") =>
        new AuditEventBuilder()
            .WithOccurredAt(Base.AddMinutes(offsetMinutes))
            .WithResource(kind, overlay)
            .WithFab(fab)
            .Build();

    [Fact]
    public async Task Returns_rows_ascending_by_OccurredAt()
    {
        TestAuditEventQuerySource source = new([Row(10), Row(0), Row(5)]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(3);
        result.Value.Rows[0].OccurredAt.ShouldBe(Base);
        result.Value.Rows[2].OccurredAt.ShouldBe(Base.AddMinutes(10));
    }

    [Fact]
    public async Task Excludes_rows_for_a_different_resource()
    {
        TestAuditEventQuerySource source = new([
            Row(0),
            Row(5, overlay: "overlay-other"),
            Row(10),
        ]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(2);
        result.Value.Rows.All(r => r.ResourceIdentifier == TargetOverlay).ShouldBeTrue();
    }

    [Fact]
    public async Task A_row_with_no_fab_is_returned_by_a_fab_scoped_timeline()
    {
        TestAuditEventQuerySource source = new([Row(0, fab: null)]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
        result.Value.Rows[0].Fab.ShouldBeNull();
    }

    [Fact]
    public async Task A_row_belonging_to_another_fab_is_still_excluded()
    {
        TestAuditEventQuerySource source = new([
            Row(0, fab: "berlin"),
            Row(5, fab: null),
        ]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        // Today (unconditional Fab == fabFilter) this returns zero rows — the
        // null row is excluded too, which is the filed defect. The guard is
        // deliberately shaped to hold in both states: it fails only if a
        // "berlin" row ever leaks in, which is what a deleted (rather than
        // widened) predicate would do.
        result.Value.Rows.ShouldAllBe(row => row.Fab == null);
        result.Value.Rows.ShouldNotContain(row => row.Fab == "berlin");
    }

    [Fact]
    public async Task A_fab_neutral_row_for_a_different_resource_is_still_excluded()
    {
        TestAuditEventQuerySource source = new([Row(0, kind: "camera", fab: null)]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Since_filter_narrows_the_window()
    {
        TestAuditEventQuerySource source = new([Row(0), Row(5), Row(10)]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(since: Base.AddMinutes(7)), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
        result.Value.Rows[0].OccurredAt.ShouldBe(Base.AddMinutes(10));
    }

    [Fact]
    public async Task Rejects_an_unknown_resource_kind()
    {
        TestAuditEventQuerySource source = new([]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(resourceKind: "audit"), default);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetResourceTimelineError.UnknownResourceKind>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Default_pageSize_kicks_in_for_zero_and_rejects_over_maximum(int pageSize)
    {
        TestAuditEventQuerySource source = new([Row(0), Row(5)]);
        GetResourceTimelineQueryHandler handler = new(source);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(Q(pageSize: pageSize), default);

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
