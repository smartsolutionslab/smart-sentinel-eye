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

    private readonly KeycloakTokenProvider _tokens;
    private readonly ILogger<MqttPublisher> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly IManagedMqttClient _client;
    private readonly TokenHolder _token = new();
    private bool _started;

    public MqttPublisher(IOptions<SimulatorOptions> options, KeycloakTokenProvider tokens, ILogger<MqttPublisher> logger)
    {
        _tokens = tokens;
        _logger = logger;
        (_host, _port) = ParseHost(options.Value.MqttHost);
        _client = new MqttFactory().CreateManagedMqttClient();
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ConnectingFailedAsync += OnConnectingFailedAsync;
    }

    /// <summary>Mints the first token and starts the managed client (idempotent).</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _token.Value = await _tokens.GetAccessTokenAsync(cancellationToken);

        MqttClientOptions clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(Username)
            .WithTcpServer(_host, _port)
            .WithCredentials(new TokenCredentials(_token))
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

        ManagedMqttClientOptions managed = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions)
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .Build();

        await _client.StartAsync(managed);
        _started = true;
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
            await _client.EnqueueAsync(message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.MqttPublishFailed(topic, ex.Message);
        }
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.MqttPublisherConnected($"{_host}:{_port}", Username);
        return Task.CompletedTask;
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _logger.MqttPublisherDisconnected($"{_host}:{_port}");
        // Refresh the token so the imminent auto-reconnect presents a fresh JWT.
        try
        {
            _token.Value = await _tokens.GetAccessTokenAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.MqttPublishFailed("(reconnect-token)", ex.Message);
        }
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        _logger.MqttPublishFailed("(connect)", args.Exception?.Message ?? "connect failed");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _client.StopAsync();
        _client.Dispose();
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
