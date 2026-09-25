using System.Diagnostics;
using MQTTnet;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 250 (#2449) — pins the fake's drop/hold controls, as
/// <see cref="FakeMqttClientContractTests"/> pins the liveness check spec 179
/// added. A separate file rather than an addition to that one, because that
/// file is a characterisation guard for spec 179's work and is left alone.
///
/// <para>
/// <b>What is pinned here is where the drop fires from, not merely that it
/// fires.</b> Both controls answer the CONNECT <c>Success</c> first — the
/// publisher never subscribes, so the answered CONNECT is the last packet of a
/// "connection that completed" — and raise the drop afterwards, so it lands as
/// a connection that was up rather than one that never existed.
/// </para>
/// </summary>
public class FakeMqttClientDropHoldContractTests
{
    [Fact]
    public async Task A_connection_dropped_on_arrival_raises_one_disconnect_for_a_connection_that_was_up()
    {
        FakeMqttClient client = new() { DropEveryConnectionImmediately = true };
        List<MqttClientDisconnectedEventArgs> disconnects = [];
        client.DisconnectedAsync += args =>
        {
            disconnects.Add(args);
            return Task.CompletedTask;
        };

        MqttClientConnectResult result = await client.ConnectAsync(new MqttClientOptions());

        result.ResultCode.ShouldBe(
            MqttClientConnectResultCode.Success,
            "the drop follows a successful CONNECT rather than replacing it — a session takeover "
            + "completes the handshake and then closes, it does not refuse it.");

        disconnects.Count.ShouldBe(
            1,
            "one connection was answered and one dropped it — not zero, and not a second one racing "
            + "behind it.");
        disconnects[0].ClientWasConnected.ShouldBeTrue(
            "the disconnect must describe a connection that was up — that flag is what the "
            + "publisher's DropSignal reads to accept it as a drop rather than a stale, pre-connect "
            + "event.");
        client.IsConnected.ShouldBeFalse("the drop leaves nothing connected behind it.");
    }

    [Fact]
    public async Task A_refused_connect_is_not_dropped_as_if_it_had_connected()
    {
        FakeMqttClient client = new() { DropEveryConnectionImmediately = true };
        client.RefuseNextConnects(1);
        int disconnects = 0;
        client.DisconnectedAsync += _ =>
        {
            disconnects++;
            return Task.CompletedTask;
        };

        MqttClientConnectResult result = await client.ConnectAsync(new MqttClientOptions());

        result.ResultCode.ShouldBe(
            MqttClientConnectResultCode.NotAuthorized,
            "the refusal branch in ConnectAsync returns before the success path, so a refusal must win "
            + "over a drop control that is also armed.");
        disconnects.ShouldBe(
            0,
            "a refusal has no connection to lose. The fake's stale-disconnect helpers already model a "
            + "refused CONNECT's own disconnect; DropEveryConnectionImmediately must not raise a second, "
            + "unrelated one on top of it.");
        client.IsConnected.ShouldBeFalse("a refused CONNECT never establishes a connection.");
    }

    [Fact]
    public async Task A_held_connection_is_up_until_the_hold_ends_and_then_drops()
    {
        TimeSpan hold = TimeSpan.FromMilliseconds(150);
        FakeMqttClient client = new();
        client.HoldEveryConnectionFor(hold);
        List<MqttClientDisconnectedEventArgs> disconnects = [];
        client.DisconnectedAsync += args =>
        {
            disconnects.Add(args);
            return Task.CompletedTask;
        };

        long connectedAt = Stopwatch.GetTimestamp();
        MqttClientConnectResult result = await client.ConnectAsync(new MqttClientOptions());

        result.ResultCode.ShouldBe(MqttClientConnectResultCode.Success);
        client.IsConnected.ShouldBeTrue(
            "the hold has not elapsed yet, straight after the CONNECT returns — a connection scheduled "
            + "to drop later is still up until it does.");

        (await WaitUntilAsync(() => disconnects.Count == 1)).ShouldBeTrue(
            "the held connection never dropped");

        TimeSpan elapsed = Stopwatch.GetElapsedTime(connectedAt);

        disconnects[0].ClientWasConnected.ShouldBeTrue("the drop follows a connection that was up.");
        client.IsConnected.ShouldBeFalse("the hold ended and the connection went with it.");

        // Task.Delay can return a shade before its argument on Windows' default
        // timer resolution; ADR-0150's poll grain (5 ms) adds a little more. 15
        // ms absorbs both without hiding a drop that fired materially early.
        elapsed.ShouldBeGreaterThanOrEqualTo(
            hold - TimeSpan.FromMilliseconds(15),
            $"the connection dropped after {elapsed.TotalMilliseconds:F0} ms against a "
            + $"{hold.TotalMilliseconds:F0} ms hold — too early to be the hold firing rather than an "
            + "immediate drop mislabelled as a held one.");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
        }

        return condition();
    }
}
