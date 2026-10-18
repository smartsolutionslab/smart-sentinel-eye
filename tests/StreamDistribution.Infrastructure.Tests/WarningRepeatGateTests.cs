using SmartSentinelEye.StreamDistribution.Infrastructure;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// #3013 / spec 409 R10. The throttle rule shared by the source repair's whole-read
/// Warnings: first sight, a changed outcome, or ten monotonic minutes warn; anything
/// else is a repeat. The same rule as SourceRepairFailureLog (spec 388 FR-002 / FR-006).
/// </summary>
public class WarningRepeatGateTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse(
        "2026-10-18T09:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    private readonly ManualTimeProvider time = new(Start);

    [Fact]
    public void The_first_outcome_warns()
    {
        WarningRepeatGate<int> gate = new(time);

        gate.ShouldWarn(1).ShouldBeTrue();
    }

    [Fact]
    public void The_same_outcome_within_ten_minutes_does_not_warn()
    {
        WarningRepeatGate<int> gate = new(time);
        gate.ShouldWarn(1);

        time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));

        gate.ShouldWarn(1).ShouldBeFalse();
    }

    [Fact]
    public void The_same_outcome_after_ten_minutes_warns_again()
    {
        WarningRepeatGate<int> gate = new(time);
        gate.ShouldWarn(1);

        time.Advance(TimeSpan.FromMinutes(10));

        gate.ShouldWarn(1).ShouldBeTrue();
    }

    [Fact]
    public void A_different_outcome_warns_at_once()
    {
        WarningRepeatGate<int> gate = new(time);
        gate.ShouldWarn(1);

        gate.ShouldWarn(2).ShouldBeTrue();
    }

    [Fact]
    public void Clearing_the_gate_makes_the_same_outcome_warn_again()
    {
        WarningRepeatGate<int> gate = new(time);
        gate.ShouldWarn(1);

        gate.Clear();

        gate.ShouldWarn(1).ShouldBeTrue();
    }

    [Fact]
    public void A_wall_clock_step_alone_does_not_end_the_interval()
    {
        WarningRepeatGate<int> gate = new(time);
        gate.ShouldWarn(1);

        time.StepWallClock(TimeSpan.FromHours(1));

        gate.ShouldWarn(1).ShouldBeFalse();
    }
}
