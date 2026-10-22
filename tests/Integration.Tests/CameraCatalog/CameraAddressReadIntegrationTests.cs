using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Integration.Tests.Identity;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 438 (#3016, ADR-0179) - the writer's full-address read, on the real stack.
///
/// <para>
/// <b>Red on unchanged code:</b> the route does not exist, so a writer's 200 is a 404 and no audit row ever
/// appears. The refusals are asserted next to a 200 for the same caller's own camera (the positive control),
/// so a 404 cannot pass as a refusal.
/// </para>
///
/// <para>
/// The read-only principal is a planted password-grant client holding a fab and <c>sse.cameras.read</c> only
/// (pattern: <c>VariableReadScopeIntegrationTests</c>), because the fixture's default token names every
/// <c>sse.*</c> scope and would answer 200.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CameraAddressReadIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string MunichOperatorPassword = SeededCredentials.Op3Munich;
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string DresdenOperatorPassword = SeededCredentials.OpDresden;
    private const string ReadOnlyUser = "operator";
    private const string ReadOnlyPassword = SeededCredentials.Operator;
    private const string Host = "10.0.5.98";

    private static readonly string[] ReadOnlyScopes =
        ["sse-identity", "sse-audience", "sse-groups", "sse.cameras.read"];

    private readonly RealmProbe realm = new(aspire);

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_writer_reads_the_full_address_with_the_version_an_ETag_and_no_store()
    {
        var marker = Marker();
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var camera = await RegisterAsync(cameras, StoredAddress(marker));
        var versionBefore = await VersionOfAsync(cameras, camera);

        var response = await cameras.GetAsync($"/cameras/{camera}/address");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(response));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rtspUrl").GetString().ShouldBe(StoredAddress(marker));
        body.GetProperty("version").GetInt32().ShouldBe(versionBefore);
        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.ETag.Tag.ShouldBe($"\"{versionBefore}\"");
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();

        (await VersionOfAsync(cameras, camera)).ShouldBe(versionBefore, "a read must not change the camera");
    }

    [Fact]
    public async Task A_caller_holding_only_the_read_scope_is_refused_with_403_and_no_address()
    {
        var marker = Marker();
        using var writer = await ClientFor(MunichOperator, MunichOperatorPassword);
        var camera = await RegisterAsync(writer, StoredAddress(marker));
        var control = await writer.GetAsync($"/cameras/{camera}/address");
        control.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"the positive control failed: a writer cannot read the address either. {await BodyAsync(control)}");

        var clientId = $"camera-address-probe-{Guid.CreateVersion7():N}";
        await realm.PlantPasswordGrantClientAsync(clientId, ReadOnlyScopes, CancellationToken.None);

        HttpResponseMessage refused;
        try
        {
            using var readOnly = aspire.CreateServiceClient("camera-catalog");
            readOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await aspire.GetAccessTokenForClientAsync(clientId, ReadOnlyUser, ReadOnlyPassword, "openid"));
            refused = await readOnly.GetAsync($"/cameras/{camera}/address");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain("rtsp");
    }

    [Fact]
    public async Task A_request_without_a_token_is_refused_with_401()
    {
        using var anonymous = aspire.CreateServiceClient("camera-catalog");

        var response = await anonymous.GetAsync($"/cameras/{Guid.CreateVersion7()}/address");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Another_fabs_camera_and_an_unknown_one_are_refused_alike_beside_a_200_for_the_cameras_own_fab()
    {
        using var munich = await ClientFor(MunichOperator, MunichOperatorPassword);
        var inMunich = await RegisterAsync(munich, StoredAddress(Marker()));

        // Positive control: the route exists and answers 200 for the camera's own fab, so the 404s below
        // are refusals and not an absent route.
        var own = await munich.GetAsync($"/cameras/{inMunich}/address");
        own.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(own));

        using var dresden = await ClientFor(DresdenOperator, DresdenOperatorPassword);

        var crossFab = await dresden.GetAsync($"/cameras/{inMunich}/address");
        var neverExisted = await dresden.GetAsync($"/cameras/{Guid.CreateVersion7()}/address");

        crossFab.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        neverExisted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var crossFabBody = await crossFab.Content.ReadAsStringAsync();
        var unknownBody = await neverExisted.Content.ReadAsStringAsync();
        crossFabBody.ShouldNotContain("rtsp");
        unknownBody.ShouldNotContain("rtsp");

        using var crossFabProblem = JsonDocument.Parse(crossFabBody);
        using var unknownProblem = JsonDocument.Parse(unknownBody);
        crossFabProblem.RootElement.GetProperty("title").GetString()
            .ShouldBe(unknownProblem.RootElement.GetProperty("title").GetString());
        crossFabProblem.RootElement.GetProperty("status").GetInt32()
            .ShouldBe(unknownProblem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Only_the_successful_read_is_audited_once_without_the_address()
    {
        var marker = Marker();
        using var cameras = await ClientFor(MunichOperator, MunichOperatorPassword);
        var camera = await RegisterAsync(cameras, StoredAddress(marker));

        // Refusals first: none of them may leave a row.
        using var dresden = await ClientFor(DresdenOperator, DresdenOperatorPassword);
        (await dresden.GetAsync($"/cameras/{camera}/address")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cameras.GetAsync($"/cameras/{Guid.CreateVersion7()}/address")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var read = await cameras.GetAsync($"/cameras/{camera}/address");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(read));

        var row = await PollForAuditRowAsync(camera);
        await Task.Delay(TimeSpan.FromSeconds(3));
        (await CountAsync(camera)).ShouldBe(1, "exactly one row: the refusals left none and the read left one");

        row.Actor.ShouldBe(SubjectOf(cameras));
        row.Fab.ShouldBe("munich");
        row.Payload.ShouldNotContain("rtsp");
        row.Payload.ShouldNotContain(Host);
        row.Payload.ShouldNotContain(marker);
    }

    private async Task<HttpClient> ClientFor(string username, string password) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, password);

    private static string Marker() => $"PWMARK-{Guid.CreateVersion7():N}";

    private static string StoredAddress(string marker) => $"rtsp://{Host}/stream/{marker}?token={marker}";

    private static Guid SubjectOf(HttpClient client)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return Guid.Parse(handler.ReadJwtToken(token).Subject);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}";

    private static async Task<int> VersionOfAsync(HttpClient cameras, Guid camera)
    {
        var read = await cameras.GetAsync($"/cameras/{camera}");
        read.EnsureSuccessStatusCode();

        return (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
    }

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string rtspUrl)
    {
        var created = await cameras.PostAsJsonAsync(
            "/cameras",
            new { name = $"read-{Guid.CreateVersion7():N}"[..24], rtspUrl });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<(Guid Actor, string Fab, string Payload)> PollForAuditRowAsync(Guid camera)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

            var rows = await context.Database
                .SqlQuery<string>($"""
                    SELECT actor_identifier::text || '|' || (payload->>'Fab') || '|' || payload::text AS "Value"
                    FROM audit_events
                    WHERE event_kind = 'CameraAddressReadV1'
                      AND resource_identifier = {camera.ToString()}
                    """)
                .ToListAsync();

            if (rows.Count > 0)
            {
                var parts = rows[0].Split('|', 3);

                return (Guid.Parse(parts[0]), parts[1], parts[2]);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No CameraAddressReadV1 audit row for camera {camera} within 30s.");
    }

    private async Task<int> CountAsync(Guid camera)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

        var rows = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = 'CameraAddressReadV1'
                  AND resource_identifier = {camera.ToString()}
                """)
            .ToListAsync();

        return rows[0];
    }
}
