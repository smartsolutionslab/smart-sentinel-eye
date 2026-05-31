using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

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
///
/// <para>
/// Malformed deliveries (bad topic shape, malformed JSON, payload
/// over 64 KB) are captured in the <c>dead_letters</c> table per
/// spec FR-015 — audit-only, no fan-out.
/// </para>
/// </summary>
public sealed class MqttSubscriberHostedService(
    MosquittoConnectionFactory connectionFactory,
    IIngestChannel channel,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<MosquittoOptions> options,
    ILogger<MqttSubscriberHostedService> logger) : IHostedService
{
    private IManagedMqttClient? _client;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client = connectionFactory.Create();
        _client.ApplicationMessageReceivedAsync += OnMessageReceived;

        string topic = options.Value.SubscribeTopic;
        await _client.SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce).ConfigureAwait(false);

        Log.MqttSubscriberStarted(logger, topic);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is null) return;
        _client.ApplicationMessageReceivedAsync -= OnMessageReceived;
        await _client.StopAsync().ConfigureAwait(false);
        _client.Dispose();
        _client = null;
        Log.MqttSubscriberStopped(logger);
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
    {
        string topic = args.ApplicationMessage.Topic;
        ReadOnlyMemory<byte> body = args.ApplicationMessage.PayloadSegment;

        ParseResult result = TryParseEnvelope(topic, body);
        if (result.Envelope is null)
        {
            await CaptureDeadLetterAsync(topic, body, result.Error ?? "unknown parse failure").ConfigureAwait(false);
            return;
        }

        // WriteAsync blocks when the bounded channel is full — the
        // broker stops receiving ACKs and holds queue depth (FR-022).
        await channel.WriteAsync(result.Envelope, CancellationToken.None).ConfigureAwait(false);
    }

    private static ParseResult TryParseEnvelope(string topic, ReadOnlyMemory<byte> body)
    {
        // Topic shape: fab/{fabId}/{source}/{deviceId}
        string[] segments = topic.Split('/');
        if (segments.Length != 4 || segments[0] != "fab")
        {
            return new ParseResult(null, $"Unexpected MQTT topic shape: '{topic}'.");
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
            return new ParseResult(null, $"envelope parse failed: {ex.Message}");
        }

        Payload payloadVo;
        try
        {
            payloadVo = Payload.From(payload.Payload.GetRawText());
        }
        catch (ArgumentException ex)
        {
            return new ParseResult(null, $"payload rejected: {ex.Message}");
        }

        EventEnvelope envelope = new(
            EventIdentifier.From(payload.EventId),
            fab,
            source,
            device,
            Kind.From(payload.Kind),
            OccurredAt.From(payload.OccurredAt),
            payloadVo);
        return new ParseResult(envelope, null);
    }

    private async Task CaptureDeadLetterAsync(string topic, ReadOnlyMemory<byte> body, string error)
    {
        Log.RejectingMqttDelivery(logger, topic, error);
        string raw = Encoding.UTF8.GetString(body.Span);
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IDeadLetterRepository deadLetters =
                scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
            deadLetters.Add(DeadLetter.Capture(topic, raw, error, clock));
            await deadLetters.SaveAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Dead-letter capture is best-effort — DB outage must not
            // bring the subscriber down. Log and move on.
            Log.DeadLetterCaptureFailed(logger, ex, topic, ex.Message);
        }
    }

    private sealed record ParseResult(EventEnvelope? Envelope, string? Error);
}
