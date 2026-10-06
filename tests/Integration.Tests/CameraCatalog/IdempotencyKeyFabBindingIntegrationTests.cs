using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 302 (#2492) — the fab dimension. <c>IdempotencyScope</c> carries no fab,
/// so <c>op-multi</c> — who holds both <c>munich</c> and <c>dresden</c> — can
/// reuse one key across the two plants and get the first plant's camera
/// identifier handed back under the second plant's name, with nothing actually
/// created there.
///
/// <para>
/// <b>The headline scenario</b> is
/// <see cref="A_key_reused_for_the_same_camera_in_another_fab_is_refused_and_creates_nothing"/>.
/// After spec 302 lands, the scope also carries the <i>resolved</i> fab, and a
/// mismatch answers <c>422 IDEMPOTENCY_KEY_REUSED</c> instead of replaying.
/// </para>
///
/// <para>
/// Mirrors <c>CameraFabResolutionIntegrationTests</c>' principals and
/// <c>IdempotentCameraRegistrationIntegrationTests</c>' request shape. No
/// per-test reset: camera names are run-unique instead, since the fixture is
/// shared across the whole run.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyKeyFabBindingIntegrationTests(AspireFixture aspire)
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// The #2492 headline scenario: one key, one body, two fabs. Today: 201
    /// twice, the second carrying munich's identifier; dresden gets nothing.
    /// </summary>
    [Fact]
    public async Task A_key_reused_for_the_same_camera_in_another_fab_is_refused_and_creates_nothing()
    {
        using HttpClient cameras = await ClientFor(MultiFabOperator);
        string name = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage first = await SendAsync(cameras, "munich", name, key);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(first));

        HttpResponseMessage second = await SendAsync(cameras, "dresden", name, key);

        second.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used to create this camera in munich must not be honoured for dresden: "
            + await BodyAsync(second));
        (await TitleOfAsync(second)).ShouldBe("IDEMPOTENCY_KEY_REUSED");
        (await NamesInAsync(cameras, "dresden")).ShouldNotContain(
            name, "dresden must not have gained a camera from the replayed request");
    }

    /// <summary>
    /// The body dimension, within one fab: a different camera name under the
    /// same key must also be refused, not replayed as the first camera.
    /// </summary>
    [Fact]
    public async Task A_key_reused_for_a_different_camera_in_the_same_fab_is_refused()
    {
        using HttpClient cameras = await ClientFor(MultiFabOperator);
        string first = UniqueName();
        string second = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await SendAsync(cameras, "munich", first, key);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        HttpResponseMessage refused = await SendAsync(cameras, "munich", second, key);

        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "a key already used for the first camera must not be honoured for a different one: "
            + await BodyAsync(refused));
        (await TitleOfAsync(refused)).ShouldBe("IDEMPOTENCY_KEY_REUSED");
        (await NamesInAsync(cameras, "munich")).ShouldNotContain(second);
    }

    /// <summary>
    /// The fab guard must still run before the idempotency comparison: a
    /// caller with no access to munich is forbidden, not refused with 422 —
    /// even though the key it reuses was first used, successfully, in dresden.
    /// </summary>
    [Fact]
    public async Task The_fab_guard_refuses_before_a_reused_key_is_examined()
    {
        using HttpClient cameras = await ClientFor(DresdenOperator);
        string name = UniqueName();
        string key = $"key-{Guid.CreateVersion7():N}";

        HttpResponseMessage created = await SendAsync(cameras, "dresden", name, key);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        HttpResponseMessage refused = await SendAsync(cameras, "munich", UniqueName(), key);

        refused.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            "a caller with no access to munich must be told so, not given a 422 that would confirm the key "
            + "mechanism even looked at the request: " + await BodyAsync(refused));
    }

    /// <summary>
    /// Over-refusal guard: the key string is per-caller (<c>IdempotencyScope.Caller</c>),
    /// so a different caller reusing the same literal key is a fresh request
    /// and must still get 201. Must hold both before and after the fix.
    /// </summary>
    [Fact]
    public async Task A_key_reused_by_another_caller_is_a_fresh_request()
    {
        string sharedKey = $"key-{Guid.CreateVersion7():N}";
        using HttpClient dresdenOnly = await ClientFor(DresdenOperator);
        using HttpClient multi = await ClientFor(MultiFabOperator);

        HttpResponseMessage first = await SendAsync(dresdenOnly, "dresden", UniqueName(), sharedKey);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(first));

        HttpResponseMessage second = await SendAsync(multi, "dresden", UniqueName(), sharedKey);

        second.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            "a key is scoped to the authenticated caller; a second caller inventing the same string must "
            + "get a fresh registration, not 422 and not a replay: " + await BodyAsync(second));
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static Task<HttpResponseMessage> SendAsync(HttpClient cameras, string fab, string name, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/cameras?fabId={fab}")
        {
            Content = JsonContent.Create(Body(name)),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return cameras.SendAsync(request);
    }

    private static object Body(string name) => new
    {
        name,
        rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
    };

    private static async Task<string[]> NamesInAsync(HttpClient cameras, string fab)
    {
        HttpResponseMessage listed = await cameras.GetAsync($"/cameras?fabId={fab}&limit=200");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!)];
    }

    private static async Task<string?> TitleOfAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        return problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("title", out JsonElement title)
            ? title.GetString()
            : null;
    }

    private static string UniqueName() => $"Cam-{Guid.NewGuid():N}"[..12];

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"camera-catalog log:{Environment.NewLine}{aspire.RecentLogs("camera-catalog")}";
}
