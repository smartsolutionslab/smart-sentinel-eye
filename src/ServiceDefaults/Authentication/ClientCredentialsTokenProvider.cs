using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Authentication;

/// <summary>
/// Mints and caches a Keycloak <c>client_credentials</c> access token, refreshing
/// proactively at 80 % of <c>expires_in</c> so a caller never races a token into
/// expiry.
///
/// <para>
/// Four contexts had written this: EventIngestion for the MQTT broker password
/// (ADR-0100), Identity for the Admin API (spec 008), the Scenario Simulator for
/// its seeding calls (ADR-0111), and StreamDistribution for the startup
/// attribution pass (ADR-0116). Each carried its own copy of the same
/// double-checked cache, the same 80 % arithmetic and the same snake_case
/// deserialisation — and the copies had already drifted three ways: one took
/// <c>IClock</c> where the others took <c>TimeProvider</c>, one did not cache at
/// all, and three leaked their <c>FormUrlEncodedContent</c>.
/// </para>
///
/// <para>
/// The uncached one was StreamDistribution's, and its reasoning was sound at the
/// time: attribution runs once per host start and asks for one token, so a cache
/// would have been machinery built for a second call that never happens. That
/// argument was about the cost of <i>writing</i> the cache, and it stops applying
/// once the cache is written down once and shared. A cache that is never hit
/// costs a field.
/// </para>
///
/// <para>
/// <b>Every wrapper is registered as a singleton</b>, which is what makes the
/// cache mean anything (#2037). Registered through <c>AddHttpClient&lt;T&gt;</c>
/// the providers were transient, so each cache lived as long as whatever
/// happened to hold it: one HTTP request for Identity's, one handler chain for
/// StreamDistribution's, and in the Scenario Simulator five separate caches of a
/// single credential. The caching was real code with tests and, nearly
/// everywhere, no effect.
/// </para>
///
/// <para>
/// Credentials arrive through a delegate rather than a bound options type,
/// because the four contexts spell the same four values four different ways
/// (<c>Username</c>, <c>ClientId</c>, <c>AdminClientId</c>,
/// <c>ClientIdentifier</c>). Reading them per call also preserves the behaviour
/// each copy had: nothing here caches configuration, only the token.
/// </para>
/// </summary>
public sealed class ClientCredentialsTokenProvider : IDisposable
{
    /// <summary>
    /// Keycloak's token response is snake_case. The <c>[JsonPropertyName]</c>
    /// attributes below would carry it alone; the policy is kept because it also
    /// covers the fields this record does not name.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly string httpClientName;
    private readonly Func<ClientCredentials> credentials;
    private readonly TimeProvider clock;
    private readonly ILogger logger;

    private readonly SemaphoreSlim gate = new(initialCount: 1, maxCount: 1);
    private string? cachedToken;
    private DateTimeOffset refreshAfter = DateTimeOffset.MinValue;

    /// <summary>
    /// Takes the factory rather than an <see cref="HttpClient"/> so that a
    /// provider can be a singleton without pinning a handler.
    ///
    /// <para>
    /// The two properties wanted here are ordinarily mutually exclusive. A cache
    /// is only worth having if the object holding it outlives a request, which
    /// means a singleton; a singleton holding a typed <c>HttpClient</c> pins one
    /// <c>HttpMessageHandler</c> for the process lifetime and defeats the
    /// factory's rotation. Asking the factory per mint gets both: the cache
    /// spans the process, and each mint uses whatever handler the factory
    /// currently considers live.
    /// </para>
    /// </summary>
    public ClientCredentialsTokenProvider(
        IHttpClientFactory httpClientFactory,
        string httpClientName,
        Func<ClientCredentials> credentials,
        TimeProvider clock,
        ILogger logger)
    {
        Ensure.That(httpClientFactory).IsNotNull();
        Ensure.That(httpClientName).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(credentials).IsNotNull();
        Ensure.That(clock).IsNotNull();
        Ensure.That(logger).IsNotNull();

        this.httpClientFactory = httpClientFactory;
        this.httpClientName = httpClientName;
        this.credentials = credentials;
        this.clock = clock;
        this.logger = logger;
    }

    public void Dispose() => gate.Dispose();

    /// <summary>
    /// A token valid for at least the next 20 % of its lifetime. Concurrent
    /// callers arriving on a cold or expiring cache mint once between them.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (Fresh() is { } cached)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-checked inside the gate: the caller that was queued behind the
            // mint must see its result rather than mint a second one.
            return Fresh() ?? await MintAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private string? Fresh() => cachedToken is not null && clock.GetUtcNow() < refreshAfter ? cachedToken : null;

    private async Task<string> MintAsync(CancellationToken cancellationToken)
    {
        ClientCredentials client = credentials();
        string url = $"{client.Authority.TrimEnd('/')}/realms/{client.Realm}/protocol/openid-connect/token";

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client.ClientIdentifier,
            ["client_secret"] = client.ClientSecret,
        });

        // Not disposed. A client from the factory is a thin wrapper over a
        // pooled handler it does not own, so disposing it frees nothing and
        // would make a test double that hands back one client fail on the
        // second mint rather than on the first.
        HttpClient httpClient = httpClientFactory.CreateClient(httpClientName);

        using HttpResponseMessage response = await httpClient.PostAsync(url, form, cancellationToken);
        response.EnsureSuccessStatusCode();

        TokenResponse payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Keycloak returned an empty token response for '{client.ClientIdentifier}'.");

        // Assigned before refreshAfter, and both before the log line: a reader
        // arriving here should not have to wonder whether a token can be
        // published with an unset expiry.
        cachedToken = payload.AccessToken;
        refreshAfter = clock.GetUtcNow().AddSeconds(payload.ExpiresIn * 0.8);

        logger.MintedClientCredentialsToken(client.ClientIdentifier, payload.ExpiresIn);
        return cachedToken;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
