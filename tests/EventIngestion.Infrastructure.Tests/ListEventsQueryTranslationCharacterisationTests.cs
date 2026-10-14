using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 346, behaviour-preserving): the fully filtered,
/// cursor-bounded event listing translates to SQL offline
/// (<c>ToQueryString()</c> opens no connection) and every filter reaches the
/// <c>WHERE</c> clause, not merely the select list. Fragments only — the full
/// SQL text moves with every EF or Npgsql patch bump. The in-memory semantics
/// of the same method are pinned by <c>ListEventsPagedQueryCharacterisationTests</c>
/// in Application.Tests. These must pass unmodified after
/// <c>BuildPagedQuery</c> is split into helpers.
/// </summary>
public class ListEventsQueryTranslationCharacterisationTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_filter_and_the_cursor_reach_the_where_clause()
    {
        var sql = Translate(FullQuery(), cursor: (Anchor.AddDays(3), Guid.Empty));

        var where = Between(sql, "WHERE", "ORDER BY");
        where.ShouldContain("fab_id");
        where.ShouldContain("source");
        where.ShouldContain("device_id");
        where.ShouldContain("kind");
        Occurrences(where, "occurred_at").ShouldBe(2, "OccurredAfter and OccurredBefore");
        Occurrences(where, "ingested_at").ShouldBe(
            5,
            "IngestedAfter, IngestedBefore, the cursor's non-strict bound (keeps the index range on "
            + "ix_events_ingested), and its strict and tie compares");
        where.ShouldContain("ingested_at::timestamptz <=", Case.Sensitive, "the redundant non-strict cursor bound");
        where.ShouldContain("event_id::uuid <", Case.Sensitive, "the strict tiebreak on the identifier");
        where.ShouldContain("event_id");
    }

    [Fact]
    public void Without_a_cursor_the_ingested_time_is_bounded_only_by_the_two_filters()
    {
        var sql = Translate(FullQuery(), cursor: null);

        Occurrences(Between(sql, "WHERE", "ORDER BY"), "ingested_at").ShouldBe(2);
    }

    [Fact]
    public void Only_the_fab_scope_is_in_the_where_clause_when_no_filter_is_set()
    {
        ListEventsQuery query = new([FabIdentifier.From("munich")], null, null, null, null, null, null, null, 100, null);

        var where = Between(Translate(query, cursor: null), "WHERE", "ORDER BY");

        where.ShouldContain("fab_id");
        where.ShouldNotContain("source");
        where.ShouldNotContain("device_id");
        where.ShouldNotContain("kind");
        where.ShouldNotContain("occurred_at");
        where.ShouldNotContain("ingested_at");
    }

    [Fact]
    public void The_listing_is_ordered_newest_ingested_first_then_by_identifier_and_limited()
    {
        var sql = Translate(FullQuery(), cursor: null);

        var orderBy = sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..];
        var ingested = orderBy.IndexOf("ingested_at", StringComparison.Ordinal);
        var identifier = orderBy.IndexOf("event_id", StringComparison.Ordinal);
        ingested.ShouldBeGreaterThan(-1);
        identifier.ShouldBeGreaterThan(ingested);
        Occurrences(orderBy, "DESC").ShouldBe(2);
        sql.ShouldContain("LIMIT");
    }

    private static ListEventsQuery FullQuery() =>
        new(
            Fabs: [FabIdentifier.From("munich")],
            Source: Source.Plc,
            Device: DeviceIdentifier.From("press-07"),
            Kind: Kind.From("Alarm"),
            OccurredAfter: Anchor,
            OccurredBefore: Anchor.AddHours(1),
            IngestedAfter: Anchor.AddDays(1),
            IngestedBefore: Anchor.AddDays(2),
            PageSize: 50,
            Cursor: null);

    private static string Translate(ListEventsQuery query, (DateTimeOffset Ingested, Guid EventId)? cursor)
    {
        using var context = new EventIngestionDbContext(
            new DbContextOptionsBuilder<EventIngestionDbContext>()
                .UseNpgsql("Host=unused;Database=translation-check")
                .Options);

        return ListEventsQueryHandler.BuildPagedQuery(context.Events, query, cursor, 50).ToQueryString();
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        from.ShouldBeGreaterThan(-1);
        to.ShouldBeGreaterThan(from);
        return text[from..to];
    }

    private static int Occurrences(string text, string fragment) =>
        (text.Length - text.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;
}
