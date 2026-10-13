using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

/// <summary><c>Mean ± Jitter</c> with no trend (e.g. finishing strip speed 9.2±0.4).</summary>
public sealed class SteadyBehaviour : ISensorBehaviour
{
    public string Name => "steady";

    public double Sample(SensorDefinition sensor, double fraction, Random random)
    {
        var mean = sensor.Mean ?? 0d;
        var jitter = sensor.Jitter ?? 0d;
        return mean + (((random.NextDouble() * 2d) - 1d) * jitter);
    }
}
