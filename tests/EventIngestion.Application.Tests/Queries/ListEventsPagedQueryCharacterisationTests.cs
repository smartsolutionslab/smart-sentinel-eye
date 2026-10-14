using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

/// <summary>
/// Characterisation (spec 346, behaviour-preserving): pins what
/// <see cref="ListEventsQueryHandler.BuildPagedQuery"/> selects, bounds and
/// orders, evaluated by LINQ-to-objects over an in-memory source. The SQL
/// translation of the same query is pinned separately by the Infrastructure
/// and Integration tests. These must pass unmodified after the method is
/// split into helpers. The cursor is the pair (ingested time, event
/// identifier), spec 360: dropping the identifier tie-break turns the
/// tied-row test red, and changing <c>CompareTo(...) &lt; 0</c> to
/// <c>&lt;= 0</c> turns the last-tied-row test red.
/// </summary>
public class ListEventsPagedQueryCharacterisationTests
{
    private static readonly DateTimeOffset Noon =
        DateTimeOffset.Parse("2026-05-28T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    [Fact]
    public void Only_events_from_the_callers_fabs_are_selected()
    {
        var munich = Ingest(1, fab: "munich");
        var dresden = Ingest(2, fab: "dresden");
        var leipzig = Ingest(3, fab: "leipzig");

        var ids = Run(
            [munich, dresden, leipzig],
            Query() with { Fabs = [FabIdentifier.From("dresden"), FabIdentifier.From("leipzig")] });

        ids.ShouldBe([3, 2]);
    }

    [Fact]
    public void The_source_filter_alone_keeps_only_that_source()
    {
        var plc = Ingest(1, source: Source.Plc);
        var manual = Ingest(2, source: Source.Manual);

        Run([plc, manual], Query() with { Source = Source.Manual }).ShouldBe([2]);
    }

    [Fact]
    public void The_device_filter_alone_keeps_only_that_device()
    {
        var oven = Ingest(1, device: "oven-1");
        var press = Ingest(2, device: "press-9");

        Run([oven, press], Query() with { Device = DeviceIdentifier.From("press-9") }).ShouldBe([2]);
    }

    [Fact]
    public void The_kind_filter_alone_keeps_only_that_kind()
    {
        var start = Ingest(1, kind: "CycleStart");
        var stop = Ingest(2, kind: "CycleStop");

        Run([start, stop], Query() with { Kind = Kind.From("CycleStop") }).ShouldBe([2]);
    }

    [Fact]
    public void The_occurred_after_bound_is_strict()
    {
        var before = Ingest(1, occurredAt: Noon.AddMinutes(-1));
        var exactly = Ingest(2, occurredAt: Noon);
        var after = Ingest(3, occurredAt: Noon.AddMinutes(1));

        Run([before, exactly, after], Query() with { OccurredAfter = Noon }).ShouldBe([3]);
    }

    [Fact]
    public void The_occurred_before_bound_is_strict()
    {
        var before = Ingest(1, occurredAt: Noon.AddMinutes(-1));
        var exactly = Ingest(2, occurredAt: Noon);
        var after = Ingest(3, occurredAt: Noon.AddMinutes(1));

        Run([before, exactly, after], Query() with { OccurredBefore = Noon }).ShouldBe([1]);
    }

    [Fact]
    public void The_ingested_after_bound_is_strict()
    {
        var before = Ingest(1, ingestedAt: Noon.AddMinutes(-1));
        var exactly = Ingest(2, ingestedAt: Noon);
        var after = Ingest(3, ingestedAt: Noon.AddMinutes(1));

        Run([before, exactly, after], Query() with { IngestedAfter = Noon }).ShouldBe([3]);
    }

    [Fact]
    public void The_ingested_before_bound_is_strict()
    {
        var before = Ingest(1, ingestedAt: Noon.AddMinutes(-1));
        var exactly = Ingest(2, ingestedAt: Noon);
        var after = Ingest(3, ingestedAt: Noon.AddMinutes(1));

        Run([before, exactly, after], Query() with { IngestedBefore = Noon }).ShouldBe([1]);
    }

    [Fact]
    public void The_occurred_bounds_do_not_look_at_the_ingested_time_and_the_reverse()
    {
        var event1 = Ingest(1, occurredAt: Noon.AddMinutes(-10), ingestedAt: Noon.AddMinutes(10));
        var event2 = Ingest(2, occurredAt: Noon.AddMinutes(3), ingestedAt: Noon.AddMinutes(-1));

        Run([event1, event2], Query() with { OccurredAfter = Noon }).ShouldBe([2]);
        Run([event1, event2], Query() with { IngestedAfter = Noon }).ShouldBe([1]);
    }

    [Fact]
    public void Every_filter_present_at_once_combines_with_and()
    {
        var match = Ingest(
            1, fab: "munich", source: Source.Plc, device: "oven-1", kind: "CycleStart",
            occurredAt: Noon, ingestedAt: Noon);
        var otherSource = Ingest(
            2, fab: "munich", source: Source.Manual, device: "oven-1", kind: "CycleStart",
            occurredAt: Noon, ingestedAt: Noon);
        var otherDevice = Ingest(
            3, fab: "munich", source: Source.Plc, device: "oven-2", kind: "CycleStart",
            occurredAt: Noon, ingestedAt: Noon);
        var otherKind = Ingest(
            4, fab: "munich", source: Source.Plc, device: "oven-1", kind: "CycleStop",
            occurredAt: Noon, ingestedAt: Noon);
        var tooEarly = Ingest(
            5, fab: "munich", source: Source.Plc, device: "oven-1", kind: "CycleStart",
            occurredAt: Noon.AddHours(-3), ingestedAt: Noon);

        var query = Query() with
        {
            Source = Source.Plc,
            Device = DeviceIdentifier.From("oven-1"),
            Kind = Kind.From("CycleStart"),
            OccurredAfter = Noon.AddHours(-1),
            OccurredBefore = Noon.AddHours(1),
            IngestedAfter = Noon.AddHours(-1),
            IngestedBefore = Noon.AddHours(1),
        };

        Run([match, otherSource, otherDevice, otherKind, tooEarly], query).ShouldBe([1]);
    }

    [Fact]
    public void A_cursor_on_a_tied_ingested_time_serves_the_tied_rows_with_a_smaller_identifier()
    {
        var events = TiedAtNoonWithOneOlder();

        var ids = Run(events, Query(), cursor: (Noon, IdentifierOf(2)));

        ids.ShouldBe([1, 4]);
    }

    [Fact]
    public void A_cursor_on_the_last_tied_row_serves_only_older_rows()
    {
        var events = TiedAtNoonWithOneOlder();

        var ids = Run(events, Query(), cursor: (Noon, IdentifierOf(1)));

        ids.ShouldBe([4]);
    }

    [Fact]
    public void Events_come_newest_ingested_first_then_by_descending_identifier()
    {
        var olderLowId = Ingest(1, ingestedAt: Noon.AddMinutes(-5));
        var tiedLowId = Ingest(2, ingestedAt: Noon);
        var tiedHighId = Ingest(3, ingestedAt: Noon);
        var newestLowestId = Ingest(0x0a, ingestedAt: Noon.AddMinutes(5));

        var ids = Run([olderLowId, tiedLowId, tiedHighId, newestLowestId], Query());

        ids.ShouldBe([0x0a, 3, 2, 1]);
    }

    [Fact]
    public void One_row_more_than_the_page_size_is_taken_to_detect_a_next_page()
    {
        var events = Enumerable.Range(1, 5)
            .Select(i => Ingest(i, ingestedAt: Noon.AddMinutes(i)))
            .ToArray();

        var ids = Run(events, Query(), pageSize: 2);

        ids.ShouldBe([5, 4, 3]);
    }

    private static List<int> Run(
        EventAggregate[] events,
        ListEventsQuery query,
        (DateTimeOffset Ingested, Guid EventId)? cursor = null,
        int pageSize = 100) =>
        ListEventsQueryHandler.BuildPagedQuery(events.AsQueryable(), query, cursor, pageSize)
            .ToList()
            .Select(Number)
            .ToList();

    private static EventAggregate[] TiedAtNoonWithOneOlder() =>
    [
        Ingest(3, ingestedAt: Noon),
        Ingest(2, ingestedAt: Noon),
        Ingest(1, ingestedAt: Noon),
        Ingest(4, ingestedAt: Noon.AddMinutes(-1)),
    ];

    private static Guid IdentifierOf(int number) =>
        new(0, 0, 0, [0, 0, 0, 0, 0, 0, 0, (byte)number]);

    private static ListEventsQuery Query() =>
        new([Munich], null, null, null, null, null, null, null, 100, null);

    private static int Number(EventAggregate eventAggregate) =>
        (int)(eventAggregate.Id.Value.ToByteArray()[15]);

    private static EventAggregate Ingest(
        int number,
        string fab = "munich",
        Source? source = null,
        string device = "oven-1",
        string kind = "CycleStart",
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? ingestedAt = null)
    {
        var identifier = EventIdentifier.From(new Guid(0, 0, 0, [0, 0, 0, 0, 0, 0, 0, (byte)number]));
        var ingested = ingestedAt ?? Noon;
        return new EventBuilder()
            .WithIdentifier(identifier)
            .WithFab(fab)
            .WithSource(source ?? Source.Plc)
            .WithDevice(device)
            .WithKind(kind)
            .WithOccurredAt(occurredAt ?? ingested)
            .WithClock(ingested)
            .Build();
    }
}
