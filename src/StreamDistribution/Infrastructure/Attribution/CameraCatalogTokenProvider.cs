using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

/// <summary>
/// The <c>stream-distribution-attribution</c> client_credentials token the
/// startup attribution pass presents to CameraCatalog (ADR-0116).
///
/// <para>
/// This was the one sibling that deliberately did not cache: attribution runs
/// once per host start and asks for one token, so a cache with a refresh window
/// was machinery for a second call that never happens. That reasoning was about
/// the cost of writing the cache, and it lapsed the moment the cache became
/// something shared rather than something rewritten. Sharing
/// <see cref="ClientCredentialsTokenProvider"/> costs a field that is never
/// read twice, and buys the loss of the fourth divergent copy.
/// </para>
/// </summary>
public sealed class CameraCatalogTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<StreamFabAttributionOptions> options,
    TimeProvider clock,
    ILogger<CameraCatalogTokenProvider> logger)
    : IDisposable
{
    /// <summary>Named client this provider mints through.</summary>
    public const string HttpClientName = "stream-distribution-attribution-token";

    private readonly ClientCredentialsTokenProvider tokens = new(
        httpClientFactory,
        HttpClientName,
        () => Credentials(options),
        clock,
        logger);

    public void Dispose() => tokens.Dispose();

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        tokens.GetAccessTokenAsync(cancellationToken);

    private static ClientCredentials Credentials(IOptions<StreamFabAttributionOptions> options)
    {
        StreamFabAttributionOptions opts = options.Value;
        return new ClientCredentials(opts.KeycloakUrl, opts.Realm, opts.ClientIdentifier, opts.ClientSecret);
    }
}
