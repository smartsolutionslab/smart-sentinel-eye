using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 270 (ADR-0160) US1 + US2 end to end (spec.md §3, plan.md §7,
/// tasks.md T007). Enrols a subject kiosk and a control kiosk, mints a
/// <c>client_credentials</c> token for each, confirms both are admitted,
/// disables the subject, and polls for both enforcement points —
/// CameraCatalog's bearer pipeline (US2) and StreamDistribution's WHEP hook
/// (US1) — to refuse the subject's token within FR-006's bound while the
/// control's token keeps working throughout the same window.
///
/// <para>
/// <b>The control is mandatory, not incidental</b> (plan.md §7; memory: an
/// assertion must not check its own input). Without a second, unrevoked
/// kiosk asserted 200 across the same window, a 401 caused by anything
/// else — an expired token, a broken realm, an unrelated regression — would
/// pass for the fix this spec builds.
/// </para>
///
/// <para>
/// <b>Red today.</b> On <c>develop</c> nothing checks <c>DisabledAt</c> at
/// request time, so the subject's token is still 200 at the 20 s ceiling —
/// tasks.md T009's load-bearing red — while the controls are green before it,
/// since enrolment and the <c>client_credentials</c> grant already work
/// unmodified. <c>GET /registered-clients/revoked</c> does not exist yet, so
/// every assertion against it 404s rather than answering 401/403/200.
/// </para>
///
/// <para>
/// Enrolment follows <see cref="CrossFabDisableIntegrationTests.EnrollKioskAsync"/>'s
/// shape; the poll idiom (20 s ceiling, 500 ms interval) follows
/// <see cref="WebhookRevocationDisablesClientIntegrationTests"/> and
/// <see cref="CrossFabWebhookRotationIntegrationTests"/>; the
/// <c>client_credentials</c> grant follows
/// <see cref="WebhookRevocationDisablesClientIntegrationTests.ClientCredentialsGrantSucceedsAsync"/>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RevokedTokenRefusedIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";
    private static readonly TimeSpan PollCeiling = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task A_revoked_kiosks_token_is_refused_on_CameraCatalog_and_WHEP_while_a_control_kiosk_stays_admitted()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        (string subjectClientId, string subjectSecret) = await EnrollKioskAsync(identity, "revoke-subject");
        (string controlClientId, string controlSecret) = await EnrollKioskAsync(identity, "revoke-control");

        string subjectToken = await ClientCredentialsTokenAsync(subjectClientId, subjectSecret);
        string controlToken = await ClientCredentialsTokenAsync(controlClientId, controlSecret);

        // Controls first, asserted before the disable: the 401s asserted
        // below can then only be caused by the disable, not by a token or a
        // wiring that never worked in the first place.
        (await CamerasStatusAsync(subjectToken)).ShouldBe(HttpStatusCode.OK);
        (await CamerasStatusAsync(controlToken)).ShouldBe(HttpStatusCode.OK);
        (await StreamsAuthorizeStatusAsync(subjectToken)).ShouldBe(HttpStatusCode.OK);
        (await StreamsAuthorizeStatusAsync(controlToken)).ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage disabled = await identity.DeleteAsync($"/kiosks/{subjectClientId}?fabId={Fab}");
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());

        DateTime deadline = DateTime.UtcNow + PollCeiling;
        HttpStatusCode subjectCameras = await CamerasStatusAsync(subjectToken);
        HttpStatusCode subjectStreams = await StreamsAuthorizeStatusAsync(subjectToken);
        while ((subjectCameras != HttpStatusCode.Unauthorized || subjectStreams != HttpStatusCode.Unauthorized)
               && DateTime.UtcNow < deadline)
        {
            // The control must stay admitted at every point in the window,
            // not merely at the start and the end.
            (await CamerasStatusAsync(controlToken)).ShouldBe(
                HttpStatusCode.OK,
                "the control kiosk was never disabled and must stay admitted for the whole poll "
                + "window; if it flips too, the subject's 401 proves nothing about revocation.");
            (await StreamsAuthorizeStatusAsync(controlToken)).ShouldBe(HttpStatusCode.OK);

            await Task.Delay(PollInterval);
            subjectCameras = await CamerasStatusAsync(subjectToken);
            subjectStreams = await StreamsAuthorizeStatusAsync(subjectToken);
        }

        subjectCameras.ShouldBe(
            HttpStatusCode.Unauthorized,
            $"'{subjectClientId}'s token must be refused by CameraCatalog within {PollCeiling.TotalSeconds}s "
            + "of the disable (FR-006). On develop nothing checks DisabledAt at request time, so this "
            + "is the load-bearing red this spec closes (ADR-0160).");
        subjectStreams.ShouldBe(
            HttpStatusCode.Unauthorized,
            $"'{subjectClientId}'s token must be refused by /streams/authorize within "
            + $"{PollCeiling.TotalSeconds}s of the disable (FR-006, US1).");

        (await CamerasStatusAsync(controlToken)).ShouldBe(HttpStatusCode.OK);
        (await StreamsAuthorizeStatusAsync(controlToken)).ShouldBe(HttpStatusCode.OK);

        using HttpClient cameraCatalog = aspire.CreateServiceClient("camera-catalog");
        using HttpRequestMessage camerasRequest = new(HttpMethod.Get, "/cameras")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", subjectToken) },
        };
        HttpResponseMessage finalRefusal = await cameraCatalog.SendAsync(camerasRequest);
        finalRefusal.Headers.WwwAuthenticate.ToString().ShouldContain(
            "invalid_token",
            customMessage: "FR-002: the refusal must take the bearer handler's standard invalid_token "
            + "challenge, revealing nothing more than an expired token would.");
    }

    [Fact]
    public async Task The_revocation_list_endpoint_requires_its_own_scope()
    {
        using HttpClient anonymous = aspire.CreateServiceClient("identity");
        (await anonymous.GetAsync("/registered-clients/revoked")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using HttpClient operatorWithoutScope = await aspire.CreateAdminClientAsync("identity");
        // The seeded admin operator holds every kiosk/device management scope
        // this realm grants management-web, and
        // sse.identity.revocations.read is deliberately not one of them
        // (ADR-0160 §4: one shared read-only service account, not a grant to
        // management-web).
        HttpResponseMessage forbidden = await operatorWithoutScope.GetAsync("/registered-clients/revoked");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        string readerToken = await ClientCredentialsTokenAsync(
            "revocation-list-reader", "dev-only-revocation-list-reader-secret");
        using HttpClient identity = aspire.CreateServiceClient("identity");
        using HttpRequestMessage request = new(HttpMethod.Get, "/registered-clients/revoked")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", readerToken) },
        };
        HttpResponseMessage allowed = await identity.SendAsync(request);

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    private static async Task<(string ClientId, string ClientSecret)> EnrollKioskAsync(
        HttpClient identity, string prefix)
    {
        string clientId = $"t269-{prefix}-{Guid.CreateVersion7():N}";

        HttpResponseMessage enrolled = await identity.PostAsJsonAsync(
            $"/kiosks/enroll?fabId={Fab}", new { clientId });
        enrolled.StatusCode.ShouldBe(HttpStatusCode.Created, await enrolled.Content.ReadAsStringAsync());

        JsonElement body = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!);
    }

    private async Task<string> ClientCredentialsTokenAsync(string clientId, string clientSecret)
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
        response.EnsureSuccessStatusCode();

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpStatusCode> CamerasStatusAsync(string token)
    {
        using HttpClient cameraCatalog = aspire.CreateServiceClient("camera-catalog");
        using HttpRequestMessage request = new(HttpMethod.Get, "/cameras")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        HttpResponseMessage response = await cameraCatalog.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> StreamsAuthorizeStatusAsync(string token)
    {
        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token, path = $"cam-{Guid.CreateVersion7()}", action = "read" });
        return response.StatusCode;
    }
}
