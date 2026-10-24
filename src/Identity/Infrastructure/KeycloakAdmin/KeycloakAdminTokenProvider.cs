using System.Net;
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
    /// than resolved by trying one: an ambiguous or absent credential is a configuration error.
    /// A 400/401 from the token endpoint surfaces as <see cref="KeycloakAdminTokenRefusedException"/>.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var adminOptions = options.Value;
        var hasKey = !string.IsNullOrEmpty(adminOptions.AdminClientPrivateKey);
        var hasSecret = !string.IsNullOrEmpty(adminOptions.AdminClientSecret);

        if (hasKey && hasSecret)
        {
            throw new InvalidOperationException(
                "Both Keycloak:AdminClientSecret and Keycloak:AdminClientPrivateKey are set; "
                + "configure exactly one credential for the Keycloak admin client (ADR-0186).");
        }

        if (!hasKey && !hasSecret)
        {
            throw new InvalidOperationException(
                "Neither Keycloak:AdminClientSecret nor Keycloak:AdminClientPrivateKey is set; "
                + "configure exactly one credential for the Keycloak admin client (ADR-0186).");
        }

        try
        {
            return await (hasKey ? assertionTokens : secretTokens).GetAccessTokenAsync(cancellationToken);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new KeycloakAdminTokenRefusedException(
                $"Keycloak's token endpoint refused the admin client's credential ({(int)exception.StatusCode}).",
                exception.StatusCode,
                exception);
        }
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
