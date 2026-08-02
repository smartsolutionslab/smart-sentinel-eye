using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// ADR-0113 Layer 1 for Identity (spec 012 T040), against the real stack — so
/// the versions here are ones the EF interceptor actually moved, which no
/// Application-layer fake reproduces.
///
/// <para>
/// The gate applies to the webhook rotation only. The device and kiosk
/// disables were reviewed out: a disable is terminal and
/// <c>GetByClientIdAsync</c> stops returning the row, so their version cannot
/// move while they are still reachable, and requiring a precondition there
/// would have been a breaking change buying nothing.
/// </para>
///
/// <para>
/// Rotation is the genuine lost update: two admins rotating concurrently
/// leaves the first holding a secret the second invalidated. The conflict
/// test proves the refused rotation left the live credential working by
/// authenticating with it, not by comparing it to another freshly minted
/// secret — that comparison passes even when the credential has been
/// destroyed.
/// </para>
///
/// <para>
/// No per-test reset: rotating creates real Keycloak clients, and wiping the
/// Postgres rows would leave those behind and desynchronised. Each test mints
/// its own name, as <c>NFR002_MqttConnectAuthTests</c> already does.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RegisteredClientConcurrencyIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string Fab = "munich";

    public async Task InitializeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
        await aspire.App.ResourceNotifications
            .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Disabling_a_device_needs_no_precondition()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = await RegisterDeviceAsync(identity);

        HttpResponseMessage disabled = await identity.DeleteAsync($"/devices/{clientId}");

        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(disabled));
        (await FindDeviceAsync(identity, clientId)).GetProperty("disabledAt").ValueKind
            .ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_rotation_without_a_precondition_is_refused_with_428()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");

        HttpResponseMessage refused = await identity.PostAsync(
            $"/webhook-integrations/{UniqueIntegrationName()}/rotate", Body());

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task A_created_client_is_listed_with_the_version_its_next_rotation_needs()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        int returned = await CreateAsync(identity, name);

        // The read path exists so a caller who lost the rotation response is
        // not locked out of ever rotating again.
        (await FindWebhookAsync(identity, name)).GetProperty("version").GetInt32().ShouldBe(returned);
    }

    [Fact]
    public async Task Each_rotation_returns_the_version_the_next_one_must_send()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();

        // Chained three deep on purpose. Two would still pass if the response
        // version were frozen at 0, because an Added root is not bumped — the
        // third is what proves the value tracks the interceptor.
        int afterCreate = await CreateAsync(identity, name);
        int afterSecond = await RotateAsync(identity, name, afterCreate);
        int afterThird = await RotateAsync(identity, name, afterSecond);

        afterSecond.ShouldBeGreaterThan(afterCreate);
        afterThird.ShouldBeGreaterThan(afterSecond);
    }

    [Fact]
    public async Task A_rotation_superseded_by_another_admin_leaves_the_live_secret_working()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        int shared = await CreateAsync(identity, name);

        // Both admins hold `shared`. The first wins and its secret is live.
        (int version, string secret) winner = await RotateForSecretAsync(identity, name, shared);

        HttpResponseMessage refused = await identity.SendAsync(
            Conditional(name, shared, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_STALE");

        // The assertion that matters: the winner's credential still
        // authenticates. Comparing it to another freshly rotated secret would
        // pass even if the refused request had destroyed it.
        (await CanAuthenticateAsync($"webhook-{name}", winner.secret))
            .ShouldBeTrue("the refused rotation invalidated the live secret");
    }

    [Fact]
    public async Task Rotating_a_client_that_does_not_exist_creates_nothing()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();

        HttpResponseMessage refused = await identity.SendAsync(Conditional(name, 0, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_NOT_FOUND");
        (await ListWebhooksAsync(identity)).EnumerateArray()
            .ShouldNotContain(row => row.GetProperty("clientId").GetString() == $"webhook-{name}");
    }

    [Fact]
    public async Task Re_creating_an_existing_client_does_not_roll_its_secret()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();
        await CreateAsync(identity, name);
        (int version, string secret) live = await RotateForSecretAsync(identity, name, 0);

        // The replayed first-time rotation: If-None-Match: * against a client
        // that now exists.
        HttpResponseMessage refused = await identity.SendAsync(CreateConditional(name, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_ALREADY_EXISTS");
        (await CanAuthenticateAsync($"webhook-{name}", live.secret))
            .ShouldBeTrue("the refused create rolled the existing secret");
    }

    /// <summary>
    /// Client-credentials grant with the rotated secret. This is what the
    /// webhook sender does, so it is the only assertion that actually
    /// distinguishes a live credential from a dead one.
    /// </summary>
    private async Task<bool> CanAuthenticateAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage token = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);

        return token.IsSuccessStatusCode;
    }

    private async Task<string> RegisterDeviceAsync(HttpClient identity)
    {
        HttpResponseMessage created = await identity.PostAsJsonAsync(
            $"/devices/register?fabId={Fab}",
            new { deviceType = "plc", deviceIdentifier = $"t040-{Guid.CreateVersion7():N}" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));

        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("clientId").GetString()!;
    }

    private async Task<int> CreateAsync(HttpClient identity, string name)
    {
        HttpResponseMessage created = await identity.SendAsync(CreateConditional(name, Body()));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(created));

        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();
    }

    private async Task<int> RotateAsync(HttpClient identity, string name, int expectedVersion) =>
        (await RotateForSecretAsync(identity, name, expectedVersion)).Version;

    private async Task<(int Version, string Secret)> RotateForSecretAsync(
        HttpClient identity, string name, int expectedVersion)
    {
        HttpResponseMessage rotated = await identity.SendAsync(
            Conditional(name, expectedVersion, Body()));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));

        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("version").GetInt32(), body.GetProperty("clientSecret").GetString()!);
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

    /// <summary>
    /// The list is fab-wide and shared with every other test in the run, so the
    /// row is found by clientId rather than by position.
    /// </summary>
    private static async Task<JsonElement> FindDeviceAsync(HttpClient identity, string clientId)
    {
        HttpResponseMessage listed = await identity.GetAsync($"/devices?fabId={Fab}");
        listed.EnsureSuccessStatusCode();

        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return rows.EnumerateArray().Single(row =>
            string.Equals(row.GetProperty("clientId").GetString(), clientId, StringComparison.Ordinal));
    }

    private static JsonContent Body() => JsonContent.Create(new { fabId = Fab });

    private static HttpRequestMessage Conditional(string name, int version, JsonContent content)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

        return request;
    }

    private static HttpRequestMessage CreateConditional(string name, JsonContent content)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        return request;
    }

    private static async Task<string> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    // Keycloak's client-id grammar allows letters, digits, '.', '_' and '-',
    // and the handler prefixes this with "webhook-".
    private static string UniqueIntegrationName() => $"t040-{Guid.CreateVersion7():N}";

    /// <summary>
    /// A bare "500" tells a reader nothing, and CI has no other route to the
    /// service's stack trace. Attach the response body and Identity's recent
    /// output so an unexpected status is diagnosable from the CI log alone.
    /// </summary>
    private async Task<string> DiagnoseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}identity log:{Environment.NewLine}{aspire.RecentLogs("identity")}";
    }
}
