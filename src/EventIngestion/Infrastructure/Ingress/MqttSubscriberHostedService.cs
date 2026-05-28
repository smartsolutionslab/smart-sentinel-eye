using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// MQTT subscriber for PLC + inference events (spec 006 FR-008).
/// Subscribes to <c>fab/+/+/+</c> at QoS 1, parses each delivery
/// into an <see cref="EventEnvelope"/>, and pushes it onto the
/// shared <see cref="IIngestChannel"/>. The persistence loop drains
/// the channel and runs the dedup + persist + publish.
///
/// <para>
/// When the channel is full the call to
/// <see cref="IIngestChannel.WriteAsync"/> blocks; that delays the
/// MQTTnet handler from returning, the broker stops getting ACKs,
/// queue depth absorbs the burst per spec FR-022.
/// </para>
/// </summary>
public sealed class MqttSubscriberHostedService(
    MosquittoConnectionFactory connectionFactory,
    IIngestChannel channel,
    IOptions<MosquittoOptions> options,
    ILogger<MqttSubscriberHostedService> log) : IHostedService
{
    private IManagedMqttClient? _client;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client = connectionFactory.Create();
        _client.ApplicationMessageReceivedAsync += OnMessageReceived;

        string topic = options.Value.SubscribeTopic;
        await _client.SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce).ConfigureAwait(false);

        log.LogInformation(
            "MQTT subscriber started; subscribed to topic '{Topic}' at QoS 1.", topic);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is null) return;
        _client.ApplicationMessageReceivedAsync -= OnMessageReceived;
        await _client.StopAsync().ConfigureAwait(false);
        _client.Dispose();
        _client = null;
        log.LogInformation("MQTT subscriber stopped.");
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
    {
        string topic = args.ApplicationMessage.Topic;
        ReadOnlyMemory<byte> body = args.ApplicationMessage.PayloadSegment;

        EventEnvelope? envelope = TryParseEnvelope(topic, body);
        if (envelope is null)
        {
            // Malformed message — drop to dead-letter in a follow-on
            // PR; for now we just log and ACK so the broker doesn't
            // retry forever.
            log.LogWarning(
                "Dropping malformed MQTT message on topic '{Topic}' ({Bytes} bytes).",
                topic, body.Length);
            return;
        }

        // WriteAsync blocks when the bounded channel is full — the
        // broker stops receiving ACKs and holds queue depth (FR-022).
        await channel.WriteAsync(envelope, CancellationToken.None).ConfigureAwait(false);
    }

    private EventEnvelope? TryParseEnvelope(string topic, ReadOnlyMemory<byte> body)
    {
        // Topic shape: fab/{fabId}/{source}/{deviceId}
        string[] segments = topic.Split('/');
        if (segments.Length != 4 || segments[0] != "fab")
        {
            log.LogWarning("Unexpected MQTT topic shape: '{Topic}'.", topic);
            return null;
        }

        FabIdentifier fab;
        Source source;
        DeviceIdentifier device;
        MqttIngressPayload payload;
        try
        {
            fab = FabIdentifier.From(segments[1]);
            source = Source.From(segments[2]);
            device = DeviceIdentifier.From(segments[3]);

            payload = JsonSerializer.Deserialize<MqttIngressPayload>(body.Span)
                ?? throw new InvalidOperationException("payload is null");
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            log.LogWarning(ex,
                "Failed to parse MQTT envelope from topic '{Topic}': {Message}",
                topic, ex.Message);
            return null;
        }

        Payload payloadVo;
        try
        {
            payloadVo = Payload.From(payload.Payload.GetRawText());
        }
        catch (ArgumentException ex)
        {
            log.LogWarning(ex,
                "Rejected MQTT payload from '{Topic}': {Message}", topic, ex.Message);
            return null;
        }

        return new EventEnvelope(
            EventIdentifier.From(payload.EventId),
            fab,
            source,
            device,
            Kind.From(payload.Kind),
            OccurredAt.From(payload.OccurredAt),
            payloadVo);
    }
}
