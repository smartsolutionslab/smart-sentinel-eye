using System.Buffers;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Exceptions;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>
/// A broker that never was (ADR-0054 — hand-written, not a mocking framework).
///
/// <para>
/// <b>The connect is gated rather than delayed.</b> The publisher's outage
/// window is the interval in which samples are dropped, and a test that opened
/// it with a sleep would be asserting against a race. Holding the CONNECT until
/// the test releases it makes the window exact.
/// </para>
///
/// <para>
/// <b>It models MQTTnet 5, and the difference from 4 is the whole point.</b>
/// Until now this fake could not refuse — <c>ConnectAsync</c> answered
/// <c>Success</c> unconditionally — so the publisher's copy of the refusal
/// defect had no test that could reach it: a refusal taken for a connection
/// logs a connect, runs <c>ReportDrops</c>, which declares over an outage that
/// never began, and then blocks for good on a drop no connection can raise. A
/// fake speaking the previous major version's contract is how the original
/// defect survived; a fake that cannot refuse is the same mistake.
/// </para>
///
/// <para>
/// A deliberate second copy of EventIngestion's fake of the same name: the two
/// test assemblies share no project. What differs between them is what each
/// records — this one gates connects and forces publish failures where that
/// one records credentials and topics — the drop/hold controls below are
/// shared between both, not part of that difference.
/// </para>
/// </summary>
internal sealed class FakeMqttClient : IMqttClient
{
    private TaskCompletionSource? connectGate;
    private int refusals;
    private int connectAttempts;
    private bool staleDuringNextConnect;
    private TimeSpan? holdFor;

    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;

    public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;

    public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;

    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;

    public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

    /// <summary>Payloads the broker accepted, in order.</summary>
    public List<string> Published { get; } = [];

    /// <summary>
    /// Makes the next publish throw as a connection that dropped between the
    /// <c>IsConnected</c> check and the publish does — the US2-AC3 race.
    /// </summary>
    public bool FailNextPublishAsNotConnected { get; set; }

    /// <summary>
    /// Makes the next publish throw as a QoS 1 delivery whose PUBACK never
    /// arrived does. <b>A different type from the one above</b>, and the
    /// distinction is the whole point: both derive from
    /// <see cref="MqttCommunicationException"/>, so a catch naming only the
    /// not-connected leaf lets this one out — and its caller is a
    /// <c>BackgroundService</c> that catches nothing but cancellation.
    /// </summary>
    public bool FailNextPublishAsTimedOut { get; set; }

    /// <summary>CONNECTs the broker answered, refusals included.</summary>
    public int ConnectAttempts => Volatile.Read(ref connectAttempts);

    public bool IsConnected { get; private set; }

    public MqttClientOptions Options { get; private set; } = new();

    /// <summary>Holds every CONNECT until <see cref="AllowConnect"/> is called.</summary>
    public void GateConnect() =>
        connectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void AllowConnect() => connectGate?.TrySetResult();

    /// <summary>Makes the next <paramref name="count"/> CONNECTs be refused.</summary>
    public void RefuseNextConnects(int count) => refusals += count;

    /// <summary>
    /// Refuses every CONNECT, for good. A credential the broker will never
    /// accept is a permanent condition, unlike the JWKS re-fetch after a
    /// mosquitto restart that finishes on its own.
    /// </summary>
    public void RefuseEveryConnect() => refusals = int.MaxValue;

    /// <summary>
    /// Makes every connection end the moment it is usable, as a session takeover
    /// does: both real clients connect with a fixed client id
    /// (<c>event-ingestion</c>, <c>scenario-simulator</c>), so a second pod — or
    /// a restart before the broker reaps the old session — is answered with a
    /// CONNACK and then a close. The drop is raised from inside the CONNECT that
    /// answers <c>Success</c>, so this is a connection that completed and then
    /// vanished rather than one that was never usable — this publisher never
    /// subscribes, unlike the loop this fake is borrowed from.
    /// </summary>
    public bool DropEveryConnectionImmediately { get; set; }

    /// <summary>
    /// Makes every connection last <paramref name="duration"/> and then drop —
    /// a peer that keeps taking the session back at roughly its own reconnect
    /// period. With a fixed client id two pods do exactly this to each other,
    /// and the period they converge on is a little above the shorter of their
    /// two floors, because whichever reconnects first wins the session.
    ///
    /// <para>
    /// Distinct from <see cref="DropEveryConnectionImmediately"/>, and the
    /// distance between them is the point: a connection that dies on arrival is
    /// already covered, and a connection that outlives the backoff floor by a
    /// hair is the case <c>ResetIfHeld</c>'s fixed yardstick cannot tell from a
    /// connection that held.
    /// </para>
    /// </summary>
    public void HoldEveryConnectionFor(TimeSpan duration) => holdFor = duration;

    /// <summary>
    /// Raises a disconnect for a connection that never existed, leaving
    /// <see cref="IsConnected"/> alone.
    ///
    /// <para>
    /// Not a contrivance: MQTTnet 5 calls <c>DisconnectInternal</c> on a
    /// non-success CONNACK and dispatches the handler fire-and-forget
    /// (<c>Task.Run(...).RunInBackground(_logger)</c>, not awaited), so a stale
    /// disconnect is in flight after <em>every</em> refusal, by design. It
    /// carries <c>ClientWasConnected=false</c>, captured at the moment of the
    /// disconnect — measured off the real client in
    /// <c>MqttClientWasConnectedContractTests</c>.
    /// </para>
    /// </summary>
    public async Task RaiseStaleDisconnectAsync()
    {
        Func<MqttClientDisconnectedEventArgs, Task>? handler = DisconnectedAsync;
        if (handler is not null)
        {
            await handler(new MqttClientDisconnectedEventArgs(
                clientWasConnected: false,
                connectResult: null!,
                reason: MqttClientDisconnectReason.UnspecifiedError,
                reasonString: "a refused CONNECT's disconnect, delivered late",
                userProperties: [],
                exception: null!));
        }
    }

    /// <summary>
    /// Delivers that stale disconnect from <b>inside</b> the next CONNECT,
    /// before it is answered.
    ///
    /// <para>
    /// The attempt has attached its handler and is waiting on a network round
    /// trip, so the client reports no connection — and a guard reading
    /// <c>IsConnected</c> therefore takes the stale event for a drop of a
    /// connection that has not happened yet, completing the wait of the attempt
    /// that is about to succeed. Delivering it from inside the call makes that
    /// interleaving a fact of the test rather than a thread-pool starvation it
    /// would have to provoke.
    /// </para>
    /// </summary>
    public void RaiseStaleDisconnectDuringNextConnect() => staleDuringNextConnect = true;

    public async Task DropAsync()
    {
        IsConnected = false;

        Func<MqttClientDisconnectedEventArgs, Task>? handler = DisconnectedAsync;
        if (handler is not null)
        {
            await handler(new MqttClientDisconnectedEventArgs(
                clientWasConnected: true,
                connectResult: null!,
                reason: MqttClientDisconnectReason.UnspecifiedError,
                reasonString: "the broker went away",
                userProperties: [],
                exception: null!));
        }
    }

    /// <summary>
    /// <b>A refused CONNECT returns; it does not throw.</b> MQTTnet 4's
    /// <c>MqttClientOptionsBuilder</c> set
    /// <c>ThrowOnNonSuccessfulConnectResponse = true</c>, so a rejection arrived
    /// as an exception and a caller that discarded the result still noticed. In
    /// MQTTnet 5 that property is gone. Verified against mosquitto 2.0.18: a bad
    /// credential answers <c>ResultCode=NotAuthorized</c> with
    /// <c>IsConnected=false</c> and throws nothing, so the reason code
    /// <em>is</em> the answer.
    ///
    /// <para>
    /// <b>It yields before answering</b>, because a CONNECT is a network round
    /// trip and nothing in the real client completes one synchronously. A fake
    /// that answers on the calling thread turns a caller that retries without a
    /// delay into a loop that never reaches an await — which does not merely
    /// mis-measure the spin, it hangs the test host inside the call that started
    /// the loop. That was harmless only while this fake could not refuse.
    /// </para>
    ///
    /// <para>
    /// <b>Connecting a client that is already connected throws</b>, as
    /// <c>MqttClient.ThrowIfConnected</c> does. No CONNECT leaves the machine in
    /// that case, so nothing is recorded and <see cref="ConnectAttempts"/> does
    /// not move — the failure is visible in the log, not in the count. The check
    /// runs <b>before</b> the connect gate rather than after it: this fake's gate
    /// is its own test affordance, and a CONNECT the real client refuses outright
    /// must not instead park on a gate nobody has released.
    /// </para>
    /// </summary>
    public async Task<MqttClientConnectResult> ConnectAsync(
        MqttClientOptions options, CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        Options = options;

        if (staleDuringNextConnect)
        {
            staleDuringNextConnect = false;
            await RaiseStaleDisconnectAsync();
        }

        if (IsConnected)
        {
            throw new InvalidOperationException(
                "It is not allowed to connect with a server after the connection is established.");
        }

        if (connectGate is not null)
        {
            await connectGate.Task.WaitAsync(cancellationToken);
        }

        Interlocked.Increment(ref connectAttempts);

        if (refusals > 0)
        {
            refusals--;
            IsConnected = false;

            return new MqttClientConnectResult
            {
                ResultCode = MqttClientConnectResultCode.NotAuthorized,
                ReasonString = "the broker refused the credential",
            };
        }

        IsConnected = true;

        MqttClientConnectResult result = new() { ResultCode = MqttClientConnectResultCode.Success };

        if (DropEveryConnectionImmediately)
        {
            await DropAsync();
            return result;
        }

        if (holdFor is TimeSpan hold)
        {
            // Scheduled rather than awaited: a CONNECT that took the whole hold
            // to answer would move the delay into the wrong packet.
            _ = HoldThenDropAsync(hold);
        }

        return result;
    }

    public Task<MqttClientPublishResult> PublishAsync(
        MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default)
    {
        if (FailNextPublishAsNotConnected)
        {
            FailNextPublishAsNotConnected = false;
            throw new MqttClientNotConnectedException();
        }

        if (FailNextPublishAsTimedOut)
        {
            FailNextPublishAsTimedOut = false;
            throw new MqttCommunicationTimedOutException();
        }

        Published.Add(System.Text.Encoding.UTF8.GetString(applicationMessage.Payload.ToArray()));

        return Task.FromResult(new MqttClientPublishResult(
            packetIdentifier: 1, MqttClientPublishReasonCode.Success, reasonString: string.Empty, userProperties: []));
    }

    public Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<MqttClientSubscribeResult> SubscribeAsync(
        MqttClientSubscribeOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MqttClientSubscribeResult(
            packetIdentifier: 1, items: [], reasonString: string.Empty, userProperties: []));

    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(
        MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MqttClientUnsubscribeResult(
            packetIdentifier: 1, items: [], reasonString: string.Empty, userProperties: []));

    public Task PingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendEnhancedAuthenticationExchangeDataAsync(
        MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Dispose()
    {
        // Nothing native is held; the connection state is a bool.
    }

    /// <summary>Kept so the compiler does not warn on events this fake never raises.</summary>
    internal void SilenceUnusedEvents()
    {
        _ = ApplicationMessageReceivedAsync;
        _ = ConnectedAsync;
        _ = ConnectingAsync;
        _ = InspectPacketAsync;
    }

    private async Task HoldThenDropAsync(TimeSpan hold)
    {
        await Task.Delay(hold);
        await DropAsync();
    }
}
