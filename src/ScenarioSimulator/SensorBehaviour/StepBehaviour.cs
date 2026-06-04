using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

/// <summary>
/// Flat <c>Before</c>, then a single jump to <c>After</c> at
/// <c>StepAtFraction</c> of the dwell (e.g. coiler coil-weight 4→24 at 0.6).
/// </summary>
public sealed class StepBehaviour : ISensorBehaviour
{
    public string Name => "step";

    public double Sample(SensorDefinition sensor, double fraction, Random random)
    {
        double before = sensor.Before ?? 0d;
        double after = sensor.After ?? before;
        double at = sensor.StepAtFraction ?? 0.5d;
        return fraction < at ? before : after;
    }
}
