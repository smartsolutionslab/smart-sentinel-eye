using System.Net;
using System.Net.Http.Headers;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// The behaviour three clients used to spell as a write to
/// <c>HttpClient.DefaultRequestHeaders</c>, and which none of them tested.
/// </summary>
public class AuthorizingHandlerTests
{
    [Fact]
    public async Task The_credential_is_set_on_the_request()
    {
        RecordingHandler downstream = new();
        using HttpClient client = Compose(new StubAuthorizingHandler(_ => new("Bearer", "a-token")), downstream);

        await client.GetAsync("https://catalog.test/cameras", CancellationToken.None);

        downstream.LastAuthorization?.Scheme.ShouldBe("Bearer");
        downstream.LastAuthorization?.Parameter.ShouldBe("a-token");
    }

    /// <summary>
    /// The property the DefaultRequestHeaders version did not have: two requests
    /// through the same client carry two different credentials. LayoutComposition
    /// forwards the caller's own token, so this is the difference between an
    /// operator seeing their fab and an operator seeing the previous caller's.
    /// </summary>
    [Fact]
    public async Task Two_requests_through_one_client_carry_their_own_credentials()
    {
        RecordingHandler downstream = new();
        int call = 0;
        using HttpClient client = Compose(
            new StubAuthorizingHandler(_ => new("Bearer", $"token-{++call}")), downstream);

        await client.GetAsync("https://catalog.test/one", CancellationToken.None);
        string? first = downstream.LastAuthorization?.Parameter;

        await client.GetAsync("https://catalog.test/two", CancellationToken.None);
        string? second = downstream.LastAuthorization?.Parameter;

        first.ShouldBe("token-1");
        second.ShouldBe("token-2");
    }

    /// <summary>
    /// A null credential must travel as "no header", not as an empty one — the
    /// call is meant to reach CameraCatalog and be refused, which is what turns
    /// a missing token into a refused tile.
    /// </summary>
    [Fact]
    public async Task An_absent_credential_sends_no_authorization_header_at_all()
    {
        RecordingHandler downstream = new();
        using HttpClient client = Compose(new StubAuthorizingHandler(_ => null), downstream);

        await client.GetAsync("https://catalog.test/cameras", CancellationToken.None);

        downstream.LastAuthorization.ShouldBeNull();
        downstream.SawAuthorizationHeader.ShouldBeFalse();
    }

    [Fact]
    public async Task The_requests_cancellation_token_reaches_the_credential_lookup()
    {
        RecordingHandler downstream = new();
        using CancellationTokenSource cts = new();
        CancellationToken observed = CancellationToken.None;

        using HttpClient client = Compose(
            new StubAuthorizingHandler(token =>
            {
                observed = token;
                return new AuthenticationHeaderValue("Bearer", "a-token");
            }),
            downstream);

        await client.GetAsync("https://catalog.test/cameras", cts.Token);

        observed.ShouldNotBe(CancellationToken.None,
            "a token lookup is itself an HTTP call, so a cancelled request must be able to abandon it.");
    }

    private static HttpClient Compose(AuthorizingHandler authorizing, RecordingHandler downstream)
    {
        authorizing.InnerHandler = downstream;
        return new HttpClient(authorizing);
    }

    private sealed class StubAuthorizingHandler(Func<CancellationToken, AuthenticationHeaderValue?> credential)
        : AuthorizingHandler
    {
        protected override Task<AuthenticationHeaderValue?> AuthorizationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(credential(cancellationToken));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        public bool SawAuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization;
            SawAuthorizationHeader = request.Headers.Contains("Authorization");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
