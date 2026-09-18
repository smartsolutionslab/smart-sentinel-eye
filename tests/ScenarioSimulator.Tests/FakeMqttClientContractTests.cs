using MQTTnet;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 179 (#2233) — pins the fake's own fidelity, not <c>MqttPublisher</c>'s
/// behaviour.
///
/// <para>
/// <b>There is precedent for a test whose subject is the double rather than the
/// loop.</b> EventIngestion's <c>MqttClientWasConnectedContractTests</c> measures
/// the <b>real</b> client to justify what both fakes encode about
/// <c>ClientWasConnected</c>; this is the other half of that pair — it pins what
/// this fake encodes about <c>MqttClient.ThrowIfConnected</c>. Until now
/// <c>ConnectAsync</c> answered <c>Success</c> unconditionally on a live
/// connection, so nothing here could ever have failed for the right reason.
/// </para>
///
/// <para>
/// <b>The placement of the new check is the thing under test, not only its
/// existence.</b> The fake has a connect gate the real client has not; a check
/// placed after that gate would park a CONNECT the real client refuses outright —
/// a third behaviour belonging to neither. The last case below is the one that
/// would pass if the check were misplaced there, which is why it exists.
/// </para>
/// </summary>
public class FakeMqttClientContractTests
{
    /// <summary>
    /// <c>MqttClient.ThrowIfConnected</c>'s own wording — asserted verbatim
    /// rather than paraphrased, because the counterfactual in
    /// <c>MqttPublisherDropAccountingTests</c> reads this exact text out of a log
    /// line as the evidence that the symptom is the real one.
    /// </summary>
    private const string RefusalMessage =
        "It is not allowed to connect with a server after the connection is established.";

    [Fact]
    public async Task A_connect_against_a_live_connection_is_refused_as_the_real_client_refuses_it()
    {
        FakeMqttClient client = new();
        await client.ConnectAsync(new MqttClientOptions());

        InvalidOperationException thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(new MqttClientOptions()),
            "a second CONNECT against a connection nothing closed must be refused the way "
            + "MqttClient.ThrowIfConnected refuses it, not answered as if the client were free.");

        thrown.Message.ShouldBe(
            RefusalMessage,
            "a paraphrase would model a different client, and the counterfactual reads this exact "
            + "text out of the publisher's log line as its evidence.");
    }

    [Fact]
    public async Task A_refused_reconnect_does_not_end_and_does_not_close_the_connection()
    {
        FakeMqttClient client = new();
        await client.ConnectAsync(new MqttClientOptions());

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => client.ConnectAsync(new MqttClientOptions()),
                $"refusal #{attempt} — the refusal is permanent because nothing ever closes the "
                + "live connection it is protecting, not a one-off rejection of a single retry.");
        }

        client.IsConnected.ShouldBeTrue(
            "nothing in this test disconnected the client, so the connection the first CONNECT "
            + "opened is still the one standing.");

        client.ConnectAttempts.ShouldBe(
            1,
            "a CONNECT refused by ThrowIfConnected never leaves the client, so none of the four "
            + "further calls may move the count past the one that actually connected.");
    }

    /// <summary>#2130's shape, driven directly rather than through <c>MqttPublisher</c>.</summary>
    [Fact]
    public async Task A_stale_disconnect_reused_against_a_live_connection_is_refused()
    {
        FakeMqttClient client = new();
        await client.ConnectAsync(new MqttClientOptions());

        await client.RaiseStaleDisconnectAsync();

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(new MqttClientOptions()),
            "the stale disconnect describes a connection that never existed (ClientWasConnected="
            + "false) and leaves IsConnected untouched, so a caller that takes it for a drop and "
            + "reconnects is issuing a CONNECT against a client that is still live — the exact "
            + "shape #2130 left the simulator's suite unable to see.");
    }

    [Fact]
    public async Task A_broker_refusal_is_still_a_result_rather_than_a_throw()
    {
        FakeMqttClient client = new();
        client.RefuseEveryConnect();

        MqttClientConnectResult result = await client.ConnectAsync(new MqttClientOptions());

        result.ResultCode.ShouldBe(
            MqttClientConnectResultCode.NotAuthorized,
            "the new liveness check must not have turned this fake's existing refusal path into an "
            + "exception — a disconnected client being refused a credential is a result, not a "
            + "fault, exactly as mosquitto answers it (verified in this fake's own ConnectAsync doc "
            + "comment).");

        client.IsConnected.ShouldBeFalse("a refused CONNECT never establishes a connection.");
    }

    /// <summary>
    /// Proves the placement decision in <c>plan.md</c> §2: the liveness check
    /// must run <b>before</b> the connect gate. A check placed after it would
    /// block on <see cref="FakeMqttClient.AllowConnect"/>, which this test never
    /// calls — so a misplaced check hangs rather than throws, and the bounded
    /// wait below turns that hang into a failure instead of a stuck test run
    /// (ADR-0150).
    /// </summary>
    [Fact]
    public async Task A_gated_connect_against_a_live_connection_is_refused_rather_than_parked()
    {
        FakeMqttClient client = new();
        await client.ConnectAsync(new MqttClientOptions());

        client.GateConnect();

        Task<MqttClientConnectResult> connecting = client.ConnectAsync(new MqttClientOptions());
        Task first = await Task.WhenAny(connecting, Task.Delay(TimeSpan.FromSeconds(5)));

        first.ShouldBe(
            connecting,
            "AllowConnect was never called, so a CONNECT that reached the gate would still be "
            + "waiting after five seconds. A liveness check placed after the gate would park this "
            + "CONNECT instead of refusing it outright — a third behaviour belonging to neither the "
            + "real client nor this fake's intended design.");

        InvalidOperationException thrown = await Should.ThrowAsync<InvalidOperationException>(() => connecting);
        thrown.Message.ShouldBe(RefusalMessage);
    }
}
