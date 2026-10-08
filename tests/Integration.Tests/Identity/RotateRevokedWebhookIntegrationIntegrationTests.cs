using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 318 (#2628) — rotating a revoked webhook integration must not mint
/// or re-enable a Keycloak client. Modelled on
/// <see cref="WebhookRevocationDisablesClientIntegrationTests"/> for the
/// Aspire fixture / <see cref="RealmProbe"/> shape (register on
/// event-ingestion, rotate on identity, revoke on event-ingestion, poll
/// Keycloak directly rather than trusting the <c>registered_clients</c> row
/// — spec 121 (#2165/#2207) is the proof those two can disagree).
///
/// <para>
/// <b>I2, I3 and I4 are the load-bearing reds</b> (spec.md §7, tasks.md
/// T008): on <c>develop</c>, <c>RotateWebhookClientCommandHandler</c> never
/// asks EventIngestion whether the integration is revoked, so I2 and I3
/// mint/re-enable a live Keycloak client and answer 200, and I4 answers the
/// misleading 412 <c>WEBHOOK_CLIENT_NOT_FOUND</c> instead of 409
/// <c>WEBHOOK_INTEGRATION_REVOKED</c>.
/// </para>
///
/// <para>
/// <b>I1 is the declared green-on-develop control</b> (spec.md §2, §7): an
/// active integration must keep rotating exactly as it does today, both
/// before and after the fix.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RotateRevokedWebhookIntegrationIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private readonly RealmProbe realm = new(aspire);

    [Fact]
    public async Task An_active_integration_still_rotates()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueName("active");

        await RegisterAsync(events, name);
        (string clientId, string clientSecret) = await RotateCreateIntentAsync(identity, name);

        (await ClientCredentialsGrantSucceedsAsync(clientId, clientSecret)).ShouldBeTrue(
            $"a client_credentials grant for '{clientId}' must succeed right after rotation — this is "
            + "the control, not the claim under test");
    }

    [Fact]
    public async Task A_revoked_integration_that_was_never_rotated_is_refused_and_no_client_is_created()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueName("never-rotated");
        string clientId = $"webhook-{name}";

        await RegisterAsync(events, name);
        await RevokeAsync(events, name);

        HttpResponseMessage response = await SendRotateAsync(identity, name, ifNoneMatchStar: true);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            $"a never-rotated, revoked integration must be refused, not minted a fresh Keycloak "
            + $"client; got {response.StatusCode}: {body}");
        body.ShouldContain("WEBHOOK_INTEGRATION_REVOKED");

        JsonElement clients = await KeycloakClientsAsync(clientId);
        clients.GetArrayLength().ShouldBe(
            0, $"Keycloak must hold no client '{clientId}' for a revoked integration that was never "
            + "rotated — on develop this rotation mints a fresh, enabled one");
    }

    [Fact]
    public async Task A_revoked_and_disabled_integration_is_not_recreated_by_a_create_intent_rotation()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueName("recreate");

        await RegisterAsync(events, name);
        (string clientId, _) = await RotateCreateIntentAsync(identity, name);
        string keycloakUuidBeforeAttack = await KeycloakUuidAsync(clientId);

        await RevokeAsync(events, name);

        // Control, asserted before the attack rotation: the closing
        // assertions below can only fail because of the rotation, not
        // because spec 264's disable never landed.
        await WaitUntilDisabledAsync(clientId);

        HttpResponseMessage response = await SendRotateAsync(identity, name, ifNoneMatchStar: true);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            $"a revoked, disabled integration must not be deleted and recreated by a create-intent "
            + $"rotation (#2728's delete-and-recreate replacement path); got {response.StatusCode}: {body}");
        body.ShouldContain("WEBHOOK_INTEGRATION_REVOKED");

        JsonElement client = await KeycloakSingleClientAsync(clientId);
        client.GetProperty("enabled").GetBoolean().ShouldBeFalse(
            $"'{clientId}' must still be disabled in Keycloak");
        client.GetProperty("id").GetString().ShouldBe(
            keycloakUuidBeforeAttack,
            $"'{clientId}' must be the SAME Keycloak client as before the attack rotation, not a "
            + "freshly minted one with a new internal id");
    }

    [Fact]
    public async Task A_revoked_and_disabled_integration_answers_revoked_to_a_rotate_intent_rotation()
    {
        using HttpClient events = await aspire.CreateAdminClientAsync("event-ingestion");
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueName("rotate-intent");

        await RegisterAsync(events, name);
        (string clientId, int _) = await RotateCreateIntentReturningVersionAsync(identity, name);

        await RevokeAsync(events, name);
        await WaitUntilDisabledAsync(clientId);

        // Identity's own disable handler is itself a second writer on this
        // row (spec 264), and it bumps the version — so the version from the
        // rotation above is stale by now. Sending it regardless is what the
        // scenario means by "revoked, disabled, at version V": V is the
        // version the row *settled at* once disabled, read from Identity's
        // own list rather than inferred from Keycloak, so this fact does not
        // race that second writer the way a reused pre-disable version would
        // (test-adversary finding, #2628 phase 4a).
        int settledVersion = await IdentitySettledVersionAsync(identity, clientId);

        HttpResponseMessage response = await SendRotateAsync(
            identity, name, ifNoneMatchStar: false, ifMatchVersion: settledVersion);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            $"on develop this answers 412 WEBHOOK_CLIENT_NOT_FOUND, which misleadingly invites the "
            + $"operator to retry with If-None-Match: * — straight into the re-enable; got "
            + $"{response.StatusCode}: {body}");
        body.ShouldContain("WEBHOOK_INTEGRATION_REVOKED");
    }

    private static async Task RegisterAsync(HttpClient events, string name)
    {
        HttpResponseMessage registered = await events.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        registered.StatusCode.ShouldBe(
            HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> SendRotateAsync(
        HttpClient identity, string name, bool ifNoneMatchStar, int? ifMatchVersion = null)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };

        if (ifNoneMatchStar)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        }

        return await identity.SendAsync(request);
    }

    private static async Task<(string ClientId, string ClientSecret)> RotateCreateIntentAsync(
        HttpClient identity, string name)
    {
        HttpResponseMessage rotated = await SendRotateAsync(identity, name, ifNoneMatchStar: true);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());

        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }

    private static async Task<(string ClientId, int Version)> RotateCreateIntentReturningVersionAsync(
        HttpClient identity, string name)
    {
        HttpResponseMessage rotated = await SendRotateAsync(identity, name, ifNoneMatchStar: true);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());

        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("version").GetInt32());
    }

    /// <summary>
    /// Revoke carrying the version the listing just reported (ADR-0113),
    /// mirroring <c>WebhookRevocationDisablesClientIntegrationTests.RevokeAsync</c>.
    ///
    /// <para>
    /// A fact that rotates before it revokes races this version read against
    /// EventIngestion's own asynchronous consumption of
    /// <c>WebhookIntegrationRotatedV1</c>, which bumps this same row's
    /// version via <c>MarkAsRotated</c> (test-adversary finding, #2628 phase
    /// 4a). A version read before that lands goes stale by the time this
    /// DELETE arrives, and EventIngestion answers 409
    /// <c>AGGREGATE_VERSION_STALE</c> — a setup race, not the defect this
    /// class asserts. Retrying with a freshly re-read version settles it
    /// without touching what I2-I4 assert about the rotation itself.
    /// </para>
    /// </summary>
    private static async Task RevokeAsync(HttpClient events, string name)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            int version = (await FindAsync(events, name)).GetProperty("version").GetInt32();

            HttpRequestMessage request = new(HttpMethod.Delete, $"/webhook-integrations/{name}");
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

            HttpResponseMessage revoked = await events.SendAsync(request);
            if (revoked.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            string body = await revoked.Content.ReadAsStringAsync();
            if (revoked.StatusCode != HttpStatusCode.Conflict || DateTime.UtcNow >= deadline)
            {
                revoked.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private static async Task<JsonElement> FindAsync(HttpClient events, string name)
    {
        HttpResponseMessage listed = await events.GetAsync("/webhook-integrations?includeRevoked=false");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private async Task<JsonElement> KeycloakClientsAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        return await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);
    }

    private async Task<JsonElement> KeycloakSingleClientAsync(string clientId) =>
        (await KeycloakClientsAsync(clientId)).EnumerateArray().Single();

    private async Task<string> KeycloakUuidAsync(string clientId) =>
        (await KeycloakSingleClientAsync(clientId)).GetProperty("id").GetString()!;

    private async Task WaitUntilDisabledAsync(string clientId)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        bool enabled = (await KeycloakSingleClientAsync(clientId)).GetProperty("enabled").GetBoolean();
        while (enabled && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            enabled = (await KeycloakSingleClientAsync(clientId)).GetProperty("enabled").GetBoolean();
        }

        enabled.ShouldBeFalse(
            $"'{clientId}' must be disabled in Keycloak within 20 s of the revoke (spec 264) — this is "
            + "the control for this fact, not the claim under test");
    }

    /// <summary>
    /// Identity's own version for this client, once its <c>DisabledAt</c>
    /// has been set — i.e. once spec 264's disable handler has committed its
    /// own write to this row, not merely called Keycloak. Reading this
    /// directly, rather than reusing a version captured before the disable,
    /// is what makes the If-Match this fact sends settled rather than racing
    /// the disable's own save (test-adversary finding, #2628 phase 4a: a
    /// reused pre-disable version answers 409 AGGREGATE_VERSION_STALE from
    /// that race, not the WEBHOOK_CLIENT_NOT_FOUND/WEBHOOK_INTEGRATION_REVOKED
    /// this fact is about).
    /// </summary>
    private static async Task<int> IdentitySettledVersionAsync(HttpClient identity, string clientId)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            HttpResponseMessage listed = await identity.GetAsync($"/webhook-integrations?fabId={Fab}");
            listed.EnsureSuccessStatusCode();
            JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

            JsonElement row = rows.EnumerateArray().Single(candidate =>
                string.Equals(candidate.GetProperty("clientId").GetString(), clientId, StringComparison.Ordinal));

            if (row.TryGetProperty("disabledAt", out JsonElement disabledAt) && disabledAt.ValueKind != JsonValueKind.Null)
            {
                return row.GetProperty("version").GetInt32();
            }

            bool settledInTime = DateTime.UtcNow < deadline;
            settledInTime.ShouldBeTrue(
                $"'{clientId}' was disabled in Keycloak but Identity's own registered_clients row "
                + "never showed disabledAt within 20 s — the two disagree longer than this fact waits");

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>
    /// Whether Keycloak still mints an access token for this client's secret —
    /// mirrors <c>WebhookRevocationDisablesClientIntegrationTests</c>.
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
