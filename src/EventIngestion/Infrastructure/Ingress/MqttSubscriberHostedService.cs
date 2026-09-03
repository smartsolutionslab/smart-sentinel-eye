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

    /// <summary>
    /// Re-mints the JWT after a failed connect attempt, for the same reason
    /// <see cref="OnDisconnectedAsync"/> does after a dropped one.
    ///
    /// <para>
    /// <b>These are different events and only one of them was covered.</b>
    /// <c>DisconnectedAsync</c> fires when an established connection drops;
    /// a CONNECT the broker refuses — an expired or absent token — raises this
    /// instead. So a subscriber that never managed a first connection re-presented
    /// the same dead credential every five seconds, forever, and the log said
    /// only that connecting had failed. Nothing recovered it but a restart.
    /// </para>
    ///
    /// <para>
    /// That path became reachable on purpose when the startup mint stopped being
    /// fatal (see <see cref="MosquittoConnectionFactory"/>): the client may now
    /// legitimately start with no token at all, and this is what turns that into
    /// a connection rather than a loop.
    /// </para>
    /// </summary>
    private async Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        MosquittoOptions opts = options.Value;
        logger.MqttSubscriberConnectFailed(
            $"{opts.Host}:{opts.Port}", args.Exception?.Message ?? "connect failed");

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
            // Keycloak is still away. The next attempt is five seconds off and
            // will try again; failing here would only replace a retry with a
            // crash.
            logger.MqttReconnectTokenFailed(ex.Message);
        }
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
    {
        // Spec 020 FR-001. Returning from this handler used to acknowledge the
        // delivery, which told the broker we had the event before anything had
        // been written — so it discarded its copy, and a failed write or a
        // restart lost an event we had already claimed. The acknowledgement now
        // waits for the write and travels with the envelope.
        args.AutoAcknowledge = false;

        string topic = args.ApplicationMessage.Topic;
        ReadOnlyMemory<byte> body = args.ApplicationMessage.PayloadSegment;

        ParseResult result = TryParseEnvelope(topic, body);
        if (result.Envelope is null)
        {
            // A delivery that cannot be parsed will not parse on redelivery
            // either, so it is recorded and released here rather than left to
            // come back forever.
            //
            // Released only if the record was actually written. The capture
            // fails for the same reason an event write fails - the database is
            // away - and acknowledging anyway would discard the payload with
            // one log line, which is the defect this feature exists to close,
            // on the one path that has no second chance at it. Unacknowledged,
            // the broker brings it back and the capture is retried.
            if (await CaptureDeadLetterAsync(topic, body, result.Error ?? "unknown parse failure"))
            {
                await args.AcknowledgeAsync(CancellationToken.None);
            }

            return;
        }

        // WriteAsync blocks when the bounded channel is full — the handler stops
        // returning, the broker's in-flight window fills, and queue depth
        // absorbs the burst (FR-022). That backpressure now matters more, not
        // less: the window is what the acknowledgement is holding open.
        await channel.WriteAsync(
            new IngestDelivery(result.Envelope, new MqttDeliveryCompletion(args)),
            CancellationToken.None);
    }

    /// <summary>
    /// Reports a delivery's outcome by acknowledging it to the broker.
    ///
    /// <para>
    /// Both outcomes acknowledge, and that is deliberate. <c>Stored</c> releases
    /// the sender's copy because we now genuinely have it; <c>Abandoned</c>
    /// releases it because the delivery has been recorded in
    /// <c>dead_letters</c> and QoS 1 would otherwise redeliver it forever,
    /// filling the in-flight window and blocking every event behind it.
    /// </para>
    /// </summary>
    private sealed class MqttDeliveryCompletion(MqttApplicationMessageReceivedEventArgs args)
        : IIngestCompletion
    {
        public Task StoredAsync(CancellationToken cancellationToken) =>
            args.AcknowledgeAsync(cancellationToken);

        public Task AbandonedAsync(CancellationToken cancellationToken) =>
            args.AcknowledgeAsync(cancellationToken);
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

    private async Task<bool> CaptureDeadLetterAsync(string topic, ReadOnlyMemory<byte> body, string error)
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
            deadLetters.Add(DeadLetter.Capture(DeliveryTopic.From(topic), fab, RawPayload.From(raw), RejectionReason.From(error), clock));
            await deadLetters.SaveAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A database outage must not bring the subscriber down, so this is
            // caught - but it is no longer "best effort and move on". The
            // caller leaves the delivery unacknowledged, so the broker keeps it
            // and the capture is retried rather than the payload being lost.
            logger.DeadLetterCaptureFailed(ex, topic, ex.Message);
            return false;
        }
    }

    private sealed record ParseResult(EventEnvelope? Envelope, string? Error);
}
