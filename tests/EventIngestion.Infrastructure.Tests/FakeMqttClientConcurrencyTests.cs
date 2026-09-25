using System.Text;
using MQTTnet;
using SmartSentinelEye.EventIngestion.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Issue #2451. Before the fix, <c>FakeMqttClient.PresentedCredentials</c> and
/// <c>SubscribedTopics</c> were plain <c>List&lt;string&gt;</c>, and
/// <c>ConnectAttempts</c> read <c>PresentedCredentials.Count</c>. The red this
/// suite actually observed (commit 18141f7a, before the fix) was
/// <c>List&lt;T&gt;</c>'s version-checked enumerator throwing
/// <c>InvalidOperationException: Collection was modified</c> mid-<c>foreach</c>
/// when the writer's concurrent <c>Add</c> landed during a read — the issue's
/// original premise.
///
/// <para>
/// The fix (now <c>ConcurrentQueue&lt;string&gt;</c>, with
/// <c>ConnectAttempts</c> its own counter incremented only after the element is
/// enqueued) additionally closes a second, related race by construction rather
/// than by ever having been seen failing in this shape: <c>List&lt;T&gt;.Add</c>
/// publishes the new count before it stores the element
/// (<c>_size = size + 1; array[size] = item;</c>), so a reader that read a count
/// and then indexed up to it could in principle observe an element the storage
/// had not caught up with. Nothing in this repository's runs ever reproduced
/// that ordering directly — the enumeration throw always won the race first —
/// so this test asserts the fixed invariant (a snapshot is never behind the
/// count read before it) going forward rather than re-deriving a failure that
/// was never independently observed.
/// </para>
///
/// <para>
/// This is a guard whose subject is the test double itself, in the shape of
/// <see cref="MqttClientWasConnectedContractTests"/> here and
/// <c>ScenarioSimulator.Tests/FakeMqttClientContractTests.cs</c> there. A
/// single-threaded test cannot reach either race at all — it needs a writer and
/// a reader genuinely overlapping on different threads — so this is a bounded
/// stress test rather than an ordinary fact. That is also what makes it
/// trustworthy in both directions: once the recorder is FIFO with the count
/// published only after the element is stored, no interleaving can fail it, so
/// its green is deterministic; against the unmodified <c>List&lt;T&gt;</c> a
/// version-checked <c>foreach</c> racing a concurrent <c>Add</c> throws within
/// a few thousand overlaps, so its red is near-certain. The only way either
/// test can be wrong is by missing a regression, never by failing a correct
/// fake.
/// </para>
///
/// <para>
/// Reading is deliberately an explicit <c>foreach</c>, not a spread or
/// <c>.ToList()</c> — both of those go through <c>ICollection.CopyTo</c> on a
/// <c>List&lt;T&gt;</c>, which copies the backing array directly and never
/// touches the version check that makes the race observable.
/// </para>
/// </summary>
[Collection(FakeMqttClientConcurrencyCollection.Name)]
public class FakeMqttClientConcurrencyTests
{
    private const int Writes = 20_000;

    private static readonly TimeSpan OverallBound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// F1. Every CONNECT is refused, so the client never becomes connected and
    /// the writer runs the full 20,000 iterations without <c>ThrowIfConnected</c>
    /// ever skipping one.
    /// </summary>
    [Fact]
    public async Task A_count_read_while_credentials_are_being_recorded_is_never_ahead_of_them()
    {
        FakeMqttClient client = new();
        client.RefuseEveryConnect();

        using CancellationTokenSource writerDone = new();
        ViolationTracker tracker = new();

        Task writer = Task.Run(async () =>
        {
            try
            {
                for (int i = 1; i <= Writes; i++)
                {
                    await client.ConnectAsync(OptionsPresenting($"token-{i}"));
                }
            }
            finally
            {
                await writerDone.CancelAsync();
            }
        });

        Task reader = Task.Run(() =>
        {
            while (!writerDone.IsCancellationRequested)
            {
                // Read the count first: the invariant under test is that a
                // snapshot taken afterwards is never behind it.
                int counted = client.ConnectAttempts;
                int index = 0;

                try
                {
                    foreach (string credential in client.PresentedCredentials)
                    {
                        string expected = $"token-{index + 1}";
                        if (credential != expected)
                        {
                            tracker.RecordViolation(
                                $"element {index} was {Describe(credential)}, expected \"{expected}\" "
                                + $"(counted {counted} before this snapshot of length {index + 1})");
                        }

                        index++;
                    }
                }
                catch (Exception exception)
                {
                    tracker.RecordViolation(
                        $"enumeration threw after {index} of the counted {counted} elements: {exception}");
                }

                if (index < counted)
                {
                    tracker.RecordViolation(
                        $"snapshot length {index} is behind the count {counted} read before it");
                }

                if (counted is > 0 and < Writes)
                {
                    tracker.RecordOverlap();
                }
            }
        });

        await Task.WhenAll(writer, reader).WaitAsync(OverallBound);

        tracker.Violation.ShouldBeNull(tracker.Violation ?? string.Empty);
        tracker.OverlappingReads.ShouldBeGreaterThan(
            0, "the reader never overlapped the writer, so this run proves nothing");
        client.ConnectAttempts.ShouldBe(Writes);
    }

    /// <summary>
    /// F2. The identical shape for <c>SubscribedTopics</c> — same defect, same
    /// file (spec 246 §1). Each iteration connects, subscribes to one topic and
    /// disconnects, so <c>SubscribeAsync</c>'s <c>AddRange</c> onto
    /// <c>SubscribedTopics</c> is what the reader races.
    /// </summary>
    [Fact]
    public async Task Topics_read_while_subscriptions_are_being_recorded_arrive_whole_and_in_order()
    {
        FakeMqttClient client = new();

        using CancellationTokenSource writerDone = new();
        ViolationTracker tracker = new();

        Task writer = Task.Run(async () =>
        {
            try
            {
                for (int i = 1; i <= Writes; i++)
                {
                    await client.ConnectAsync(new MqttClientOptions());
                    await client.SubscribeAsync(
                        new MqttClientSubscribeOptionsBuilder().WithTopicFilter($"topic-{i}").Build());
                    await client.DisconnectAsync(new MqttClientDisconnectOptions());
                }
            }
            finally
            {
                await writerDone.CancelAsync();
            }
        });

        Task reader = Task.Run(() =>
        {
            while (!writerDone.IsCancellationRequested)
            {
                int index = 0;

                try
                {
                    foreach (string topic in client.SubscribedTopics)
                    {
                        string expected = $"topic-{index + 1}";
                        if (topic != expected)
                        {
                            tracker.RecordViolation(
                                $"element {index} was {Describe(topic)}, expected \"{expected}\" "
                                + $"(snapshot length {index + 1})");
                        }

                        index++;
                    }
                }
                catch (Exception exception)
                {
                    tracker.RecordViolation($"enumeration threw after {index} elements: {exception}");
                }

                // No separate counter here (spec 246 §2): the snapshot length
                // itself stands in for "counted" in the overlap check.
                if (index is > 0 and < Writes)
                {
                    tracker.RecordOverlap();
                }
            }
        });

        await Task.WhenAll(writer, reader).WaitAsync(OverallBound);

        tracker.Violation.ShouldBeNull(tracker.Violation ?? string.Empty);
        tracker.OverlappingReads.ShouldBeGreaterThan(
            0, "the reader never overlapped the writer, so this run proves nothing");
        client.SubscribedTopics.Count.ShouldBe(Writes);
    }

    /// <summary>
    /// F3. Single-threaded characterisation, green before and after: the one
    /// placement the fix could plausibly move is the enqueue/increment relative
    /// to <c>ConnectAsync</c>'s <c>IsConnected</c> guard, and this pins that a
    /// refusal there records nothing.
    /// </summary>
    [Fact]
    public async Task A_connect_refused_because_the_client_is_already_connected_is_not_recorded()
    {
        FakeMqttClient client = new();
        await client.ConnectAsync(new MqttClientOptions());

        await Should.ThrowAsync<InvalidOperationException>(() => client.ConnectAsync(new MqttClientOptions()));

        client.ConnectAttempts.ShouldBe(1);
        client.PresentedCredentials.Count.ShouldBe(1);
    }

    private static MqttClientOptions OptionsPresenting(string password) =>
        new()
        {
            Credentials = new MqttClientCredentials("user", Encoding.UTF8.GetBytes(password)),
        };

    private static string Describe(string? value) => value is null ? "null" : $"\"{value}\"";

    /// <summary>
    /// Owned by the reader task alone — only that one thread ever calls its
    /// methods — so it needs no locking of its own; it is read from the test
    /// thread only after <c>Task.WhenAll</c> has completed both tasks.
    /// </summary>
    private sealed class ViolationTracker
    {
        public string? Violation { get; private set; }

        public int OverlappingReads { get; private set; }

        public void RecordViolation(string message) => Violation ??= message;

        public void RecordOverlap() => OverlappingReads++;
    }
}
