using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// End-to-end coverage of the dual-mode bearer validation in
/// <c>POST /events/webhook/{integrationName}</c> (spec 008 FR-016,
/// <c>EventsEndpoints.AuthenticateWebhookAsync</c>). Both modes route
/// through the same anonymous endpoint, and the JWT branch delegates
/// signature/expiry/scope/azp/group validation to the real ASP.NET Core
/// JwtBearer middleware, so the only faithful test is through the booted
/// event-ingestion service against the real Keycloak.
///
/// <para>
/// StaticHash integrations are created via the public
/// <c>POST /webhook-integrations</c> endpoint (which returns the plaintext
/// token once). JWT integrations have no registration endpoint — rotation
/// flips them via the <c>WebhookIntegrationRotatedV1</c> integration event —
/// so the JWT fixtures are seeded straight into the event-ingestion DB with
/// <see cref="BearerValidationMode.Jwt"/> and a known Keycloak clientId.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WebhookBearerValidationIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    // management-web is a public client with the password grant enabled. A
    // token minted through it for 'admin' carries azp=management-web,
    // groups=[/fabs/munich], and (when requested) scope sse.events.write —
    // exactly what ValidateJwtAsync checks.
    private const string JwtClientId = "management-web";
    private const string JwtScope = "openid sse.events.write";

    private static readonly JsonElement Payload =
        JsonDocument.Parse("""{"severity":"high"}""").RootElement;

    [Fact]
    public async Task StaticHash_mode_accepts_the_matching_legacy_bearer()
    {
        string name = UniqueName("hash-ok");
        string token = await RegisterStaticHashIntegrationAsync(name);

        HttpResponseMessage response = await PostWebhookAsync(name, Fab, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task StaticHash_mode_rejects_a_wrong_bearer()
    {
        string name = UniqueName("hash-bad");
        await RegisterStaticHashIntegrationAsync(name);

        HttpResponseMessage response = await PostWebhookAsync(name, Fab, "not-the-real-token");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jwt_mode_accepts_a_valid_token_for_the_rotated_client_and_fab()
    {
        string name = UniqueName("jwt-ok");
        await SeedJwtIntegrationAsync(name, JwtClientId);
        string jwt = await aspire.GetAccessTokenForClientAsync(
            JwtClientId, AspireFixture.AdminUsername, AspireFixture.AdminPassword, JwtScope);

        HttpResponseMessage response = await PostWebhookAsync(name, Fab, jwt);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // scenario-simulator (ADR-0111, dev-only service account, /fabs/munich) is
    // the one real client in the realm whose default scopes never include
    // sse.events.write — every human-facing client (management-web, kiosk-web)
    // grants it by default (spec 200 / issue #2279), so a password-grant token
    // for any of them can no longer isolate "valid azp, missing scope" from
    // "the client just doesn't have it". A client_credentials grant sidesteps
    // that: azp is exactly the client asked for, and this one genuinely lacks
    // the scope, so this is a stronger isolation than a human client gave it.
    private const string ScopeLessClientId = "scenario-simulator";
    private const string ScopeLessClientSecret = "dev-only-scenario-simulator-secret";

    [Fact]
    public async Task Jwt_mode_rejects_a_token_without_the_events_write_scope()
    {
        string name = UniqueName("jwt-scope");
        await SeedJwtIntegrationAsync(name, ScopeLessClientId);
        string jwt = await ScopeLessServiceAccountTokenAsync();

        HttpResponseMessage response = await PostWebhookAsync(name, Fab, jwt);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jwt_mode_rejects_a_token_whose_azp_does_not_match_the_integration_client()
    {
        string name = UniqueName("jwt-azp");
        await SeedJwtIntegrationAsync(name, "some-other-rotated-client");
        // Valid token, valid scope, valid fab — but azp=management-web does
        // not equal the integration's KeycloakClientId.
        string jwt = await aspire.GetAccessTokenForClientAsync(
            JwtClientId, AspireFixture.AdminUsername, AspireFixture.AdminPassword, JwtScope);

        HttpResponseMessage response = await PostWebhookAsync(name, Fab, jwt);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jwt_mode_rejects_a_caller_outside_the_target_fab_group()
    {
        string name = UniqueName("jwt-fab");
        // Seeded in berlin so the delivery's fab matches the integration's own
        // and the request reaches ValidateJwtAsync's group check, which is what
        // this case is for. Seeding it in munich would now be refused a step
        // earlier by the integration-fab comparison (#1545) and this test would
        // pass without ever exercising the check it names.
        await SeedJwtIntegrationAsync(name, JwtClientId, "berlin");
        string jwt = await aspire.GetAccessTokenForClientAsync(
            JwtClientId, AspireFixture.AdminUsername, AspireFixture.AdminPassword, JwtScope);

        // admin is in /fabs/munich only; berlin is not in the groups claim.
        HttpResponseMessage response = await PostWebhookAsync(name, "berlin", jwt);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// #1545 — the gap this amendment closes. A <c>StaticHash</c> integration
    /// presenting its own valid token, naming a plant that is not its own.
    /// Before the fab existed on the aggregate this was <b>202 Accepted</b>:
    /// the hash matched, <c>?fabId=</c> was never consulted, and the event was
    /// filed into the other plant.
    /// </summary>
    [Fact]
    public async Task StaticHash_mode_rejects_a_delivery_naming_another_plants_fab()
    {
        string name = UniqueName("hash-fab");
        string token = await RegisterStaticHashIntegrationAsync(name);

        HttpResponseMessage response = await PostWebhookAsync(name, "dresden", token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// And the refusal is the same one an unknown integration gets, so it
    /// cannot be used to discover that a name is taken in another plant.
    /// </summary>
    [Fact]
    public async Task A_delivery_to_another_plants_integration_looks_like_one_that_does_not_exist()
    {
        string name = UniqueName("hash-probe");
        string token = await RegisterStaticHashIntegrationAsync(name);

        HttpResponseMessage otherFab = await PostWebhookAsync(name, "dresden", token);
        HttpResponseMessage neverExisted = await PostWebhookAsync(UniqueName("absent"), "dresden", token);

        otherFab.StatusCode.ShouldBe(neverExisted.StatusCode);
        (await otherFab.Content.ReadAsStringAsync())
            .ShouldBe(await neverExisted.Content.ReadAsStringAsync());
    }

    private async Task<string> RegisterStaticHashIntegrationAsync(string name)
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("event-ingestion");
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private Task SeedJwtIntegrationAsync(string name, string keycloakClientId) =>
        SeedJwtIntegrationAsync(name, keycloakClientId, Fab);

    private async Task SeedJwtIntegrationAsync(string name, string keycloakClientId, string fab)
    {
        await using EventIngestionDbContext context = await aspire.CreateEventIngestionDbContextAsync();

        SystemClock clock = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From(name), FabIdentifier.From(fab), Kind.From("WebhookAlarm"), clock);
        integration.MarkAsRotated(KeycloakClientIdentifier.From(keycloakClientId), clock);
        integration.ClearPendingEvents();

        context.WebhookIntegrations.Add(integration);
        await context.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string name, string fabId, string bearer)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"/events/webhook/{name}?fabId={fabId}")
        {
            Content = JsonContent.Create(new { payload = Payload }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await aspire.EventIngestion.SendAsync(request);
    }

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 33)];

    /// <summary>
    /// The <c>client_credentials</c> grant for <see cref="ScopeLessClientId"/>,
    /// hand-rolled here rather than added to <see cref="AspireFixture"/> for the
    /// same reason <c>VariableReadScopeIntegrationTests</c> hand-rolls its own
    /// (ADR-0036): two call sites are not yet a pattern.
    /// </summary>
    private async Task<string> ScopeLessServiceAccountTokenAsync()
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ScopeLessClientId,
            ["client_secret"] = ScopeLessClientSecret,
        });

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("access_token").GetString()!;
    }
}
