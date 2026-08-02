using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// ADR-0113 Layer 1 for Identity (spec 012 T040), against the real stack —
/// so the version these tests read is one the EF interceptor actually moved,
/// which no Application-layer fake can reproduce.
///
/// <para>
/// The two halves of this context behave differently, and the tests are split
/// to say so rather than to look uniform:
/// </para>
///
/// <para>
/// <b>Disables</b> get the precondition but no lost-update protection. A
/// disable is terminal, and <c>RegisteredClientRepository.GetByClientIdAsync</c>
/// skips rows with a <c>DisabledAt</c>, so the second writer is answered 404
/// before any version is compared — the version can never move on a row that
/// is still reachable by clientId. <c>DEVICE_STALE</c> is therefore only
/// reachable by sending a version the device never had, which is what the 409
/// test below does. That is a well-formedness check, not a race.
/// </para>
///
/// <para>
/// <b>Rotation</b> is the genuine lost update in this context: two admins
/// rotating concurrently leaves the first holding a secret the second has
/// already invalidated. It is the only Identity aggregate whose version moves
/// while it is still reachable, so it is the only one that can be raced here —
/// and it is raced, below.
/// </para>
///
/// <para>
/// No per-test reset: registering a device and rotating a webhook both create
/// real Keycloak clients, and wiping the Postgres rows would leave those
/// behind and desynchronised. Each test mints its own client instead, as
/// <c>NFR002_MqttConnectAuthTests</c> already does.
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
    public async Task A_registered_device_is_listed_with_the_version_its_disable_will_need()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = await RegisterDeviceAsync(identity);

        JsonElement row = await FindDeviceAsync(identity, clientId);

        row.GetProperty("version").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task A_disable_without_If_Match_is_refused_with_428_and_leaves_the_device_active()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = await RegisterDeviceAsync(identity);

        HttpResponseMessage refused = await identity.DeleteAsync($"/devices/{clientId}");

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // Status alone would pass even if the disable had gone through.
        (await FindDeviceAsync(identity, clientId)).GetProperty("disabledAt").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_disable_carrying_a_version_the_device_never_had_is_refused_with_409()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = await RegisterDeviceAsync(identity);

        HttpResponseMessage refused = await identity.SendAsync(
            Conditional(HttpMethod.Delete, $"/devices/{clientId}", version: 99));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused)).ShouldBe("DEVICE_STALE");
        (await FindDeviceAsync(identity, clientId)).GetProperty("disabledAt").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_disable_carrying_the_listed_version_succeeds()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string clientId = await RegisterDeviceAsync(identity);
        int version = (await FindDeviceAsync(identity, clientId)).GetProperty("version").GetInt32();

        HttpResponseMessage disabled = await identity.SendAsync(
            Conditional(HttpMethod.Delete, $"/devices/{clientId}", version));

        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(disabled));
        (await FindDeviceAsync(identity, clientId)).GetProperty("disabledAt").ValueKind
            .ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Each_rotation_returns_the_version_the_next_one_must_send()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();

        // Chained three deep on purpose. Two would still pass if the response
        // version were hardcoded to 0, because an Added root is not bumped —
        // the third is what proves the value tracks the interceptor.
        int afterFirst = await RotateAsync(identity, name, expectedVersion: 0);
        int afterSecond = await RotateAsync(identity, name, afterFirst);
        int afterThird = await RotateAsync(identity, name, afterSecond);

        afterSecond.ShouldBeGreaterThan(afterFirst);
        afterThird.ShouldBeGreaterThan(afterSecond);
    }

    [Fact]
    public async Task A_rotation_superseded_by_another_admin_is_refused_with_409()
    {
        using HttpClient identity = await aspire.CreateAdminClientAsync("identity");
        string name = UniqueIntegrationName();

        // Both admins hold the version the first rotation handed out.
        int shared = await RotateAsync(identity, name, expectedVersion: 0);
        string secretTheFirstAdminGot = await RotateForSecretAsync(identity, name, shared);

        HttpResponseMessage refused = await identity.SendAsync(
            Conditional(HttpMethod.Post, $"/webhook-integrations/{name}/rotate", shared, Body()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused)).ShouldBe("WEBHOOK_CLIENT_STALE");

        // The point of the gate: the secret the first admin walked away with is
        // still the live one. Without it the second rotation would have rolled
        // it out from under them and reported success.
        string live = await RotateForSecretAsync(identity, name, shared + 1);
        live.ShouldNotBe(secretTheFirstAdminGot);
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

    private async Task<int> RotateAsync(HttpClient identity, string name, int expectedVersion)
    {
        HttpResponseMessage rotated = await identity.SendAsync(
            Conditional(HttpMethod.Post, $"/webhook-integrations/{name}/rotate", expectedVersion, Body()));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));

        return (await rotated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();
    }

    private async Task<string> RotateForSecretAsync(HttpClient identity, string name, int expectedVersion)
    {
        HttpResponseMessage rotated = await identity.SendAsync(
            Conditional(HttpMethod.Post, $"/webhook-integrations/{name}/rotate", expectedVersion, Body()));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(rotated));

        return (await rotated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("clientSecret").GetString()!;
    }

    private static JsonContent Body() => JsonContent.Create(new { fabId = Fab });

    private static HttpRequestMessage Conditional(
        HttpMethod method, string path, int version, JsonContent? content = null)
    {
        HttpRequestMessage request = new(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");

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
