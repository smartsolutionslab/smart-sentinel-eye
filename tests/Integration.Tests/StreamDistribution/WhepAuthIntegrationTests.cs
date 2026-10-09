using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// MediaMTX calls back to StreamDistribution.Api on every WHEP open
/// (FR-007). The endpoint is AllowAnonymous at the routing layer; the
/// handler validates the forwarded bearer + checks scope + checks stream
/// state. These tests hit the endpoint directly with various tokens.
/// </summary>
[Collection(AspireCollection.Name)]
public class WhepAuthIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string OperatorPassword = SeededCredentials.Op3Munich;
    private const string WallBerlinUsername = "wall-berlin";
    private const string WallBerlinPassword = SeededCredentials.WallBerlin;

    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Authorize_without_a_token_returns_401()
    {
        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token = (string?)null, path = $"cam-{Guid.CreateVersion7()}", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authorize_with_an_invalid_path_returns_403()
    {
        string token = await aspire.GetAdminAccessTokenAsync();

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token, path = "not-a-cam-guid", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorize_with_a_valid_admin_token_returns_200()
    {
        string token = await aspire.GetAdminAccessTokenAsync();

        // Path doesn't need to exist for the auth check; absence falls
        // through to "stream not registered" which is treated as
        // "allow because the WHEP path will 404 later anyway".
        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token, path = $"cam-{Guid.CreateVersion7()}", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorize_with_a_malformed_token_returns_401()
    {
        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token = "this-is-not-a-jwt", path = $"cam-{Guid.CreateVersion7()}", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authorize_with_a_Bearer_prefix_strips_it_and_validates()
    {
        string token = await aspire.GetAdminAccessTokenAsync();

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token = $"Bearer {token}", path = $"cam-{Guid.CreateVersion7()}", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    /// <summary>
    /// The broadest token this hook ever sees, on the action it never grants.
    /// Today nothing but MediaMTX's own refusal of publishers on a path with a
    /// static <c>source</c> stands between a caller and a publish — another
    /// component's configuration file, not this endpoint.
    /// </summary>
    [Fact]
    public async Task Authorize_a_publish_with_a_valid_admin_token_returns_403()
    {
        string token = await aspire.GetAdminAccessTokenAsync();

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token, path = $"cam-{Guid.CreateVersion7()}", action = "publish" });

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ADR-0161. A real, single-fab (<c>berlin</c>) token against a real
    /// stream provisioned in <c>munich</c>: a wall or operator holding the
    /// read scope, but not the fab, must not reach the camera's video.
    /// </summary>
    /// <remarks>
    /// Minted via the cached admin-token helper's client (<c>management-web</c>,
    /// <c>directAccessGrantsEnabled: true</c>), not <c>kiosk-wall</c>: that
    /// client is PKCE-only (<c>directAccessGrantsEnabled: false</c>, by
    /// design — spec 052 wall sign-in never accepts a password directly), so
    /// a resource-owner password grant against it is refused before this
    /// test ever reaches the WHEP hook, on Keycloak's own
    /// <c>unauthorized_client</c>, not on anything this fix touches. The
    /// <c>wall-berlin</c> user's <c>groups</c> claim (the thing actually
    /// under test) is identical however the token was minted.
    /// </remarks>
    [Fact]
    public async Task Authorize_for_a_stream_in_another_fab_is_refused()
    {
        Guid camera = await RegisterCameraInMunichAsync();
        await WaitForStreamAsync(camera);

        string berlinToken = await aspire.GetAccessTokenAsync(WallBerlinUsername, WallBerlinPassword);

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token = berlinToken, path = $"cam-{camera}", action = "read" });

        // Not merely 403 — Forbidden (scope) and StreamUnavailable are also
        // 403, and a regression that dropped the fab check for one that
        // reused either would still pass a status-only assertion.
        await AssertStatusAndTitleAsync(response, HttpStatusCode.Forbidden, "WHEP_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// The control for the fact above: the same munich stream, a token that
    /// actually names munich, admitted. Without this, a hook that refused
    /// every WHEP open would also satisfy the cross-fab fact.
    /// </summary>
    [Fact]
    public async Task Authorize_for_a_stream_in_the_callers_own_fab_returns_200()
    {
        Guid camera = await RegisterCameraInMunichAsync();
        await WaitForStreamAsync(camera);

        string munichToken = await aspire.GetAccessTokenAsync(MunichOperator, OperatorPassword);

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token = munichToken, path = $"cam-{camera}", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    /// <summary>
    /// ADR-0116 applied to the WHEP path: a stream with no fab attributed
    /// yet — the shape a pre-spec-016 legacy row has before
    /// <c>StreamFabAttributionService</c> backfills it — is refused to
    /// everyone, not merely to callers outside a fab it doesn't have. The
    /// domain's own public surface (<c>Stream.Provision</c>) never produces
    /// this state deliberately (every new stream is provisioned with its
    /// fab already known); reached here by nulling the column directly,
    /// the same shape EF's own materialisation of a genuine legacy row would
    /// have — not something achievable through any command this product
    /// exposes.
    /// </summary>
    [Fact]
    public async Task Authorize_for_a_stream_with_no_fab_attributed_yet_is_refused()
    {
        Guid camera = await RegisterCameraInMunichAsync();
        await WaitForStreamAsync(camera);

        await using StreamDistributionDbContext db = await aspire.CreateStreamDistributionDbContextAsync();
        int updated = await db.Database.ExecuteSqlAsync($"UPDATE streams SET fab = NULL WHERE camera_id = {camera}");
        updated.ShouldBe(1, "the stream this test just provisioned should be the only row updated");

        string munichToken = await aspire.GetAccessTokenAsync(MunichOperator, OperatorPassword);

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token = munichToken, path = $"cam-{camera}", action = "read" });

        await AssertStatusAndTitleAsync(response, HttpStatusCode.Forbidden, "WHEP_FAB_NOT_AUTHORIZED");
    }

    private async Task<Guid> RegisterCameraInMunichAsync()
    {
        using HttpClient cameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", MunichOperator, OperatorPassword);

        HttpResponseMessage created = await cameras.PostAsJsonAsync(
            "/cameras?fabId=munich",
            new
            {
                name = $"Cam-{Guid.NewGuid():N}"[..12],
                rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task WaitForStreamAsync(Guid camera)
    {
        using HttpClient streams = await aspire.CreateAuthenticatedClientAsync(
            "stream-distribution", MunichOperator, OperatorPassword);
        DateTime deadline = DateTime.UtcNow + ProvisionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await streams.GetAsync($"/streams/{camera}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not appear within {ProvisionTimeout.TotalSeconds:F0}s.{Environment.NewLine}" +
            $"stream-distribution log:{Environment.NewLine}{aspire.RecentLogs("stream-distribution")}");
    }

    // Asserts the status and, on mismatch, surfaces the response body. The
    // StreamDistribution API runs the developer exception page in the E2E
    // stack, so a 500 body carries the server-side stack — making a CI-only
    // WHEP authorize failure (passes locally) diagnosable from the test log.
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected,
            $"unexpected status. response body:\n{(body.Length > 4000 ? body[..4000] : body)}");
    }

    /// <summary>
    /// Like <see cref="AssertStatusAsync"/>, plus the problem body's
    /// <c>title</c> — several of this handler's refusals share a status, so
    /// the status alone cannot tell a regression that swapped in a different
    /// one of them from a genuine pass.
    /// </summary>
    private static async Task AssertStatusAndTitleAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedTitle)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expectedStatus, $"unexpected status. response body:\n{body}");

        string? title = JsonDocument.Parse(body).RootElement.GetProperty("title").GetString();
        title.ShouldBe(expectedTitle, $"unexpected problem title. response body:\n{body}");
    }
}
