using System.Text;
using MQTTnet;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 254 (#2450) — pins the credential history <c>FakeMqttClient</c> gained in
/// this spec, the way <see cref="FakeMqttClientContractTests"/> pins spec 179's
/// liveness check and <see cref="FakeMqttClientDropHoldContractTests"/> pins spec
/// 250's drop/hold controls. A separate file for the same reason those are
/// separate: each characterises one addition to the fake without touching the
/// file that characterises another.
///
/// <para>
/// Drives the fake directly, with no publisher involved, using a test-local
/// <see cref="MutableSlotCredentials"/> that reproduces the shape of the
/// publisher's private <c>TokenCredentials</c>: a synchronous credential provider
/// reading whatever a mutable slot holds when it is asked. That shape is what
/// makes a stale-credential defect visible at all — a provider that always
/// returns a fixed string could never tell a fresh presentation from a reused
/// one.
/// </para>
/// </summary>
public class FakeMqttClientCredentialContractTests
{
    [Fact]
    public async Task Each_connect_records_the_password_it_presented_at_the_time_it_was_sent()
    {
        FakeMqttClient client = new();
        client.RefuseNextConnects(1);
        MutableSlotCredentials credentials = new("first");

        MqttClientConnectResult first = await client.ConnectAsync(OptionsWith(credentials));

        first.ResultCode.ShouldBe(
            MqttClientConnectResultCode.NotAuthorized,
            "the arrange armed one refusal, so the first CONNECT must be the one the fake refuses — "
            + "otherwise the refusal lands on the second CONNECT below instead, and this test would no "
            + "longer be exercising a refused attempt followed by an accepted one.");

        credentials.Slot = "second";
        MqttClientConnectResult second = await client.ConnectAsync(OptionsWith(credentials));

        second.ResultCode.ShouldBe(
            MqttClientConnectResultCode.Success,
            "the one armed refusal was spent on the first CONNECT, so the second must be the one the "
            + "fake accepts.");

        client.PresentedCredentials.ShouldBe(
            ["first", "second"],
            "each CONNECT must record the password that was live at the moment it was sent, not the "
            + "password the slot holds when the test later reads the history back — a loop that mints "
            + "a token and then presents a stale one must be visible here (#2038), which a fake that "
            + "read the slot lazily could never show.");
        client.ConnectAttempts.ShouldBe(
            2,
            "both CONNECTs were answered by the broker — one refused, one accepted — so both must "
            + "count, and PresentedCredentials.Count must equal it.");
    }

    [Fact]
    public async Task A_connect_refused_because_the_client_is_already_connected_records_nothing()
    {
        FakeMqttClient client = new();
        MutableSlotCredentials credentials = new("first");

        await client.ConnectAsync(OptionsWith(credentials));

        credentials.Slot = "second";

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(OptionsWith(credentials)),
            "a second CONNECT against a connection nothing closed must be refused the way "
            + "MqttClient.ThrowIfConnected refuses it — the same contract FakeMqttClientContractTests "
            + "already pins for the connection state itself.");

        client.PresentedCredentials.ShouldBe(
            ["first"],
            "a CONNECT ThrowIfConnected refuses never reaches the point where a real client reads the "
            + "password to build the packet, so nothing about \"second\" belongs in the history — only "
            + "what the first, successful CONNECT actually presented.");
        client.ConnectAttempts.ShouldBe(
            1,
            "a CONNECT refused before it is sent never leaves the client, so the count must not move "
            + "for it either.");
    }

    [Fact]
    public async Task A_gated_connect_records_the_credential_read_before_the_gate()
    {
        FakeMqttClient client = new();
        client.GateConnect();
        SignallingCredentials credentials = new("before");

        Task<MqttClientConnectResult> connecting = client.ConnectAsync(OptionsWith(credentials));

        bool read = await WaitForReadAsync(credentials, TimeSpan.FromSeconds(5));
        read.ShouldBeTrue(
            "the fake never read the credential while the gate was held closed — the read must happen "
            + "before the gate, at the same point the real client reads the password to build the "
            + "CONNECT packet, not after it.");

        credentials.Slot = "after";
        client.AllowConnect();

        MqttClientConnectResult result = await connecting;

        result.ResultCode.ShouldBe(
            MqttClientConnectResultCode.Success,
            "arrange check — nothing armed a refusal, so the held CONNECT must succeed once released.");
        client.PresentedCredentials.ShouldBe(
            ["before"],
            "the credential was read while the slot still held \"before\"; setting it to \"after\" once "
            + "the read had already happened, and only then releasing the gate, must not change what "
            + "was recorded — a read taken after the gate would instead capture whatever the slot holds "
            + "when the test releases it, not what was actually sent.");
    }

    /// <summary>
    /// Waits for the read itself, not a proxy of it (ADR-0150). Polling
    /// <see cref="FakeMqttClient.Options"/> would race: the fake assigns it
    /// before the liveness check, ahead of the credential read this test needs
    /// to observe, so a test that changed the slot on first seeing
    /// <c>Options</c> set could beat the read that has not happened yet.
    /// </summary>
    private static async Task<bool> WaitForReadAsync(SignallingCredentials credentials, TimeSpan within)
    {
        try
        {
            await credentials.Read.Task.WaitAsync(within);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static MqttClientOptions OptionsWith(IMqttClientCredentialsProvider credentials) =>
        new MqttClientOptionsBuilder().WithTcpServer("mosquitto.test", 1883).WithCredentials(credentials).Build();

    /// <summary>
    /// The publisher's private <c>TokenCredentials</c>, reproduced locally
    /// because that class is not accessible here: a synchronous provider that
    /// reads whatever <see cref="Slot"/> holds at the moment it is asked.
    /// </summary>
    private sealed class MutableSlotCredentials(string slot) : IMqttClientCredentialsProvider
    {
        public string Slot { get; set; } = slot;

        public string GetUserName(MqttClientOptions clientOptions) => "scenario-simulator";

        public byte[] GetPassword(MqttClientOptions clientOptions) => Encoding.UTF8.GetBytes(Slot);
    }

    /// <summary>
    /// The same shape as <see cref="MutableSlotCredentials"/>, plus a completion
    /// signal raised from inside <see cref="GetPassword"/> itself — the read
    /// event the gated test waits on, rather than a fixed delay or a poll of
    /// something that only correlates with the read.
    /// </summary>
    private sealed class SignallingCredentials(string slot) : IMqttClientCredentialsProvider
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Slot { get; set; } = slot;

        public string GetUserName(MqttClientOptions clientOptions) => "scenario-simulator";

        public byte[] GetPassword(MqttClientOptions clientOptions)
        {
            byte[] password = Encoding.UTF8.GetBytes(Slot);
            Read.TrySetResult();
            return password;
        }
    }
}
