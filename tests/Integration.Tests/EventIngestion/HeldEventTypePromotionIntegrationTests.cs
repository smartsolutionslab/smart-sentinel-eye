using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Integration.Tests.Identity;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventSourceModeApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// T014 (spec 317, #2325) — US2/US3, FR-005/FR-007/FR-008. Listing and
/// promotion against the real stack: a declared discovery pair holds an
/// unregistered kind, and registering it promotes that fab's held rows —
/// convergently, including the already-registered (409) path (A7).
/// </summary>
[Collection(AspireCollection.Name)]
public class HeldEventTypePromotionIntegrationTests(AspireFixture aspire)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";

    private readonly RealmProbe realm = new(aspire);

    private static readonly string[] EventSourceScopes =
        ["sse-identity", "sse-groups", "sse-audience", "sse.events.write"];

    [Fact]
    public async Task Registering_a_held_kind_promotes_only_that_fabs_rows_and_admits_the_next_event()
    {
        string kind = EventTypeRegistryApi.UniqueKind();
        using HttpClient berlin = await ClientFor(BerlinOperator);
        using HttpClient munich = await ClientFor(MunichOperator);

        try
        {
            await SetAsync(berlin, "manual", "discovery");
            await SetAsync(munich, "manual", "discovery");

            // Three held rows in berlin, one in munich — all for the same kind.
            for (int i = 0; i < 3; i++)
            {
                HttpResponseMessage held = await IngestManualAsync(berlin, kind);
                held.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Diagnose(held));
            }
            HttpResponseMessage munichHeld = await IngestManualAsync(munich, kind);
            munichHeld.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Diagnose(munichHeld));

            await WaitForHeldCountAsync(berlin, kind, 3, TimeSpan.FromMinutes(2));
            await WaitForHeldCountAsync(munich, kind, 1, TimeSpan.FromMinutes(2));

            HttpResponseMessage registered = await EventTypeRegistryApi.RegisterAsync(berlin, kind);
            registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(registered));

            int berlinPromoted = await WaitForPromotedCountAsync(berlin, kind, 3, TimeSpan.FromMinutes(2));
            berlinPromoted.ShouldBe(3, "every berlin row for this kind must be promoted");

            int munichHeldAfter = await CountByStateAsync(munich, kind, "Held");
            munichHeldAfter.ShouldBe(1, "a different fab's held row for the same kind must be untouched");

            HttpResponseMessage nextEvent = await IngestManualAsync(berlin, kind);
            nextEvent.StatusCode.ShouldBe(
                HttpStatusCode.Created,
                await Diagnose(nextEvent) + Environment.NewLine + "registering the kind must open the door");
        }
        finally
        {
            await UndeclareAsync(berlin, "manual");
            await UndeclareAsync(munich, "manual");
        }
    }

    /// <summary>
    /// FR-003's predicate: the promotion update's <c>WHERE</c> clause keys on
    /// <c>reason = UnknownEventType AND state = Held</c>, not merely on
    /// <c>(fab, kind)</c> — so a <c>Refused</c> row for the same kind, held by
    /// a different, strict source in the same fab, must survive a promotion
    /// untouched.
    /// </summary>
    [Fact]
    public async Task A_refused_row_for_the_same_fab_and_kind_is_not_promoted()
    {
        string kind = EventTypeRegistryApi.UniqueKind();
        using HttpClient berlin = await ClientFor(BerlinOperator);

        try
        {
            await SetAsync(berlin, "manual", "discovery");
            await SetAsync(berlin, "inference", "strict");

            HttpResponseMessage held = await IngestManualAsync(berlin, kind);
            held.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Diagnose(held));

            await PublishAsync($"fab/berlin/inference/promotion-probe-1", kind);

            await WaitForHeldCountAsync(berlin, kind, 1, TimeSpan.FromMinutes(2));
            await WaitForReasonCountAsync(berlin, kind, "Refused", 1, TimeSpan.FromMinutes(2));

            HttpResponseMessage registered = await EventTypeRegistryApi.RegisterAsync(berlin, kind);
            registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(registered));

            await WaitForPromotedCountAsync(berlin, kind, 1, TimeSpan.FromMinutes(2));
            int refusedStillHeld = await CountByReasonAndStateAsync(berlin, kind, "Refused", "Held");
            refusedStillHeld.ShouldBe(1, "a Refused row must never be promoted, whatever its (fab, kind)");
        }
        finally
        {
            await UndeclareAsync(berlin, "manual");
            await UndeclareAsync(berlin, "inference");
        }
    }

    /// <summary>
    /// FR-007's convergence, A7: the 409-already-registered path must run the
    /// same promotion as a successful registration — closing the gap a batch
    /// assessed just before the original registration committed would
    /// otherwise leave open. The race itself is not reproduced; the row it
    /// would leave behind is seeded directly, and this asserts the 409 path
    /// promotes it.
    /// </summary>
    [Fact]
    public async Task A_lingering_held_row_for_an_already_registered_kind_is_promoted_by_a_409_registration()
    {
        string kind = EventTypeRegistryApi.UniqueKind();
        using HttpClient berlin = await ClientFor(BerlinOperator);

        HttpResponseMessage registered = await EventTypeRegistryApi.RegisterAsync(berlin, kind);
        registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(registered));

        await SeedHeldRowAsync("berlin", kind);
        await WaitForHeldCountAsync(berlin, kind, 1, TimeSpan.FromMinutes(2));

        HttpResponseMessage retry = await EventTypeRegistryApi.RegisterAsync(berlin, kind);
        retry.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Diagnose(retry));
        (await EventTypeRegistryApi.TitleOfAsync(retry)).ShouldBe("EVENT_TYPE_ALREADY_REGISTERED");

        await WaitForPromotedCountAsync(berlin, kind, 1, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Bad_filter_values_answer_400()
    {
        using HttpClient berlin = await ClientFor(BerlinOperator);

        HttpResponseMessage badReason = await berlin.GetAsync("/events/dead-letters?reason=Unknown");
        badReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(badReason));
        (await TitleOfAsync(badReason)).ShouldBe("DEAD_LETTER_INVALID_FILTER");

        HttpResponseMessage badState = await berlin.GetAsync("/events/dead-letters?state=Released");
        badState.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(badState));
        (await TitleOfAsync(badState)).ShouldBe("DEAD_LETTER_INVALID_FILTER");
    }

    /// <summary>
    /// Spec 143 FR-010's gotcha, re-asserted for the promotion path (spec.md
    /// §4): an event source must not be able to promote its own unknown type
    /// into the registry.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_only_events_write_cannot_promote_a_held_kind()
    {
        string kind = EventTypeRegistryApi.UniqueKind();
        using HttpClient berlin = await ClientFor(BerlinOperator);

        try
        {
            await SetAsync(berlin, "manual", "discovery");
            HttpResponseMessage held = await IngestManualAsync(berlin, kind);
            held.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Diagnose(held));
            await WaitForHeldCountAsync(berlin, kind, 1, TimeSpan.FromMinutes(2));

            string clientId = $"event-source-promote-probe-{Guid.CreateVersion7():N}";
            await PlantEventSourceClientAsync(clientId);
            try
            {
                using HttpClient source = await EventSourceClientAsync(clientId);

                HttpResponseMessage refused = await EventTypeRegistryApi.RegisterAsync(source, kind);

                refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await Diagnose(refused));
            }
            finally
            {
                await realm.DeleteAsync(clientId, CancellationToken.None);
            }

            int stillHeld = await CountByStateAsync(berlin, kind, "Held");
            stillHeld.ShouldBe(1, "a 403 must not have promoted the row");
        }
        finally
        {
            await UndeclareAsync(berlin, "manual");
        }
    }

    private static Task<HttpResponseMessage> IngestManualAsync(HttpClient client, string kind) =>
        client.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "promotion-probe",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = $"spec 317 promotion probe {kind}" },
        });

    private async Task PublishAsync(string topic, string kind)
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
                .WithPayload(JsonSerializer.Serialize(new
                {
                    eventId = Guid.CreateVersion7(),
                    kind,
                    occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    payload = new { note = $"spec 317 promotion probe (refused) {kind}" },
                }))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        published.IsSuccess.ShouldBeTrue($"the broker refused publish to {topic}");

        await client.DisconnectAsync();
    }

    private async Task<int> CountByReasonAsync(HttpClient client, string kind, string reason)
    {
        HttpResponseMessage listed = await client.GetAsync($"/events/dead-letters?reason={reason}&limit=1000");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await Diagnose(listed));
        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return rows.EnumerateArray().Count(row =>
            row.GetProperty("kind").GetString() == kind);
    }

    private async Task<int> CountByStateAsync(HttpClient client, string kind, string state)
    {
        HttpResponseMessage listed = await client.GetAsync($"/events/dead-letters?state={state}&limit=1000");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await Diagnose(listed));
        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return rows.EnumerateArray().Count(row => row.GetProperty("kind").GetString() == kind);
    }

    private async Task<int> CountByReasonAndStateAsync(HttpClient client, string kind, string reason, string state)
    {
        HttpResponseMessage listed = await client.GetAsync(
            $"/events/dead-letters?reason={reason}&state={state}&limit=1000");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await Diagnose(listed));
        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return rows.EnumerateArray().Count(row => row.GetProperty("kind").GetString() == kind);
    }

    private async Task<int> WaitForHeldCountAsync(HttpClient client, string kind, int expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            int count = await CountByReasonAndStateAsync(client, kind, "UnknownEventType", "Held");
            if (count >= expected || DateTime.UtcNow >= deadline)
            {
                return count;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private async Task<int> WaitForPromotedCountAsync(HttpClient client, string kind, int expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            int count = await CountByReasonAndStateAsync(client, kind, "UnknownEventType", "Promoted");
            if (count >= expected || DateTime.UtcNow >= deadline)
            {
                return count;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private async Task<int> WaitForReasonCountAsync(HttpClient client, string kind, string reason, int expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            int count = await CountByReasonAsync(client, kind, reason);
            if (count >= expected || DateTime.UtcNow >= deadline)
            {
                return count;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>
    /// Seeds a <c>Held</c>, <c>UnknownEventType</c> row directly — standing in
    /// for the row a batch assessed just before a registration committed
    /// would have written (the race itself is not reproduced; see this
    /// class's remarks).
    /// </summary>
    private async Task SeedHeldRowAsync(string fab, string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dead_letters (dead_letter_id, topic, fab, raw_payload, error, reason, kind, state, rejected_at, version)
            VALUES ({Guid.CreateVersion7()}, {$"event/{fab}/manual/promotion-race-probe"}, {fab}, {"{}"},
                    {"EVENT_TYPE_HELD: race probe"}, {"UnknownEventType"}, {kind}, {"Held"}, {DateTimeOffset.UtcNow}, 0)
            """);
    }

    private async Task PlantEventSourceClientAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/clients",
            new
            {
                clientId,
                enabled = true,
                publicClient = true,
                standardFlowEnabled = false,
                serviceAccountsEnabled = false,
                directAccessGrantsEnabled = true,
                defaultClientScopes = EventSourceScopes,
                optionalClientScopes = Array.Empty<string>(),
            },
            CancellationToken.None);

        created.IsSuccessStatusCode.ShouldBeTrue(
            $"planting '{clientId}' answered {(int)created.StatusCode}: "
            + await created.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> EventSourceClientAsync(string clientId)
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(clientId, DresdenOperator, DresdenOperatorPassword, "openid");

        HttpClient client = aspire.CreateServiceClient(ResourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        return client;
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, username, PasswordFor(username));

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
