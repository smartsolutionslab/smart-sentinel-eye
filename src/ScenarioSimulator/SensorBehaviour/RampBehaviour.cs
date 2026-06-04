using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

/// <summary>Linear rise from <c>Min</c> to <c>Max</c> across the dwell (e.g. roughing temperature 950→1180).</summary>
public sealed class RampBehaviour : ISensorBehaviour
{
    public string Name => "ramp";

    public double Sample(SensorDefinition sensor, double fraction, Random random)
    {
        double min = sensor.Min ?? 0d;
        double max = sensor.Max ?? min;
        return min + ((max - min) * fraction);
    }
}
