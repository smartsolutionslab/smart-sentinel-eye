using System.Net;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// ADR-0171, security review. <see cref="MediaMtxReconcilerIntegrationTests"/>
/// and <see cref="MediaMtxRtspGatewayIntegrationTests"/> prove the gateway
/// still works once it is credentialed; <c>AuthorizeMediaMtxApiCommandHandlerTests</c>
/// and <c>MediaMtxApiAuthenticatorTests</c> prove the C# refusal logic in
/// isolation. None of those is the actual counterfactual the fix claims: that
/// MediaMTX <em>itself</em>, talked to directly on its real control-API port,
/// now refuses a caller presenting no credential and admits one presenting
/// the configured secret. This is that proof, end to end, with no C# handler
/// standing in for MediaMTX's own enforcement.
/// </summary>
[Collection(AspireCollection.Name)]
public class MediaMtxControlApiAuthIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync() => await aspire.ResetMediaMtxAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_uncredentialed_call_into_MediaMtxs_control_API_is_refused()
    {
        using HttpClient uncredentialed = aspire.App.CreateHttpClient("mediamtx", "api");

        HttpResponseMessage response = await uncredentialed.GetAsync("/v3/config/paths/list");

        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "mediamtx.yml no longer excludes `api` from the authorize hook (ADR-0171); an " +
            "uncredentialed control-API call must be refused by MediaMTX itself, not merely by " +
            "this service's own handler logic");
    }

    /// <summary>
    /// The control for the fact above, and the one that proves the fix did
    /// not just move the denial somewhere else: the same endpoint, the real
    /// configured secret, admitted.
    /// </summary>
    [Fact]
    public async Task A_credentialed_call_into_MediaMtxs_control_API_succeeds()
    {
        using HttpClient credentialed = aspire.CreateMediaMtxApiClient();

        HttpResponseMessage response = await credentialed.GetAsync("/v3/config/paths/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
