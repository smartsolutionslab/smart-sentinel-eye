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
    private IManagedMqttClient? client;
    private MqttConnection? connection;

    // Deliveries this process rejected without being able to name their plant
    // (FR-012). Interlocked because MQTTnet dispatches handlers concurrently.
    private long unattributableDeadLetters;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        connection = await connectionFactory.CreateAsync(cancellationToken);
        client = connection.Client;

        client.ApplicationMessageReceivedAsync += OnMessageReceived;
        client.ConnectedAsync += OnConnectedAsync;
        client.DisconnectedAsync += OnDisconnectedAsync;
        client.ConnectingFailedAsync += OnConnectingFailedAsync;

        string topic = options.Value.SubscribeTopic;
        await client.SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce);
        await client.StartAsync(connection.Options);

        logger.MqttSubscriberStarted(topic);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (client is null)
        {
            return;
        }

        client.ApplicationMessageReceivedAsync -= OnMessageReceived;
        client.ConnectedAsync -= OnConnectedAsync;
        client.DisconnectedAsync -= OnDisconnectedAsync;
        client.ConnectingFailedAsync -= OnConnectingFailedAsync;
        await client.StopAsync();
        client.Dispose();
        client = null;
        connection = null;
        logger.MqttSubscriberStopped();
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        MosquittoOptions opts = options.Value;
        logger.MqttSubscriberConnected($"{opts.Host}:{opts.Port}", opts.Username);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Refreshes the JWT so the imminent auto-reconnect presents a live one.
    /// The broker closes the connection when the token expires, so without
    /// this the subscriber would reconnect-loop on a stale credential.
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        logger.MqttSubscriberDisconnected(args.Reason.ToString());

        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.RefreshTokenAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.MqttReconnectTokenFailed(ex.Message);
        }
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        MosquittoOptions opts = options.Value;
        logger.MqttSubscriberConnectFailed(
            $"{opts.Host}:{opts.Port}", args.Exception?.Message ?? "connect failed");
        return Task.CompletedTask;
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
    {
        string topic = args.ApplicationMessage.Topic;
        ReadOnlyMemory<byte> body = args.ApplicationMessage.PayloadSegment;

        ParseResult result = TryParseEnvelope(topic, body);
        if (result.Envelope is null)
        {
            await CaptureDeadLetterAsync(topic, body, result.Error ?? "unknown parse failure");
            return;
        }

        // WriteAsync blocks when the bounded channel is full — the
        // broker stops receiving ACKs and holds queue depth (FR-022).
        await channel.WriteAsync(result.Envelope, CancellationToken.None);
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

        // Guarded like the two blocks above. Unguarded, a field the value
        // objects reject threw straight out of the MQTT handler: MQTTnet never
        // ACKed, the broker stalled once its in-flight window filled, and the
        // delivery was never dead-lettered (FR-015) — so one bad message
        // wedged ingestion permanently, because QoS 1 redelivers it forever.
        EventEnvelope envelope;
        try
        {
            envelope = new(
                EventIdentifier.From(payload.EventId),
                fab,
                source,
                device,
                Kind.From(payload.Kind),
                OccurredAt.From(payload.OccurredAt),
                payloadVo);
        }
        catch (ArgumentException ex)
        {
            return new ParseResult(null, $"envelope rejected: {ex.Message}");
        }

        return new ParseResult(envelope, null);
    }

    /// <summary>
    /// The plant the delivery address establishes, or <c>null</c> when it
    /// establishes none (spec 018 FR-008, FR-010).
    ///
    /// <para>
    /// The two failure modes are not the same and must not be conflated. A
    /// malformed <em>payload</em> under a well-formed topic — the common case —
    /// has a plant, and its own operators can see it. Only a malformed
    /// <em>address</em> leaves the origin unknown. Treating every rejection as
    /// orphaned would hide the whole list while looking like correct scoping.
    /// </para>
    ///
    /// <para>
    /// Four segments do not by themselves yield a usable fab: the segment may be
    /// present and still not be a legal <see cref="FabIdentifier"/>. So this
    /// attempts the parse and falls back to null rather than assuming.
    /// </para>
    /// </summary>
    private static FabIdentifier? TryParseFab(string topic)
    {
        string[] segments = topic.Split('/');
        if (segments.Length != 4 || segments[0] != "fab")
        {
            return null;
        }

        try
        {
            return FabIdentifier.From(segments[1]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task CaptureDeadLetterAsync(string topic, ReadOnlyMemory<byte> body, string error)
    {
        logger.RejectingMqttDelivery(topic, error);

        FabIdentifier? fab = TryParseFab(topic);
        if (fab is null)
        {
            // FR-012. The row is about to become visible to nobody (FR-011),
            // which is the fail-closed answer but also a real diagnostic gap.
            // The topic and the running count only — never the payload, which is
            // production data of unknown origin and the reason the row is
            // hidden in the first place.
            logger.UnattributableDeadLetter(topic, Interlocked.Increment(ref unattributableDeadLetters));
        }

        string raw = Encoding.UTF8.GetString(body.Span);
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IDeadLetterRepository deadLetters =
                scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
            deadLetters.Add(DeadLetter.Capture(topic, fab, raw, error, clock));
            await deadLetters.SaveAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Dead-letter capture is best-effort — DB outage must not
            // bring the subscriber down. Log and move on.
            logger.DeadLetterCaptureFailed(ex, topic, ex.Message);
        }
    }

    private sealed record ParseResult(EventEnvelope? Envelope, string? Error);
}
