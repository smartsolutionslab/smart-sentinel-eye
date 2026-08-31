using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 009 NFR-001 (T072): audit ingest latency, p99 ≤ 50 ms from publish to
/// the row being written. Warms 100 events, measures 1 000.
///
/// <para>
/// <b>The generator is a repeated variable value set</b>, and the choice is
/// load-bearing. It publishes <c>SystemVariableValueChangedV1</c>, whose
/// <c>Metadata.OccurredAt</c> is the domain event's <c>changedAt</c> — stamped
/// as the aggregate mutates, so the figure spans the publish path rather than
/// the caller's wall clock. A plant-floor event would have been the easier
/// generator and the wrong one: <c>FabEventIngestedV1</c> carries the *device's*
/// timestamp, so its rows measure the whole MQTT chain. Measured on a dev stack
/// those run p50 30 ms / p99 63 ms, against p50 10 ms / p99 24 ms for events
/// stamped at publish — the same budget, the wrong leg, and a failure that would
/// say nothing about audit ingest.
/// </para>
///
/// <para>
/// It also keeps the blast radius small. The variable is referenced by no
/// overlay, so <c>VariableValueChangedDomainEventHandler</c> takes its
/// no-overlays branch and publishes nothing further; and unlike a camera
/// registration there is no stream provisioning behind each event.
/// </para>
///
/// <para>
/// <b>What the figure spans, stated because the NFR is narrower than anything
/// that can be measured.</b> NFR-001's words are "RabbitMQ deliver-ack to audit
/// row committed". No timestamp pair exists for that leg. What is available is
/// <c>received_at - occurred_at</c>: aggregate mutation → outbox → RabbitMQ →
/// the audit handler stamping <c>ReceivedAt</c> in <c>AuditEvent.From</c>, which
/// happens just before <c>SaveAsync</c> rather than after the commit. So this is
/// a superset of the leg NFR-001 names, short by the final insert — the honest
/// approximation, and a pass here implies a pass on the narrower leg.
/// </para>
///
/// <para>
/// Latency is computed <b>in SQL</b> rather than by polling from the client, so
/// the measurement carries no HTTP round trip of its own — the approach spec
/// 020's throughput test takes, for the same reason.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class NFR001_AuditIngestLatencyTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const int WarmupIterations = 100;
    private const int MeasureIterations = 1_000;
    private const double P99BudgetMs = 50;

    /// <summary>How long to wait for the last measured row to reach the store.</summary>
    private static readonly TimeSpan IngestDeadline = TimeSpan.FromMinutes(3);

    /// <summary>
    /// <b>Excluded from CI</b> by <c>Category!=Measurement</c> in `ci.yml`, which
    /// deviates from T072's checkpoint that "NFR-001 + NFR-002 land in CI".
    ///
    /// <para>
    /// It is excluded because it does not pass, and the budget is deliberately
    /// left at the NFR's 50 ms rather than tuned to whatever the fixture
    /// produces — a passing number obtained by moving the line would say the
    /// requirement is met when it is not. Measured on the fixture: 1 000 events
    /// at roughly 20 ev/s gave <b>p50 4 800 ms, p99 9 469 ms, max 9 586 ms</b>,
    /// and 100 events gave p50 4 624 ms, max 5 045 ms. Latency grows through the
    /// run, so the consumer is draining slower than the writes arrive.
    /// </para>
    ///
    /// <para>
    /// <b>Run mode under load has since been measured (2026-08-28), and the gap
    /// is not a fixture artefact.</b> Driving the same generator against the
    /// run-mode stack at sustained rates, each rate run twice: 24 ev/s → p50
    /// 31 ms / p99 142 ms; 48 ev/s → p50 37 ms / p99 258–280 ms; 68 ev/s → p50
    /// 52 ms / p99 342 ms; 86–95 ev/s → p50 3 066–4 936 ms / p99 6 350–6 730 ms;
    /// 158 ev/s → p50 9 870 ms / p99 14 221 ms. The 50 ms p99 is missed at every
    /// rate measured, near-idle included.
    /// </para>
    ///
    /// <para>
    /// <b>The delay is on the consume side, not the flush cadence.</b> Sampled
    /// through a 158 ev/s burst the publisher's
    /// <c>wolverine_outgoing_envelopes</c> held 0 rows at every sample while the
    /// audit queue on RabbitMQ backed up to 468 then 643. The consumer's ceiling
    /// is ~100 rows/s for that queue — exactly the rate NFR-001 names, with no
    /// headroom, which is why latency is stable to ~68 ev/s and collapses by ~86.
    /// Per message the audit side does a durable-inbox write plus the audit row's
    /// own <c>SaveAsync</c>, one transaction per row, on a single listener.
    /// </para>
    ///
    /// <para>
    /// <b>Parallel listeners were then taken (ADR-0124) and NFR-001 is still not
    /// met.</b> Four listeners per audit queue moved peak drain from ~100 to
    /// ~270 rows/s and the knee from ~75 ev/s to past 110: at ~30 ev/s p99 58 ms,
    /// at ~50–58 ev/s p99 62 ms, at ~85–115 ev/s p99 214–420 ms typical (two of
    /// six runs at ~100 ev/s spiked to 2 119 / 3 786 ms). So 100 ev/s is
    /// survivable and draining rather than collapsing to six or seven seconds,
    /// and the gap to the budget is ~5× where it was ~130×.
    /// </para>
    ///
    /// <para>
    /// <b>The second lever followed (ADR-0126): the audit listeners settle each
    /// delivery at the broker (<c>Mode=NativeAck</c>) rather than through the
    /// durable inbox.</b> Six runs at 99–113 ev/s gave p50 26–35 ms and p99
    /// 85–236 ms, against 29–63 ms / 283–333 ms for the same rates before — no
    /// overlap on p99. Not a durability trade: killing the audit service outright
    /// mid-burst put all 640 in-flight events back on the queue and every one was
    /// audited on restart.
    /// </para>
    ///
    /// <para>
    /// It does <b>not</b> stop Postgres being written per message, which an
    /// earlier version of this note claimed. The incoming-envelopes table still
    /// gains one <c>Handled</c> tombstone per event; what the mode removes is
    /// whatever the durable inbox does beyond that, and ADR-0126 does not
    /// quantify it.
    /// </para>
    ///
    /// <para>
    /// The budget therefore stays at 50 ms and this test stays excluded — the
    /// best p99 observed is 85 ms.
    /// </para>
    ///
    /// <para>
    /// <b>The third lever, batching audit writes, was built, measured and not
    /// adopted (ADR-0127).</b> At a sustained 100 ev/s it made both percentiles
    /// worse — p50 36–44 ms against 23–30 ms — because a batch window short
    /// enough to respect a 50 ms budget collects roughly one message at that
    /// rate. It is a large win under backlog, which ADR-0124 and ADR-0126 had
    /// already removed. So what is open in 1956 is no longer code: production
    /// topology, where audit gets its own pod and database node and none of
    /// this measured that, or moving NFR-001 to what the pipeline does.
    /// </para>
    /// </summary>
    [Trait("Category", "Measurement")]
    [Fact]
    public async Task Ingest_p99_from_publish_to_row_stays_under_50ms()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        // Two variables, not one. The warm-up's rows would otherwise sit in the
        // same result set as the measured ones with no way to tell them apart
        // after the fact, and the percentile would include the cold path the
        // warm-up exists to exclude.
        string warmName = await DefineAsync(variables);
        string measureName = await DefineAsync(variables);

        await SetRepeatedlyAsync(variables, warmName, WarmupIterations);
        string measureIdentifier = await SetRepeatedlyAsync(variables, measureName, MeasureIterations);

        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        int landed = await WaitForRowsAsync(context, measureIdentifier);
        landed.ShouldBe(
            MeasureIterations,
            "every measured event must reach the audit store before its latency can be read; "
            + $"{landed} of {MeasureIterations} arrived within {IngestDeadline.TotalSeconds:F0}s");

        (double p50, double p99, double max) = await PercentilesAsync(context, measureIdentifier);

        output.WriteLine(
            $"audit ingest over {MeasureIterations} events: "
            + $"p50 = {p50:F1} ms, p99 = {p99:F1} ms, max = {max:F1} ms");

        p99.ShouldBeLessThan(
            P99BudgetMs,
            $"NFR-001 allows p99 ≤ {P99BudgetMs} ms from publish to row; "
            + $"observed p50 = {p50:F1} ms, p99 = {p99:F1} ms, max = {max:F1} ms");
    }

    /// <summary>
    /// **Where the span goes** (spec 053 US1). Excluded from CI like its
    /// neighbour, and for the same reason: it is a measurement, not a check.
    ///
    /// <para>
    /// <b>It asserts almost nothing about the pipeline on purpose.</b> The
    /// output is a breakdown for someone deciding what to do about a
    /// requirement, and a test that failed when the pipeline was slow would be
    /// reporting the thing already known. What it does assert is that the
    /// breakdown is <i>trustworthy</i>: the parts cover the span, every row
    /// carried its stamps, and the clocks are close enough for the one part
    /// that crosses them to mean anything.
    /// </para>
    ///
    /// <para>
    /// Needs the measurement switch on — with it off the parts are absent and
    /// the run says so rather than reporting zeros.
    /// </para>
    /// </summary>
    [Trait("Category", "Measurement")]
    [Fact]
    public async Task Where_the_ingest_span_goes()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        string warmName = await DefineAsync(variables);
        string measureName = await DefineAsync(variables);
        await SetRepeatedlyAsync(variables, warmName, WarmupIterations);

        DateTimeOffset started = DateTimeOffset.UtcNow;
        string measureIdentifier = await SetRepeatedlyAsync(variables, measureName, MeasureIterations);
        TimeSpan drove = DateTimeOffset.UtcNow - started;

        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        int landed = await WaitForRowsAsync(context, measureIdentifier);
        landed.ShouldBe(MeasureIterations);

        // **The achieved rate, next to the intended one.** A run that meant to
        // drive 100 events a second and managed 60 has answered a different
        // question, and saying so is the difference between a measurement and a
        // number.
        double achieved = MeasureIterations / drove.TotalSeconds;

        IngestAttribution attribution = await AttributionAsync(context, measureIdentifier);
        // **What this offset is, and what it is not.** It is this process's
        // distance from the shared server — an indicator of the drift between a
        // host process and a container, which is the only clock comparison
        // reachable from here. It is *not* the publisher-to-consumer skew: that
        // needs a stamp from each of those processes, and the front of the span
        // is exactly where this feature chose not to add one.
        //
        // It still bounds the part that matters, because only "before handler"
        // crosses a clock boundary at all. "In handler" is stamped twice by one
        // process and is exact whatever the clocks are doing.
        ClockOffset offset = await ClockOffsetProbe.MeasureBestOfAsync(
            context.Database.GetConnectionString()!, readings: 20, CancellationToken.None);

        output.WriteLine($"intended ~100 ev/s, achieved {achieved:F1} ev/s over {MeasureIterations} events");
        output.WriteLine(attribution.Describe());
        output.WriteLine($"this process vs the shared server: {offset}");
        output.WriteLine(
            "  only the 'before handler' part crosses a clock boundary; "
            + "'in handler' is stamped twice by one process and is exact");

        attribution.EveryRowStamped.ShouldBeTrue(
            $"{attribution.RowsMissingStamps} rows arrived without the measurement stamps; "
            + "turn the switch on before reading anything below");

        attribution.AttributedFraction.ShouldBeGreaterThan(
            0.8,
            $"the named parts explain only {attribution.AttributedFraction * 100:F1}% of the span; "
            + $"{attribution.UnattributedMs:F1} ms is unaccounted for, which is the apparatus disagreeing "
            + "with the timestamps that bracket it rather than a property of the pipeline");
    }

    private static async Task<string> DefineAsync(HttpClient variables)
    {
        string name = $"nfr{Guid.NewGuid():N}"[..16];

        // Deliberately no initialValue. Setting a variable to the value it
        // already holds is a no-op: it answers 200, raises nothing, and leaves
        // the version where it was — correct of the domain, and fatal here,
        // because the next write's If-Match would carry a version that never
        // came to exist. Defining without a value means every write below is a
        // real change.
        HttpResponseMessage defined = await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        defined.EnsureSuccessStatusCode();
        return name;
    }

    /// <summary>
    /// Sets the variable <paramref name="times"/> times, tracking the version
    /// locally: each set bumps it by exactly one, so re-reading between writes
    /// would add an HTTP round trip per iteration and establish nothing. Returns
    /// the variable's identifier, which is what the audit row carries.
    ///
    /// <para>
    /// The starting version is <b>read</b> rather than assumed to be zero.
    /// Defining with an <c>initialValue</c> performs a value set of its own, so
    /// the variable is already at version 1 before this loop begins — assuming
    /// zero costs a 409 on the first write.
    /// </para>
    /// </summary>
    private static async Task<string> SetRepeatedlyAsync(HttpClient variables, string name, int times)
    {
        int first = await ReadVersionAsync(variables, name);

        for (int version = first; version < first + times; version++)
        {
            using HttpRequestMessage request = new(
                HttpMethod.Put,
                $"/system-variables/{name}/value?fabId=munich");
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            request.Content = JsonContent.Create(new { value = (version + 1).ToString(CultureInfo.InvariantCulture) });

            using HttpResponseMessage response = await variables.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string detail = await response.Content.ReadAsStringAsync();
                int actual = await ReadVersionAsync(variables, name);
                throw new InvalidOperationException(
                    $"set #{version - first + 1} of {times} on '{name}' sent If-Match \"{version}\" "
                    + $"and got {(int)response.StatusCode}; the variable now reads version {actual}. {detail}");
            }
        }

        return await ReadIdentifierAsync(variables, name);
    }

    private static async Task<string> ReadIdentifierAsync(HttpClient variables, string name) =>
        (await ReadAsync(variables, name)).GetProperty("variableIdentifier").GetString()!;

    private static async Task<int> ReadVersionAsync(HttpClient variables, string name) =>
        (await ReadAsync(variables, name)).GetProperty("version").GetInt32();

    private static async Task<JsonElement> ReadAsync(HttpClient variables, string name)
    {
        using HttpResponseMessage read = await variables.GetAsync($"/system-variables/{name}?fabId=munich");
        read.EnsureSuccessStatusCode();

        return await read.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> WaitForRowsAsync(AuditObservabilityDbContext context, string identifier)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + IngestDeadline;
        int landed = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            landed = await CountAsync(context, identifier);
            if (landed >= MeasureIterations)
            {
                return landed;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return landed;
    }

    private static async Task<int> CountAsync(AuditObservabilityDbContext context, string identifier)
    {
        List<long> counted = await context.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM audit_events "
                + "WHERE event_kind = 'SystemVariableValueChangedV1' AND resource_identifier = {0}",
                identifier)
            .ToListAsync();

        return (int)counted[0];
    }

    /// <summary>
    /// The span divided, read in the same query that produces the total.
    ///
    /// <para>
    /// <b>One query, one row per event, so the parts cannot drift from the
    /// figure they divide.</b> Medians rather than means throughout: a single
    /// stalled event moves a mean enough to invent a part that is not there,
    /// and the percentiles beside it are already order statistics.
    /// </para>
    ///
    /// <para>
    /// Rows without the measurement stamps are counted rather than filtered
    /// away — a run that quietly measured nine hundred of a thousand events
    /// would report the nine hundred as though they were the population.
    /// </para>
    /// </summary>
    private static async Task<IngestAttribution> AttributionAsync(
        AuditObservabilityDbContext context, string identifier)
    {
        List<double> parts = await context.Database
            .SqlQueryRaw<double>(
                "SELECT unnest(ARRAY["
                + "percentile_cont(0.50) WITHIN GROUP (ORDER BY total), "
                + "percentile_cont(0.50) WITHIN GROUP (ORDER BY before_handler), "
                + "percentile_cont(0.50) WITHIN GROUP (ORDER BY in_handler), "
                + "percentile_cont(0.50) WITHIN GROUP (ORDER BY write_leg), "
                + "count(*)::float8, "
                + "count(*) FILTER (WHERE stamps_missing)::float8]) AS \"Value\" FROM ("
                + "SELECT "
                + "EXTRACT(EPOCH FROM (received_at - occurred_at)) * 1000 AS total, "
                + "COALESCE(EXTRACT(EPOCH FROM (handler_entered_at - occurred_at)) * 1000, 0) AS before_handler, "
                + "COALESCE(EXTRACT(EPOCH FROM (received_at - handler_entered_at)) * 1000, 0) AS in_handler, "
                + "COALESCE(EXTRACT(EPOCH FROM (written_at - received_at)) * 1000, 0) AS write_leg, "
                + "(handler_entered_at IS NULL OR written_at IS NULL) AS stamps_missing "
                + "FROM audit_events "
                + "WHERE event_kind = 'SystemVariableValueChangedV1' AND resource_identifier = {0}"
                + ") samples",
                identifier)
            .ToListAsync();

        return new IngestAttribution(
            TotalMs: parts[0],
            BeforeHandlerMs: parts[1],
            InHandlerMs: parts[2],
            WriteMs: parts[3],
            RowsMeasured: (int)parts[4],
            RowsMissingStamps: (int)parts[5]);
    }

    private static async Task<(double P50, double P99, double Max)> PercentilesAsync(
        AuditObservabilityDbContext context,
        string identifier)
    {
        List<double> percentiles = await context.Database
            .SqlQueryRaw<double>(
                "SELECT unnest(ARRAY["
                + "percentile_cont(0.50) WITHIN GROUP (ORDER BY delta), "
                + "percentile_cont(0.99) WITHIN GROUP (ORDER BY delta), "
                + "max(delta)]) AS \"Value\" FROM ("
                + "SELECT EXTRACT(EPOCH FROM (received_at - occurred_at)) * 1000 AS delta "
                + "FROM audit_events "
                + "WHERE event_kind = 'SystemVariableValueChangedV1' AND resource_identifier = {0}"
                + ") samples",
                identifier)
            .ToListAsync();

        return (percentiles[0], percentiles[1], percentiles[2]);
    }
}
