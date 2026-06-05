using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

public class SensorBehaviourTests
{
    private static readonly Random Any = new(1);

    [Fact]
    public void Ramp_rises_linearly_from_min_to_max()
    {
        SensorDefinition sensor = new() { Behaviour = "ramp", Min = 100d, Max = 200d };
        RampBehaviour behaviour = new();

        behaviour.Sample(sensor, 0d, Any).ShouldBe(100d);
        behaviour.Sample(sensor, 0.5d, Any).ShouldBe(150d);
        behaviour.Sample(sensor, 1d, Any).ShouldBe(200d);
    }

    [Fact]
    public void Decay_falls_monotonically_from_start_toward_floor()
    {
        SensorDefinition sensor = new() { Behaviour = "decay", Start = 980d, Floor = 580d };
        DecayBehaviour behaviour = new();

        double atStart = behaviour.Sample(sensor, 0d, Any);
        double atMid = behaviour.Sample(sensor, 0.5d, Any);
        double atEnd = behaviour.Sample(sensor, 1d, Any);

        atStart.ShouldBe(980d);
        atMid.ShouldBeLessThan(atStart);
        atEnd.ShouldBeLessThan(atMid);
        atEnd.ShouldBeGreaterThan(580d); // approaches but never reaches the floor
    }

    [Fact]
    public void Step_holds_before_then_jumps_after_the_step_fraction()
    {
        SensorDefinition sensor = new() { Behaviour = "step", Before = 4d, After = 24d, StepAtFraction = 0.6d };
        StepBehaviour behaviour = new();

        behaviour.Sample(sensor, 0.5d, Any).ShouldBe(4d);
        behaviour.Sample(sensor, 0.6d, Any).ShouldBe(24d);
        behaviour.Sample(sensor, 0.9d, Any).ShouldBe(24d);
    }

    [Fact]
    public void Steady_stays_within_the_jitter_band_around_the_mean()
    {
        SensorDefinition sensor = new() { Behaviour = "steady", Mean = 9.2d, Jitter = 0.4d };
        SteadyBehaviour behaviour = new();
        Random random = new(7);

        for (int i = 0; i < 100; i++)
        {
            double value = behaviour.Sample(sensor, i / 100d, random);
            value.ShouldBeInRange(9.2d - 0.4d, 9.2d + 0.4d);
        }
    }

    [Fact]
    public void Burst_stays_at_baseline_or_spikes_to_peak()
    {
        SensorDefinition sensor = new() { Behaviour = "burst", Mean = 100d, Jitter = 10d, Peak = 500d };
        BurstBehaviour behaviour = new();
        Random random = new(3);

        for (int i = 0; i < 200; i++)
        {
            double value = behaviour.Sample(sensor, i / 200d, random);
            bool baseline = value is >= 90d and <= 110d;
            bool spike = Math.Abs(value - 500d) < 1e-9d;
            (baseline || spike).ShouldBeTrue();
        }
    }
}
