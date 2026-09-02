using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// The caching behaviour four contexts used to each own a copy of. Only one of
/// those copies had a test — Identity's, against a live Keycloak — which is part
/// of why the copies drifted.
/// </summary>
public class ClientCredentialsTokenProviderTests
{
    private static readonly ClientCredentials Credentials =
        new("https://keycloak.test/", "smart-sentinel-eye", "a-service-account", "a-secret");

    [Fact]
    public async Task A_cold_provider_mints_a_token()
    {
        MintingHandler keycloak = new(expiresIn: 300);
        using ClientCredentialsTokenProvider provider = Create(keycloak, out _);

        string token = await provider.GetAccessTokenAsync(CancellationToken.None);

        token.ShouldBe("token-1");
        keycloak.Mints.ShouldBe(1);
    }

    [Fact]
    public async Task The_grant_is_posted_to_the_realms_token_endpoint()
    {
        MintingHandler keycloak = new(expiresIn: 300);
        using ClientCredentialsTokenProvider provider = Create(keycloak, out _);

        await provider.GetAccessTokenAsync(CancellationToken.None);

        keycloak.LastUrl.ShouldBe(
            "https://keycloak.test/realms/smart-sentinel-eye/protocol/openid-connect/token",
            "the authority carried a trailing slash, which must not survive into the URL as a double one.");
        keycloak.LastForm.ShouldContain("grant_type=client_credentials");
        keycloak.LastForm.ShouldContain("client_id=a-service-account");
    }

    [Fact]
    public async Task A_second_call_inside_the_refresh_window_reuses_the_cached_token()
    {
        MintingHandler keycloak = new(expiresIn: 300);
        using ClientCredentialsTokenProvider provider = Create(keycloak, out AdvanceableClock clock);

        string first = await provider.GetAccessTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(239));
        string second = await provider.GetAccessTokenAsync(CancellationToken.None);

        second.ShouldBe(first);
        keycloak.Mints.ShouldBe(1, "80 % of a 300 s lifetime is 240 s; at 239 s the cached token still stands.");
    }

    /// <summary>
    /// The point of refreshing at 80 % rather than at expiry: the token is
    /// replaced while the one in hand is still valid, so a caller never presents
    /// a JWT that expires in flight.
    /// </summary>
    [Fact]
    public async Task The_token_is_replaced_once_four_fifths_of_its_life_has_passed()
    {
        MintingHandler keycloak = new(expiresIn: 300);
        using ClientCredentialsTokenProvider provider = Create(keycloak, out AdvanceableClock clock);

        string first = await provider.GetAccessTokenAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(241));
        string second = await provider.GetAccessTokenAsync(CancellationToken.None);

        second.ShouldNotBe(first);
        keycloak.Mints.ShouldBe(2);
    }

    /// <summary>
    /// What the gate is for. Without it, a cold provider hit by every consumer at
    /// once — which is exactly what a host start looks like — mints a token per
    /// caller and burns a Keycloak rate limit on its first second of life.
    /// </summary>
    [Fact]
    public async Task Concurrent_callers_on_a_cold_provider_mint_once_between_them()
    {
        MintingHandler keycloak = new(expiresIn: 300);
        using ClientCredentialsTokenProvider provider = Create(keycloak, out _);

        string[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => provider.GetAccessTokenAsync(CancellationToken.None)));

        keycloak.Mints.ShouldBe(1);
        tokens.Distinct(StringComparer.Ordinal).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_refusing_Keycloak_surfaces_rather_than_caching_the_refusal()
    {
        MintingHandler keycloak = new(expiresIn: 300) { Status = HttpStatusCode.Unauthorized };
        using ClientCredentialsTokenProvider provider = Create(keycloak, out _);

        await Should.ThrowAsync<HttpRequestException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        keycloak.Status = HttpStatusCode.OK;
        string recovered = await provider.GetAccessTokenAsync(CancellationToken.None);

        recovered.ShouldBe("token-1",
            "a failed mint must leave the cache untouched, so the next call retries rather than "
            + "inheriting a refusal for the rest of the lifetime.");
    }

    [Fact]
    public async Task An_empty_body_is_refused_rather_than_cached_as_a_null_token()
    {
        MintingHandler keycloak = new(expiresIn: 300) { Body = "null" };
        using ClientCredentialsTokenProvider provider = Create(keycloak, out _);

        await Should.ThrowAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));
    }

    private static ClientCredentialsTokenProvider Create(MintingHandler keycloak, out AdvanceableClock clock)
    {
        clock = new AdvanceableClock(new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero));
        return new ClientCredentialsTokenProvider(
            new HttpClient(keycloak),
            () => Credentials,
            clock,
            NullLogger.Instance);
    }

    /// <summary>
    /// Hand-written rather than <c>FakeTimeProvider</c> — ADR-0054, and the only
    /// member under test is <see cref="GetUtcNow"/>.
    /// </summary>
    private sealed class AdvanceableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now = now.Add(by);
    }

    /// <summary>
    /// Answers every POST with a distinct token, so "did it mint again" is
    /// readable from the token itself and not only from the counter.
    /// </summary>
    private sealed class MintingHandler(int expiresIn) : HttpMessageHandler
    {
        private int minted;

        public int Mints => minted;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? Body { get; set; }

        public string LastUrl { get; private set; } = string.Empty;

        public string LastForm { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri!.GetLeftPart(UriPartial.Path);

            HttpContent? posted = request.Content;
            LastForm = posted is null ? string.Empty : await posted.ReadAsStringAsync(cancellationToken);

            if (Status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Status);
            }

            int sequence = Interlocked.Increment(ref minted);
            string body = Body
                ?? $$"""{"access_token":"token-{{sequence}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                },
            };
        }
    }
}
