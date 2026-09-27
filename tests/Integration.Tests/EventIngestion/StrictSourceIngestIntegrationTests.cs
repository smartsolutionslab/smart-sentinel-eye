using System.Globalization;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using SmartSentinelEye.Integration.Tests.Fixtures;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventSourceModeApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Phase 4a (spec 269 T003f) — spec.md §4/§6. Strict mode's actual effect on
/// both ingest insertion points, against the real stack. Every case here is
/// red on arrival except
/// <see cref="An_undeclared_source_still_ingests_an_unknown_kind"/> (spec.md
/// §4's default scenario, tasks.md T003f.17).
///
/// <para>
/// Uses <c>berlin</c> + <c>manual</c> (HTTP) and <c>berlin</c> + <c>inference</c>
/// (MQTT) — tasks.md's own candidates. <c>berlin</c> + <c>plc</c> is used
/// only as a control (never declared). <b>Grep result</b>:
/// <c>fab/berlin/inference</c> and <c>fab/berlin/plc</c> are untouched
/// elsewhere in <c>tests/Integration.Tests</c>, but <c>berlin</c> +
/// <c>manual</c> is not free — <c>FabPartitionProvisioningIntegrationTests</c>
/// posts unregistered kinds to <c>/events/manual</c> as <c>op-berlin</c>. That
/// is safe here only because every integration test class shares one
/// <c>AspireCollection</c> and therefore runs serially against the one
/// Aspire stack: each test below unconditionally restores <c>discovery</c> in
/// <c>finally</c> before it finishes, so no other test — including that
/// one — ever observes <c>berlin</c> + <c>manual</c> left strict. A test that
/// died before reaching its <c>finally</c> (a process kill, not an assertion
/// failure) is the one thing this does not protect against; spec.md §6 step
/// 13 is the same trade for the manual verification run.
/// </para>
///
/// <para>
/// <see cref="An_undeclared_source_still_ingests_an_unknown_kind"/> uses
/// <c>dresden</c> + <c>manual</c> instead, and that pair is never declared by
/// any test in this file — declaring it would falsify the very default this
/// test exists to pin (spec.md's counterfactual 3).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StrictSourceIngestIntegrationTests(AspireFixture aspire)
{
    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";
    private const string Fab = "berlin";

    [Fact]
    public async Task A_strict_manual_source_refuses_an_unregistered_kind_with_400()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            await SetAsync(berlin, "manual", "strict");
            string kind = UniqueKind();

            HttpResponseMessage refused = await IngestManualAsync(berlin, kind);

            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(refused));
            (await TitleOfAsync(refused)).ShouldBe("EVENT_TYPE_NOT_REGISTERED");
            (await CountStoredAsync(berlin, kind)).ShouldBe(0, "a refused event must not be stored");
        }
        finally
        {
            await RestoreDiscoveryAsync(berlin, "manual");
        }
    }

    [Fact]
    public async Task A_strict_manual_source_admits_a_registered_kind()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            string kind = UniqueKind();
            HttpResponseMessage registered = await EventTypeRegistryApi.RegisterAsync(berlin, kind);
            registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(registered));

            await SetAsync(berlin, "manual", "strict");

            HttpResponseMessage created = await IngestManualAsync(berlin, kind);

            created.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(created));
        }
        finally
        {
            await RestoreDiscoveryAsync(berlin, "manual");
        }
    }

    /// <summary>
    /// The one case the HTTP tests cannot stand in for (spec.md §6's closing
    /// note): if the batch path's <c>Build</c> ever lost its
    /// <c>verdicts.Refuses</c> check, this is the test that would notice —
    /// every HTTP test above would stay green regardless (plan.md §9
    /// counterfactual 4).
    /// </summary>
    [Fact]
    public async Task A_strict_inference_source_dead_letters_an_unregistered_kind_published_over_MQTT()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            await SetAsync(berlin, "inference", "strict");
            string kind = UniqueKind();

            await PublishAsync($"fab/{Fab}/inference/strict-mqtt-1", kind);

            string reason = await WaitForDeadLetterReasonAsync(berlin, kind, TimeSpan.FromMinutes(2));
            reason.ShouldNotBeNullOrEmpty($"no dead letter matching '{kind}' was found within the wait window");
            reason.ShouldStartWith("EVENT_TYPE_NOT_REGISTERED:");
            (await CountStoredAsync(berlin, kind)).ShouldBe(0);
        }
        finally
        {
            await RestoreDiscoveryAsync(berlin, "inference");
        }
    }

    /// <summary>
    /// The characterisation half of strict mode: a pair left undeclared in the
    /// same fab as a declared-strict source stays open, over MQTT.
    /// </summary>
    [Fact]
    public async Task Strict_on_inference_leaves_plc_in_the_same_fab_open()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            await SetAsync(berlin, "inference", "strict");
            string kind = UniqueKind();

            await PublishAsync($"fab/{Fab}/plc/strict-mqtt-2", kind);

            int count = await WaitForStoredCountAsync(berlin, kind, TimeSpan.FromMinutes(2));
            count.ShouldBe(1, "plc is undeclared in berlin, so an unregistered kind on it must still be admitted");
        }
        finally
        {
            await RestoreDiscoveryAsync(berlin, "inference");
        }
    }

    [Fact]
    public async Task Switching_back_to_discovery_admits_the_unknown_again()
    {
        using HttpClient berlin = await BerlinClientAsync();
        try
        {
            await SetAsync(berlin, "manual", "strict");
            HttpResponseMessage stillRefused = await IngestManualAsync(berlin, UniqueKind());
            stillRefused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Diagnose(stillRefused));

            await SetAsync(berlin, "manual", "discovery");
            HttpResponseMessage nowAdmitted = await IngestManualAsync(berlin, UniqueKind());

            nowAdmitted.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(nowAdmitted));
        }
        finally
        {
            await RestoreDiscoveryAsync(berlin, "manual");
        }
    }

    /// <summary>Green on arrival — see this class's remarks (tasks.md T003f.17).</summary>
    [Fact]
    public async Task An_undeclared_source_still_ingests_an_unknown_kind()
    {
        using HttpClient dresden = await aspire.CreateAuthenticatedClientAsync(
            ResourceName, DresdenOperator, OperatorPassword);

        HttpResponseMessage created = await IngestManualAsync(dresden, UniqueKind());

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await Diagnose(created));
    }

    private static Task<HttpResponseMessage> IngestManualAsync(HttpClient client, string kind) =>
        client.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "strict-mode-probe",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = $"spec 269 strict-mode probe {kind}" },
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

    /// <summary>
    /// The dead letter is matched on the raw payload, mirroring
    /// <c>DeadLetterReasonIntegrationTests</c>: the kind is the only thing in
    /// it that identifies this test's own delivery.
    /// </summary>
    private async Task<string> WaitForDeadLetterReasonAsync(HttpClient client, string kind, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            HttpResponseMessage listed = await client.GetAsync("/events/dead-letters?limit=1000");
            listed.StatusCode.ShouldBe(HttpStatusCode.OK, await Diagnose(listed));
            JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.GetProperty("rawPayload").GetString()?.Contains(kind, StringComparison.Ordinal) == true)
                {
                    return row.GetProperty("error").GetString() ?? string.Empty;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return string.Empty;
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
                .WithPayload(Payload(kind))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        published.IsSuccess.ShouldBeTrue($"the broker refused publish to {topic}");

        await client.DisconnectAsync();
    }

    private static string Payload(string kind) => JsonSerializer.Serialize(new
    {
        eventId = Guid.CreateVersion7(),
        kind,
        occurredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        payload = new { note = $"spec 269 strict-mode MQTT probe {kind}" },
    });

    private Task<HttpClient> BerlinClientAsync() =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, BerlinOperator, OperatorPassword);

    private static string UniqueKind() => $"StrictMode{Guid.NewGuid():N}"[..24];

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
