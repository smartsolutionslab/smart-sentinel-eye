using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 213 T003 — issue #2428. The batch refusal (<c>:194</c>) dead-letters a
/// clock-skewed device on its first pass, with no retry at all, yet today's
/// row reads <c>"not storable after 00:05:00 of retrying"</c> — a sentence
/// built from a configuration value, not from anything this delivery did.
///
/// <para>
/// <see cref="PoisonDeliveryEscapeIntegrationTests"/> already covers the one
/// site where that sentence is true: a delivery that genuinely exhausts the
/// retry window (<c>:215</c>), via a dropped partition. Its
/// <c>error.ShouldContain("not storable")</c> assertion must keep passing
/// unmodified — this file does not touch it. What is missing is the sibling
/// case: a delivery refused outright, never retried, that this spec makes
/// name the rule it broke instead.
/// </para>
///
/// <para>
/// The auth scenario is not new behaviour — <c>ListDeadLettersQueryHandler</c>
/// already scopes rows by fab — but it is re-asserted here because the reason
/// now embeds a device's <c>occurredAt</c>, so the row carries more than it
/// did before and the scoping needs to keep excluding it from an operator who
/// does not hold the fab.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class DeadLetterReasonIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const string SkewedFab = "munich";
    private const string SkewedDevice = "clock-drift-1";

    // Seeded dev-only operator, group /fabs/hamburg only (AppHost realm).
    // DeadLetterFabScopingIntegrationTests uses its own single-fab operator
    // (op-dresden@dresden.test) — a different, also-seeded user, not this one.
    private const string HamburgOperator = "op-hamburg@hamburg.test";
    private const string OperatorPassword = "Operator1234";

    [Fact]
    public async Task A_clock_skewed_delivery_records_the_rule_it_broke_not_the_retry_window()
    {
        string kind = $"ClockDrift{Guid.CreateVersion7():N}"[..20];
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow.AddHours(1);

        await PublishAsync($"fab/{SkewedFab}/plc/{SkewedDevice}", kind, occurredAt);
        output.WriteLine($"published clock-skewed delivery, kind={kind}, occurredAt={occurredAt:O}");

        // Step 2+3 — the row exists and names the rule, not the window.
        string error = await WaitForDeadLetterAsync(kind, TimeSpan.FromMinutes(2));
        output.WriteLine($"dead letter error: {error}");

        error.ShouldNotBeNullOrEmpty("the skewed delivery was never dead-lettered");
        error.ShouldContain("EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE");
        error.ShouldNotContain(
            "of retrying",
            customMessage: "this delivery was refused on its first pass and never retried at all — " +
            "the row is still carrying the fabricated window sentence");

        // Step 4 — a further wait, then re-count. A reason that is over-long
        // or that throws while being recorded lands the delivery back on
        // `carried` instead of being acknowledged (spec 213's "bad request"
        // scenario), and MQTT QoS 1 redelivers it — which would show up here
        // as a second row for the same kind. One row after this wait is the
        // observable proof the delivery was acknowledged, not carried.
        await Task.Delay(TimeSpan.FromSeconds(30));
        long rows = await CountDeadLettersAsync(kind);
        rows.ShouldBe(1, "the delivery was carried rather than acknowledged — a second row appeared");

        // Step 5 — an operator holding only fab hamburg must not see the
        // munich row, even though its reason now embeds the device's
        // occurredAt. A positive control alongside it: the same operator's
        // own hamburg row, also clock-skewed, must still be visible — an
        // empty or broken listing would satisfy the negative assertion alone
        // without the scoping mechanism actually working (DeadLetterFabScopingIntegrationTests
        // asserts both directions for the same reason).
        string hamburgKind = $"ClockDrift{Guid.CreateVersion7():N}"[..20];
        await PublishAsync("fab/hamburg/plc/clock-drift-2", hamburgKind, occurredAt);
        output.WriteLine($"published hamburg control delivery, kind={hamburgKind}");
        await WaitForDeadLetterAsync(hamburgKind, TimeSpan.FromMinutes(2));

        using HttpClient hamburgEvents = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", HamburgOperator, OperatorPassword);
        HttpResponseMessage listed = await hamburgEvents.GetAsync("/events/dead-letters?limit=1000");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());

        JsonElement listedRows = await listed.Content.ReadFromJsonAsync<JsonElement>();
        IReadOnlyList<JsonElement> rowElements = [.. listedRows.EnumerateArray()];
        output.WriteLine($"hamburg operator's dead-letter listing: {rowElements.Count} row(s)");

        bool sawOwnHamburgRow = rowElements.Any(row =>
            row.GetProperty("rawPayload").GetString()?.Contains(hamburgKind, StringComparison.Ordinal) == true);
        sawOwnHamburgRow.ShouldBeTrue(
            "an operator holding fab hamburg could not see their own hamburg row — " +
            "the listing itself is broken, which would make the munich exclusion below meaningless");

        bool munichRowVisibleToHamburg = rowElements.Any(row =>
            row.GetProperty("rawPayload").GetString()?.Contains(kind, StringComparison.Ordinal) == true);
        munichRowVisibleToHamburg.ShouldBeFalse(
            "an operator holding only fab hamburg saw a munich row — the richer reason widened fab scoping");
    }

    /// <summary>
    /// The dead letter is matched on the payload rather than the topic, same
    /// as <see cref="PoisonDeliveryEscapeIntegrationTests"/>: the escape
    /// records the delivery it gave up on, and the kind is the only thing in
    /// it that identifies this test's run.
    /// </summary>
    private async Task<string> WaitForDeadLetterAsync(string kind, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using EventIngestionDbContext database =
                await aspire.CreateEventIngestionDbContextAsync();
            string[] found = await database.Database
                .SqlQueryRaw<string>(
                    "SELECT error AS \"Value\" FROM dead_letters WHERE raw_payload LIKE {0}",
                    $"%{kind}%")
                .ToArrayAsync();

            if (found.Length > 0)
            {
                return found[0];
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        return string.Empty;
    }

    private async Task<long> CountDeadLettersAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM dead_letters WHERE raw_payload LIKE {0}", $"%{kind}%")
            .SingleAsync();
    }

    private async Task PublishAsync(string topic, string kind, DateTimeOffset occurredAt)
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
        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        MqttClientConnectResult connected = await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId($"{SimulatorClientId}-{Guid.CreateVersion7():N}")
            .WithCredentials(SimulatorClientId, jwt)
            .WithTcpServer(broker.Host, broker.Port)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build());
        connected.ResultCode.ShouldBe(MqttClientConnectResultCode.Success);

        MqttClientPublishResult published = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Payload(kind, occurredAt))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        published.IsSuccess.ShouldBeTrue($"the broker refused publish to {topic}");

        await client.DisconnectAsync();
    }

    private static string Payload(string kind, DateTimeOffset occurredAt) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = occurredAt.ToString("O", CultureInfo.InvariantCulture),
        // The kind is repeated inside the payload because the dead letter
        // keeps the raw payload, and that is all this test has to recognise
        // its own delivery by.
        payload = new { note = $"spec 213 clock-skew reason {kind}" },
    });
}
