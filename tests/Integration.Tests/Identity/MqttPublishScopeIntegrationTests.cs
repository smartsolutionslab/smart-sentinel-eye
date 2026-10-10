using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 330 (#2286) — the scope <c>sse.events.publish</c> is catalogued, granted
/// to every device, and today enforced by nothing. The Mosquitto plugin
/// (<c>src/AppHost/mosquitto/plugin/jwt_auth.go</c>) registers only
/// <c>MOSQ_EVT_BASIC_AUTH</c>; it has no ACL callback, so a PUBLISH from any
/// identity whose JWT merely satisfies CONNECT succeeds regardless of its
/// <c>scope</c> claim, and topic authority is decided by <c>acl.txt</c> alone.
///
/// <para>
/// <b>AS-1 is the red fact.</b> Today the probe connects (its CONNACK is
/// <c>Success</c>) and its PUBLISH also succeeds, because nothing reads
/// <c>scope</c>. After the fix the CONNECT is unaffected and the PUBLISH is
/// refused with PUBACK reason code <c>NotAuthorized</c>.
/// </para>
///
/// <para>
/// <b>AS-2 is the control.</b> The same identity, same topic, same ACL row —
/// only the scope differs — must keep publishing successfully both before and
/// after, so a fix that refuses every publish cannot be mistaken for the fix
/// this spec describes.
/// </para>
///
/// <para>
/// <b>AS-3 is the other control.</b> A registered device holds the scope (the
/// device bundle grants it today) but has no <c>acl.txt</c> row, so it is
/// refused before and after — for the plugin's own "defer to acl.txt" reason
/// after the fix, by coincidence (no ACL logic exists at all) before it. This
/// pins finding 2 of the spec: the plugin may only narrow authority, never
/// widen it.
/// </para>
///
/// <para>
/// <b>Why MQTT 5.</b> Only a v5 CONNECT/PUBLISH exchange carries a PUBACK
/// reason code (spec finding 5); MQTT 3.1.1 drops a refused publish silently.
/// <see cref="MqttAudienceIntegrationTests"/> and <see cref="PlantFloor"/> use
/// v3.1.1 because they never need to observe a refusal finer than "no
/// CONNACK" — this class exists because that is exactly what AS-1 needs to
/// observe.
/// </para>
///
/// <para>
/// <b>The premise is asserted before the conclusion</b>, the same discipline
/// <see cref="MqttAudienceIntegrationTests"/> uses: each fact decodes the
/// minted token and asserts what it does or does not carry in <c>scope</c>
/// before any MQTT call, so a wrong premise (Keycloak granting the scope
/// anyway, or withholding it from a control) cannot produce a false pass.
/// </para>
///
/// <para>
/// <b>Fixed probe id, delete-before-create.</b> <c>mqtt-publish-scope-probe</c>
/// is the only username <c>acl.txt</c> grants write access to
/// <c>sse-probe/publish-scope</c> (T001); no realm client holds that id
/// normally, so each fact creates it and deletes any leftover of the same id
/// first — a failed teardown from an earlier run must not collide with this
/// one. The topic is outside <c>fab/+/+/+</c>, so nothing this class publishes
/// is ever ingested.
/// </para>
///
/// <para>
/// The private helpers below are copied from <see cref="MqttAudienceIntegrationTests"/>,
/// not extracted into a shared location — refactoring that class is not part
/// of this behaviour-changing change (ADR-0036, plan.md §5).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MqttPublishScopeIntegrationTests(AspireFixture aspire, ITestOutputHelper output) : IAsyncLifetime
{
    private const string ProbeClientId = "mqtt-publish-scope-probe";
    private const string ProbeTopic = "sse-probe/publish-scope";
    private const string AudienceScope = "sse-audience";
    private const string PublishScope = "sse.events.publish";

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("mosquitto", KnownResourceStates.Running, cts.Token);
    }

    /// <summary>
    /// Belt and suspenders with the delete-before-create at the top of every
    /// fact below: this removes the probe this test instance created, and a
    /// no-op if nothing of that id remains (the loop in
    /// <see cref="DeleteProbeClientIfPresentAsync"/> simply has nothing to
    /// iterate).
    /// </summary>
    public async Task DisposeAsync() => await DeleteProbeClientIfPresentAsync();

    /// <summary>
    /// AS-1 — red today. A probe identity without <c>sse.events.publish</c>
    /// connects (CONNACK <c>Success</c>) and must have its PUBLISH refused
    /// (PUBACK <c>NotAuthorized</c>). Before the fix nothing checks the scope
    /// at all, so this PUBACK is <c>Success</c> instead — the wrong-direction
    /// result that is the whole point of this spec.
    /// </summary>
    [Fact]
    public async Task A_publish_without_the_scope_is_refused_though_the_connect_still_succeeds()
    {
        await DeleteProbeClientIfPresentAsync();
        ClientCredentials probe = await CreateProbeClientAsync([AudienceScope]);
        string token = await MintClientCredentialsTokenAsync(probe.ClientId, probe.ClientSecret);

        ScopesOf(token).ShouldNotContain(PublishScope,
            customMessage: $"the probe '{probe.ClientId}' was created with default scopes "
            + $"['{AudienceScope}'] only, so its token must not carry '{PublishScope}'. If it did, a "
            + "refused publish below would prove nothing about the scope check this spec adds.");
        AuthorizedPartyOf(token).ShouldBe(probe.ClientId,
            customMessage: "the plugin binds the credential to the connecting identity by requiring "
            + "azp == MQTT username. If they differ the CONNECT is refused for that reason and the "
            + "scope check is never reached.");

        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        try
        {
            MqttConnectOutcome connected = await TryConnectAsync(client, probe.ClientId, token);
            output.WriteLine($"AS-1 CONNECT: {connected}");

            connected.ResultCode.ShouldNotBeNull(
                customMessage: "the broker answered no CONNACK at all, so nothing was decided about "
                + $"the token: {connected.Failure}. That is a transport failure, not the scope check "
                + "this fact exercises.");
            connected.ResultCode.Value.ShouldBe(MqttClientConnectResultCode.Success,
                customMessage: $"'{probe.ClientId}' lacks '{PublishScope}' but still presents a "
                + "realm-signed, correctly-audienced token — CONNECT must succeed regardless, "
                + "because the scope is checked only on PUBLISH (spec decision table).");

            MqttClientPublishResult published = await client.PublishAsync(ProbeMessage());
            output.WriteLine($"AS-1 PUBACK: {published.ReasonCode}");

            published.ReasonCode.ShouldBe(MqttClientPublishReasonCode.NotAuthorized,
                customMessage: $"'{probe.ClientId}' published to '{ProbeTopic}' without holding "
                + $"'{PublishScope}', and the broker must refuse it. acl.txt alone would allow this "
                + "(T001's row names exactly this user and topic), so a NotAuthorized here can only "
                + "come from the scope check this spec adds.");
        }
        finally
        {
            await DisconnectIfConnectedAsync(client);
        }
    }

    /// <summary>
    /// AS-2 — the positive control, green both before and after. Same probe
    /// id, same ACL row, same topic as AS-1; the only difference is that this
    /// token carries <c>sse.events.publish</c>.
    /// </summary>
    [Fact]
    public async Task A_publish_with_the_scope_still_succeeds()
    {
        await DeleteProbeClientIfPresentAsync();
        ClientCredentials probe = await CreateProbeClientAsync([AudienceScope, PublishScope]);
        string token = await MintClientCredentialsTokenAsync(probe.ClientId, probe.ClientSecret);

        ScopesOf(token).ShouldContain(PublishScope,
            customMessage: $"the probe '{probe.ClientId}' was created with '{PublishScope}' among its "
            + "default scopes, so its token must carry it. Without this the control proves nothing — "
            + "a publish refused here would be indistinguishable from AS-1's refusal.");

        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        try
        {
            MqttConnectOutcome connected = await TryConnectAsync(client, probe.ClientId, token);
            output.WriteLine($"AS-2 CONNECT: {connected}");

            connected.ResultCode.ShouldNotBeNull(
                customMessage: $"the broker answered no CONNACK for '{probe.ClientId}': {connected.Failure}");
            connected.ResultCode.Value.ShouldBe(MqttClientConnectResultCode.Success,
                customMessage: $"'{probe.ClientId}' holds a valid, correctly-audienced token and must "
                + "be able to connect.");

            MqttClientPublishResult published = await client.PublishAsync(ProbeMessage());
            output.WriteLine($"AS-2 PUBACK: {published.ReasonCode}");

            published.ReasonCode.ShouldBe(MqttClientPublishReasonCode.Success,
                customMessage: $"'{probe.ClientId}' holds '{PublishScope}' and acl.txt grants it write "
                + $"on '{ProbeTopic}'. A fix that refused everything would pass AS-1 and fail here — "
                + "that is exactly what this control exists to catch.");
        }
        finally
        {
            await DisconnectIfConnectedAsync(client);
        }
    }

    /// <summary>
    /// AS-3 — the other control, green both before and after. A device
    /// registered through <c>POST /devices/register</c> holds
    /// <c>sse.events.publish</c> via <c>KeycloakScopeBundles.Device</c>, but
    /// <c>acl.txt</c> has no row for its client id at all. Today it is refused
    /// because <c>acl.txt</c> fails closed for an unknown user. After the fix
    /// it must still be refused — the plugin only ever defers or denies, it
    /// never grants (spec finding 2) — so the scope cannot widen authority
    /// acl.txt does not already give.
    /// </summary>
    [Fact]
    public async Task A_registered_device_with_the_scope_is_still_refused_without_an_acl_row()
    {
        string adminToken = await aspire.GetAdminAccessTokenAsync();
        ClientCredentials device = await RegisterDeviceAsync(adminToken);
        string token = await MintClientCredentialsTokenAsync(device.ClientId, device.ClientSecret);

        ScopesOf(token).ShouldContain(PublishScope,
            customMessage: $"'{device.ClientId}' was registered through POST /devices/register, which "
            + $"grants '{PublishScope}' via KeycloakScopeBundles.Device. Without the scope this fact "
            + "would be refused for the wrong reason and prove nothing about finding 2.");

        string topic = $"fab/munich/plc/{device.ClientId}";

        using IMqttClient client = new MqttClientFactory().CreateMqttClient();
        try
        {
            MqttConnectOutcome connected = await TryConnectAsync(client, device.ClientId, token);
            output.WriteLine($"AS-3 CONNECT: {connected}");

            connected.ResultCode.ShouldNotBeNull(
                customMessage: $"the broker answered no CONNACK for '{device.ClientId}': {connected.Failure}");
            connected.ResultCode.Value.ShouldBe(MqttClientConnectResultCode.Success,
                customMessage: $"a device registered through the Identity API must still be able to "
                + "connect; this fact is about the publish, not the connect.");

            MqttClientPublishResult published = await client.PublishAsync(
                new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload("spec-330-as-3-probe")
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
            output.WriteLine($"AS-3 PUBACK: {published.ReasonCode}");

            published.ReasonCode.ShouldBe(MqttClientPublishReasonCode.NotAuthorized,
                customMessage: $"'{device.ClientId}' holds '{PublishScope}' but acl.txt has no row for "
                + $"it, so '{topic}' must still be refused. A refusal here cannot come from the scope "
                + "check — the scope is held — it can only come from acl.txt, which is the point: "
                + "holding the scope must never widen topic authority acl.txt does not grant.");
        }
        finally
        {
            await DisconnectIfConnectedAsync(client);
        }
    }

    private static MqttApplicationMessage ProbeMessage() =>
        new MqttApplicationMessageBuilder()
            .WithTopic(ProbeTopic)
            .WithPayload("spec-330-probe")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

    private static async Task DisconnectIfConnectedAsync(IMqttClient client)
    {
        if (client.IsConnected)
        {
            await client.DisconnectAsync();
        }
    }

    /// <summary>
    /// Removes any client of <see cref="ProbeClientId"/>, and is a no-op when
    /// none exists — the shape a delete-before-create needs, as opposed to
    /// <c>MqttAudienceIntegrationTests</c>' teardown, which always expects
    /// exactly the one client it tracked.
    /// </summary>
    private async Task DeleteProbeClientIfPresentAsync()
    {
        string adminToken = await MintClientCredentialsTokenAsync(RealmProbe.AdminClientId, RealmProbe.AdminClientSecret);
        using HttpClient keycloak = CreateAdminApiClient(adminToken);

        using HttpResponseMessage response = await keycloak.GetAsync(
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(ProbeClientId)}");
        response.EnsureSuccessStatusCode();
        JsonElement rows = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (JsonElement row in rows.EnumerateArray())
        {
            string uuid = row.GetProperty("id").GetString()!;
            using HttpResponseMessage deleted = await keycloak.DeleteAsync(
                $"admin/realms/{RealmProbe.Realm}/clients/{uuid}");
            output.WriteLine(
                $"delete-before-create: DELETE clients/{uuid} ('{ProbeClientId}') answered "
                + $"{(int)deleted.StatusCode} {deleted.StatusCode}");
        }
    }

    private async Task<ClientCredentials> CreateProbeClientAsync(IReadOnlyList<string> defaultClientScopes)
    {
        string adminToken = await MintClientCredentialsTokenAsync(RealmProbe.AdminClientId, RealmProbe.AdminClientSecret);
        using HttpClient keycloak = CreateAdminApiClient(adminToken);

        using HttpResponseMessage created = await keycloak.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/clients",
            new
            {
                clientId = ProbeClientId,
                name = "Spec 330 (#2286) throwaway — the only client this acl.txt row names",
                protocol = "openid-connect",
                enabled = true,
                publicClient = false,
                serviceAccountsEnabled = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = false,
                defaultClientScopes,
                optionalClientScopes = Array.Empty<string>(),
            });
        if (!created.IsSuccessStatusCode)
        {
            string body = await created.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST admin/realms/{RealmProbe.Realm}/clients failed with {(int)created.StatusCode} "
                + $"{created.StatusCode}. Body: {body}");
        }

        string uuid = await ReadClientUuidAsync(keycloak, ProbeClientId);

        using HttpResponseMessage secret = await keycloak.GetAsync(
            $"admin/realms/{RealmProbe.Realm}/clients/{uuid}/client-secret");
        secret.EnsureSuccessStatusCode();
        JsonElement credential = await secret.Content.ReadFromJsonAsync<JsonElement>();

        return new ClientCredentials(ProbeClientId, credential.GetProperty("value").GetString()!);
    }

    private static async Task<string> ReadClientUuidAsync(HttpClient keycloak, string clientId)
    {
        using HttpResponseMessage response = await keycloak.GetAsync(
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}");
        response.EnsureSuccessStatusCode();
        JsonElement rows = await response.Content.ReadFromJsonAsync<JsonElement>();

        return rows.GetArrayLength() == 0
            ? throw new InvalidOperationException(
                $"Keycloak accepted POST /clients but no client with clientId='{clientId}' is visible.")
            : rows[0].GetProperty("id").GetString()!;
    }

    private HttpClient CreateAdminApiClient(string adminToken)
    {
        HttpClient keycloak = aspire.CreateKeycloakClient();
        keycloak.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return keycloak;
    }

    private async Task<ClientCredentials> RegisterDeviceAsync(string adminToken)
    {
        using HttpClient identity = aspire.CreateServiceClient("identity");
        using HttpRequestMessage request = new(HttpMethod.Post, "/devices/register?fabId=munich")
        {
            Content = JsonContent.Create(new
            {
                deviceType = "plc",
                deviceIdentifier = $"mqtt-publish-scope-as3-{Guid.CreateVersion7():N}",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using HttpResponseMessage response = await identity.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /devices/register failed with {(int)response.StatusCode} {response.StatusCode}. "
                + $"Body: {body}{Environment.NewLine}identity log:{Environment.NewLine}"
                + aspire.RecentLogs("identity"));
        }

        JsonElement registered = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new ClientCredentials(
            registered.GetProperty("clientId").GetString()!,
            registered.GetProperty("clientSecret").GetString()!);
    }

    private async Task<string> MintClientCredentialsTokenAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        using HttpResponseMessage response = await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token", form);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"client_credentials grant failed for '{clientId}': "
                + $"{(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// One CONNECT, reported rather than thrown — the same shape
    /// <c>MqttAudienceIntegrationTests.TryConnectAsync</c> uses, except this
    /// one takes an already-created client and leaves it connected on
    /// success, because every fact here needs to publish on the same
    /// connection afterwards. MQTTnet 5 returns a non-success
    /// <c>ResultCode</c> when the broker answers CONNACK with a refusal, and
    /// throws <c>MqttConnectingFailedException</c> when no CONNACK arrives at
    /// all — so a refusal and a down broker cannot be mistaken for each other.
    /// </summary>
    private async Task<MqttConnectOutcome> TryConnectAsync(IMqttClient client, string clientId, string jwt)
    {
        Uri mqtt = aspire.App.GetEndpoint("mosquitto", "mqtt");
        MqttClientOptions options = new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId($"{clientId}-{Guid.CreateVersion7():N}")
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithCredentials(clientId, jwt)
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        try
        {
            MqttClientConnectResult result = await client.ConnectAsync(options);
            return new MqttConnectOutcome(result.ResultCode, Failure: null);
        }
        catch (MqttCommunicationException failure)
        {
            return new MqttConnectOutcome(ResultCode: null, failure.ToString());
        }
    }

    /// <summary>
    /// The <c>scope</c> claim as Keycloak issued it, split on spaces — read,
    /// not validated, the same discipline <c>MqttAudienceIntegrationTests</c>
    /// applies to <c>aud</c>.
    /// </summary>
    private static string[] ScopesOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
        string? scope = handler.ReadJwtToken(token).Claims
            .FirstOrDefault(claim => claim.Type == "scope")?.Value;

        return scope is null
            ? []
            : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
