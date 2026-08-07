using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.Mqtt;

/// <summary>
/// Publishes billet sensor samples to mosquitto as the simulated PLC/inference
/// device (ADR-0111 M2). Authenticates with the <c>scenario-simulator</c>
/// Keycloak JWT (username == <c>azp</c>, the go-auth plugin's requirement). The
/// managed client auto-reconnects; a credentials provider hands it the latest
/// token on each (re)connect, refreshed on disconnect, so a reconnect after the
/// token rotates still authenticates. Dev-only.
/// </summary>
public sealed class MqttPublisher : IAsyncDisposable
{
    private const string Username = "scenario-simulator";

    private readonly KeycloakTokenProvider tokens;
    private readonly ILogger<MqttPublisher> logger;
    private readonly string host;
    private readonly int port;
    private readonly IManagedMqttClient client;
    private readonly TokenHolder token = new();
    private bool started;

    public MqttPublisher(IOptions<SimulatorOptions> options, KeycloakTokenProvider tokens, ILogger<MqttPublisher> logger)
    {
        this.tokens = tokens;
        this.logger = logger;
        (host, port) = ParseHost(options.Value.MqttHost);
        client = new MqttFactory().CreateManagedMqttClient();
        client.ConnectedAsync += OnConnectedAsync;
        client.DisconnectedAsync += OnDisconnectedAsync;
        client.ConnectingFailedAsync += OnConnectingFailedAsync;
    }

    /// <summary>Mints the first token and starts the managed client (idempotent).</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (started)
        {
            return;
        }

        token.Value = await tokens.GetAccessTokenAsync(cancellationToken);

        MqttClientOptions clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(Username)
            .WithTcpServer(host, port)
            .WithCredentials(new TokenCredentials(token))
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

        ManagedMqttClientOptions managed = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions)
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .Build();

        await client.StartAsync(managed);
        started = true;
    }

    public async Task PublishAsync(string topic, string payloadJson, CancellationToken cancellationToken)
    {
        try
        {
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payloadJson)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await client.EnqueueAsync(message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.MqttPublishFailed(topic, ex.Message);
        }
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        logger.MqttPublisherConnected($"{host}:{port}", Username);
        return Task.CompletedTask;
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        logger.MqttPublisherDisconnected($"{host}:{port}");
        // Refresh the token so the imminent auto-reconnect presents a fresh JWT.
        try
        {
            token.Value = await tokens.GetAccessTokenAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.MqttPublishFailed("(reconnect-token)", ex.Message);
        }
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        logger.MqttPublishFailed("(connect)", args.Exception?.Message ?? "connect failed");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await client.StopAsync();
        client.Dispose();
    }

    private static (string Host, int Port) ParseHost(string mqttHost)
    {
        string value = mqttHost ?? string.Empty;
        int scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            value = value[(scheme + 3)..];
        }

        string[] parts = value.Split(':');
        string host = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "localhost";
        int port = parts.Length > 1 && int.TryParse(parts[1], out int parsed) ? parsed : 1883;
        return (host, port);
    }

    private sealed class TokenHolder
    {
        public string Value { get; set; } = string.Empty;
    }

    // MQTTnet's credentials provider is synchronous; it reads the latest token
    // the publisher keeps refreshed, so every (re)connect presents a live JWT.
    private sealed class TokenCredentials(TokenHolder token) : IMqttClientCredentialsProvider
    {
        public string GetUserName(MqttClientOptions clientOptions) => Username;

        public byte[] GetPassword(MqttClientOptions clientOptions) => Encoding.UTF8.GetBytes(token.Value);
    }
}
