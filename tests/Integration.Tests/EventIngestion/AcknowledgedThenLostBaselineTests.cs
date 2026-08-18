using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 020 T001 — records what "accepted" is worth today, <b>before</b> the
/// acknowledgement is moved. Temporary: deleted once the observations are on
/// the PR.
///
/// <para>
/// The decisive moment is not that the event fails to store — spec 019 already
/// logs that. It is what happens <b>after</b> the obstacle is removed. The
/// broker was told we had the event on arrival, so it discarded its copy; when
/// storage becomes possible again, nothing brings the event back. There is no
/// retry, no redelivery, and no record beyond one log line.
/// </para>
///
/// <para>
/// Asserts almost nothing on purpose. It reports.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class AcknowledgedThenLostBaselineTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const string HamburgOperator = "op-hamburg@hamburg.test";
    private const string OperatorPassword = "Operator1234";

    [Fact]
    public async Task Record_that_an_acknowledged_event_never_comes_back()
    {
        string kind = $"Baseline{Guid.CreateVersion7():N}"[..20];

        // Persistence fails for hamburg alone; the database itself stays
        // perfectly healthy, so this isolates "the write failed" from "the
        // database is away".
        await ExecuteAsync("DROP TABLE IF EXISTS events_hamburg;");
        output.WriteLine("dropped events_hamburg — writes for this fab will now fail");

        await PublishAsync("fab/hamburg/plc/dev-1", Payload(kind));
        output.WriteLine("published one event over the broker for fab hamburg");

        await Task.Delay(TimeSpan.FromSeconds(6));
        output.WriteLine($"stored after the failed write: {await CountAsync(kind)}");

        foreach (string line in aspire.RecentLogs("event-ingestion").Split('\n'))
        {
            if (line.Contains("No event storage for fab", StringComparison.Ordinal)
                || line.Contains("dispatch faulted", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine($"  log: {line.Trim()}");
            }
        }

        // The obstacle is removed. If the broker still held the delivery, it
        // would be redelivered now and the event would appear.
        DateTime now = DateTime.UtcNow;
        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS events_hamburg PARTITION OF events
                FOR VALUES IN ('hamburg') PARTITION BY RANGE (ingested_at);
            """);
        await ExecuteAsync(
            $"""
            CREATE TABLE IF NOT EXISTS "events_hamburg_{now:yyyyMM}" PARTITION OF events_hamburg
                FOR VALUES FROM ('{now:yyyy-MM}-01') TO ('{now.AddMonths(1):yyyy-MM}-01');
            """);
        output.WriteLine("restored events_hamburg — storage is possible again");

        await Task.Delay(TimeSpan.FromSeconds(15));
        output.WriteLine($"stored after storage was restored: {await CountAsync(kind)}");
        output.WriteLine("  ^ still zero means the broker had already been told we had it");

        // The direct path, for the same reason.
        using HttpClient hamburg = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", HamburgOperator, OperatorPassword);
        await ExecuteAsync("DROP TABLE IF EXISTS events_hamburg;");
        await Task.Delay(TimeSpan.FromSeconds(35)); // let the spec 019 readiness cache notice

        string httpKind = $"Http{Guid.CreateVersion7():N}"[..20];
        HttpResponseMessage submitted = await hamburg.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "baseline-device",
            kind = httpKind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = "spec 020 baseline" },
        });
        output.WriteLine($"POST /events/manual with storage unavailable -> {(int)submitted.StatusCode}");
        output.WriteLine($"  stored: {await CountAsync(httpKind)}");

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS events_hamburg PARTITION OF events
                FOR VALUES IN ('hamburg') PARTITION BY RANGE (ingested_at);
            """);
        await ExecuteAsync(
            $"""
            CREATE TABLE IF NOT EXISTS "events_hamburg_{now:yyyyMM}" PARTITION OF events_hamburg
                FOR VALUES FROM ('{now:yyyy-MM}-01') TO ('{now.AddMonths(1):yyyy-MM}-01');
            """);
    }

    private async Task<long> CountAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
#pragma warning disable EF1002
        await database.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private static string Payload(string kind) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = DateTimeOffset.UtcNow,
        payload = new { note = "spec 020 baseline" },
    });

    private async Task PublishAsync(string topic, string payload)
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
        string jwt = (await token.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;

        Uri broker = aspire.App.GetEndpoint("mosquitto", "mqtt");
        using IMqttClient client = new MqttFactory().CreateMqttClient();
        MqttClientConnectResult connected = await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId($"{SimulatorClientId}-{Guid.CreateVersion7():N}")
            .WithCredentials(SimulatorClientId, jwt)
            .WithTcpServer(broker.Host, broker.Port)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build());
        output.WriteLine($"broker connect: {connected.ResultCode}");

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        await client.DisconnectAsync();
    }
}
