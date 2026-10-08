using System.Globalization;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using SmartSentinelEye.Integration.Tests.Fixtures;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventSourceModeApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// T011 (spec 317, #2325) — spec.md §4/§6, US1. A declared discovery pair's
/// actual effect on both ingest insertion points, against the real stack —
/// the quarantine half of decision 018, mirroring spec 269's
/// <c>StrictSourceIngestIntegrationTests</c>.
///
/// <para>
/// Uses <c>berlin</c> + <c>manual</c> (HTTP) and <c>berlin</c> + <c>inference</c>
/// (MQTT), the same reserved pairs <c>StrictSourceIngestIntegrationTests</c>
/// uses — safe for the same reason that file gives: every integration test
/// class shares one <c>AspireCollection</c> and runs serially, and every test
/// below undeclares what it declared in a <c>finally</c> (FR-015).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class DiscoverySourceHoldsIntegrationTests(AspireFixture aspire)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const string Fab = "berlin";

    [Fact]
    public async Task A_discovery_manual_source_holds_an_unregistered_kind_with_202()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            await SetAsync(berlin, "manual", "discovery");
            string kind = UniqueKind();

            HttpResponseMessage held = await IngestManualAsync(berlin, kind);

            held.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Diagnose(held));
            (await CountStoredAsync(berlin, kind)).ShouldBe(0, "a held event must not be stored");

            JsonElement row = await WaitForHeldRowAsync(berlin, kind, TimeSpan.FromMinutes(2));
            row.GetProperty("fab").GetString().ShouldBe("berlin");
            row.GetProperty("reason").GetString().ShouldBe("UnknownEventType");
            row.GetProperty("state").GetString().ShouldBe("Held");
        }
        finally
        {
            await UndeclareAsync(berlin, "manual");
        }
    }

    [Fact]
    public async Task A_discovery_manual_source_admits_a_registered_kind_with_201()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            string kind = UniqueKind();
            HttpResponseMessage registered = await EventTypeRegistryApi.RegisterAsync(berlin, kind);
            registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(registered));

            await SetAsync(berlin, "manual", "discovery");

            HttpResponseMessage created = await IngestManualAsync(berlin, kind);

            created.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(created));
            (await CountStoredAsync(berlin, kind)).ShouldBe(1);
        }
        finally
        {
            await UndeclareAsync(berlin, "manual");
        }
    }

    /// <summary>
    /// The one case the HTTP tests cannot stand in for (spec.md §6's closing
    /// note): if the batch path's hold branch were ever dropped, this is the
    /// test that would notice — every HTTP test above would stay green
    /// regardless (plan.md §9 counterfactual 2).
    /// </summary>
    [Fact]
    public async Task A_discovery_inference_source_holds_one_and_stores_the_other_in_a_mixed_MQTT_burst()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            await SetAsync(berlin, "inference", "discovery");
            string registeredKind = UniqueKind();
            string unknownKind = UniqueKind();
            HttpResponseMessage registered = await EventTypeRegistryApi.RegisterAsync(berlin, registeredKind);
            registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(registered));

            await PublishAsync($"fab/{Fab}/inference/discovery-mqtt-1", registeredKind);
            await PublishAsync($"fab/{Fab}/inference/discovery-mqtt-2", unknownKind);

            int storedCount = await WaitForStoredCountAsync(berlin, registeredKind, TimeSpan.FromMinutes(2));
            storedCount.ShouldBe(1, "the registered kind must be stored");

            JsonElement heldRow = await WaitForHeldRowAsync(berlin, unknownKind, TimeSpan.FromMinutes(2));
            heldRow.GetProperty("state").GetString().ShouldBe("Held");
            (await CountStoredAsync(berlin, unknownKind)).ShouldBe(0, "the held kind must not be stored");
        }
        finally
        {
            await UndeclareAsync(berlin, "inference");
        }
    }

    /// <summary>Q1, option A (spec.md §4's default scenario) — asserted here too, alongside the hold cases.</summary>
    [Fact]
    public async Task An_undeclared_source_still_admits_an_unknown_kind_with_201()
    {
        using HttpClient dresden = await aspire.CreateAuthenticatedClientAsync(
            ResourceName, DresdenOperator, OperatorPassword);
        string kind = UniqueKind();

        HttpResponseMessage created = await IngestManualAsync(dresden, kind);

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(created));
    }

    private static Task<HttpResponseMessage> IngestManualAsync(HttpClient client, string kind) =>
        client.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "discovery-hold-probe",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = $"spec 317 discovery-hold probe {kind}" },
        });

    private async Task<int> CountStoredAsync(HttpClient client, string kind)
    {
        HttpResponseMessage listed = await client.GetAsync($"/events?fabId={Fab}&kind={kind}");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await Diagnose(listed));
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return page.GetProperty("items").GetArrayLength();
    }

    private async Task<int> WaitForStoredCountAsync(HttpClient client, string kind, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            int count = await CountStoredAsync(client, kind);
            if (count > 0 || DateTime.UtcNow >= deadline)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private async Task<JsonElement> WaitForHeldRowAsync(HttpClient client, string kind, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            HttpResponseMessage listed = await client.GetAsync("/events/dead-letters?reason=UnknownEventType&limit=1000");
            listed.StatusCode.ShouldBe(HttpStatusCode.OK, await Diagnose(listed));
            JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.GetProperty("kind").GetString() == kind)
                {
                    return row;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"no held dead letter for kind '{kind}' appeared within {timeout}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

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
                    payload = new { note = $"spec 317 discovery-hold MQTT probe {kind}" },
                }))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        published.IsSuccess.ShouldBeTrue($"the broker refused publish to {topic}");

        await client.DisconnectAsync();
    }

    private Task<HttpClient> BerlinClientAsync() =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, BerlinOperator, OperatorPassword);

    private static string UniqueKind() => $"DiscoveryHold{Guid.NewGuid():N}"[..24];

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
