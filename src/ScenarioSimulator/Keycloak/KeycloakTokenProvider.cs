using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.ScenarioSimulator.Keycloak;

/// <summary>
/// The <c>scenario-simulator</c> client_credentials token the worker presents to
/// camera-catalog and the seeding endpoints. Dev-only client.
///
/// <para>
/// Caching and refresh come from <see cref="ClientCredentialsTokenProvider"/>;
/// this names the options the credential is bound to and nothing else.
/// </para>
/// </summary>
public sealed class KeycloakTokenProvider(
    HttpClient httpClient,
    IOptions<SimulatorOptions> options,
    TimeProvider clock,
    ILogger<KeycloakTokenProvider> logger)
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

    private static ClientCredentials Credentials(IOptions<SimulatorOptions> options)
    {
        SimulatorOptions opts = options.Value;
        return new ClientCredentials(opts.KeycloakUrl, opts.Realm, opts.ClientId, opts.ClientSecret);
    }
}
