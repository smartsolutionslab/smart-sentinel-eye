using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 090 — the broker requires the audience the APIs require.
///
/// <para>
/// The Mosquitto plugin (<c>src/AppHost/mosquitto/plugin/jwt_auth.go</c>,
/// compiled into the image by <c>mosquitto/Dockerfile</c> stage 2) validates a
/// CONNECT password's RS256 signature, its expiry, its <c>azp</c> and its
/// issuer. <c>azp</c> says which client <em>asked for</em> the token;
/// <c>aud</c> says which service the realm <em>minted it for</em>. A broker
/// checking only the first is trusting the client's own claim about itself,
/// while the nine HTTP APIs (<c>AuthenticationDefaults.ApiAudience</c>) and the
/// WHEP hook (<c>WhepAuthValidator.CreateParameters</c>) both check the second.
/// </para>
///
/// <para>
/// <b>Why this is an Aspire-fixture test and not a Go test.</b> The check lives
/// only inside a Go binary compiled into a container image: there is no Go test
/// suite in the repository and no Go toolchain on a developer machine. There is
/// no in-process object to assert against either — the plugin's C# counterpart,
/// <c>BearerAudienceTests</c>, has no equivalent here. Either the behaviour is
/// observed against the running broker or it is not observed at all.
/// </para>
///
/// <para>
/// <b>Why creating a client is not what spec 069 refused.</b>
/// <c>TokenAudienceIntegrationTests</c> declined this negative on the grounds
/// that "a test that rewrites the realm under a running stack is worse than the
/// documented drill". The objection is to <em>mutating shared realm state</em> —
/// the client definitions every other test authenticates through. This creates a
/// uniquely-named throwaway client and deletes it again, which is exactly what
/// the third fact in that same class already does on every run through
/// <c>POST /devices/register</c>. No existing client and no realm-level setting
/// is touched, so <c>RealmAudienceTests</c> and <c>RuntimeClientAudienceTests</c>
/// are unaffected.
/// </para>
///
/// <para>
/// <b>The premise is asserted before the conclusion.</b> A refused CONNECT
/// proves nothing on its own: a mismatched <c>azp</c>, a wrong secret and a
/// broker that is simply down all produce the same red. So the negative test
/// decodes the minted token first and asserts that its <c>aud</c> omits the API
/// and its <c>azp</c> equals the username it is about to present, leaving the
/// audience as the only difference from the positive control.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MqttAudienceIntegrationTests(AspireFixture aspire, ITestOutputHelper output) : IAsyncLifetime
{
    private const string ApiAudience = "smart-sentinel-eye-api";

    // Deliberately not sse-audience. defaultDefaultClientScopes is empty in the
    // realm file and sse-audience is not one of Keycloak's built-ins, so a client
    // created without naming it genuinely lacks it.
    private const string PublishScope = "sse.events.publish";

    private string? throwawayClientUuid;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("mosquitto", KnownResourceStates.Running, cts.Token);
    }

    /// <summary>
    /// The throwaway client is removed here. A failed teardown leaves a
    /// uniquely-named, single-scope service account behind rather than colliding
    /// with the next run — and once spec 090 lands it cannot open a broker
    /// session at all, which is the point of the test.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (throwawayClientUuid is null)
        {
            return;
        }

        var adminToken = await MintClientCredentialsTokenAsync(RealmProbe.AdminClientId, RealmProbe.AdminClientSecret);
        using var keycloak = CreateAdminApiClient(adminToken);
        using var response = await keycloak.DeleteAsync(
            $"admin/realms/{RealmProbe.Realm}/clients/{throwawayClientUuid}");
        output.WriteLine(
            $"teardown: DELETE clients/{throwawayClientUuid} answered {(int)response.StatusCode} {response.StatusCode}");
    }

    /// <summary>
    /// The behaviour spec 090 adds. Before the change this connects, because the
    /// plugin reads <c>azp</c> and never reads <c>aud</c>.
    /// </summary>
    /// <remarks>
    /// This throwaway client's token carries no <c>aud</c> claim at all (measured —
    /// see spec 090's FR-005 note), so this exercises golang-jwt's
    /// <c>errorIfRequired</c> branch of <c>jwt.WithAudience</c>, never
    /// <c>ErrTokenInvalidAudience</c> (the wrong-value branch, where <c>aud</c> is
    /// present but names something else). That branch was covered manually — a
    /// build with the plugin's audience const drifted to <c>"account"</c> was
    /// probed and refused correctly (see FR-005's note) — not in this suite.
    /// Covering it here would need the throwaway client to carry its own audience
    /// mapper naming something other than the API, which is not worth the realm
    /// churn for one more branch of a third-party library.
    /// </remarks>
    [Fact]
    public async Task A_token_minted_without_the_api_audience_is_refused_by_the_broker()
    {
        var audienceless = await CreateAudiencelessClientAsync();
        var token = await MintClientCredentialsTokenAsync(
            audienceless.ClientId, audienceless.ClientSecret);

        AudiencesOf(token).ShouldBeEmpty(
            customMessage: $"the throwaway client '{audienceless.ClientId}' was created without the "
            + $"'sse-audience' default scope, so its token must carry no 'aud' claim at all — measured, "
            + $"not merely one without '{ApiAudience}'. Keycloak granted some audience anyway, so this "
            + "test would refuse for a reason that has nothing to do with the broker.");
        AuthorizedPartyOf(token).ShouldBe(audienceless.ClientId,
            customMessage: "the plugin binds the credential to the connecting identity by requiring "
            + "azp == MQTT username. If they differ the CONNECT is refused for that reason and the "
            + "audience is never reached, so the test would be green on the wrong evidence.");

        var outcome = await TryConnectAsync(audienceless.ClientId, token);
        output.WriteLine($"audience-less CONNECT: {outcome}");

        outcome.ResultCode.ShouldNotBeNull(
            customMessage: "the broker answered no CONNACK at all, so nothing was decided about the "
            + $"token. That is a transport failure, not a refusal: {outcome.Failure}");
        outcome.ResultCode.Value.ShouldNotBe(MqttClientConnectResultCode.Success,
            customMessage: $"'{audienceless.ClientId}' holds a realm-signed token whose azp names "
            + $"itself but whose aud does not name '{ApiAudience}', and the broker let it in. azp is "
            + "the client's claim about which client asked for the token; aud is the realm's statement "
            + "of which service the token was minted for. Spec 090 FR-001 requires the second.");
    }

    /// <summary>
    /// The positive control, in the same class as the red so a change that
    /// refuses <em>everything</em> cannot be mistaken for the fix. A device
    /// registered through the Identity API carries <c>sse-audience</c> by way of
    /// <c>KeycloakScopeBundles.AudienceScope</c>.
    /// </summary>
    [Fact]
    public async Task A_token_minted_with_the_api_audience_still_connects()
    {
        var adminToken = await aspire.GetAdminAccessTokenAsync();

        var device = await RegisterDeviceAsync(adminToken);
        var token = await MintClientCredentialsTokenAsync(device.ClientId, device.ClientSecret);

        AudiencesOf(token).ShouldContain(ApiAudience,
            customMessage: $"'{device.ClientId}' was registered through POST /devices/register and its "
            + $"token does not name '{ApiAudience}'. The control cannot say anything about the broker "
            + "until the token it presents is the one a real device presents.");

        var outcome = await TryConnectAsync(device.ClientId, token);
        output.WriteLine($"registered-device CONNECT: {outcome}");

        outcome.ResultCode.ShouldNotBeNull(
            customMessage: $"the broker answered no CONNACK for '{device.ClientId}': {outcome.Failure}");
        outcome.ResultCode.Value.ShouldBe(MqttClientConnectResultCode.Success,
            customMessage: $"a device registered through the Identity API can no longer reach the "
            + "broker. Every MQTT publisher and the event-ingestion subscriber present exactly this "
            + "kind of token, so this is MQTT ingestion going dark.");
    }

    private async Task<ClientCredentials> CreateAudiencelessClientAsync()
    {
        var clientId = $"audienceless-{Guid.CreateVersion7():N}";
        var adminToken = await MintClientCredentialsTokenAsync(RealmProbe.AdminClientId, RealmProbe.AdminClientSecret);
        using var keycloak = CreateAdminApiClient(adminToken);

        using var created = await keycloak.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/clients",
            new
            {
                clientId,
                name = "Spec 090 throwaway — deliberately without sse-audience",
                protocol = "openid-connect",
                enabled = true,
                publicClient = false,
                serviceAccountsEnabled = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = false,
                defaultClientScopes = new[] { PublishScope },
                optionalClientScopes = Array.Empty<string>(),
            });
        if (!created.IsSuccessStatusCode)
        {
            var body = await created.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST admin/realms/{RealmProbe.Realm}/clients failed with {(int)created.StatusCode} "
                + $"{created.StatusCode}. Body: {body}");
        }

        var uuid = await ReadClientUuidAsync(keycloak, clientId);
        throwawayClientUuid = uuid;

        using var secret = await keycloak.GetAsync(
            $"admin/realms/{RealmProbe.Realm}/clients/{uuid}/client-secret");
        secret.EnsureSuccessStatusCode();
        var credential = await secret.Content.ReadFromJsonAsync<JsonElement>();

        return new ClientCredentials(clientId, credential.GetProperty("value").GetString()!);
    }

    private static async Task<string> ReadClientUuidAsync(HttpClient keycloak, string clientId)
    {
        using var response = await keycloak.GetAsync(
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();

        return rows.GetArrayLength() == 0
            ? throw new InvalidOperationException(
                $"Keycloak accepted POST /clients but no client with clientId='{clientId}' is visible.")
            : rows[0].GetProperty("id").GetString()!;
    }

    private HttpClient CreateAdminApiClient(string adminToken)
    {
        var keycloak = aspire.CreateKeycloakClient();
        keycloak.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return keycloak;
    }

    private async Task<ClientCredentials> RegisterDeviceAsync(string adminToken)
    {
        using var identity = aspire.CreateServiceClient("identity");
        using HttpRequestMessage request = new(HttpMethod.Post, "/devices/register?fabId=munich")
        {
            Content = JsonContent.Create(new
            {
                deviceType = "plc",
                deviceIdentifier = $"mqtt-audience-{Guid.CreateVersion7():N}",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await identity.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /devices/register failed with {(int)response.StatusCode} {response.StatusCode}. "
                + $"Body: {body}{Environment.NewLine}identity log:{Environment.NewLine}"
                + aspire.RecentLogs("identity"));
        }

        var registered = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new ClientCredentials(
            registered.GetProperty("clientId").GetString()!,
            registered.GetProperty("clientSecret").GetString()!);
    }

    private async Task<string> MintClientCredentialsTokenAsync(string clientId, string clientSecret)
    {
        using var keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        using var response = await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token", form);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"client_credentials grant failed for '{clientId}': "
                + $"{(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// One CONNECT, reported rather than thrown. MQTTnet 5 returns a non-success
    /// <c>ResultCode</c> when the broker answers CONNACK with a refusal, and
    /// throws <c>MqttConnectingFailedException</c> when no CONNACK arrives at
    /// all — so the two are distinguishable, and a broker that is simply down
    /// cannot be mistaken for a broker that refused.
    /// </summary>
    private async Task<MqttConnectOutcome> TryConnectAsync(string clientId, string jwt)
    {
        var mqtt = aspire.App.GetEndpoint("mosquitto", "mqtt");

        using var client = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId($"{clientId}-{Guid.CreateVersion7():N}")
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithCredentials(clientId, jwt)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        try
        {
            var result = await client.ConnectAsync(options);
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                await client.DisconnectAsync();
            }

            return new MqttConnectOutcome(result.ResultCode, Failure: null);
        }
        catch (MqttCommunicationException failure)
        {
            return new MqttConnectOutcome(ResultCode: null, failure.ToString());
        }
    }

    /// <summary>
    /// The <c>aud</c> claim as Keycloak issued it — read, not validated. Same
    /// helper as <c>TokenAudienceIntegrationTests</c>, for the same reason: this
    /// asserts what is in the token, not what a handler would do with it.
    /// </summary>
    private static IReadOnlyCollection<string> AudiencesOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return [.. handler.ReadJwtToken(token).Audiences];
    }

    private static string AuthorizedPartyOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return handler.ReadJwtToken(token).Claims
            .First(claim => claim.Type == "azp").Value;
    }

    private sealed record ClientCredentials(string ClientId, string ClientSecret);

    private sealed record MqttConnectOutcome(MqttClientConnectResultCode? ResultCode, string? Failure)
    {
        public override string ToString() =>
            ResultCode is null ? $"no CONNACK — {Failure}" : $"CONNACK {ResultCode}";
    }
}
