using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// The <c>revocation-list-reader</c> client_credentials token every
/// non-Identity API mints to read <c>GET /registered-clients/revoked</c>
/// (spec 270, ADR-0160 §4). A wrapper over the
/// shared <see cref="ClientCredentialsTokenProvider"/> cache, shaped like the
/// camera-catalog token provider StreamDistribution had until spec 423.
/// </summary>
public sealed class RevocationListTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RevocationListOptions> options,
    TimeProvider clock,
    ILogger<RevocationListTokenProvider> logger) : IDisposable
{
    /// <summary>Named client this provider mints through.</summary>
    public const string HttpClientName = "revocation-list-token";

    private readonly ClientCredentialsTokenProvider tokens = new(
        httpClientFactory,
        HttpClientName,
        () => Credentials(options),
        clock,
        logger);

    public void Dispose() => tokens.Dispose();

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        tokens.GetAccessTokenAsync(cancellationToken);

    private static ClientCredentials Credentials(IOptions<RevocationListOptions> options)
    {
        var opts = options.Value;
        return new ClientCredentials(opts.KeycloakUrl, opts.Realm, opts.ClientId, opts.ClientSecret);
    }
}
