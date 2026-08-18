using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 020 T022–T024 — quickstart step 5. The requirement most likely to be
/// broken quietly, and the one the spec insists is measured rather than argued.
///
/// <para>
/// Three numbers from one run, because they are three properties of the same
/// load and measuring them separately would let each be taken at a different
/// pressure: sustained throughput (FR-010, SC-005), arrival-to-visible latency
/// against the ≤ 200 ms leg of the end-to-end budget (FR-012, SC-006,
/// constitution §IV), and per-source ordering under that load (FR-011).
/// </para>
///
/// <para>
/// Excluded from CI by its category. A thirty-second saturating burst on a
/// shared runner measures the runner, and a number that measures the runner
/// would be quoted as if it measured this code.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
[Trait("Category", "Measurement")]
public class IngestThroughputMeasurementTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";

    /// <summary>Spec 006 sized this path for this rate.</summary>
    private const int TargetRatePerSecond = 5_000;
    private const int DurationSeconds = 30;
    private const int Publishers = 8;

    /// <summary>The leg of the end-to-end budget this path spends.</summary>
    private static readonly TimeSpan EventToStateBudget = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Sustained_load_keeps_its_rate_its_latency_and_its_order()
    {
        string kind = $"Load{Guid.CreateVersion7():N}"[..20];
        int perPublisher = TargetRatePerSecond * DurationSeconds / Publishers;

        Stopwatch elapsed = Stopwatch.StartNew();
        int publishedCount = await PublishConcurrentlyAsync(kind, perPublisher);
        TimeSpan publishTook = elapsed.Elapsed;

        output.WriteLine(
            $"published {publishedCount} in {publishTook.TotalSeconds:F1}s "
            + $"= {publishedCount / publishTook.TotalSeconds:F0}/s offered");

        long stored = await WaitForDrainAsync(kind, publishedCount, TimeSpan.FromMinutes(5));
        TimeSpan drainTook = elapsed.Elapsed;

        output.WriteLine(
            $"stored {stored} by {drainTook.TotalSeconds:F1}s "
            + $"= {stored / drainTook.TotalSeconds:F0}/s sustained end to end");
        stored.ShouldBe(publishedCount, "the load lost events");

        await ReportLatencyAsync(kind);
        await ReportOrderingAsync(kind);
    }

    /// <summary>
    /// T023. Arrival to visible, per event, from the publish stamp the payload
    /// carries to the moment storage recorded it. Reported at percentiles: a
    /// mean would hide exactly the tail the budget is about.
    /// </summary>
    private async Task ReportLatencyAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        double[] milliseconds = await database.Database
            .SqlQueryRaw<double>(
                """
                SELECT EXTRACT(EPOCH FROM (ingested_at - (payload->>'publishedAt')::timestamptz)) * 1000
                    AS "Value"
                FROM events WHERE kind = {0} ORDER BY 1
                """,
                kind)
            .ToArrayAsync();

        milliseconds.Length.ShouldBeGreaterThan(0);
        output.WriteLine(
            $"arrival→visible ms: p50={Percentile(milliseconds, 0.50):F0} "
            + $"p95={Percentile(milliseconds, 0.95):F0} "
            + $"p99={Percentile(milliseconds, 0.99):F0} "
            + $"max={milliseconds[^1]:F0} "
            + $"(budget for this leg: {EventToStateBudget.TotalMilliseconds:F0} ms)");
    }

    /// <summary>
    /// T024. Per-source FIFO is guaranteed by the channel being FIFO and the
    /// loop being single-reader, and batching is exactly where that quietly
    /// stops being true. Counted as inversions rather than asserted on the first
    /// mismatch, so a failure says how badly rather than merely that.
    /// </summary>
    private async Task ReportOrderingAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();

        for (int publisher = 0; publisher < Publishers; publisher++)
        {
            long[] sequence = await database.Database
                .SqlQueryRaw<long>(
                    """
                    SELECT (payload->>'sequence')::bigint AS "Value"
                    FROM events WHERE kind = {0} AND device_id = {1}
                    ORDER BY ingested_at
                    """,
                    kind,
                    $"load-{publisher}")
                .ToArrayAsync();

            int inversions = 0;
            for (int i = 1; i < sequence.Length; i++)
            {
                if (sequence[i] < sequence[i - 1])
                {
                    inversions++;
                }
            }

            output.WriteLine($"load-{publisher}: {sequence.Length} events, {inversions} out of order");
            inversions.ShouldBe(0, $"per-source order was lost for load-{publisher}");
        }
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        int index = (int)Math.Clamp(Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[index];
    }

    private async Task<long> WaitForDrainAsync(string kind, long expected, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        long stored = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using EventIngestionDbContext database =
                await aspire.CreateEventIngestionDbContextAsync();
            stored = await database.Database
                .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
                .SingleAsync();

            if (stored >= expected)
            {
                return stored;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return stored;
    }

    private async Task<int> PublishConcurrentlyAsync(string kind, int perPublisher)
    {
        string jwt = await TokenAsync();
        Uri broker = aspire.App.GetEndpoint("mosquitto", "mqtt");
        ConcurrentBag<int> counts = [];

        // One client per source, so each source's stream is genuinely ordered by
        // its own publisher. Sharing a client across sources would make the
        // ordering check a test of the test.
        await Parallel.ForAsync(0, Publishers, async (publisher, cancellationToken) =>
            counts.Add(await PublishOneSourceAsync(
                broker, jwt, kind, publisher, perPublisher, cancellationToken)));

        return counts.Sum();
    }

    private static async Task<int> PublishOneSourceAsync(
        Uri broker,
        string jwt,
        string kind,
        int publisher,
        int count,
        CancellationToken cancellationToken)
    {
        using IMqttClient client = new MqttFactory().CreateMqttClient();
        await client.ConnectAsync(
            new MqttClientOptionsBuilder()
                .WithClientId($"{SimulatorClientId}-load-{publisher}-{Guid.CreateVersion7():N}")
                .WithCredentials(SimulatorClientId, jwt)
                .WithTcpServer(broker.Host, broker.Port)
                .WithCleanSession(true)
                .WithTimeout(TimeSpan.FromSeconds(30))
                .Build(),
            cancellationToken);

        string topic = $"fab/munich/plc/load-{publisher}";
        int sent = 0;

        for (int sequence = 0; sequence < count; sequence++)
        {
            await client.PublishAsync(
                new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(Payload(kind, sequence))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                cancellationToken);
            sent++;
        }

        await client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
        return sent;
    }

    private async Task<string> TokenAsync()
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = SimulatorClientId,
            ["client_secret"] = SimulatorClientSecret,
        });
        HttpResponseMessage token = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        token.EnsureSuccessStatusCode();
        return (await token.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    private static string Payload(string kind, int sequence) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        // Both carried in the payload because the stored row is all the
        // measurement has afterwards: publishedAt gives the latency, sequence
        // gives the order.
        payload = new
        {
            publishedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            sequence,
        },
    });
}
