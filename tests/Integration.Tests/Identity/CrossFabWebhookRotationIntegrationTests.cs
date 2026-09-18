using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 182 US1 (#2280), AS-1/AS-3/AS-4.
/// <see cref="SmartSentinelEye.Identity.Application.Commands.Handlers.RotateWebhookClientCommandHandler"/>
/// resolved the rotation target with <c>GetByClientIdAsync</c>, unscoped
/// across every fab — so a munich-only operator naming <b>their own</b> fab
/// (not Dresden's) could roll, and read back, another fab's webhook client
/// secret. Modelled directly on spec 180's
/// <see cref="CrossFabDisableIntegrationTests"/>: I1 is the sharper attack
/// (attacker names their own fab, AS-4), I2 is the evidence that Keycloak's
/// own secret never moved, I3 is the AS-3 regression guard (naming the
/// victim's fab — already refused today by the fab guard), and I4 is the
/// legitimate rotation the fix must not break.
///
/// <para>
/// Each test mints its own dresden integration by rotating it once
/// (<c>If-None-Match: *</c>), the same pattern
/// <see cref="RegisteredClientConcurrencyIntegrationTests"/> uses — a real
/// Keycloak client, so nothing here is torn down or reused across tests.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabWebhookRotationIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    /// <summary>I1 (AS-4) — the defect: the attacker names their own fab.</summary>
    [Fact]
    public async Task A_munich_operator_naming_its_own_fab_cannot_rotate_a_dresden_webhook_client()
    {
        using HttpClient dresden = await DresdenClientAsync();
        (string name, int version, _) = await RegisterAndRotateAsync(dresden);

        using HttpClient munich = await aspire.CreateAdminClientAsync("identity");
        HttpResponseMessage attack = await munich.SendAsync(Conditional(name, version, "munich"));

        string body = await attack.Content.ReadAsStringAsync();
        attack.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionFailed,
            $"a munich-only operator naming its own fab must find no client to rotate; got "
            + $"{(int)attack.StatusCode} {body}");
        (await ProblemTitleAsync(attack)).ShouldBe("WEBHOOK_CLIENT_NOT_FOUND");
        body.ShouldNotContain(
            "clientSecret",
            customMessage: $"no clientSecret may appear anywhere in the refusal body — that would be "
                + $"Dresden's credential disclosed to munich. Body: {body}");
    }

    /// <summary>
    /// I2 (AS-4 evidence) — proof the attack never reached Keycloak, read back
    /// through the provider's own Admin API rather than our
    /// <c>registered_clients</c> row (spec 121, #2165/#2207, is the proof that
    /// row can disagree with Keycloak).
    /// </summary>
    [Fact]
    public async Task Dresdens_keycloak_secret_is_unchanged_after_a_cross_fab_rotation_attempt()
    {
        using HttpClient dresden = await DresdenClientAsync();
        (string name, int version, string originalSecret) = await RegisterAndRotateAsync(dresden);

        using HttpClient munich = await aspire.CreateAdminClientAsync("identity");
        await munich.SendAsync(Conditional(name, version, "munich"));

        string currentSecret = await ReadKeycloakSecretAsync($"webhook-{name}");

        currentSecret.ShouldBe(
            originalSecret,
            "a refused cross-fab rotation must never reach Keycloak; a changed secret here means the "
            + "attack actually rolled Dresden's live credential — the disclosure-plus-destruction this "
            + "spec exists to close");
    }

    /// <summary>I3 (AS-3 regression) — naming the victim's own fab; already refused today.</summary>
    [Fact]
    public async Task A_munich_operator_naming_dresdens_fab_cannot_rotate_a_dresden_webhook_client()
    {
        using HttpClient dresden = await DresdenClientAsync();
        (string name, int version, _) = await RegisterAndRotateAsync(dresden);

        using HttpClient munich = await aspire.CreateAdminClientAsync("identity");
        HttpResponseMessage attack = await munich.SendAsync(Conditional(name, version, "dresden"));

        attack.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"naming the victim's fab must be refused by the guard before any lookup runs; got "
            + $"{(int)attack.StatusCode} {await attack.Content.ReadAsStringAsync()}");
        (await ProblemTitleAsync(attack)).ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>I4 — the legitimate path, which the fix must not break.</summary>
    [Fact]
    public async Task A_dresden_operator_rotates_its_own_webhook_client()
    {
        using HttpClient dresden = await DresdenClientAsync();
        (string name, int version, string originalSecret) = await RegisterAndRotateAsync(dresden);

        HttpResponseMessage rotated = await dresden.SendAsync(Conditional(name, version, "dresden"));

        rotated.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"a caller who genuinely holds the target's fab must still be able to rotate it; got "
            + $"{(int)rotated.StatusCode} {await rotated.Content.ReadAsStringAsync()}");
        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("clientSecret").GetString().ShouldNotBe(originalSecret);
        body.GetProperty("version").GetInt32().ShouldBeGreaterThan(version);
    }

    private Task<HttpClient> DresdenClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "op-dresden@dresden.test", "Operator1234");

    /// <summary>
    /// Creates a fresh dresden webhook integration and performs its first
    /// rotation (<c>If-None-Match: *</c>), returning the name, the version the
    /// next rotation must send, and the secret this call minted.
    /// </summary>
    private static async Task<(string Name, int Version, string Secret)> RegisterAndRotateAsync(
        HttpClient dresden)
    {
        string name = $"t182-{Guid.CreateVersion7():N}";

        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = "dresden" }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        HttpResponseMessage created = await dresden.SendAsync(request);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return (name, body.GetProperty("version").GetInt32(), body.GetProperty("clientSecret").GetString()!);
    }

    private static HttpRequestMessage Conditional(string name, int version, string fab)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = fab }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    private async Task<string> ReadKeycloakSecretAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);
        string uuid = clients.EnumerateArray().Single().GetProperty("id").GetString()!;

        JsonElement secret = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients/{uuid}/client-secret",
            CancellationToken.None);

        return secret.GetProperty("value").GetString()!;
    }
}
