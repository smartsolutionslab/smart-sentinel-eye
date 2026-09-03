using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Builds the managed MQTT client for the subscriber. Authenticates with a
/// Keycloak-minted JWT as the password (ADR-0100) — the go-auth plugin
/// enforces <c>azp == username</c>, so the client credential and the
/// <c>acl.txt</c> user are the same <c>event-ingestion</c> identity.
/// Persistent session (cleanSession=false) so a process restart resumes any
/// QoS 1 messages the broker still holds.
///
/// <para>
/// Returns the client <em>unstarted</em> together with its options so the
/// hosted service can attach handlers, subscribe, and start in one awaited
/// sequence. An earlier version started it here as
/// <c>_ = client.StartAsync(managed)</c>, which discarded the Task and with
/// it every connect failure.
/// </para>
/// </summary>
public sealed class MosquittoConnectionFactory(
    IOptions<MosquittoOptions> options,
    MqttTokenProvider tokens,
    ILogger<MosquittoConnectionFactory> logger)
{
    public async Task<MqttConnection> CreateAsync(CancellationToken cancellationToken)
    {
        MosquittoOptions opts = options.Value;

        TokenHolder token = new();

        // **Minted here when it can be, but never fatally.** This runs on
        // IHostedService.StartAsync, so an exception escaping it does not fail a
        // connection — it fails the whole host, and event-ingestion enters
        // FailedToStart because Keycloak was briefly slow. A service that cannot
        // start while its identity provider blinks is a worse property than a
        // subscriber that connects a few seconds late, and the machinery to
        // connect late already exists: the managed client retries every five
        // seconds and the token is re-minted on each failed attempt.
        //
        // Found while investigating #2038, where a restart under load left
        // event-ingestion down.
        try
        {
            token.Value = await tokens.GetAccessTokenAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.InitialMqttTokenFailed(exception.Message);
        }

        MqttClientOptionsBuilder clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(opts.ClientId)
            .WithTcpServer(opts.Host, opts.Port)
            .WithCredentials(new TokenCredentials(opts.Username, token))
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (opts.UseTls)
        {
            clientOptions = clientOptions.WithTlsOptions(builder => builder.UseTls());
        }

        ManagedMqttClientOptions managed = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions.Build())
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .Build();

        return new MqttConnection(new MqttFactory().CreateManagedMqttClient(), managed, token, tokens);
    }
}

/// <summary>
/// An unstarted managed client plus the options it must be started with.
/// Owns its token slot so the subscriber can refresh the credential on
/// disconnect without taking a dependency on the provider itself.
/// </summary>
public sealed class MqttConnection(
    IManagedMqttClient client,
    ManagedMqttClientOptions options,
    TokenHolder token,
    MqttTokenProvider tokens)
{
    public IManagedMqttClient Client => client;

    public ManagedMqttClientOptions Options => options;

    /// <summary>
    /// Re-mints the JWT into the slot the credentials provider reads, so the
    /// next (re)connect presents a live one.
    /// </summary>
    public async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        token.Value = await tokens.GetAccessTokenAsync(cancellationToken);
    }
}

/// <summary>
/// Mutable slot holding the current JWT. Refreshed on disconnect so the
/// auto-reconnect presents a live token.
/// </summary>
public sealed class TokenHolder
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// MQTTnet's credentials provider is synchronous; it reads the latest token
/// from <see cref="TokenHolder"/> so every (re)connect presents a live JWT.
/// </summary>
public sealed class TokenCredentials(string username, TokenHolder token) : IMqttClientCredentialsProvider
{
    public string GetUserName(MqttClientOptions clientOptions) => username;

    public byte[] GetPassword(MqttClientOptions clientOptions) => Encoding.UTF8.GetBytes(token.Value);
}
