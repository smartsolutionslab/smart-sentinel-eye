using System.Globalization;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

/// <summary>
/// Characterisation (spec 352, ADR-0144), captured green on unmodified <c>develop</c> before
/// <c>SearchAuditQueryHandler.HandleAsync</c> is split into steps. Pins newest-first paging
/// across rows that share one <c>OccurredAt</c>, the literal next cursors, the window bounds
/// and what each single filter narrows to.
///
/// <para>
/// The cursor strings are hand-written: <c>base64("&lt;UtcTicks D19&gt;.&lt;audit id N&gt;")</c>
/// for <c>2026-03-10T12:00:00Z</c> (ticks 639087408000000000) and the identifier named in each
/// constant, so a handler that encodes a different row, or a different shape, fails. The tied
/// identifiers differ in their first field so <see cref="Guid.CompareTo(Guid)"/> orders them
/// the way the literals read.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: changing the tie-break <c>CompareTo(...) &lt; 0</c> to
/// <c>&lt;= 0</c> turns <see cref="Paging_newest_first_walks_ties_by_descending_identifier_without_overlap_or_gap"/> red.
/// </para>
/// </summary>
public class SearchAuditPagingCharacterisationTests
{
    private const string Newest = "00000030-0000-4000-8000-000000000000";
    private const string TiedHighest = "00000013-0000-4000-8000-000000000000";
    private const string TiedMiddle = "00000012-0000-4000-8000-000000000000";
    private const string TiedLowest = "00000011-0000-4000-8000-000000000000";
    private const string Oldest = "00000020-0000-4000-8000-000000000000";

    // base64("0639087409200000000.00000030000040008000000000000000"), 2026-03-10T12:02:00Z
    private const string CursorAfterNewest = "MDYzOTA4NzQwOTIwMDAwMDAwMC4wMDAwMDAzMDAwMDA0MDAwODAwMDAwMDAwMDAwMDAwMA==";

    // base64("0639087408000000000.00000013000040008000000000000000")
    private const string CursorAfterTiedHighest = "MDYzOTA4NzQwODAwMDAwMDAwMC4wMDAwMDAxMzAwMDA0MDAwODAwMDAwMDAwMDAwMDAwMA==";

    // base64("0639087408000000000.00000011000040008000000000000000")
    private const string CursorAfterTiedLowest = "MDYzOTA4NzQwODAwMDAwMDAwMC4wMDAwMDAxMTAwMDA0MDAwODAwMDAwMDAwMDAwMDAwMA==";

    private static readonly DateTimeOffset Tied =
        DateTimeOffset.Parse("2026-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private static AuditEventEntity[] FiveRows() =>
    [
        new AuditEventBuilder().WithOccurredAt(Tied.AddMinutes(2)).Build().WithIdentifier(Newest),
        new AuditEventBuilder().WithOccurredAt(Tied).Build().WithIdentifier(TiedLowest),
        new AuditEventBuilder().WithOccurredAt(Tied).Build().WithIdentifier(TiedHighest),
        new AuditEventBuilder().WithOccurredAt(Tied).Build().WithIdentifier(TiedMiddle),
        new AuditEventBuilder().WithOccurredAt(Tied.AddMinutes(-2)).Build().WithIdentifier(Oldest),
    ];

    private static SearchAuditQuery Query(
        int pageSize = 50,
        string? cursor = null,
        DateTimeOffset? since = null,
        DateTimeOffset? until = null,
        Guid? actor = null,
        string? actorUsername = null,
        string? eventKind = null,
        string? resourceKind = null,
        string? resourceIdentifier = null) =>
        new("munich", ["munich"], actor, actorUsername, eventKind, resourceKind, resourceIdentifier, since, until, pageSize, cursor);

    private static SearchAuditQueryHandler HandlerOver(params AuditEventEntity[] rows) =>
        new(new TestAuditEventQuerySource(rows));

    private static async Task ShouldPageAsync(
        SearchAuditQueryHandler handler,
        SearchAuditQuery query,
        string? expectedNextCursor,
        params string[] expectedRows)
    {
        var result = await handler.HandleAsync(query, default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ShouldBe(expectedRows);
        result.Value.NextCursor.ShouldBe(expectedNextCursor);
    }

    [Fact]
    public async Task Paging_newest_first_walks_ties_by_descending_identifier_without_overlap_or_gap()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(handler, Query(pageSize: 2), CursorAfterTiedHighest, Newest, TiedHighest);
        await ShouldPageAsync(
            handler,
            Query(pageSize: 2, cursor: CursorAfterTiedHighest),
            CursorAfterTiedLowest,
            TiedMiddle,
            TiedLowest);
        await ShouldPageAsync(handler, Query(pageSize: 2, cursor: CursorAfterTiedLowest), null, Oldest);
    }

    [Fact]
    public async Task A_page_that_is_exactly_full_carries_no_next_cursor()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(
            handler,
            Query(pageSize: 5),
            null,
            Newest,
            TiedHighest,
            TiedMiddle,
            TiedLowest,
            Oldest);
        await ShouldPageAsync(
            handler,
            Query(pageSize: 2, cursor: CursorAfterTiedLowest, until: Tied.AddMinutes(10)),
            null,
            Oldest);
    }

    [Fact]
    public async Task A_page_one_row_short_of_the_rows_left_carries_a_next_cursor_for_its_last_row()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(
            handler,
            Query(pageSize: 4),
            CursorAfterTiedLowest,
            Newest,
            TiedHighest,
            TiedMiddle,
            TiedLowest);
        await ShouldPageAsync(handler, Query(pageSize: 1), CursorAfterNewest, Newest);
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
            Newest,
            TiedHighest,
            TiedMiddle,
            TiedLowest);
        await ShouldPageAsync(handler, Query(until: Tied), null, Oldest);
        await ShouldPageAsync(
            handler,
            Query(until: Tied.AddMinutes(2)),
            null,
            TiedHighest,
            TiedMiddle,
            TiedLowest,
            Oldest);
        await ShouldPageAsync(
            handler,
            Query(since: Tied, until: Tied.AddMinutes(2)),
            null,
            TiedHighest,
            TiedMiddle,
            TiedLowest);
    }

    [Fact]
    public async Task A_cursor_combines_with_a_window_so_both_bounds_still_apply()
    {
        var handler = HandlerOver(FiveRows());

        await ShouldPageAsync(
            handler,
            Query(cursor: CursorAfterTiedHighest, until: Tied.AddMinutes(1), since: Tied.AddMinutes(-1)),
            null,
            TiedMiddle,
            TiedLowest);
    }

    [Fact]
    public async Task Each_filter_narrows_the_result_to_its_own_row_set()
    {
        var actorMatch = Guid.Parse("00000001-0000-4000-8000-000000000001");
        var actorOther = Guid.Parse("00000002-0000-4000-8000-000000000002");
        var one = "00000051-0000-4000-8000-000000000000";
        var two = "00000052-0000-4000-8000-000000000000";
        var three = "00000053-0000-4000-8000-000000000000";
        var four = "00000054-0000-4000-8000-000000000000";
        var five = "00000055-0000-4000-8000-000000000000";

        // actor {1,2}; username alice {2,3}; event kind KindOne {3,4}; resource kind camera {4,5};
        // resource identifier res-1 {1,5}. Newest first is 5,4,3,2,1.
        AuditEventEntity[] rows =
        [
            Row(1, actorMatch, "bob", "KindTwo", "stream", "res-1", one),
            Row(2, actorMatch, "alice", "KindTwo", "stream", "res-2", two),
            Row(3, actorOther, "alice", "KindOne", "stream", "res-2", three),
            Row(4, actorOther, "bob", "KindOne", "camera", "res-2", four),
            Row(5, actorOther, "bob", "KindTwo", "camera", "res-1", five),
        ];
        var handler = HandlerOver(rows);

        await ShouldPageAsync(handler, Query(actor: actorMatch), null, two, one);
        await ShouldPageAsync(handler, Query(actorUsername: "alice"), null, three, two);
        await ShouldPageAsync(handler, Query(eventKind: "KindOne"), null, four, three);
        await ShouldPageAsync(handler, Query(resourceKind: "camera"), null, five, four);
        await ShouldPageAsync(handler, Query(resourceIdentifier: "res-1"), null, five, one);
        await ShouldPageAsync(handler, Query(actor: actorMatch, actorUsername: "alice"), null, two);
        await ShouldPageAsync(handler, Query(eventKind: "KindOne", resourceKind: "camera"), null, four);
        await ShouldPageAsync(handler, Query(eventKind: "KindThree"), null);
    }

    private static AuditEventEntity Row(
        int minute,
        Guid actor,
        string username,
        string eventKind,
        string resourceKind,
        string resourceIdentifier,
        string identifier) =>
        new AuditEventBuilder()
            .WithOccurredAt(Tied.AddMinutes(minute))
            .WithActor(actor, username)
            .WithEventKind(eventKind)
            .WithResource(resourceKind, resourceIdentifier)
            .Build()
            .WithIdentifier(identifier);
}
