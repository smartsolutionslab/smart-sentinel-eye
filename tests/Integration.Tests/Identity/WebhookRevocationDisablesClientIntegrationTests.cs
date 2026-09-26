using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 264 (#2206), US1 — the crossing this fix builds. Revoking a webhook
/// integration on EventIngestion's side must, once Identity hears the
/// announcement, disable the integration's Keycloak client the same way
/// <c>DisableKioskCommandHandler</c> / <c>DisableDeviceCommandHandler</c> do —
/// see plan.md §6.1.
///
/// <para>
/// Modelled on <see cref="CrossFabDisableIntegrationTests"/> (its
/// <c>IsEnabledInKeycloakAsync</c> reads Keycloak's own Admin API via
/// <see cref="RealmProbe"/>, <b>not</b> the <c>registered_clients</c> row — spec
/// 121 (#2165/#2207) is the proof those two can disagree) and on
/// <see cref="SmartSentinelEye.Integration.Tests.EventIngestion.CrossFabWebhookRotationEffectIntegrationTests"/>
/// (register on event-ingestion, rotate on identity, a bounded 20 s / 500 ms
/// poll for an asynchronous, eventual effect — spec.md §2 US1 "Eventual, not
/// synchronous").
/// </para>
///
/// <para>
/// <b>Fact 1 is the load-bearing red</b> (spec.md §6, tasks.md T008): on
/// <c>develop</c>, nothing registers an <c>IDomainEventHandler
/// &lt;WebhookIntegrationRevokedDomainEvent&gt;</c>, so the announcement is
/// never published and Identity never hears it — the client stays enabled
/// forever and the closing <c>client_credentials</c> grant keeps succeeding.
/// </para>
///
/// <para>
/// <b>Fact 2 is a declared green-on-develop control</b> (plan.md §6.1): it pins
/// that publishing the new message for every first revoke — rotated or not —
/// does not make the revoke endpoint fail when there is no Keycloak client to
/// disable.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WebhookRevocationDisablesClientIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private readonly RealmProbe realm = new(aspire);

    [Fact]
    public async Task A_rotated_webhook_integrations_client_is_disabled_once_it_is_revoked()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueName("revoke-disable");

        await RegisterAsync(events, name);
        (string clientId, string clientSecret) = await RotateAsync(identity, name);

        // Controls, asserted before the revoke: the closing assertions below
        // can only fail because of the revoke, not because the client was
        // never really enabled or the grant never really worked.
        (await IsEnabledInKeycloakAsync(clientId)).ShouldBeTrue(
            $"'{clientId}' must be enabled right after rotation — this is the control, not the claim "
            + "under test");
        (await ClientCredentialsGrantSucceedsAsync(clientId, clientSecret)).ShouldBeTrue(
            $"a client_credentials grant for '{clientId}' with the freshly rotated secret must succeed "
            + "before the revoke — this is the control, not the claim under test");

        await RevokeAsync(events, name);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        bool enabled = await IsEnabledInKeycloakAsync(clientId);
        while (enabled && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            enabled = await IsEnabledInKeycloakAsync(clientId);
        }

        enabled.ShouldBeFalse(
            $"'{clientId}' must be disabled in Keycloak within 20 s of the revoke — on develop nothing "
            + "publishes WebhookIntegrationRevokedV1 for a revocation, so Identity never hears it and "
            + "the client stays enabled forever; that is exactly the defect this spec closes (#2206)");

        (await ClientCredentialsGrantSucceedsAsync(clientId, clientSecret)).ShouldBeFalse(
            $"a client_credentials grant for '{clientId}' with the same secret must be refused once the "
            + "client is disabled");
    }

    /// <summary>
    /// Declared green-on-develop control (plan.md §6.1 Fact 2). An integration
    /// still on <c>StaticHash</c> validation has no Keycloak client at all, so
    /// its revocation must still succeed and must leave Keycloak's realm
    /// exactly as it found it — the new announcement must not turn a harmless
    /// not-found on Identity's side into a failed revoke on EventIngestion's.
    /// </summary>
    [Fact]
    public async Task Revoking_a_never_rotated_integration_still_succeeds()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        string name = UniqueName("never-rotated");

        await RegisterAsync(events, name);
        await RevokeAsync(events, name);

        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString($"webhook-{name}")}",
            CancellationToken.None);

        clients.GetArrayLength().ShouldBe(
            0, $"an integration that was never rotated has no Keycloak client at all; revoking it must "
            + "not create or touch one");
    }

    private static async Task RegisterAsync(HttpClient events, string name)
    {
        HttpResponseMessage registered = await events.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        registered.StatusCode.ShouldBe(
            HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());
    }

    private static async Task<(string ClientId, string ClientSecret)> RotateAsync(HttpClient identity, string name)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        HttpResponseMessage rotated = await identity.SendAsync(request);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());

        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }

    /// <summary>
    /// Revoke carrying the version the listing just reported (ADR-0113),
    /// mirroring <c>WebhookRevocationRefusesDeliveryIntegrationTests.RevokeAsync</c>.
    /// </summary>
    private static async Task RevokeAsync(HttpClient events, string name)
    {
        int version = (await FindAsync(events, name)).GetProperty("version").GetInt32();

        HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        HttpResponseMessage revoked = await events.SendAsync(request);
        revoked.StatusCode.ShouldBe(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> FindAsync(HttpClient events, string name)
    {
        HttpResponseMessage listed = await events.GetAsync("/webhook-integrations?includeRevoked=false");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private async Task<bool> IsEnabledInKeycloakAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);

        return clients.EnumerateArray().Single().GetProperty("enabled").GetBoolean();
    }

    /// <summary>
    /// Whether Keycloak still mints an access token for this client's rotated
    /// secret — the observable a revoked webhook client must lose on every
    /// path that trusts a Keycloak token, <c>/events/manual</c> included
    /// (spec.md §2 US1).
    /// </summary>
    private async Task<bool> ClientCredentialsGrantSucceedsAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage response = await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token", form);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("access_token", out JsonElement token)
            && !string.IsNullOrEmpty(token.GetString());
    }

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 33)];
}
