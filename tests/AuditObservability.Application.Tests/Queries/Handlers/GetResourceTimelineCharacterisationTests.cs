using System.Globalization;
using System.Net;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

/// <summary>
/// Characterisation (spec 352, ADR-0144), captured green on unmodified <c>develop</c> before
/// <c>GetResourceTimelineQueryHandler.HandleAsync</c> is split into steps. Pins the order the
/// timeline refuses a request in (resource kind first, then page size, then cursor: the
/// reverse of the search for the first two), which rows a caller's fab sees, and oldest-first
/// paging across rows sharing one <c>OccurredAt</c> with literal next cursors.
///
/// <para>
/// The cursors are hand-written <c>base64("&lt;UtcTicks D19&gt;.&lt;audit id N&gt;")</c> for
/// <c>2026-03-10T12:00:00Z</c> (ticks 639087408000000000). The tied identifiers differ in
/// their first field so <see cref="Guid.CompareTo(Guid)"/> orders them as the literals read.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: changing the ascending tie-break <c>CompareTo(...) &gt; 0</c>
/// to <c>&gt;= 0</c> turns <see cref="Paging_oldest_first_walks_ties_by_ascending_identifier_without_overlap_or_gap"/> red.
/// </para>
/// </summary>
public class GetResourceTimelineCharacterisationTests
{
    private const string Newest = "00000030-0000-4000-8000-000000000000";
    private const string TiedHighest = "00000013-0000-4000-8000-000000000000";
    private const string TiedMiddle = "00000012-0000-4000-8000-000000000000";
    private const string TiedLowest = "00000011-0000-4000-8000-000000000000";
    private const string Oldest = "00000020-0000-4000-8000-000000000000";

    // base64("0639087408000000000.00000013000040008000000000000000")
    private const string CursorAfterTiedHighest = "MDYzOTA4NzQwODAwMDAwMDAwMC4wMDAwMDAxMzAwMDA0MDAwODAwMDAwMDAwMDAwMDAwMA==";

    // base64("0639087408000000000.00000011000040008000000000000000")
    private const string CursorAfterTiedLowest = "MDYzOTA4NzQwODAwMDAwMDAwMC4wMDAwMDAxMTAwMDA0MDAwODAwMDAwMDAwMDAwMDAwMA==";

    // base64("0639087406800000000.00000020000040008000000000000000"), 2026-03-10T11:58:00Z
    private const string CursorAfterOldest = "MDYzOTA4NzQwNjgwMDAwMDAwMC4wMDAwMDAyMDAwMDA0MDAwODAwMDAwMDAwMDAwMDAwMA==";

    private static readonly DateTimeOffset Tied =
        DateTimeOffset.Parse("2026-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private static GetResourceTimelineQuery Query(
        string resourceKind = "overlay",
        string fab = "munich",
        int pageSize = 50,
        string? cursor = null,
        DateTimeOffset? since = null,
        DateTimeOffset? until = null) =>
        new(resourceKind, ResourceIdentifier.From("overlay-pilot"), FabIdentifier.From(fab), since, until, pageSize, cursor);

    private static AuditEventEntity Row(
        DateTimeOffset occurredAt,
        string identifier,
        string? fab = "munich",
        FabScope scope = FabScope.Owned,
        string resource = "overlay-pilot") =>
        new AuditEventBuilder()
            .WithOccurredAt(occurredAt)
            .WithFab(fab)
            .WithFabScope(scope)
            .WithResource("overlay", resource)
            .Build()
            .WithIdentifier(identifier);

    private static AuditEventEntity[] FiveRows() =>
    [
        Row(Tied.AddMinutes(2), Newest),
        Row(Tied, TiedLowest),
        Row(Tied, TiedHighest),
        Row(Tied, TiedMiddle),
        Row(Tied.AddMinutes(-2), Oldest),
    ];

    private static GetResourceTimelineQueryHandler HandlerOver(params AuditEventEntity[] rows) =>
        new(new TestAuditEventQuerySource(rows));

    private static async Task ShouldPageAsync(
        GetResourceTimelineQueryHandler handler,
        GetResourceTimelineQuery query,
        string? expectedNextCursor,
        params string[] expectedRows)
    {
        var result = await handler.HandleAsync(query, default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ShouldBe(expectedRows);
        result.Value.NextCursor.ShouldBe(expectedNextCursor);
    }

    private static void ShouldBeRefusal<TError>(
        Result<AuditPageDto, GetResourceTimelineError> result,
        string code,
        string message)
        where TError : GetResourceTimelineError
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<TError>();
        result.Error.Code.ShouldBe(code);
        result.Error.Message.ShouldBe(message);
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_timeline_with_every_input_invalid_is_refused_for_its_resource_kind_first()
    {
        var result = await HandlerOver().HandleAsync(
            Query(resourceKind: "bogus", pageSize: 201, cursor: "not-a-cursor!"), default);

        ShouldBeRefusal<GetResourceTimelineError.UnknownResourceKind>(
            result,
            "AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND",
            "resourceKind 'bogus' is not a known audit resource.");
    }

    [Fact]
    public async Task A_timeline_with_a_known_resource_kind_but_a_bad_page_size_and_cursor_is_refused_for_its_page_size()
    {
        var result = await HandlerOver().HandleAsync(
            Query(pageSize: 201, cursor: "not-a-cursor!"), default);

        ShouldBeRefusal<GetResourceTimelineError.PageSizeOutOfRange>(
            result,
            "AUDIT_TIMELINE_PAGE_SIZE_OUT_OF_RANGE",
            "pageSize 201 must be between 1 and 200.");
    }

    [Fact]
    public async Task A_timeline_with_only_a_bad_cursor_is_refused_for_its_cursor()
    {
        var result = await HandlerOver().HandleAsync(Query(cursor: "not-a-cursor!"), default);

        ShouldBeRefusal<GetResourceTimelineError.InvalidCursor>(
            result,
            "AUDIT_TIMELINE_INVALID_CURSOR",
            "Cursor 'not-a-cursor!' is not a valid pagination cursor.");
    }

    [Fact]
    public async Task An_empty_string_cursor_is_refused_as_an_invalid_cursor()
    {
        var result = await HandlerOver().HandleAsync(Query(cursor: string.Empty), default);

        ShouldBeRefusal<GetResourceTimelineError.InvalidCursor>(
            result,
            "AUDIT_TIMELINE_INVALID_CURSOR",
            "Cursor '' is not a valid pagination cursor.");
    }

    [Fact]
    public async Task A_resource_kind_in_the_wrong_case_is_unknown()
    {
        var result = await HandlerOver().HandleAsync(Query(resourceKind: "Camera"), default);

        ShouldBeRefusal<GetResourceTimelineError.UnknownResourceKind>(
            result,
            "AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND",
            "resourceKind 'Camera' is not a known audit resource.");
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    public async Task A_page_size_is_defaulted_or_accepted_up_to_the_maximum(int requested, int expectedRows)
    {
        AuditEventEntity[] rows =
        [.. Enumerable.Range(0, 201).Select(minute => new AuditEventBuilder()
            .WithOccurredAt(Tied.AddMinutes(minute))
            .WithResource("overlay", "overlay-pilot")
            .Build())];

        var result = await HandlerOver(rows).HandleAsync(Query(pageSize: requested), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(expectedRows);
        result.Value.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public void The_page_size_limits_are_these()
    {
        GetResourceTimelineQueryHandler.DefaultPageSize.ShouldBe(50);
        GetResourceTimelineQueryHandler.MaximumPageSize.ShouldBe(200);
    }

    [Fact]
    public async Task The_callers_fab_sees_its_own_rows_and_every_fab_less_row_but_no_other_fab_and_no_other_resource()
    {
        var own = "00000061-0000-4000-8000-000000000000";
        var otherFab = "00000062-0000-4000-8000-000000000000";
        var unresolved = "00000063-0000-4000-8000-000000000000";
        var notApplicable = "00000064-0000-4000-8000-000000000000";
        var otherResource = "00000065-0000-4000-8000-000000000000";
        var handler = HandlerOver(
            Row(Tied.AddMinutes(5), otherResource, resource: "overlay-other"),
            Row(Tied.AddMinutes(4), notApplicable, fab: null, scope: FabScope.Neutral),
            Row(Tied.AddMinutes(3), unresolved, fab: null),
            Row(Tied.AddMinutes(2), otherFab, fab: "berlin"),
            Row(Tied.AddMinutes(1), own));

        await ShouldPageAsync(handler, Query(), null, own, unresolved, notApplicable);
        await ShouldPageAsync(handler, Query(fab: "berlin"), null, otherFab, unresolved, notApplicable);
        await ShouldPageAsync(handler, Query(fab: "hamburg"), null, unresolved, notApplicable);
    }

    [Fact]
    public async Task A_not_applicable_row_is_returned_by_a_fab_scoped_timeline_and_another_fabs_row_is_not()
    {
        var own = "00000071-0000-4000-8000-000000000000";
        var otherFab = "00000072-0000-4000-8000-000000000000";
        var notApplicable = "00000073-0000-4000-8000-000000000000";
        var handler = HandlerOver(
            Row(Tied.AddMinutes(3), notApplicable, fab: null, scope: FabScope.Neutral),
            Row(Tied.AddMinutes(2), otherFab, fab: "berlin"),
            Row(Tied.AddMinutes(1), own));

        var result = await handler.HandleAsync(Query(), default);

        var identifiers = result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ToList();
        identifiers.ShouldContain(own);
        identifiers.ShouldContain(notApplicable);
        identifiers.ShouldNotContain(otherFab);
    }

    [Fact]
    public async Task Paging_oldest_first_walks_ties_by_ascending_identifier_without_overlap_or_gap()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(handler, Query(pageSize: 2), CursorAfterTiedLowest, Oldest, TiedLowest);
        await ShouldPageAsync(
            handler,
            Query(pageSize: 2, cursor: CursorAfterTiedLowest),
            CursorAfterTiedHighest,
            TiedMiddle,
            TiedHighest);
        await ShouldPageAsync(handler, Query(pageSize: 2, cursor: CursorAfterTiedHighest), null, Newest);
    }

    [Fact]
    public async Task A_page_that_is_exactly_full_carries_no_next_cursor_and_one_short_carries_one()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(
            handler,
            Query(pageSize: 5),
            null,
            Oldest,
            TiedLowest,
            TiedMiddle,
            TiedHighest,
            Newest);
        await ShouldPageAsync(
            handler,
            Query(pageSize: 4),
            CursorAfterTiedHighest,
            Oldest,
            TiedLowest,
            TiedMiddle,
            TiedHighest);
        await ShouldPageAsync(
            handler,
            Query(pageSize: 1, since: Tied.AddMinutes(1)),
            null,
            Newest);
        await ShouldPageAsync(handler, Query(pageSize: 1, until: Tied.AddMinutes(3)), CursorAfterOldest, Oldest);
    }

    [Fact]
    public async Task Since_is_inclusive_and_until_is_exclusive()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(handler, Query(since: Tied.AddMinutes(2)), null, Newest);
        await ShouldPageAsync(
            handler,
            Query(since: Tied),
            null,
            TiedLowest,
            TiedMiddle,
            TiedHighest,
            Newest);
        await ShouldPageAsync(handler, Query(until: Tied), null, Oldest);
        await ShouldPageAsync(
            handler,
            Query(until: Tied.AddMinutes(2)),
            null,
            Oldest,
            TiedLowest,
            TiedMiddle,
            TiedHighest);
        await ShouldPageAsync(
            handler,
            Query(since: Tied, until: Tied.AddMinutes(2)),
            null,
            TiedLowest,
            TiedMiddle,
            TiedHighest);
    }

    [Fact]
    public async Task A_cursor_combines_with_a_window_so_both_bounds_still_apply()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(
            handler,
            Query(cursor: CursorAfterTiedLowest, since: Tied.AddMinutes(-1), until: Tied.AddMinutes(1)),
            null,
            TiedMiddle,
            TiedHighest);
    }
}
