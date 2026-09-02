using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// The <c>event-ingestion</c> client_credentials token the MQTT subscriber
/// presents as its broker password (ADR-0100).
///
/// <para>
/// Refreshing proactively at 80 % of <c>expires_in</c> is what keeps a reconnect
/// from presenting an expired JWT; that behaviour now lives in
/// <see cref="ClientCredentialsTokenProvider"/>, alongside the three sibling
/// providers that had each written their own copy of it.
/// </para>
/// </summary>
public sealed class MqttTokenProvider(
    HttpClient httpClient,
    IOptions<MosquittoOptions> options,
    TimeProvider clock,
    ILogger<MqttTokenProvider> logger)
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

    /// <summary>
    /// <c>Username</c>, not a separate client-id field: the go-auth plugin
    /// enforces <c>azp == username</c>, so the broker account and the Keycloak
    /// client are one name by construction.
    /// </summary>
    private static ClientCredentials Credentials(IOptions<MosquittoOptions> options)
    {
        MosquittoOptions opts = options.Value;
        return new ClientCredentials(opts.KeycloakUrl, opts.Realm, opts.Username, opts.ClientSecret);
    }
}
