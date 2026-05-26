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
    private readonly AspireFixture _aspire = aspire;

    public async Task InitializeAsync()
    {
        await _aspire.ResetMediaMtxAsync();
        await _aspire.ResetStreamDistributionAsync();
        await _aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Authorize_without_a_token_returns_401()
    {
        HttpResponseMessage response = await _aspire.StreamDistribution.PostAsJsonAsync(
            $"/streams/cam-{Guid.CreateVersion7()}/authorize",
            new { token = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authorize_with_an_invalid_path_returns_403()
    {
        string token = await _aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        HttpResponseMessage response = await _aspire.StreamDistribution.PostAsJsonAsync(
            "/streams/not-a-cam-guid/authorize",
            new { token });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorize_with_a_valid_admin_token_returns_200()
    {
        string token = await _aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        // Path doesn't need to exist for the auth check; absence falls
        // through to "stream not registered" which is treated as
        // "allow because the WHEP path will 404 later anyway".
        HttpResponseMessage response = await _aspire.StreamDistribution.PostAsJsonAsync(
            $"/streams/cam-{Guid.CreateVersion7()}/authorize",
            new { token });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorize_with_a_malformed_token_returns_401()
    {
        HttpResponseMessage response = await _aspire.StreamDistribution.PostAsJsonAsync(
            $"/streams/cam-{Guid.CreateVersion7()}/authorize",
            new { token = "this-is-not-a-jwt" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authorize_with_a_Bearer_prefix_strips_it_and_validates()
    {
        string token = await _aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        HttpResponseMessage response = await _aspire.StreamDistribution.PostAsJsonAsync(
            $"/streams/cam-{Guid.CreateVersion7()}/authorize",
            new { token = $"Bearer {token}" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
