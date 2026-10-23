using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// The <c>identity-admin</c> client_credentials token, cached across every
/// Keycloak Admin API call (spec 008 plan §"Composition root + API").
///
/// <para>
/// The mint, the cache and the refresh-at-80 % live in
/// <see cref="ClientCredentialsTokenProvider"/>; what remains here is the one
/// thing that is Identity's — which options carry the credential.
/// </para>
/// </summary>
public sealed class KeycloakAdminTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakAdminOptions> options,
    TimeProvider clock,
    ILogger<KeycloakAdminTokenProvider> logger)
    : IDisposable
{
    /// <summary>Named client this provider mints through.</summary>
    public const string HttpClientName = "identity-admin-token";

    /// <summary>
    /// Named client the signed-assertion mint uses. Separate from <see cref="HttpClientName"/>
    /// because that one retries every method and a retried assertion replays its jti.
    /// </summary>
    public const string AssertionHttpClientName = "identity-admin-assertion-token";

    private readonly ClientCredentialsTokenProvider secretTokens = new(
        httpClientFactory,
        HttpClientName,
        () => Credentials(options),
        clock,
        logger);

    private readonly ClientCredentialsTokenProvider assertionTokens = new(
        httpClientFactory,
        AssertionHttpClientName,
        () => SignedCredentials(options),
        clock,
        logger);

    public void Dispose()
    {
        secretTokens.Dispose();
        assertionTokens.Dispose();
    }

    /// <summary>
    /// Mints with whichever credential is configured. Both, or neither, is refused rather
    /// than resolved by trying one: an ambiguous credential is a configuration error.
    /// </summary>
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var hasKey = !string.IsNullOrEmpty(opts.AdminClientPrivateKey);

        if (hasKey && !string.IsNullOrEmpty(opts.AdminClientSecret))
        {
            throw new InvalidOperationException(
                "Both Keycloak:AdminClientSecret and Keycloak:AdminClientPrivateKey are set; "
                + "configure exactly one credential for the Keycloak admin client (ADR-0186).");
        }

        return (hasKey ? assertionTokens : secretTokens).GetAccessTokenAsync(cancellationToken);
    }

    private static SignedJwtClientCredentials SignedCredentials(IOptions<KeycloakAdminOptions> options)
    {
        var opts = options.Value;
        return new SignedJwtClientCredentials(opts.BaseUrl, opts.Realm, opts.AdminClientId, opts.AdminClientPrivateKey);
    }

    private static ClientCredentials Credentials(IOptions<KeycloakAdminOptions> options)
    {
        var opts = options.Value;
        return new ClientCredentials(opts.BaseUrl, opts.Realm, opts.AdminClientId, opts.AdminClientSecret);
    }
}
