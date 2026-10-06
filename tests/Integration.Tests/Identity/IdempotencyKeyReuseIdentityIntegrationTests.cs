using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 302 (#2424/#2492) — the resource dimension. <c>IdempotencyScope</c> is
/// <c>(Key, Endpoint, Caller)</c> only, so a caller who reuses one key for a
/// <b>different</b> request on the same endpoint gets the first request's
/// answer replayed — not refused — regardless of what the second request
/// actually named.
///
/// <para>
/// <b>The headline exploit</b> is
/// <see cref="A_key_reused_to_rotate_a_different_webhook_integration_is_refused_and_discloses_no_secret"/>:
/// rotating <c>beta</c> with a key already used for <c>alpha</c> answers 200
/// with <c>alpha</c>'s live secret, labelled as <c>beta</c>'s
/// (<c>WebhookRotationEndpoints</c>'s replay lambda builds the DTO from
/// <i>this</i> request's route <c>name</c>, but the secret comes from the
/// replayed identifier — <c>alpha</c>'s). <c>beta</c> is never created.
/// </para>
///
/// <para>
/// After spec 302 lands, <c>IdempotencyScope</c> carries a fingerprint over the
/// bound request (plus, for rotation, the route <c>{name}</c> and the upsert
/// precondition), and a mismatch answers
/// <c>422 IDEMPOTENCY_KEY_REUSED</c> before any replay or any work runs.
/// </para>
///
/// <para>
/// Every test mints its own names, as <c>RegisteredClientConcurrencyIntegrationTests</c>
/// does: these write real Keycloak clients, and nothing here resets Postgres
/// rows between runs.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyKeyReuseIdentityIntegrationTests(AspireFixture aspire)
{
    private const string Fab = "munich";

    /// <summary>
    /// The #2424 headline scenario. Today: 200, with <c>alpha</c>'s secret
    /// disclosed under <c>beta</c>'s name. After the fix: 422, no secret, and
    /// <c>beta</c> was never created.
    /// </summary>
    [Fact]
    public async Task A_key_reused_to_rotate_a_different_webhook_integration_is_refused_and_discloses_no_secret()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string alpha = UniqueIntegrationName();
        string beta = UniqueIntegrationName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage first = await identity.SendAsync(CreateConditional(alpha, key));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(first));

        HttpResponseMessage second = await identity.SendAsync(CreateConditional(beta, key));

        second.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used to create alpha must not be honoured for a different integration (beta): "
            + await DiagnoseAsync(second));
        JsonElement secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        TitleOf(secondBody).ShouldBe("IDEMPOTENCY_KEY_REUSED");
        secondBody.TryGetProperty("clientSecret", out _).ShouldBeFalse(
            "a refusal must not carry a client secret — today it discloses alpha's, mislabelled as beta's");
        (await ListWebhooksAsync(identity)).EnumerateArray().ShouldNotContain(
            row => string.Equals(row.GetProperty("clientId").GetString(), $"webhook-{beta}", StringComparison.Ordinal),
            "beta must never have been created by the replayed request");
    }

    /// <summary>
    /// Same integration, but the second request is a <b>rotate</b> (If-Match)
    /// where the first was a <b>create</b> (If-None-Match: *) — two different
    /// operations under one key. Today the rotate replays the create's answer
    /// (the same secret, unrotated) instead of actually rotating or being
    /// refused.
    /// </summary>
    [Fact]
    public async Task A_key_reused_to_rotate_the_same_integration_under_a_different_precondition_is_refused()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await identity.SendAsync(CreateConditional(name, key));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));
        int version = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();

        HttpResponseMessage rotated = await identity.SendAsync(RotateConditional(name, version, key));

        rotated.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key used for the create must not also answer the rotate — they are two different operations "
            + "under the same integration: " + await DiagnoseAsync(rotated));
        TitleOf(await rotated.Content.ReadFromJsonAsync<JsonElement>()).ShouldBe("IDEMPOTENCY_KEY_REUSED");

        JsonElement listed = await FindWebhookAsync(identity, name);
        listed.GetProperty("version").GetInt32().ShouldBe(
            version, "the refused rotate must not have changed the client's version");
    }

    /// <summary>
    /// Positive control: a genuine retry — same integration, same precondition,
    /// same key — must keep replaying. This must hold both before and after the
    /// fix; a test suite that only ever answers 422 for a reused key would also
    /// break every legitimate retry.
    /// </summary>
    [Fact]
    public async Task A_repeated_rotation_with_the_same_key_and_request_still_replays_the_same_secret()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage first = await identity.SendAsync(CreateConditional(name, key));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(first));
        JsonElement firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();

        HttpResponseMessage second = await identity.SendAsync(CreateConditional(name, key));

        second.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(second));
        JsonElement secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("clientSecret").GetString().ShouldBe(
            firstBody.GetProperty("clientSecret").GetString(),
            "a genuine retry of the same request must still replay the same secret");
        secondBody.GetProperty("registeredClientIdentifier").GetGuid().ShouldBe(
            firstBody.GetProperty("registeredClientIdentifier").GetGuid());
    }

    /// <summary>
    /// Device-registration twin of the webhook exploit: D2's registration
    /// never happens, but today's reply hands back D1's live client secret as
    /// if it belonged to D2.
    /// </summary>
    [Fact]
    public async Task A_key_reused_to_register_a_different_device_is_refused_and_returns_no_secret()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string deviceOne = NewDeviceIdentifier();
        string deviceTwo = NewDeviceIdentifier();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage first = await RegisterDeviceAsync(identity, deviceOne, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(first));

        HttpResponseMessage second = await RegisterDeviceAsync(identity, deviceTwo, key);

        second.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used to register device one must not be honoured for device two: "
            + await DiagnoseAsync(second));
        JsonElement secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        TitleOf(secondBody).ShouldBe("IDEMPOTENCY_KEY_REUSED");
        secondBody.TryGetProperty("clientSecret", out _).ShouldBeFalse(
            "a refusal must not disclose device one's secret under device two's name");
        (await ListDevicesAsync(identity)).EnumerateArray().ShouldNotContain(
            row => string.Equals(row.GetProperty("clientId").GetString(), $"plc-{deviceTwo}", StringComparison.Ordinal),
            "device two must never have been registered by the replayed request");
    }

    /// <summary>Kiosk-enrolment twin of the same exploit.</summary>
    [Fact]
    public async Task A_key_reused_to_enroll_a_different_kiosk_is_refused_and_returns_no_secret()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string kioskOne = NewKioskIdentifier();
        string kioskTwo = NewKioskIdentifier();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage first = await EnrollKioskAsync(identity, kioskOne, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(first));

        HttpResponseMessage second = await EnrollKioskAsync(identity, kioskTwo, key);

        second.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used to enroll kiosk one must not be honoured for kiosk two: "
            + await DiagnoseAsync(second));
        JsonElement secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        TitleOf(secondBody).ShouldBe("IDEMPOTENCY_KEY_REUSED");
        secondBody.TryGetProperty("clientSecret", out _).ShouldBeFalse(
            "a refusal must not disclose kiosk one's secret under kiosk two's name");
        (await ListKiosksAsync(identity)).EnumerateArray().ShouldNotContain(
            row => string.Equals(row.GetProperty("clientId").GetString(), kioskTwo, StringComparison.Ordinal),
            "kiosk two must never have been enrolled by the replayed request");
    }

    private static string UniqueIntegrationName() => $"t302-{Guid.CreateVersion7():N}";

    private static string NewDeviceIdentifier() => $"t302-{Guid.CreateVersion7():N}";

    private static string NewKioskIdentifier() => $"t302-{Guid.CreateVersion7():N}";

    private static HttpRequestMessage CreateConditional(string name, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static HttpRequestMessage RotateConditional(string name, int expectedVersion, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = Fab }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    private static Task<HttpResponseMessage> RegisterDeviceAsync(HttpClient identity, string device, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/devices/register?fabId={Fab}")
        {
            Content = JsonContent.Create(new { deviceType = "plc", deviceIdentifier = device }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return identity.SendAsync(request);
    }

    private static Task<HttpResponseMessage> EnrollKioskAsync(HttpClient identity, string clientId, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/kiosks/enroll?fabId={Fab}")
        {
            Content = JsonContent.Create(new { clientId }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return identity.SendAsync(request);
    }

    private static async Task<JsonElement> ListWebhooksAsync(HttpClient identity)
    {
        HttpResponseMessage listed = await identity.GetAsync($"/webhook-integrations?fabId={Fab}");
        listed.EnsureSuccessStatusCode();

        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> FindWebhookAsync(HttpClient identity, string name) =>
        (await ListWebhooksAsync(identity)).EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("clientId").GetString(), $"webhook-{name}", StringComparison.Ordinal));

    private static async Task<JsonElement> ListDevicesAsync(HttpClient identity)
    {
        HttpResponseMessage listed = await identity.GetAsync($"/devices?fabId={Fab}");
        listed.EnsureSuccessStatusCode();

        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> ListKiosksAsync(HttpClient identity)
    {
        HttpResponseMessage listed = await identity.GetAsync($"/kiosks?fabId={Fab}");
        listed.EnsureSuccessStatusCode();

        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Takes an already-read body rather than the response, because
    /// <see cref="HttpContent"/> cannot be read twice — a caller that also
    /// needs other fields off the same body (e.g. <c>clientSecret</c>) must
    /// read once and pass the parsed <see cref="JsonElement"/> to both.
    /// </summary>
    private static string? TitleOf(JsonElement problem) =>
        problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("title", out JsonElement title)
            ? title.GetString()
            : null;

    private async Task<string> DiagnoseAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"identity log:{Environment.NewLine}{aspire.RecentLogs("identity")}";
}
