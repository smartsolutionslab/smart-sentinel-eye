using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 006 T092 (#626). Filtering and cursor pagination, executed against the
/// real database and asserted on the rows that come back.
///
/// <para>
/// <b>Why this was missing while looking covered.</b> The unit tests for
/// <c>ListEventsQueryHandler</c> assert two validation failures — page size and
/// a malformed cursor — and say in their own summary that the filter and
/// pagination behaviour is left to an integration test, because the in-memory
/// LINQ provider cannot translate the handler's cursor chain. That was the right
/// call. The integration test it defers to checks that the query
/// <b>translates to SQL</b>, offline, without opening a connection.
/// </para>
///
/// <para>
/// Translating is not returning the right rows. Between the two files the path
/// looks tested from either end, and a filter that compiles to valid SQL and
/// selects the wrong events passes both. That is the gap here.
/// </para>
///
/// <para>
/// <b>Every case is scoped by a unique kind.</b> The events table is shared with
/// every other test in this collection and with anything the fixture's simulator
/// publishes, so an assertion on "how many events came back" would be a race.
/// The kind minted per run is the discriminator; each assertion is about a set
/// this test owns entirely.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ListEventsFilteringTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    /// <summary>
    /// FR-018. Each filter is asked for in isolation against a seeded set whose
    /// correct answer is known, so a filter that silently matches everything
    /// and one that silently matches nothing both fail.
    /// </summary>
    [Fact]
    public async Task Each_filter_narrows_to_the_events_it_names()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string kind = UniqueKind("Filter");

        DateTimeOffset old = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset recent = DateTimeOffset.UtcNow.AddMinutes(-1);

        await SeedAsync(events, kind, device: "press-01", occurredAt: old);
        await SeedAsync(events, kind, device: "press-01", occurredAt: recent);
        await SeedAsync(events, kind, device: "press-02", occurredAt: recent);

        // The whole seeded set, so the per-filter answers below are read against
        // a known total rather than an assumed one.
        (await ListAsync(events, $"kind={kind}")).Count.ShouldBe(3);

        // Device.
        IReadOnlyList<JsonElement> press01 = await ListAsync(events, $"kind={kind}&deviceId=press-01");
        press01.Count.ShouldBe(2, "the device filter did not narrow to the device it names");
        press01.ShouldAllBe(e => e.GetProperty("device").GetString() == "press-01");

        // Kind — asserted by a kind nothing was seeded under, which is the case
        // that catches a predicate that quietly matches everything.
        (await ListAsync(events, $"kind={UniqueKind("Absent")}")).Count.ShouldBe(
            0, "a kind no event carries returned events anyway");

        // Occurred-at window.
        IReadOnlyList<JsonElement> since = await ListAsync(
            events, $"kind={kind}&occurredAfter={Iso(old.AddMinutes(1))}");
        since.Count.ShouldBe(2, "the occurredAfter filter did not exclude the older event");

        IReadOnlyList<JsonElement> before = await ListAsync(
            events, $"kind={kind}&occurredBefore={Iso(old.AddMinutes(1))}");
        before.Count.ShouldBe(1, "the occurredBefore filter did not exclude the newer events");

        // Source. Manual ingest stamps Source.Manual; nothing in this test
        // publishes as a machine, so a source filter naming the other one must
        // come back empty.
        (await ListAsync(events, $"kind={kind}&source=plc")).Count.ShouldBe(
            0, "events filed by an operator were returned under a machine source");
    }

    /// <summary>
    /// FR-018's cursor contract: pages pick up strictly after the previous one.
    ///
    /// <para>
    /// This is the case worth having. A cursor that skips an event or serves one
    /// twice produces a page that looks entirely reasonable in isolation — right
    /// shape, right fields, plausible count — and is only wrong when the pages
    /// are put back together. Walking the whole set and comparing it to what was
    /// seeded is the only assertion that can see it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Paging_through_a_set_yields_every_event_exactly_once()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string kind = UniqueKind("Page");
        const int seeded = 7;
        const int pageSize = 2;

        for (int i = 0; i < seeded; i++)
        {
            await SeedAsync(events, kind, device: $"line-{i}", occurredAt: DateTimeOffset.UtcNow);
        }

        List<Guid> walked = [];
        string? cursor = null;
        int pages = 0;

        do
        {
            string query = $"kind={kind}&pageSize={pageSize}"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

            HttpResponseMessage response = await events.GetAsync($"/events?fabId={Fab}&{query}");
            response.EnsureSuccessStatusCode();
            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();

            foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
            {
                walked.Add(item.GetProperty("eventIdentifier").GetGuid());
            }

            cursor = page.TryGetProperty("nextCursor", out JsonElement next)
                && next.ValueKind == JsonValueKind.String
                    ? next.GetString()
                    : null;

            // A cursor that never clears would page for ever; failing here says
            // that plainly instead of hanging the suite.
            (++pages).ShouldBeLessThanOrEqualTo(
                seeded + 2, "pagination did not terminate — the cursor is not advancing");
        }
        while (cursor is not null);

        walked.Count.ShouldBe(
            seeded,
            $"paging returned {walked.Count} events for a set of {seeded}: "
            + (walked.Count < seeded ? "the cursor skipped some" : "the cursor repeated some"));

        walked.Distinct().Count().ShouldBe(
            seeded, "the same event was served on more than one page");
    }

    // ---- seeding and reading -------------------------------------------------

    /// <summary>
    /// Unique per run, and legal: `Kind` must start with an uppercase letter and
    /// carry only letters or digits, so the prefix is capitalised here rather
    /// than trusted from the call site. A hex suffix satisfies the rest.
    /// </summary>
    private static string UniqueKind(string prefix) =>
        $"{char.ToUpperInvariant(prefix[0])}{prefix[1..]}{Guid.NewGuid():N}"[..16];

    private static string Iso(DateTimeOffset moment) =>
        Uri.EscapeDataString(moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    private static async Task SeedAsync(
        HttpClient events, string kind, string device, DateTimeOffset occurredAt)
    {
        HttpResponseMessage filed = await events.PostAsJsonAsync(
            $"/events/manual?fabId={Fab}",
            new
            {
                deviceId = device,
                kind,
                occurredAt,
                payload = new { seededBy = nameof(ListEventsFilteringTests) },
            });

        filed.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<JsonElement>> ListAsync(HttpClient events, string query)
    {
        HttpResponseMessage response = await events.GetAsync($"/events?fabId={Fab}&{query}&pageSize=100");
        response.EnsureSuccessStatusCode();

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.GetProperty("items").EnumerateArray()];
    }
}
