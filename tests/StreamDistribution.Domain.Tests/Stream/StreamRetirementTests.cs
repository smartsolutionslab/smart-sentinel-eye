using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

/// <summary>
/// Spec 028 T023 — retiring a stream because its camera was retired (FR-008).
///
/// <para>
/// Two properties carry the weight here, and neither is the happy path. First,
/// <b>any</b> state can be retired: a camera can be pulled off the wall while
/// its stream is healthy, degraded, offline or still provisioning, and a
/// transition table that only allowed one of those would strand the rest.
/// Second, and the reason the guard exists at all: the health watcher and a
/// retirement race by construction. A sweep can read a stream, probe MediaMTX,
/// and come back to report on it after the retirement committed. Before the
/// guard, <c>ReportHealthy</c> set the state unconditionally, so that late
/// probe would move a retired stream back to Healthy — and the watcher would
/// resume announcing about hardware that no longer exists.
/// </para>
/// </summary>
public class StreamRetirementTests
{
    private static readonly DateTimeOffset RetiredAt =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    public static TheoryData<string> EveryState => new("Provisioning", "Healthy", "Degraded", "Offline");

    [Theory]
    [MemberData(nameof(EveryState))]
    public void A_stream_can_be_retired_from_any_state(string from)
    {
        Domain.Stream.Stream stream = InState(from);

        stream.Retire(Clock());

        stream.State.ShouldBe(StreamState.Retired);
    }

    [Theory]
    [MemberData(nameof(EveryState))]
    public void A_health_report_after_retirement_is_refused_from_any_state(string from)
    {
        Domain.Stream.Stream stream = InState(from);
        stream.Retire(Clock());

        // All three, because the watcher can arrive at any of them depending on
        // what MediaMTX said, and one unguarded route is all a resurrection
        // needs.
        Should.Throw<InvalidOperationException>(() =>
            stream.ReportHealthy(TranscodeMode.Passthrough, Clock()));
        Should.Throw<InvalidOperationException>(() =>
            stream.ReportDegraded("no frames", Clock()));
        Should.Throw<InvalidOperationException>(() =>
            stream.ReportOffline("no frames", Clock()));

        stream.State.ShouldBe(StreamState.Retired, "a refused report must not have moved the state either");
    }

    [Fact]
    public void Retiring_raises_one_event_recording_where_it_came_from()
    {
        Domain.Stream.Stream stream = InState("Healthy");
        stream.ClearPendingEvents();

        stream.Retire(Clock());

        StreamHealthChangedDomainEvent raised = stream.PendingEvents
            .OfType<StreamHealthChangedDomainEvent>()
            .ShouldHaveSingleItem();

        raised.FromState.ShouldBe(StreamState.Healthy);
        raised.ToState.ShouldBe(StreamState.Retired);
        raised.ChangedAt.ShouldBe(RetiredAt);
    }

    /// <summary>
    /// Idempotent as "no event", not as "no error". The announcement rides the
    /// outbox and can be redelivered; a second retirement that returned quietly
    /// while raising again would tell every subscriber the stream was retired
    /// twice, and the audit trail would agree.
    /// </summary>
    [Fact]
    public void Retiring_a_retired_stream_raises_nothing_further()
    {
        Domain.Stream.Stream stream = InState("Healthy");
        stream.Retire(Clock());
        stream.ClearPendingEvents();

        stream.Retire(Clock());

        stream.PendingEvents.ShouldBeEmpty();
        stream.State.ShouldBe(StreamState.Retired);
    }

    private static FixedClock Clock() => new(RetiredAt);

    /// <summary>
    /// Drives the aggregate through its real transitions rather than reaching
    /// past them: Offline is only reachable via Degraded, and a builder that set
    /// the state directly would let this file assert about states the aggregate
    /// cannot actually be in.
    /// </summary>
    private static Domain.Stream.Stream InState(string state)
    {
        Domain.Stream.Stream stream = new StreamBuilder().Build();
        FixedClock clock = Clock();

        switch (state)
        {
            case "Provisioning":
                break;
            case "Healthy":
                stream.ReportHealthy(TranscodeMode.Passthrough, clock);
                break;
            case "Degraded":
                stream.ReportDegraded("no frames", clock);
                break;
            case "Offline":
                stream.ReportDegraded("no frames", clock);
                stream.ReportOffline("still no frames", clock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unhandled state.");
        }

        stream.State.Value.ShouldBe(state);

        return stream;
    }

    private sealed class FixedClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
