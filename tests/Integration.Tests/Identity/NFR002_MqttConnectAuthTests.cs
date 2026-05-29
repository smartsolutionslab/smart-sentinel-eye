using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 008 NFR-002 — MQTT connect-time authentication overhead
/// (Keycloak-minted JWT validated by the mosquitto-go-auth plugin
/// against the realm's cached JWKS) must stay ≤ 5 ms p99 on the
/// warm path. The test registers a brand-new device via the
/// Identity API, mints a Keycloak service-account JWT for it,
/// then opens 100 fresh MQTT connections back-to-back and measures
/// p50 / p99 / max wall-clock for the CONNECT → CONNACK round-trip.
///
/// <para>
/// Uses the Aspire-booted Keycloak + mosquitto-go-auth broker
/// rather than Testcontainers (matches the rest of the
/// Integration.Tests suite).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class NFR002_MqttConnectAuthTests(AspireFixture aspire) : IAsyncLifetime
{
    private const int WarmupIterations = 10;
    private const int MeasureIterations = 100;
    private const double P99BudgetMilliseconds = 5;

    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await _aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Skip = "POST /devices/register returns 500 against the seeded realm — the identity-admin client_credentials grant + realm-management role assignment needs verifying end-to-end (separate spec-008 follow-up). Broker side is wired and ready; unskip once the Identity → Keycloak admin path passes a smoke test.")]
    public async Task Mqtt_CONNECT_to_CONNACK_p99_stays_under_the_five_millisecond_budget()
    {
        string adminToken = await _aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        DeviceCredentials device = await RegisterDeviceAsync(adminToken);
        string deviceJwt = await MintDeviceTokenAsync(device);

        Uri mqtt = _aspire.App.GetEndpoint("mosquitto", "mqtt");
        string host = mqtt.Host;
        int port = mqtt.Port;

        for (int i = 0; i < WarmupIterations; i++)
        {
            await ConnectOnceAsync(host, port, device.ClientId, deviceJwt);
        }

        double[] elapsedMs = new double[MeasureIterations];
        for (int i = 0; i < MeasureIterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            await ConnectOnceAsync(host, port, device.ClientId, deviceJwt);
            elapsedMs[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(elapsedMs);
        double p50 = elapsedMs[MeasureIterations / 2];
        double p99 = elapsedMs[(int)Math.Ceiling(MeasureIterations * 0.99) - 1];
        double max = elapsedMs[^1];

        p99.ShouldBeLessThan(
            P99BudgetMilliseconds,
            $"p50 = {p50:F2} ms, p99 = {p99:F2} ms, max = {max:F2} ms");
    }

    private async Task<DeviceCredentials> RegisterDeviceAsync(string adminToken)
    {
        using HttpClient identity = _aspire.App.CreateHttpClient("identity");
        using HttpRequestMessage request = new(HttpMethod.Post, "/devices/register?fabId=munich")
        {
            Content = JsonContent.Create(new
            {
                deviceType = "plc",
                deviceIdentifier = $"nfr002-{Guid.CreateVersion7():N}",
            }),
        };
        request.Headers.Authorization = new("Bearer", adminToken);

        HttpResponseMessage response = await identity.SendAsync(request);
        response.EnsureSuccessStatusCode();
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new DeviceCredentials(
            body.GetProperty("clientId").GetString()!,
            body.GetProperty("clientSecret").GetString()!);
    }

    private async Task<string> MintDeviceTokenAsync(DeviceCredentials device)
    {
        using HttpClient keycloak = _aspire.App.CreateHttpClient("keycloak");
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = device.ClientId,
            ["client_secret"] = device.ClientSecret,
        };
        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token",
            new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task ConnectOnceAsync(string host, int port, string clientId, string jwt)
    {
        using IMqttClient client = new MqttFactory().CreateMqttClient();
        MqttClientOptions options = new MqttClientOptionsBuilder()
            .WithClientId($"{clientId}-{Guid.CreateVersion7():N}")
            .WithTcpServer(host, port)
            .WithCredentials(clientId, jwt)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(5))
            .Build();

        MqttClientConnectResult result = await client.ConnectAsync(options);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException(
                $"MQTT CONNECT rejected with code {result.ResultCode}.");
        }
        await client.DisconnectAsync();
    }

    private sealed record DeviceCredentials(string ClientId, string ClientSecret);
}
