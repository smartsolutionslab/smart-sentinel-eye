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
        new(resourceKind, ResourceIdentifier.From(resourceIdentifier), FabIdentifier.From(fab), since, null, pageSize, null);

    private static AuditEventEntity Row(
        int offsetMinutes,
        string overlay = TargetOverlay,
        string kind = "overlay",
        string? fab = "munich",
        FabScope scope = FabScope.Owned) =>
        new AuditEventBuilder()
            .WithOccurredAt(Base.AddMinutes(offsetMinutes))
            .WithResource(kind, overlay)
            .WithFab(fab)
            .WithFabScope(scope)
            .Build();

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task An_unresolved_row_is_not_returned_by_a_fab_scoped_timeline()
    {
        var unresolved = Row(0, fab: null);
        TestAuditEventQuerySource source = new([unresolved]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(row => row.AuditIdentifier).ShouldNotContain(unresolved.Id.Value);
        result.Value.Rows.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_not_applicable_row_is_returned_by_a_fab_scoped_timeline()
    {
        var notApplicable = Row(0, fab: null, scope: FabScope.Neutral);
        TestAuditEventQuerySource source = new([notApplicable]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(row => row.AuditIdentifier).ShouldBe([notApplicable.Id.Value]);
        result.Value.Rows[0].Fab.ShouldBeNull();
    }

    [Fact]
    public async Task A_row_belonging_to_another_fab_or_unresolved_is_excluded()
    {
        // Seeds the four rows that matter on one resource: munich, berlin,
        // Unresolved and NotApplicable. Asserting the exact set by identifier
        // — not just "no berlin" — makes this non-vacuous: it fails if either
        // disjunct of the predicate is missing and if the predicate were
        // deleted outright (berlin and Unresolved would then leak in).
        var munich = Row(0, fab: "munich");
        var berlin = Row(5, fab: "berlin");
        var unresolved = Row(10, fab: null);
        var notApplicable = Row(15, fab: null, scope: FabScope.Neutral);
        TestAuditEventQuerySource source = new([munich, berlin, unresolved, notApplicable]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(row => row.AuditIdentifier)
            .ShouldBe([munich.Id.Value, notApplicable.Id.Value], ignoreOrder: true);
    }

    [Fact]
    public async Task A_fab_neutral_row_for_a_different_resource_is_still_excluded()
    {
        TestAuditEventQuerySource source = new([Row(0, kind: "camera", fab: null)]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Since_filter_narrows_the_window()
    {
        TestAuditEventQuerySource source = new([Row(0), Row(5), Row(10)]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(since: Base.AddMinutes(7)), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(1);
        result.Value.Rows[0].OccurredAt.ShouldBe(Base.AddMinutes(10));
    }

    [Fact]
    public async Task Rejects_an_unknown_resource_kind()
    {
        TestAuditEventQuerySource source = new([]);
        GetResourceTimelineQueryHandler handler = new(source);

        var result = await handler.HandleAsync(Q(resourceKind: "audit"), default);

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
