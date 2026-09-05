using SmartSentinelEye.Integration.Tests.Fixtures;

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
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token, path = "not-a-cam-guid", action = "read" });

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorize_with_a_valid_admin_token_returns_200()
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

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
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

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
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        HttpResponseMessage response = await aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/authorize",
            new { token, path = $"cam-{Guid.CreateVersion7()}", action = "publish" });

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
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
}
