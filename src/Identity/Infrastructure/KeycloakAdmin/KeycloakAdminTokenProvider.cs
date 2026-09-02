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
    HttpClient httpClient,
    IOptions<KeycloakAdminOptions> options,
    TimeProvider clock,
    ILogger<KeycloakAdminTokenProvider> logger)
    : IDisposable
{
    private readonly ClientCredentialsTokenProvider tokens = new(
        httpClient,
        () => Credentials(options),
        clock,
        logger);

    public void Dispose() => tokens.Dispose();

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        tokens.GetAccessTokenAsync(cancellationToken);

    private static ClientCredentials Credentials(IOptions<KeycloakAdminOptions> options)
    {
        KeycloakAdminOptions opts = options.Value;
        return new ClientCredentials(opts.BaseUrl, opts.Realm, opts.AdminClientId, opts.AdminClientSecret);
    }
}
